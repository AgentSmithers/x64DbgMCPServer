using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using DotNetPlugin.NativeBindings.SDK;

namespace DotNetPlugin
{

    public enum CommandCategory
    {
        GeneralPurpose,
        DebugControl,
        BreakpointControl,
        ConditionalBreakpointControl,
        Tracing,
        ThreadControl,
        MemoryOperations,
        OperatingSystemControl,
        WatchControl,
        Variables,
        Searching,
        UserDatabase,
        Analysis,
        Types,
        Plugins,
        ScriptCommands,
        GUI,
        Miscellaneous,
        DebugFunctions
    }





    #region CommandTargets

    /// <summary>
    /// Where a command is surfaced. Replaces the MCPOnly / X64DbgOnly bool pair,
    /// which permitted the nonsensical "visible nowhere" state.
    /// </summary>
    [Flags]
    public enum CommandTargets
    {
        None = 0,
        X64Dbg = 1 << 0,
        Mcp = 1 << 1,
        All = X64Dbg | Mcp
    }

    #endregion

    #region CommandAttribute

    /// <summary>
    /// Marks a static method as a registrable command (x64dbg and/or MCP tool).
    /// AllowMultiple = true is intentional: it enables command aliases
    /// ([Command("refstr")] [Command("strref")] on one method).
    /// Inherited = false is correct: registration reflects over concrete static
    /// methods, so attribute inheritance never participates.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
    public sealed class CommandAttribute : Attribute
    {
        // ---- identity -------------------------------------------------------

        /// <summary>Command/tool name. Null => engine falls back to method name.</summary>
        public string Name { get; }

        /// <summary>Optional human-readable title (MCP `title` field).</summary>
        public string Title { get; set; }

        /// <summary>
        /// Tool description (MCP `description` field). Renamed from
        /// MCPCmdDescription to match spec terminology.
        /// </summary>
        public string Description { get; set; }

        /// <summary>Back-compat alias. Existing [Command(MCPCmdDescription = ...)] call sites keep compiling.</summary>
        [Obsolete("Use Description instead.")]
        public string MCPCmdDescription
        {
            get => Description;
            set => Description = value;
        }

        // ---- visibility / routing -------------------------------------------

        public CommandCategory Category { get; set; } = CommandCategory.GeneralPurpose;

        /// <summary>Hidden from tools/list while no debug session is active.</summary>
        public bool DebugOnly { get; set; }

        /// <summary>Which surfaces register this command. Defaults to both.</summary>
        public CommandTargets Targets { get; set; } = CommandTargets.All;

        [Obsolete("Use Targets = CommandTargets.Mcp instead.")]
        public bool MCPOnly
        {
            get => Targets == CommandTargets.Mcp;
            set { if (value) Targets = CommandTargets.Mcp; }
        }

        [Obsolete("Use Targets = CommandTargets.X64Dbg instead.")]
        public bool X64DbgOnly
        {
            get => Targets == CommandTargets.X64Dbg;
            set { if (value) Targets = CommandTargets.X64Dbg; }
        }

        // ---- MCP tool annotations (spec: ToolAnnotations) --------------------
        // Nullable backing fields let the engine distinguish "developer said
        // false" from "developer said nothing" (attributes cannot expose bool?).

        private bool? _readOnlyHint;
        private bool? _destructiveHint;
        private bool? _idempotentHint;
        private bool? _openWorldHint;

        /// <summary>Tool does not modify its environment (e.g. GetCallStack).</summary>
        public bool ReadOnlyHint { get => _readOnlyHint ?? false; set => _readOnlyHint = value; }
        public bool ReadOnlyHintSpecified => _readOnlyHint.HasValue;

        /// <summary>Tool may perform destructive updates (e.g. WriteMemToAddress).</summary>
        public bool DestructiveHint { get => _destructiveHint ?? true; set => _destructiveHint = value; }
        public bool DestructiveHintSpecified => _destructiveHint.HasValue;

