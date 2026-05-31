using DotNetPlugin.NativeBindings.SDK;
using Microsoft.Win32;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace DotNetPlugin
{
    #region McpParamAttribute

    /// <summary>
    /// Optional attribute for describing MCP tool parameters in the
    /// <c>tools/list</c> JSON schema. May be applied directly to the
    /// parameter (description only) or to the method (name + description)
    /// when the parameter itself cannot be annotated.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Method,
                    AllowMultiple = true, Inherited = true)]
    public sealed class McpParamAttribute : Attribute
    {
        /// <summary>Parameter name (only required for method-level usage).</summary>
        public string Name { get; }

        /// <summary>Human-readable description shown to the MCP client.</summary>
        public string Description { get; }

        /// <summary>Optional JSON-schema type override (e.g. "string","integer").</summary>
        public string Type { get; set; }

        /// <summary>Whether this parameter must be supplied. Defaults to true.</summary>
        public bool Required { get; set; } = true;

        /// <summary>Parameter-level constructor (description only).</summary>
        public McpParamAttribute(string description)
        {
            Description = description;
        }

        /// <summary>Method-level constructor (specifies which param it describes).</summary>
        public McpParamAttribute(string name, string description)
        {
            Name = name;
            Description = description;
        }
    }

    #endregion

    #region McpSseSession

    /// <summary>
    /// Per-client SSE session state. Wraps the long-lived response writer
    /// plus a semaphore that serialises writes (one frame at a time) and a
    /// CancellationTokenSource that the heartbeat loop and cleanup share.
    /// </summary>
    internal sealed class McpSseSession : IDisposable
    {
        public string SessionId { get; }
        public StreamWriter Writer { get; }
        public HttpListenerResponse Response { get; }
        public SemaphoreSlim WriteLock { get; } = new SemaphoreSlim(1, 1);
        public CancellationTokenSource Cts { get; } = new CancellationTokenSource();

        private int _disposed;

        public McpSseSession(string sessionId,
                             HttpListenerResponse response,
                             StreamWriter writer)
        {
            SessionId = sessionId;
            Response = response;
            Writer = writer;
        }

        public bool IsDisposed
        {
            get { return Volatile.Read(ref _disposed) == 1; }
        }

        public void Dispose()
        {
            // Idempotent — only the first caller actually tears down.
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            try { Cts.Cancel(); } catch { /* ignored */ }
            try { Writer?.Flush(); } catch { /* ignored */ }
            try { Writer?.Dispose(); } catch { /* ignored */ }
            try { Response?.OutputStream?.Dispose(); } catch { /* ignored */ }
            try { Response?.Close(); } catch { /* ignored */ }
            try { Cts.Dispose(); } catch { /* ignored */ }
            try { WriteLock.Dispose(); } catch { /* ignored */ }
        }
    }

    #endregion

    /// <summary>
    /// MCP server supporting both classic single-shot JSON requests and the
    /// modern Streamable HTTP transport (long-lived SSE) on a single endpoint.
    /// Mode is auto-detected per request from HTTP method, Accept header and
    /// the presence of the Mcp-Session-Id header.
    /// </summary>
    public class SimpleMcpServer
    {
        #region Constants & static state

        private const string ProtocolVersion = "2025-11-25";
        private const string ServerName = "AgentSmithes x96Dbg";
        private const string ServerVersion = "1.4";
        private const int HeartbeatIntervalMs = 15000;

        // Shared serializer — JavaScriptSerializer is thread-safe for
        // independent Serialize/Deserialize calls; we bump the max length
        // because some tools/list responses get sizeable.
        private static readonly JavaScriptSerializer _json =
            new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        #endregion

        #region Fields

        private readonly HttpListener _listener = new HttpListener();
        private readonly Dictionary<string, MethodInfo> _commands =
            new Dictionary<string, MethodInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, McpSseSession> _sessions =
            new ConcurrentDictionary<string, McpSseSession>();

        private readonly Type _targetType;
        private readonly McpServerConfig _config;
        private readonly string _bearerToken;
        private readonly bool _authEnabled;
        private bool _isRunning;

        /// <summary>Mirrors Bridge.DbgIsDebugging(); setter is a no-op (state lives in Bridge).</summary>
        public bool IsActivelyDebugging
        {
            get { return Bridge.DbgIsDebugging(); }
            set { /* accept assignments from event callbacks; actual state comes from Bridge */ }
        }

        #endregion

        #region Construction

        /// <summary>
        /// Builds an MCP server with auth DISABLED, using the IP/port from
        /// <see cref="McpServerConfig.Load"/>.
        /// </summary>
        public SimpleMcpServer(Type commandSourceType)
            : this(commandSourceType, McpServerConfig.Load(), bearerToken: null) { }

        /// <summary>
        /// Backward-compatible constructor: existing call sites that pass a
        /// pre-built <see cref="McpServerConfig"/> keep working. Auth is
        /// disabled (no token).
        /// </summary>
        public SimpleMcpServer(Type commandSourceType, McpServerConfig config)
            : this(commandSourceType, config, bearerToken: null) { }

        /// <summary>
        /// Builds an MCP server using the default config and a Bearer token.
        /// When <paramref name="bearerToken"/> is null or whitespace, Bearer
        /// authentication is disabled.
        /// </summary>
        public SimpleMcpServer(Type commandSourceType, string bearerToken)
            : this(commandSourceType, McpServerConfig.Load(), bearerToken) { }

        /// <summary>
        /// Full constructor. When <paramref name="bearerToken"/> is null or
        /// whitespace the server runs WITHOUT authentication (every request
        /// is allowed); otherwise the token must arrive in the
        /// "Authorization: Bearer ..." header on every non-OPTIONS request.
        /// </summary>
        public SimpleMcpServer(Type commandSourceType,
                               McpServerConfig config,
                               string bearerToken)
        {
            _targetType = commandSourceType ?? throw new ArgumentNullException(nameof(commandSourceType));
            _config = config ?? McpServerConfig.Load();
            _bearerToken = string.IsNullOrWhiteSpace(bearerToken) ? null : bearerToken.Trim();
            _authEnabled = _bearerToken != null;

            string ip = _config.IpAddress;
            string port = _config.Port.ToString(CultureInfo.InvariantCulture);
            string baseUrl = _config.GetBaseUrl();

            Console.WriteLine("MCP (Streamable) listening on " + baseUrl);
            Console.WriteLine("MCP server listening on " + ip + ":" + port);
            Console.WriteLine("Bearer auth: " + (_authEnabled ? "ENABLED" : "DISABLED (no token configured)"));

            // SINGLE unified endpoint — the "/" prefix catches every path
            // under host:port. All routing is done inside HandleRequestAsync.
            _listener.Prefixes.Add("http://" + ip + ":" + port + "/");

            RegisterCommands();
        }

        private void RegisterCommands()
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var method in _targetType.GetMethods(flags))
            {
                var attr = method.GetCustomAttribute<CommandAttribute>();
                if (attr != null && !string.IsNullOrEmpty(attr.Name))
                {
                    _commands[attr.Name] = method;
                }
            }
        }

        #endregion

        #region Optional HTTP-header tweaks (preserved verbatim from legacy)

        /// <summary>
        /// Sets the registry flag that asks http.sys to omit the "Server:"
        /// response header. Preserved from the original implementation; not
        /// called automatically because it requires admin and a service
        /// restart.
        /// </summary>
        public static void DisableServerHeader()
        {
            const string keyPath = @"SYSTEM\CurrentControlSet\Services\HTTP\Parameters";
            const string valueName = "DisableServerHeader";
            const int desiredValue = 2;

            try
            {
                using (var key = Registry.LocalMachine.CreateSubKey(keyPath, true))
                {
                    if (key == null)
                    {
                        Console.WriteLine("Failed to open or create the registry key.");
                        return;
                    }

                    var currentValue = key.GetValue(valueName);
                    if (currentValue == null || (int)currentValue != desiredValue)
                    {
                        key.SetValue(valueName, desiredValue, RegistryValueKind.DWord);
                        Console.WriteLine("Registry value updated. Restarting HTTP service...");
                        RestartHttpService();
                    }
                    else
                    {
                        Console.WriteLine("DisableServerHeader is already set to 2. No changes made.");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error modifying registry: " + ex.Message);
            }
        }

        private static void RestartHttpService()
        {
            try
            {
                ExecuteCommand("net stop http");
                ExecuteCommand("net start http");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to restart HTTP service. Try rebooting manually. Error: " + ex.Message);
            }
        }

        private static void ExecuteCommand(string command)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo("cmd.exe", "/c " + command)
                {
                    Verb = "runas",
                    CreateNoWindow = true,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                }
            };
            process.Start();
            process.WaitForExit();
        }

        #endregion

        #region Lifecycle

        /// <summary>Starts the listener and accepts requests asynchronously.</summary>
        public void Start()
        {
            if (_isRunning)
            {
                Console.WriteLine("MCP server is already running.");
                return;
            }

            try
            {
                _listener.Start();
                _isRunning = true;
                BeginAccept();
                Console.WriteLine("MCP server started. CurrentlyDebugging: "
                                  + Bridge.DbgIsDebugging()
                                  + " IsRunning: " + Bridge.DbgIsRunning());
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to start MCP server: " + ex.Message);
            }
        }

        /// <summary>
        /// Stops the listener, cancels heartbeats, and disposes every active
        /// SSE session. Safe to call multiple times.
        /// </summary>
        public void Stop()
        {
            if (!_isRunning)
            {
                Console.WriteLine("MCP server is already stopped.");
                return;
            }

            try
            {
                _isRunning = false;                 // stop arming new accepts
                try { _listener.Stop(); } catch { } // abort pending GetContext

                foreach (var kv in _sessions.ToArray())
                {
                    try { kv.Value.Dispose(); } catch { /* ignored */ }
                    _sessions.TryRemove(kv.Key, out _);
                }

                Console.WriteLine("MCP server stopped.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to stop MCP server: " + ex.Message);
            }
        }

        private void BeginAccept()
        {
            try { _listener.BeginGetContext(OnAcceptCallback, null); }
            catch { /* listener stopped */ }
        }

        private void OnAcceptCallback(IAsyncResult ar)
        {
            HttpListenerContext ctx;
            try { ctx = _listener.EndGetContext(ar); }
            catch (ObjectDisposedException) { return; }
            catch (HttpListenerException) { return; }
            catch { return; }

            // Re-arm the listener BEFORE processing so we don't gate accept
            // on the duration of any one request.
            if (_isRunning && _listener.IsListening) BeginAccept();

            //Console.WriteLine("MCP (Streamable) incoming connection");
            // Process the request on a thread-pool task so the IO completion
            // callback returns immediately.
            _ = Task.Run(() => SafeHandle(ctx));
        }

        private async Task SafeHandle(HttpListenerContext ctx)
        {
            try
            {
                await HandleRequestAsync(ctx).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Unhandled MCP error: " + ex);
                try
                {
                    ctx.Response.StatusCode = 500;
                    ctx.Response.Close();
                }
                catch { /* response may already be flushed */ }
            }
        }

        #endregion

        #region Auth + CORS

        /// <summary>
        /// Applies the standard CORS / MCP response headers. Called BEFORE
        /// any body write so headers aren't already on the wire.
        /// </summary>
        private static void ApplyStandardHeaders(HttpListenerResponse res)
        {
            res.Headers["Server"] = "Kestrel";
            res.Headers["Access-Control-Allow-Origin"] = "*";
            res.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS, DELETE";
            res.Headers["Access-Control-Allow-Headers"] = "Content-Type, Mcp-Session-Id, Authorization, mcp-protocol-version, Accept";
            res.Headers["Access-Control-Expose-Headers"] = "Mcp-Session-Id";
        }

        /// <summary>
        /// Validates the Bearer token. Returns true if the request may
        /// proceed (auth disabled OR token matches). Writes a 401 response
        /// and returns false otherwise.
        /// </summary>
        private bool TryAuthorize(HttpListenerRequest req, HttpListenerResponse res)
        {
            if (!_authEnabled) return true;

            string authHeader = req.Headers["Authorization"];
            if (string.IsNullOrEmpty(authHeader) ||
                !authHeader.StartsWith("Bearer ", StringComparison.Ordinal))
            {
                WriteUnauthorized(res, "Missing or malformed Authorization header.");
                return false;
            }

            string supplied = authHeader.Substring("Bearer ".Length).Trim();
            // Constant-time compare to discourage timing oracles.
            if (!ConstantTimeEquals(supplied, _bearerToken))
            {
                WriteUnauthorized(res, "Invalid Bearer token.");
                return false;
            }

            return true;
        }

        private static bool ConstantTimeEquals(string a, string b)
        {
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        private static void WriteUnauthorized(HttpListenerResponse res, string reason)
        {
            try
            {
                res.StatusCode = 401;
                res.Headers["WWW-Authenticate"] = "Bearer realm=\"MCP\", error=\"invalid_token\"";
                res.ContentType = "text/plain; charset=utf-8";
                byte[] body = Encoding.UTF8.GetBytes(reason);
                res.ContentLength64 = body.Length;
                res.OutputStream.Write(body, 0, body.Length);
            }
            catch { /* client may have disconnected */ }
            finally
            {
                try { res.Close(); } catch { /* ignored */ }
            }
        }

        #endregion

        #region Top-level routing

        private async Task HandleRequestAsync(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var res = ctx.Response;

            ApplyStandardHeaders(res);

            // 1. OPTIONS preflight — no auth, no body, just CORS headers.
            if (string.Equals(req.HttpMethod, "OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                res.StatusCode = 200;
                res.Close();
                return;
            }

            // 2. Authentication gate (checked before anything else).
            if (!TryAuthorize(req, res)) return;

            // 3. Resolve session id (header first, query-string second).
            string sessionId = req.Headers["Mcp-Session-Id"];
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                sessionId = req.QueryString["sessionId"];
            }

            string method = req.HttpMethod;
            string accept = req.Headers["Accept"] ?? "";
            bool wantsSse = accept.IndexOf("text/event-stream", StringComparison.OrdinalIgnoreCase) >= 0;
            bool wantsJson = accept.IndexOf("application/json", StringComparison.OrdinalIgnoreCase) >= 0
                             || string.IsNullOrEmpty(accept);

            // 4. DELETE — terminate session.
            if (string.Equals(method, "DELETE", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(sessionId)) CleanupSession(sessionId);
                res.StatusCode = 200;
                res.ContentType = "text/plain; charset=utf-8";
                byte[] ok = Encoding.UTF8.GetBytes("OK");
                res.ContentLength64 = ok.Length;
                try { res.OutputStream.Write(ok, 0, ok.Length); } catch { }
                try { res.Close(); } catch { }
                return;
            }

            // 5. GET — open a long-lived server-push SSE stream.
            if (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
            {
                if (!wantsSse)
                {
                    // GET without an SSE Accept is treated as a discovery
                    // ping. Send back a minimal JSON describing the server.
                    await WriteJsonAsync(res, 200, new
                    {
                        jsonrpc = "2.0",
                        result = new
                        {
                            name = ServerName,
                            version = ServerVersion,
                            protocolVersion = ProtocolVersion,
                            transports = new[] { "streamable-http", "sse" }
                        }
                    }).ConfigureAwait(false);
                    return;
                }

                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    sessionId = GenerateSessionId();
                    res.Headers["Mcp-Session-Id"] = sessionId;
                }

                await HandleSseStreamAsync(ctx, sessionId).ConfigureAwait(false);
                return;
            }

            // 6. POST — JSON-RPC message.
            if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
            {
                await HandlePostAsync(ctx, sessionId, wantsSse, wantsJson).ConfigureAwait(false);
                return;
            }

            // 7. Anything else — not supported.
            res.StatusCode = 405;
            res.Headers["Allow"] = "GET, POST, DELETE, OPTIONS";
            res.Close();
        }

        #endregion

        #region GET — long-lived SSE stream (server -> client push)

        private async Task HandleSseStreamAsync(HttpListenerContext ctx, string sessionId)
        {
            var res = ctx.Response;

            res.StatusCode = 200;
            res.ContentType = "text/event-stream";
            res.SendChunked = true;
            res.KeepAlive = true;
            res.Headers["Cache-Control"] = "no-cache, no-store";
            res.Headers["Connection"] = "keep-alive";
            res.Headers["X-Accel-Buffering"] = "no";          // disable proxy buffering
            res.Headers["Mcp-Session-Id"] = sessionId;

            // UTF-8 without BOM, AutoFlush so each Write hits the wire.
            var writer = new StreamWriter(res.OutputStream, new UTF8Encoding(false))
            {
                AutoFlush = true,
                NewLine = "\n"
            };

            var session = new McpSseSession(sessionId, res, writer);

            // Replace any previous session living under the same id.
            if (_sessions.TryGetValue(sessionId, out var existing))
            {
                try { existing.Dispose(); } catch { /* ignored */ }
            }
            _sessions[sessionId] = session;

            try
            {
                // Legacy 2024-11-05 "endpoint" handshake — harmless to send
                // on modern clients; they ignore unknown event types.
                await session.WriteLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    await writer.WriteAsync("event: endpoint\n").ConfigureAwait(false);
                    await writer.WriteAsync("data: /?sessionId=" + sessionId + "\n\n").ConfigureAwait(false);
                    await writer.FlushAsync().ConfigureAwait(false);
                }
                finally { session.WriteLock.Release(); }

                // Heartbeat loop keeps proxies / load balancers from
                // collapsing the idle connection. Lives on a background
                // task; exits when the session is disposed.
                var heartbeat = Task.Run(() => HeartbeatLoopAsync(session));

                // Park the request thread until the session is cancelled.
                try
                {
                    await Task.Delay(Timeout.Infinite, session.Cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { /* expected on shutdown */ }

                try { await heartbeat.ConfigureAwait(false); } catch { /* ignored */ }
            }
            catch (Exception ex)
            {
                Console.WriteLine("SSE stream error for session " + sessionId + ": " + ex.Message);
            }
            finally
            {
                CleanupSession(sessionId);
            }
        }

        private async Task HeartbeatLoopAsync(McpSseSession session)
        {
            var token = session.Cts.Token;
            while (!token.IsCancellationRequested && !session.IsDisposed)
            {
                try
                {
                    await Task.Delay(HeartbeatIntervalMs, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { return; }

                if (session.IsDisposed) return;

                await session.WriteLock.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    // ":" prefix marks an SSE comment; the client ignores it
                    // but the bytes hitting the socket keep the link alive.
                    await session.Writer.WriteAsync(": ping\n\n").ConfigureAwait(false);
                    await session.Writer.FlushAsync().ConfigureAwait(false);
                }
                catch
                {
                    // Write failed -> connection died. Tear down and exit.
                    CleanupSession(session.SessionId);
                    return;
                }
                finally
                {
                    try { session.WriteLock.Release(); } catch { /* ignored */ }
                }
            }
        }

        private void CleanupSession(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId)) return;
            if (_sessions.TryRemove(sessionId, out var session))
            {
                try { session.Dispose(); } catch { /* ignored */ }
            }
        }

        #endregion

        #region POST — JSON-RPC entry point

        private async Task HandlePostAsync(HttpListenerContext ctx,
                                           string sessionId,
                                           bool wantsSse,
                                           bool wantsJson)
        {
            string body;
            try
            {
                using (var reader = new StreamReader(ctx.Request.InputStream,
                                                     ctx.Request.ContentEncoding ?? Encoding.UTF8))
                {
                    body = await reader.ReadToEndAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                await WriteJsonRpcErrorOnRequestAsync(ctx, null, -32700,
                    "Parse error reading body: " + ex.Message).ConfigureAwait(false);
                return;
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                ctx.Response.StatusCode = 204; // No Content
                ctx.Response.Close();
                return;
            }

            Dictionary<string, object> request;
            try
            {
                request = _json.Deserialize<Dictionary<string, object>>(body);
            }
            catch (Exception ex)
            {
                await WriteJsonRpcErrorOnRequestAsync(ctx, null, -32700,
                    "Parse error: " + ex.Message).ConfigureAwait(false);
                return;
            }

            if (request == null || !request.ContainsKey("method"))
            {
                await WriteJsonRpcErrorOnRequestAsync(ctx, null, -32600,
                    "Invalid Request: missing 'method'.").ConfigureAwait(false);
                return;
            }

            string methodName = Convert.ToString(request["method"], CultureInfo.InvariantCulture);
            object id;
            request.TryGetValue("id", out id);

            // Notification (no id) -> 202 Accepted, no body.
            if (id == null && IsNotificationMethod(methodName))
            {
                await HandleNotificationAsync(methodName, request).ConfigureAwait(false);
                ctx.Response.StatusCode = 202;
                ctx.Response.ContentType = "text/plain; charset=utf-8";
                byte[] accepted = Encoding.UTF8.GetBytes("Accepted");
                ctx.Response.ContentLength64 = accepted.Length;
                try { ctx.Response.OutputStream.Write(accepted, 0, accepted.Length); } catch { }
                ctx.Response.Close();
                return;
            }

            // Initialize is special: it both returns a result AND mints a
            // session id (if the caller didn't supply one).
            if (string.Equals(methodName, "initialize", StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    sessionId = GenerateSessionId();
                }
                ctx.Response.Headers["Mcp-Session-Id"] = sessionId;
            }

            // Build the JSON-RPC response payload.
            object responseObject;
            try
            {
                responseObject = await DispatchAsync(methodName, id, request, sessionId)
                                       .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                responseObject = BuildError(id, -32000,
                    "Internal Server Error: " + ex.Message);
            }

            // Deliver via the mode the client asked for.
            // Preference order: explicit SSE Accept > JSON Accept > JSON default.
            bool sendAsSse = wantsSse && !wantsJson;

            if (sendAsSse)
            {
                await WriteSingleSseResponseAsync(ctx, responseObject).ConfigureAwait(false);
            }
            else
            {
                await WriteJsonAsync(ctx.Response, 200, responseObject).ConfigureAwait(false);
            }
        }

        private static bool IsNotificationMethod(string method)
        {
            // The MCP spec treats anything in the "notifications/" namespace
            // as fire-and-forget. We also tolerate clients that send a
            // request-shaped object WITHOUT an id; treat that as a
            // notification too.
            if (string.IsNullOrEmpty(method)) return false;
            return method.StartsWith("notifications/", StringComparison.Ordinal);
        }

        private Task HandleNotificationAsync(string method, Dictionary<string, object> request)
        {
            // Currently we just log; future work might pump these into the
            // SSE stream for tooling. Returning a completed Task keeps the
            // call site uniform.
            Debug.WriteLine("MCP notification received: " + method);
            return Task.CompletedTask;
        }

        #endregion

        #region JSON-RPC dispatch

        private async Task<object> DispatchAsync(string method,
                                                 object id,
                                                 Dictionary<string, object> request,
                                                 string sessionId)
        {
            switch (method)
            {
                case "initialize":
                    return HandleInitialize(id, sessionId);

                case "tools/list":
                    return HandleToolsList(id);

                case "tools/call":
                    return await HandleToolsCallAsync(id, request).ConfigureAwait(false);

                case "prompts/list":
                    return HandlePromptsList(id);

                case "prompts/get":
                    return HandlePromptsGet(id, request);

                case "resources/list":
                    return HandleResourcesList(id);

                case "resources/templates/list":
                    return HandleResourceTemplatesList(id);

                case "resources/read":
                    return HandleResourceRead(id, request);

                case "ping":
                    return BuildResult(id, new { });

                case "rpc.discover":
                    // Legacy 2024-11-05 — re-route to tools/list.
                    return HandleToolsList(id);

                default:
                    return BuildError(id, -32601, "Method not found: " + method);
            }
        }

        #endregion

        #region Handler: initialize

        private object HandleInitialize(object id, string sessionId)
        {
            // Session id was already set on the response headers in
            // HandlePostAsync; we just echo it back inside the result for
            // clients that read it from the body.
            return BuildResult(id, new
            {
                protocolVersion = ProtocolVersion,
                capabilities = new
                {
                    tools = new { listChanged = true },
                    prompts = new { listChanged = true },
                    resources = new { listChanged = true, subscribe = false }
                },
                serverInfo = new { name = ServerName, version = ServerVersion },
                instructions = "",
                meta = new
                {
                    eventSourceUrl = "/?sessionId=" + sessionId,
                    sessionId = sessionId
                }
            });
        }

        #endregion

        #region Handler: tools/list & tools/call

        private object HandleToolsList(object id)
        {
            var toolsList = new List<object>();
            bool debuggerOn = Debugger.IsAttached
                              || (Bridge.DbgIsDebugging() && Bridge.DbgValFromString("$pid") > 0);

            foreach (var kv in _commands)
            {
                string commandName = kv.Key;
                MethodInfo mi = kv.Value;
                var attribute = mi.GetCustomAttribute<CommandAttribute>();
                if (attribute == null) continue;

                // DebugOnly commands hidden when no debugger session present.
                if (attribute.DebugOnly && !debuggerOn) continue;

                var properties = new Dictionary<string, object>();
                var required = new List<string>();
                foreach (var p in mi.GetParameters())
                {
                    properties[p.Name] = new
                    {
                        type = GetJsonSchemaType(p.ParameterType),
                        description = GetParameterDescription(mi, p)
                    };
                    if (!p.IsOptional) required.Add(p.Name);
                }

                string description = !string.IsNullOrEmpty(attribute.MCPCmdDescription)
                                     ? attribute.MCPCmdDescription
                                     : ("Command: " + commandName);

                toolsList.Add(new
                {
                    name = commandName,
                    description = description,
                    inputSchema = new
                    {
                        title = commandName,
                        description = description,
                        type = "object",
                        properties = properties,
                        required = required.ToArray()
                    }
                });
            }

            // Built-in Echo.
            toolsList.Add(new
            {
                name = "Echo",
                description = "Echoes the input back to the client.",
                inputSchema = new
                {
                    title = "Echo",
                    description = "Echoes the input back to the client.",
                    type = "object",
                    properties = new
                    {
                        message = new { type = "string", description = "Message to echo." }
                    },
                    required = new[] { "message" }
                }
            });

            return BuildResult(id, new { tools = toolsList.ToArray() });
        }

        private async Task<object> HandleToolsCallAsync(object id, Dictionary<string, object> request)
        {
            await Task.CompletedTask.ConfigureAwait(false); // keep handler async-shaped

            var paramsObj = request.ContainsKey("params") ? request["params"] as Dictionary<string, object> : null;
            if (paramsObj == null)
            {
                return BuildError(id, -32602, "Invalid params: missing 'params' object.");
            }

            string toolName = paramsObj.ContainsKey("name") ? Convert.ToString(paramsObj["name"]) : null;
            var arguments = paramsObj.ContainsKey("arguments")
                              ? paramsObj["arguments"] as Dictionary<string, object>
                              : new Dictionary<string, object>();

            if (string.IsNullOrEmpty(toolName))
            {
                return BuildError(id, -32602, "Invalid params: missing tool 'name'.");
            }

            arguments = arguments ?? new Dictionary<string, object>();

            // Built-in Echo.
            if (string.Equals(toolName, "Echo", StringComparison.OrdinalIgnoreCase))
            {
                string msg = arguments.ContainsKey("message")
                             ? Convert.ToString(arguments["message"])
                             : "";
                return BuildResult(id, BuildToolContent("hello " + msg, isError: false));
            }

            if (!_commands.TryGetValue(toolName, out var methodInfo))
            {
                return BuildResult(id, BuildToolContent("Command '" + toolName + "' not found", isError: true));
            }

            try
            {
                var parameters = methodInfo.GetParameters();
                var invokeArgs = new object[parameters.Length];
                for (int i = 0; i < parameters.Length; i++)
                {
                    var p = parameters[i];
                    if (arguments.ContainsKey(p.Name))
                    {
                        try
                        {
                            invokeArgs[i] = ConvertArgument(arguments[p.Name], p.ParameterType, p.Name);
                        }
                        catch (Exception ex)
                        {
                            // Catch type mismatches (e.g., LLM sent a dict instead of a string array)
                            string expectedType = p.ParameterType.Name;
                            string receivedType = arguments[p.Name]?.GetType().Name ?? "null";
                            throw new ArgumentException($"Type mismatch for parameter '{p.Name}'. Expected type '{expectedType}', but received '{receivedType}'. Inner error: {ex.Message}");
                        }
                    }
                    else if (p.IsOptional)
                    {
                        invokeArgs[i] = p.DefaultValue;
                    }
                    else
                    {
                        // --- UPDATED ARGUMENT MISMATCH FEEDBACK ---
                        // Extract the names of all parameters this C# method expects
                        string expectedParams = string.Join(", ", parameters.Select(param => param.Name));

                        // Extract the keys of all arguments the LLM actually sent
                        string receivedParams = arguments.Count > 0 ? string.Join(", ", arguments.Keys) : "None";

                        throw new ArgumentException($"Required parameter '{p.Name}' is missing.\nExpected parameters: [{expectedParams}]\nReceived arguments: [{receivedParams}]");
                    }
                }

                var result = methodInfo.Invoke(null, invokeArgs);
                string resultText = result != null
                                    ? result.ToString()
                                    : "Command executed successfully";
                return BuildResult(id, BuildToolContent(resultText, isError: false));
            }
            catch (TargetInvocationException tie)
            {
                string msg = tie.InnerException != null ? tie.InnerException.Message : tie.Message;
                return BuildResult(id, BuildToolContent("Error executing command: " + msg, isError: true));
            }
            catch (Exception ex)
            {
                // Both of our custom ArgumentExceptions will be caught here and sent back to the LLM
                return BuildResult(id, BuildToolContent("Error executing command: " + ex.Message, isError: true));
            }
        }
        private async Task<object> HandleToolsCallAsync_old(object id, Dictionary<string, object> request)
        {
            await Task.CompletedTask.ConfigureAwait(false); // keep handler async-shaped

            var paramsObj = request.ContainsKey("params") ? request["params"] as Dictionary<string, object> : null;
            if (paramsObj == null)
            {
                return BuildError(id, -32602, "Invalid params: missing 'params' object.");
            }

            string toolName = paramsObj.ContainsKey("name") ? Convert.ToString(paramsObj["name"]) : null;
            var arguments = paramsObj.ContainsKey("arguments")
                              ? paramsObj["arguments"] as Dictionary<string, object>
                              : new Dictionary<string, object>();

            if (string.IsNullOrEmpty(toolName))
            {
                return BuildError(id, -32602, "Invalid params: missing tool 'name'.");
            }

            arguments = arguments ?? new Dictionary<string, object>();

            // Built-in Echo.
            if (string.Equals(toolName, "Echo", StringComparison.OrdinalIgnoreCase))
            {
                string msg = arguments.ContainsKey("message")
                             ? Convert.ToString(arguments["message"])
                             : "";
                return BuildResult(id, BuildToolContent("hello " + msg, isError: false));
            }

            if (!_commands.TryGetValue(toolName, out var methodInfo))
            {
                return BuildResult(id, BuildToolContent("Command '" + toolName + "' not found", isError: true));
            }

            try
            {
                var parameters = methodInfo.GetParameters();
                var invokeArgs = new object[parameters.Length];
                for (int i = 0; i < parameters.Length; i++)
                {
                    var p = parameters[i];
                    if (arguments.ContainsKey(p.Name))
                    {
                        invokeArgs[i] = ConvertArgument(arguments[p.Name], p.ParameterType, p.Name);
                    }
                    else if (p.IsOptional)
                    {
                        invokeArgs[i] = p.DefaultValue;
                    }
                    else
                    {
                        throw new ArgumentException("Required parameter '" + p.Name + "' is missing");
                    }
                }

                var result = methodInfo.Invoke(null, invokeArgs);
                string resultText = result != null
                                    ? result.ToString()
                                    : "Command executed successfully";
                return BuildResult(id, BuildToolContent(resultText, isError: false));
            }
            catch (TargetInvocationException tie)
            {
                string msg = tie.InnerException != null ? tie.InnerException.Message : tie.Message;
                return BuildResult(id, BuildToolContent("Error executing command: " + msg, isError: true));
            }
            catch (Exception ex)
            {
                return BuildResult(id, BuildToolContent("Error executing command: " + ex.Message, isError: true));
            }
        }

        private static object BuildToolContent(string text, bool isError)
        {
            return new
            {
                content = new object[]
                {
                    new { type = "text", text = text }
                },
                isError = isError
            };
        }

        #endregion

        #region Handler: prompts (scaffolded; empty by default)

        // Hook these in if/when prompts are needed. Kept as instance state so
        // a future API (e.g. RegisterPrompt) can mutate them after construction.
        private readonly Dictionary<string, object> _prompts =
            new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        private object HandlePromptsList(object id)
        {
            var list = new List<object>();
            foreach (var kv in _prompts) list.Add(kv.Value);
            return BuildResult(id, new { prompts = list.ToArray() });
        }

        private object HandlePromptsGet(object id, Dictionary<string, object> request)
        {
            var paramsObj = request.ContainsKey("params") ? request["params"] as Dictionary<string, object> : null;
            string name = paramsObj != null && paramsObj.ContainsKey("name")
                            ? Convert.ToString(paramsObj["name"])
                            : null;

            if (string.IsNullOrEmpty(name) || !_prompts.ContainsKey(name))
            {
                return BuildError(id, -32601, "Prompt not found: " + (name ?? "<null>"));
            }

            return BuildResult(id, new
            {
                description = "",
                messages = new object[0]
            });
        }

        #endregion

        #region Handler: resources (scaffolded; empty by default)

        private readonly Dictionary<string, object> _resources =
            new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, object> _resourceTemplates =
            new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        private object HandleResourcesList(object id)
        {
            var list = new List<object>();
            foreach (var kv in _resources) list.Add(kv.Value);
            return BuildResult(id, new { resources = list.ToArray() });
        }

        private object HandleResourceTemplatesList(object id)
        {
            var list = new List<object>();
            foreach (var kv in _resourceTemplates) list.Add(kv.Value);
            return BuildResult(id, new { resourceTemplates = list.ToArray() });
        }

        private object HandleResourceRead(object id, Dictionary<string, object> request)
        {
            var paramsObj = request.ContainsKey("params") ? request["params"] as Dictionary<string, object> : null;
            string uri = paramsObj != null && paramsObj.ContainsKey("uri")
                            ? Convert.ToString(paramsObj["uri"])
                            : null;

            if (string.IsNullOrEmpty(uri))
            {
                return BuildError(id, -32602, "Invalid params: missing 'uri'.");
            }

            return BuildResult(id, new
            {
                contents = new object[]
                {
                    new
                    {
                        uri      = uri,
                        mimeType = "text/plain",
                        text     = ""   // populate when a real resource provider is wired in
                    }
                }
            });
        }

        #endregion

        #region Response writers

        private static async Task WriteJsonAsync(HttpListenerResponse res, int status, object body)
        {
            string json = _json.Serialize(body);
            byte[] bytes = Encoding.UTF8.GetBytes(json);

            res.StatusCode = status;
            res.ContentType = "application/json; charset=utf-8";
            res.ContentLength64 = bytes.Length;

            try
            {
                await res.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                await res.OutputStream.FlushAsync().ConfigureAwait(false);
            }
            catch { /* client gone */ }
            finally
            {
                try { res.Close(); } catch { /* ignored */ }
            }
        }

        private static async Task WriteSingleSseResponseAsync(HttpListenerContext ctx, object body)
        {
            var res = ctx.Response;

            res.StatusCode = 200;
            res.ContentType = "text/event-stream";
            res.SendChunked = true;
            res.KeepAlive = false; // single-shot stream — close after one frame
            res.Headers["Cache-Control"] = "no-cache, no-store";
            res.Headers["X-Accel-Buffering"] = "no";

            try
            {
                using (var writer = new StreamWriter(res.OutputStream, new UTF8Encoding(false)) { NewLine = "\n" })
                {
                    string payload = _json.Serialize(body);
                    await writer.WriteAsync("event: message\n").ConfigureAwait(false);
                    await writer.WriteAsync("data: ").ConfigureAwait(false);
                    await writer.WriteAsync(payload).ConfigureAwait(false);
                    await writer.WriteAsync("\n\n").ConfigureAwait(false);
                    await writer.FlushAsync().ConfigureAwait(false);
                }
            }
            catch { /* client gone */ }
            finally
            {
                try { res.Close(); } catch { /* ignored */ }
            }
        }

        private static async Task WriteJsonRpcErrorOnRequestAsync(HttpListenerContext ctx,
                                                                  object id,
                                                                  int code,
                                                                  string message)
        {
            var body = BuildError(id, code, message);
            await WriteJsonAsync(ctx.Response, 200, body).ConfigureAwait(false);
        }

        private static object BuildResult(object id, object result)
        {
            return new { jsonrpc = "2.0", id = id, result = result };
        }

        private static object BuildError(object id, int code, string message)
        {
            return new
            {
                jsonrpc = "2.0",
                id = id,
                error = new { code = code, message = message }
            };
        }

        #endregion

        #region Schema / parameter helpers

        private static string GetJsonSchemaType(Type type)
        {
            if (type == null) return "null";

            // Unwrap Nullable<T>.
            var underlying = Nullable.GetUnderlyingType(type);
            if (underlying != null) type = underlying;

            if (type == typeof(string) || type == typeof(Guid) || type.IsEnum) return "string";
            if (type == typeof(int) || type == typeof(long) || type == typeof(short)
                || type == typeof(byte) || type == typeof(uint) || type == typeof(ulong)
                || type == typeof(ushort) || type == typeof(sbyte)) return "integer";
            if (type == typeof(float) || type == typeof(double) || type == typeof(decimal)) return "number";
            if (type == typeof(bool)) return "boolean";
            if (type == typeof(DateTime) || type == typeof(DateTimeOffset)) return "string";
            if (type.IsArray || typeof(IEnumerable).IsAssignableFrom(type)) return "array";
            return "object";
        }

        private static string GetParameterDescription(MethodInfo method, ParameterInfo param)
        {
            // 1. Parameter-level [McpParam("...")]
            var pAttr = param.GetCustomAttribute<McpParamAttribute>(inherit: true);
            if (pAttr != null && !string.IsNullOrWhiteSpace(pAttr.Description))
                return pAttr.Description;

            // 2. Method-level [McpParam("paramName","...")]
            foreach (var mAttr in method.GetCustomAttributes<McpParamAttribute>(inherit: true))
            {
                if (!string.IsNullOrEmpty(mAttr.Name)
                    && string.Equals(mAttr.Name, param.Name, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(mAttr.Description))
                {
                    return mAttr.Description;
                }
            }

            // 3. Name-based fallback (preserves legacy hardcoded hints).
            switch (param.Name)
            {
                case "address": return "Address to target with function (Example format: 0x12345678)";
                case "value": return "Value to pass to command (Example format: 100)";
                case "byteCount": return "Count of how many bytes to request for (Example format: 100)";
                case "pfilepath": return "File path (Example format: C:\\output.txt)";
                case "mode": return "mode=[Comment | Label] (Example format: mode=Comment)";
                case "byteString": return "Writes the provided Hex bytes (Example format: byteString=00 90 0F)";
                default: return "Parameter for " + method.Name;
            }
        }

        private static object ConvertArgument(object argValue, Type targetType, string paramName)
        {
            if (argValue == null)
            {
                if (!targetType.IsValueType || Nullable.GetUnderlyingType(targetType) != null)
                    return null;
                throw new ArgumentNullException(paramName,
                    "Null provided for non-nullable parameter '" + paramName + "'");
            }

            if (targetType.IsInstanceOfType(argValue)) return argValue;

            // Array conversion (e.g. string[]).
            if (targetType.IsArray)
            {
                var list = argValue as IList;
                if (list == null)
                {
                    throw new ArgumentException("Parameter '" + paramName + "' should be an array");
                }

                var elementType = targetType.GetElementType();
                var typed = Array.CreateInstance(elementType, list.Count);
                for (int i = 0; i < list.Count; i++)
                {
                    var element = list[i];
                    object converted;
                    if (element == null)
                    {
                        converted = elementType.IsValueType
                                    ? Activator.CreateInstance(elementType)
                                    : null;
                    }
                    else
                    {
                        converted = Convert.ChangeType(element, elementType, CultureInfo.InvariantCulture);
                    }
                    typed.SetValue(converted, i);
                }
                return typed;
            }

            // Common scalar conversions.
            try
            {
                if (targetType.IsEnum)
                {
                    return Enum.Parse(targetType, argValue.ToString(), ignoreCase: true);
                }
                if (targetType == typeof(Guid))
                {
                    return Guid.Parse(argValue.ToString());
                }
                return Convert.ChangeType(argValue, targetType, CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                throw new ArgumentException(
                    "Cannot convert parameter '" + paramName + "' to type "
                    + targetType.Name + ": " + ex.Message, ex);
            }
        }

        #endregion

        #region Utilities

        private static string GenerateSessionId()
        {
            // 128-bit URL-safe random identifier.
            using (var rng = RandomNumberGenerator.Create())
            {
                var bytes = new byte[16];
                rng.GetBytes(bytes);
                return Convert.ToBase64String(bytes)
                              .TrimEnd('=')
                              .Replace('+', '-')
                              .Replace('/', '_');
            }
        }

        #endregion
    }
}