        /// <summary>Calling twice with the same args has no additional effect.</summary>
        public bool IdempotentHint { get => _idempotentHint ?? false; set => _idempotentHint = value; }
        public bool IdempotentHintSpecified => _idempotentHint.HasValue;

        /// <summary>Tool interacts with an open world (network etc.). Debugger tools are closed-world.</summary>
        public bool OpenWorldHint { get => _openWorldHint ?? true; set => _openWorldHint = value; }
        public bool OpenWorldHintSpecified => _openWorldHint.HasValue;

        public bool AnyAnnotationSpecified =>
            ReadOnlyHintSpecified || DestructiveHintSpecified ||
            IdempotentHintSpecified || OpenWorldHintSpecified;

        // ---- ctors ------------------------------------------------------------

        public CommandAttribute() { }

        public CommandAttribute(string name)
        {
            Name = name;
        }
    }

    #endregion

    #region McpParamAttribute

    /// <summary>
    /// Optional attribute for describing MCP tool parameters in the
    /// <c>tools/list</c> JSON schema. May be applied directly to the
    /// parameter (description only) or to the method (name + description)
    /// when the parameter itself cannot be annotated.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Method,
                    AllowMultiple = true, Inherited = true)]
    public sealed class McpParamAttribute1 : Attribute
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
        public McpParamAttribute1(string description)
        {
            Description = description;
        }

        /// <summary>Method-level constructor (specifies which param it describes).</summary>
        public McpParamAttribute1(string name, string description)
        {
            Name = name;
            Description = description;
        }
    }

    #endregion

    #region McpParamAttribute

    /// <summary>
    /// Describes one tool parameter for the generated JSON Schema.
    ///
    /// Canonical usage is parameter-level:
    ///     public static string Foo([McpParam("Start address", Pattern = HexPattern)] string address)
    ///
    /// Method-level usage ([McpParam("address", "Start address")]) exists as a
    /// fallback for generated/partial code; the engine validates the Name
    /// against real parameter names at registration and logs a hard error on
    /// mismatch, so renames cannot silently orphan documentation.
    ///
    /// Inherited = false: the runtime does not meaningfully honor attribute
    /// inheritance on ParameterInfo, so advertising `true` is misleading.
    /// AllowMultiple = true is required only for the method-level form.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Method,
                    AllowMultiple = true, Inherited = false)]
    public sealed class McpParamAttribute : Attribute
    {
        /// <summary>Common reusable pattern for hex addresses ("0x1400010A0" or "1400010A0").</summary>
        public const string HexAddressPattern = "^(0x)?[0-9A-Fa-f]+$";

        // ---- identity ---------------------------------------------------------

        /// <summary>Target parameter name (method-level usage only; null at parameter level).</summary>
        public string Name { get; }

        /// <summary>Human-readable description (MCP/JSON Schema `description`).</summary>
        public string Description { get; }

        // ---- schema typing ----------------------------------------------------

        /// <summary>
        /// OVERRIDE ONLY. The engine infers the JSON Schema type from the CLR
        /// parameter type; set this only when the C# signature does not reflect
        /// semantic intent. Renamed from JsonType to match the schema keyword.
        /// </summary>
        public string Type { get; set; }

        // ---- required-ness ----------------------------------------------------
        // Source of truth is ParameterInfo.IsOptional. The attribute may only
        // PROMOTE an optional parameter to advertised-required; it can never
        // demote a non-optional parameter (that would let the LLM omit an
        // argument the invoker will throw on).

        private bool? _required;
        public bool Required { get => _required ?? true; set => _required = value; }
        public bool RequiredSpecified => _required.HasValue;

        // ---- visibility -------------------------------------------------------

        /// <summary>
        /// Excluded from the generated schema entirely. The parameter MUST be
        /// optional (engine validates at registration); its default value is
        /// used on every invocation. Useful for diagnostic/plumbing parameters.
        /// </summary>
        public bool Hidden { get; set; }

        // ---- constraints (emitted only when set) -------------------------------

        /// <summary>Closed set of allowed values (JSON Schema `enum`). Prefer a C# enum type when possible — the engine emits enums automatically for those.</summary>
        public string[] EnumValues { get; set; }

        /// <summary>Regex constraint for string params (JSON Schema `pattern`).</summary>
        public string Pattern { get; set; }

        /// <summary>Semantic format hint (JSON Schema `format`, e.g. "uri", "date-time").</summary>
        public string Format { get; set; }

        /// <summary>Numeric lower bound. NaN sentinel = unset (attributes cannot expose double?).</summary>
        public double Minimum { get; set; }
        public bool MinimumSpecified => !double.IsNaN(Minimum);

        /// <summary>Numeric upper bound. NaN sentinel = unset.</summary>
        public double Maximum { get; set; }
        public bool MaximumSpecified => !double.IsNaN(Maximum);

        /// <summary>String length bounds. -1 sentinel = unset.</summary>
        public int MinLength { get; set; } = -1;
        public int MaxLength { get; set; } = -1;

        // ---- LLM accuracy boosters ---------------------------------------------

        /// <summary>
        /// Default value advertised in the schema (`default`). Note: the CLR
        /// default from the signature is picked up automatically; set this only
        /// to advertise something different from (or in addition to) it.
        /// </summary>
        public object DefaultValue { get; set; }
        public bool DefaultValueSpecified => DefaultValue != null;

        /// <summary>Example values (`examples`). LLMs imitate these heavily — make them realistic.</summary>
        public string[] Examples { get; set; }

        // ---- ctors --------------------------------------------------------------

        /// <summary>Parameter-level usage: [McpParam("description")].</summary>
        public McpParamAttribute(string description)
        {
            Description = description;
        }

        /// <summary>Method-level usage: [McpParam("paramName", "description")].</summary>
        public McpParamAttribute(string name, string description)
        {
            Name = name;
            Description = description;
        }
    }

    #endregion

    #region McpSchemaBuilder (reflection engine)

    /// <summary>
    /// Builds MCP tool definitions from reflected methods. Run ONCE at server
    /// startup and cache the result — tools/list must never reflect per request.
    /// </summary>
    public static class McpSchemaBuilder
    {
        /// <summary>
        /// Resolves the effective McpParam metadata for a parameter:
        /// parameter-level attribute wins; method-level attribute with matching
        /// Name is the fallback. Logs (or throws in DEBUG) on dangling
        /// method-level names so renames fail fast instead of silently.
        /// </summary>
        public static McpParamAttribute ResolveParamAttribute(MethodInfo method, ParameterInfo param)
        {
            var direct = param.GetCustomAttribute<McpParamAttribute>();
            if (direct != null) return direct;

            return method.GetCustomAttributes<McpParamAttribute>()
                         .FirstOrDefault(a => a.Name != null &&
                              string.Equals(a.Name, param.Name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Validates method-level McpParam names against real parameters.
        /// Call during registration; surface failures loudly.
        /// </summary>
        public static IEnumerable<string> ValidateMethodLevelParamNames(MethodInfo method)
        {
            var paramNames = new HashSet<string>(
                method.GetParameters().Select(p => p.Name),
                StringComparer.OrdinalIgnoreCase);

            foreach (var attr in method.GetCustomAttributes<McpParamAttribute>())
            {
                if (attr.Name == null)
                    yield return $"[{method.Name}] Method-level McpParam requires a Name.";
                else if (!paramNames.Contains(attr.Name))
                    yield return $"[{method.Name}] McpParam targets unknown parameter '{attr.Name}'.";
            }
        }

        /// <summary>
        /// Builds the JSON Schema property object for one parameter.
        /// Precedence: attribute override &gt; CLR type inference.
        /// </summary>
        public static Dictionary<string, object> BuildPropertySchema(
            MethodInfo method, ParameterInfo param, McpParamAttribute attr)
        {
            var clrType = Nullable.GetUnderlyingType(param.ParameterType) ?? param.ParameterType;
            var schema = new Dictionary<string, object>();

            // 1. type — infer, allow explicit override.
            schema["type"] = attr?.Type ?? InferJsonType(clrType);

            // 2. enum — C# enums are emitted automatically; string EnumValues override.
            if (attr?.EnumValues != null) schema["enum"] = attr.EnumValues;
            else if (clrType.IsEnum) schema["enum"] = Enum.GetNames(clrType);

            // 3. array items — never emit a bare "array".
            if (clrType.IsArray)
            {
                schema["type"] = "array";
                schema["items"] = new Dictionary<string, object>
                {
                    ["type"] = InferJsonType(clrType.GetElementType())
                };
            }

            // 4. constraints (only when explicitly set).
            if (attr != null)
            {
                if (attr.Pattern != null) schema["pattern"] = attr.Pattern;
                if (attr.Format != null) schema["format"] = attr.Format;
                if (attr.MinimumSpecified) schema["minimum"] = attr.Minimum;
                if (attr.MaximumSpecified) schema["maximum"] = attr.Maximum;
                if (attr.MinLength >= 0) schema["minLength"] = attr.MinLength;
                if (attr.MaxLength >= 0) schema["maxLength"] = attr.MaxLength;
                if (attr.Examples != null) schema["examples"] = attr.Examples;
            }

            // 5. default — CLR optional default first, attribute override second.
            if (attr?.DefaultValueSpecified == true)
                schema["default"] = attr.DefaultValue;
            else if (param.IsOptional && param.DefaultValue != null && param.DefaultValue != DBNull.Value)
                schema["default"] = param.DefaultValue;

            // 6. description — append machine-readable hints into the prose,
            //    because some MCP clients only surface `description` to the model.
            schema["description"] = ComposeDescription(method, param, attr, schema);

            return schema;
        }

        /// <summary>
        /// True if the parameter must appear in the schema's `required` array.
        /// Reflection is the source of truth; the attribute may only promote.
        /// </summary>
        public static bool IsRequired(ParameterInfo param, McpParamAttribute attr)
        {
            if (!param.IsOptional) return true;                       // signature wins
            if (attr?.RequiredSpecified == true) return attr.Required; // promotion only
            return false;
        }

        private static string ComposeDescription(
            MethodInfo method, ParameterInfo param,
            McpParamAttribute attr, IReadOnlyDictionary<string, object> schema)
        {
            string baseDesc = attr?.Description;
            if (string.IsNullOrWhiteSpace(baseDesc))
            {
                // Fail loudly in dev rather than emitting a plausible-looking stub.
                System.Diagnostics.Debug.Fail(
                    $"Missing McpParam description: {method.Name}({param.Name})");
                baseDesc = "Parameter '" + param.Name + "'.";
            }

            var sb = new System.Text.StringBuilder(baseDesc.TrimEnd());
            if (schema.TryGetValue("enum", out var ev) && ev is string[] names)
                sb.Append(" Allowed values: ").Append(string.Join(", ", names)).Append('.');
            if (schema.TryGetValue("default", out var def))
                sb.Append(" Defaults to ").Append(def).Append('.');
            if (attr?.Examples != null && attr.Examples.Length > 0)
                sb.Append(" Example: ").Append(attr.Examples[0]);
            return sb.ToString();
        }

        private static string InferJsonType(Type t)
        {
            if (t == null) return "string";
            t = Nullable.GetUnderlyingType(t) ?? t;

            if (t == typeof(string) || t == typeof(Guid) || t == typeof(char)
                || t == typeof(DateTime) || t == typeof(DateTimeOffset) || t.IsEnum)
                return "string";
            if (t == typeof(bool)) return "boolean";
            if (t == typeof(float) || t == typeof(double) || t == typeof(decimal))
                return "number";
            if (t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte)
                || t == typeof(uint) || t == typeof(ulong) || t == typeof(ushort) || t == typeof(sbyte))
                return "integer";
            if (t.IsArray) return "array";
            return "object";
        }
    }

    #endregion

    /*
    /// <summary>
    /// Attribute for automatically registering commands in x64Dbg.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
    public class CommandAttribute : Attribute
    {
        public string Name { get; }
        public CommandCategory Category { get; set; } = CommandCategory.GeneralPurpose;

        public bool DebugOnly { get; set; } //Command is only visual during an active debug session of a binary.
        public bool MCPOnly { get; set; } //Used so it is not registered as an X64Dbg Command
        public bool X64DbgOnly { get; set; } //Used so it is not registerd with MCP
        public string MCPCmdDescription { get; set; }

        public CommandAttribute() { }

        public CommandAttribute(string name)
        {
            Name = name;
        }
    }
    */

    internal static class Commands
    {
        private static Plugins.CBPLUGINCOMMAND BuildCallback(PluginBase plugin, MethodInfo method, bool reportsSuccess)
        {
            object firstArg = method.IsStatic ? null : plugin;

            if (reportsSuccess)
            {
                return (Plugins.CBPLUGINCOMMAND)Delegate.CreateDelegate(typeof(Plugins.CBPLUGINCOMMAND), firstArg, method, throwOnBindFailure: true);
            }
            else
            {
                var callback = (Action<string[]>)Delegate.CreateDelegate(typeof(Action<string[]>), firstArg, method, throwOnBindFailure: true);
                return args =>
                {
                    callback(args);
                    return true;
                };
            }
        }

        public static IDisposable Initialize(PluginBase plugin, MethodInfo[] pluginMethods)
        {
            // command names are case-insensitive
            var registeredNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var methods = pluginMethods
                .SelectMany(method => method.GetCustomAttributes<CommandAttribute>().Select(attribute => (method, attribute)));

            foreach (var (method, attribute) in methods)
            {
                var name = attribute.Name ?? method.Name;

                if (attribute.MCPOnly)
                {
                    continue; //Use only for MCPServer remote invokation
                }

                var returnType = method.ReturnType;
                var reportsSuccess = returnType == typeof(bool) || returnType.FullName == typeof(bool).FullName;

                // Check against string and void using FullName to bypass context boundary issues
                bool isVoid = returnType == typeof(void) || returnType.FullName == typeof(void).FullName;
                bool isString = returnType == typeof(string) || returnType.FullName == typeof(string).FullName;

                if (!reportsSuccess && !isVoid && !isString)
                {
                    PluginBase.LogError($"Registration of command '{name}' is skipped. Method '{method.Name}' has an invalid return type.");
                    continue;
                }

                var methodParams = method.GetParameters();

                if (methodParams.Length != 1 || methodParams[0].ParameterType != typeof(string[]))
                {
                    PluginBase.LogError($"Registration of command '{name}' is skipped. Method '{method.Name}' has an invalid signature.");
                    continue;
                }

                if (registeredNames.Contains(name) ||
                    !Plugins._plugin_registercommand(plugin.PluginHandle, name, BuildCallback(plugin, method, reportsSuccess), attribute.DebugOnly))
                {
                    PluginBase.LogError($"Registration of command '{name}' failed.");
                    continue;
                }

                registeredNames.Add(name);
            }

            return new Registrations(plugin, registeredNames);
        }

        private sealed class Registrations : IDisposable
        {
            private PluginBase _plugin;
            private HashSet<string> _registeredNames;

            public Registrations(PluginBase plugin, HashSet<string> registeredNames)
            {
                _plugin = plugin;
                _registeredNames = registeredNames;
            }

            public void Dispose()
            {
                var plugin = Interlocked.Exchange(ref _plugin, null);

                if (plugin != null)
                {
                    foreach (var name in _registeredNames)
                    {
                        if (!Plugins._plugin_unregistercommand(plugin.PluginHandle, name))
                            PluginBase.LogError($"Unregistration of command '{name}' failed.");
                    }

                    _registeredNames = null;
                }
            }
        }
    }
}
