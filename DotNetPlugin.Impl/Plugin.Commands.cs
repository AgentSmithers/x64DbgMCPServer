using DotNetPlugin.NativeBindings;
using DotNetPlugin.NativeBindings.Script;
using DotNetPlugin.NativeBindings.SDK;
using DotNetPlugin.Properties;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using static DotNetPlugin.NativeBindings.SDK.Bridge;
using static DotNetPlugin.NativeBindings.SDK.BridgeBase;

namespace DotNetPlugin
{
    partial class Plugin
    {
        //[Command("DotNetpluginTestCommand")]
        public static void cbNetTestCommand(string[] args)
        {
            Console.WriteLine(".Net test command!");
            string empty = string.Empty;
            string Left = Interaction.InputBox("Enter value pls", "NetTest", "", -1, -1);
            if (Left == null | Operators.CompareString(Left, "", false) == 0)
                Console.WriteLine("cancel pressed!");
            else
                Console.WriteLine($"line: {Left}");
        }

        //[Command("DotNetDumpProcess", DebugOnly = true)]
        public static bool cbDumpProcessCommand(string[] args)
        {
            var addr = args.Length >= 2 ? Bridge.DbgValFromString(args[1]) : Bridge.DbgValFromString("cip");
            Console.WriteLine($"addr: {addr.ToPtrString()}");
            var modinfo = new Module.ModuleInfo();
            if (!Module.InfoFromAddr(addr, ref modinfo))
            {
                Console.Error.WriteLine($"Module.InfoFromAddr failed...");
                return false;
            }
            Console.WriteLine($"InfoFromAddr success, base: {modinfo.@base.ToPtrString()}");
            var hProcess = Bridge.DbgValFromString("$hProcess");
            var saveFileDialog = new SaveFileDialog
            {
                Filter = "Executables (*.dll,*.exe)|*.exe|All Files (*.*)|*.*",
                RestoreDirectory = true,
                FileName = modinfo.name
            };
            using (saveFileDialog)
            {
                var result = DialogResult.Cancel;
                var t = new Thread(() => result = saveFileDialog.ShowDialog());
                t.SetApartmentState(ApartmentState.STA);
                t.Start();
                t.Join();
                if (result == DialogResult.OK)
                {
                    string fileName = saveFileDialog.FileName;
                    if (!TitanEngine.DumpProcess((nint)hProcess, (nint)modinfo.@base, fileName, addr))
                    {
                        Console.Error.WriteLine($"DumpProcess failed...");
                        return false;
                    }
                    Console.WriteLine($"Dumping done!");
                }
            }
            return true;
        }

        //[Command("DotNetModuleEnum", DebugOnly = true)]
        public static void cbModuleEnum(string[] args)
        {
            foreach (var mod in Module.GetList())
            {
                Console.WriteLine($"{mod.@base.ToPtrString()} {mod.name}");
                foreach (var section in Module.SectionListFromAddr(mod.@base))
                    Console.WriteLine($"    {section.addr.ToPtrString()} \"{section.name}\"");
            }
        }

        static SimpleMcpServer GSimpleMcpServer;
        static McpServerConfig GMcpServerConfig;

        // Server lifecycle is driven from the x64dbg command line only; it is
        // intentionally NOT exposed over MCP (X64DbgOnly) — an agent must not
        // be able to start or stop the transport it is connected through.
        // Keeps the string[] signature required by x64dbg command registration.
        [Command("StartMCPServer", DebugOnly = false, X64DbgOnly = true, Category = CommandCategory.GeneralPurpose)]
        public static void cbStartMCPServer(string[] args)
        {
            Console.WriteLine("Starting MCPServer");
            GMcpServerConfig = McpServerConfig.Load();
            GSimpleMcpServer = new SimpleMcpServer(typeof(DotNetPlugin.Plugin), GMcpServerConfig);
            GSimpleMcpServer.Start();
            Console.WriteLine("MCPServer Started");
            Console.WriteLine($"MCP Server URL: {GMcpServerConfig.GetDisplayUrl()}");
        }

        [Command("StopMCPServer", DebugOnly = false, X64DbgOnly = true, Category = CommandCategory.GeneralPurpose)]
        public static void cbStopMCPServer(string[] args)
        {
            Console.WriteLine("Stopping MCPServer");
            GSimpleMcpServer.Stop();
            GSimpleMcpServer = null;
            Console.WriteLine("MCPServer Stopped");
        }

        /// <summary>
        /// Gets the current MCP server configuration.
        /// </summary>
        public static McpServerConfig GetMcpServerConfig()
        {
            if (GMcpServerConfig == null)
                GMcpServerConfig = McpServerConfig.Load();
            return GMcpServerConfig;
        }

        /// <summary>
        /// Sets and saves the MCP server configuration.
        /// </summary>
        public static void SetMcpServerConfig(McpServerConfig config)
        {
            GMcpServerConfig = config;
            config.Save();
        }

        /// <summary>
        /// Executes a debugger command synchronously using x64dbg's command engine.
        ///
        /// This function wraps the native `DbgCmdExecDirect` API to simplify command execution.
        /// It blocks until the command has finished executing.
        ///
        /// Examples:
        ///   ExecuteDebuggerCommand("init C:\Path\To\Program.exe");   // Loads an executable
        ///   ExecuteDebuggerCommand("stop");                          // Restarts the current debugging session
        ///   ExecuteDebuggerCommand("run");                              // Starts execution
        /// </summary>
        /// <param name="command">The debugger command string to execute.</param>
        /// <returns>True if the command executed successfully, false otherwise.</returns>
        /*
        [Command("ExecuteDebuggerCommand", DebugOnly = false, MCPOnly = true, MCPCmdDescription = "Example: ExecuteDebuggerCommand command=init c:\\Path\\To\\Program.exe\r\nNote: See ListDebuggerCommands for list of applicable commands.")]
        public static bool ExecuteDebuggerCommand(string command)
        {
            Console.WriteLine("Executing DebuggerCommand: " + command);
            
            // Special handling for potentially problematic commands
            if (command.Trim().ToLower() == "bplist")
            {
                return ExecuteBpListSafely();
            }
            
            return DbgCmdExec(command);
        }
        */

        private static bool ExecuteBpListSafely()
        {
            try
            {
                Console.WriteLine("Executing bplist with architecture-specific safety checks...");

                // Check if debugger is in a valid state
                if (!Bridge.DbgIsDebugging())
                {
                    Console.WriteLine("Debugger is not actively debugging, skipping bplist");
                    return false;
                }

                // Try to get process ID first to ensure we have a valid process
                var pid = Bridge.DbgValFromString("$pid");
                if (pid == 0)
                {
                    Console.WriteLine("No valid process ID, skipping bplist");
                    return false;
                }

                // Detect architecture at runtime
                bool isX64 = IsRunningInX64Dbg();
                Console.WriteLine($"Detected architecture: {(isX64 ? "x64dbg" : "x32dbg")}, Process ID: {pid}");

                if (isX64)
                {
                    // x64dbg - use direct bplist (usually works fine)
                    Console.WriteLine("Using direct bplist for x64dbg...");
                    var result = DbgCmdExec("bplist");
                    Console.WriteLine($"bplist result: {result}");
                    return result;
                }
                else
                {
                    // x32dbg - use safer approach with log redirection
                    Console.WriteLine("Using log redirection approach for x32dbg...");
                    return ExecuteBpListForX32();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error executing bplist safely: {ex.Message}");
                return false;
            }
        }

        [Command("SearchForStrings", DebugOnly = false, MCPOnly = true, Category = CommandCategory.Searching,
MCPCmdDescription = "Searches process memory for a specific text string and returns the addresses where it occurs.")]
        // Notice how we define exact parameter names here instead of an array!
        public static string ExecuteSearchForStrings(
            [McpParam("The literal text to search for in the target process memory.",
                Examples = new[] { "Invalid License", "kernel32.dll" })]
            string searchText,
            [McpParam("Character encoding of the search string.",
                EnumValues = new[] { "ascii", "utf16" })]
            string encodingType,
            [McpParam("Hex address to begin the search from. Defaults to '0' to scan from the lowest mapped address.",
                Pattern = McpParamAttribute.HexAddressPattern, Required = false,
                Examples = new[] { "0", "0x7FF6B2000000" })]
            string startAddress = "0")
        {
            Console.WriteLine("----------------------------------------");
            Console.WriteLine($"METHOD: {nameof(ExecuteSearchForStrings)}");
            Console.WriteLine($"  - {nameof(searchText)}   : {searchText}");
            Console.WriteLine($"  - {nameof(encodingType)} : {encodingType}");
            Console.WriteLine($"  - {nameof(startAddress)} : {startAddress}");
            Console.WriteLine("----------------------------------------");
            try
            {
                encodingType = encodingType.ToLower();

                byte[] stringBytes;
                if (encodingType == "ascii")
                    stringBytes = System.Text.Encoding.ASCII.GetBytes(searchText);
                else if (encodingType == "utf16")
                    stringBytes = System.Text.Encoding.Unicode.GetBytes(searchText);
                else
                    return "Error: encodingType must be 'ascii' or 'utf16'.";

                string hexPattern = BitConverter.ToString(stringBytes).Replace("-", "");
                string commandString = $"findallmem {startAddress}, {hexPattern}, -1";

                bool success = Bridge.DbgCmdExecDirect(commandString);
                if (!success) return $"Error: x64dbg rejected the search. Command sent: {commandString}";

                nuint count = Bridge.DbgValFromString("ref.count()");
                if (count == 0) return $"0 occurrences found for '{searchText}'.";

                List<string> foundAddresses = new List<string>();
                for (nuint i = 0; i < count; i++)
                {
                    nuint address = Bridge.DbgValFromString($"ref.addr({i})");

                    if (address == 0) continue;

                    foundAddresses.Add($"0x{address:X}");
                    if (i >= 100)
                    {
                        foundAddresses.Add("... [Truncated]");
                        break;
                    }
                }

                return $"Found {count} occurrences of '{searchText}' at:\n" + string.Join("\n", foundAddresses);
            }
            catch (Exception e)
            {
                return $"Exception occurred: {e.Message}";
            }
        }

        [Command("FindAllMem", DebugOnly = false, MCPOnly = true, Category = CommandCategory.Searching,
 MCPCmdDescription = "Searches memory for a specific hex byte pattern (supports wildcards) and returns matching addresses.")]
        public static string ExecuteFindAllMem(
            [McpParam("Hex address to start searching from.",
                Pattern = McpParamAttribute.HexAddressPattern,
                Examples = new[] { "0x7FF6B2000000" })]
            string startAddress,
            [McpParam("Byte pattern in hex. Use '?' as a nibble/byte wildcard. Spaces are stripped automatically.",
                Examples = new[] { "EB0?90??8D", "48 8B 05" })]
            string bytePattern,
            [McpParam("Number of bytes to search. Use '-1' to search the entire memory map.",
                Required = false, Examples = new[] { "-1", "0x1000" })]
            string searchSize = "-1",
            [McpParam("Optional region filter.", Required = false,
                EnumValues = new[] { "user", "system", "module" })]
            string moduleFilter = "")
        {
            Console.WriteLine("----------------------------------------");
            Console.WriteLine($"METHOD: {nameof(ExecuteFindAllMem)}");
            Console.WriteLine($"  - {nameof(startAddress)}  : {startAddress}");
            Console.WriteLine($"  - {nameof(bytePattern)}   : {bytePattern}");
            Console.WriteLine($"  - {nameof(searchSize)}    : {searchSize}");
            Console.WriteLine($"  - {nameof(moduleFilter)}  : {moduleFilter}");
            Console.WriteLine("----------------------------------------");
            try
            {
                // 1. Sanitize the byte pattern (x64dbg command line usually chokes on spaces in hex strings)
                bytePattern = bytePattern.Replace(" ", "");

                // 2. Construct the command string dynamically based on provided arguments
                string commandString = $"findallmem {startAddress}, {bytePattern}, {searchSize}";

                // Append the module filter if the LLM provided one
                if (!string.IsNullOrWhiteSpace(moduleFilter))
                {
                    commandString += $", {moduleFilter}";
                }

                bool success = Bridge.DbgCmdExecDirect(commandString);
                if (!success)
                {
                    return $"Error: x64dbg rejected the command syntax -> {commandString}";
                }

                // 3. Query x64dbg for the number of occurrences using DbgValFromString
                nuint count = Bridge.DbgValFromString("ref.count()");

                if (count == 0)
                {
                    return $"0 occurrences found for pattern '{bytePattern}'.";
                }

                // 4. If matches were found, query the exact memory addresses
                List<string> foundAddresses = new List<string>();

                // x64dbg limits GUI references, but we can iterate through them programmatically
                for (nuint i = 0; i < count; i++)
                {
                    // The expression "ref.addr(i)" gets the address at the specific index
                    nuint address = Bridge.DbgValFromString($"ref.addr({i})");

                    // Format it nicely as a hex pointer (e.g., 0x7FF6B2001234)
                    foundAddresses.Add($"0x{address:X}");

                    // Hard cap so you don't overload the LLM context window 
                    // if it accidentally finds 50,000 matches.
                    if (i >= 100)
                    {
                        foundAddresses.Add("... [Truncated for LLM context size]");
                        break;
                    }
                }

                return $"Found {count} occurrences at the following addresses:\n" + string.Join("\n", foundAddresses);
            }
            catch (Exception e)
            {
                return $"Exception occurred: {e.Message}";
            }
        }

        private static bool IsRunningInX64Dbg()
        {
            try
            {
                // Method 1: Check if we can access x64-specific registers
                // In x64dbg, RIP register should be available and non-zero
                var rip = Bridge.DbgValFromString("$rip");
                if (rip != 0)
                {
                    Console.WriteLine("Detected x64dbg via RIP register");
                    return true;
                }

                // Method 2: Check process architecture
                var pid = Bridge.DbgValFromString("$pid");
                if (pid != 0)
                {
                    try
                    {
                        var process = System.Diagnostics.Process.GetProcessById((int)pid);
                        bool is64Bit = Environment.Is64BitProcess;
                        Console.WriteLine($"Process architecture check: {(is64Bit ? "x64" : "x32")}");
                        return is64Bit;
                    }
                    catch
                    {
                        // Fallback method
                    }
                }

                // Method 3: Check if x32-specific registers are available
                var eip = Bridge.DbgValFromString("$eip");
                if (eip != 0)
                {
                    Console.WriteLine("Detected x32dbg via EIP register");
                    return false;
                }

                // Default fallback - assume x32 if we can't determine
                Console.WriteLine("Could not determine architecture, defaulting to x32dbg");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error detecting architecture: {ex.Message}, defaulting to x32dbg");
                return false;
            }
        }

        private static bool ExecuteBpListForX32()
        {
            try
            {
                // For x32dbg, use a safer approach with log redirection
                // This avoids the direct crash that can happen with bplist

                string tempFile = null;
                try
                {
                    tempFile = Path.Combine(Path.GetTempPath(), "x32dbg_bplist_" + Guid.NewGuid().ToString("N") + ".log");

                    // Start log redirection
                    DbgCmdExec($"LogRedirect \"{tempFile}\"");
                    Thread.Sleep(100);

                    // Try bplist with a delay (safer for x32dbg)
                    Console.WriteLine("Executing bplist with safety delay for x32dbg...");
                    DbgCmdExec("bplist");
                    Thread.Sleep(300);

                    // Stop redirection
                    DbgCmdExec("LogRedirectStop");
                    Thread.Sleep(100);

                    // Read the log file
                    if (File.Exists(tempFile))
                    {
                        var content = File.ReadAllText(tempFile);
                        Console.WriteLine($"Breakpoint log content: {content}");
                        return !string.IsNullOrEmpty(content);
                    }

                    return false;
                }
                finally
                {
                    // Clean up temp file
                    try
                    {
                        if (!string.IsNullOrEmpty(tempFile) && File.Exists(tempFile))
                            File.Delete(tempFile);
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in ExecuteBpListForX32: {ex.Message}");
                return false;
            }
        }

        /*
        [Command("ExecuteDebuggerCommandWithVar", DebugOnly = false, MCPOnly = true, Category = CommandCategory.DebugFunctions, MCPCmdDescription = "Execute a command then return a debugger variable. Example: ExecuteDebuggerCommandWithVar command=init notepad.exe, resultVar=$pid, pollMs=100, pollTimeoutMs=5000")]
        public static string ExecuteDebuggerCommandWithVar(string command, string resultVar = "$result", int pollMs = 0, int pollTimeoutMs = 2000)
        {
            try
            {
                Console.WriteLine("Executing DebuggerCommandWithVar: " + command + ", resultVar=" + resultVar);
                DbgCmdExec(command);

                if (pollMs > 0 && pollTimeoutMs > 0)
                {
                    var sw = Stopwatch.StartNew();
                    while (sw.ElapsedMilliseconds < pollTimeoutMs)
                    {
                        var v = Bridge.DbgValFromString(resultVar);
                        if (v != 0)
                            return "0x" + v.ToHexString();
                        Thread.Sleep(pollMs);
                    }
                }

                {
                    var v = Bridge.DbgValFromString(resultVar);
                    return "0x" + v.ToHexString();
                }
            }
            catch (Exception ex)
            {
                return $"[ExecuteDebuggerCommandWithVar] Error: {ex.Message}";
            }
        }
        */
        [Command("SetBreakpoint", DebugOnly = true, MCPOnly = true, Category = CommandCategory.DebugFunctions,
    MCPCmdDescription = "Sets a software execution breakpoint (INT3) at an address or API symbol.")]
        public static string ExecuteSetBreakpoint(
            [McpParam("Hex address or API symbol to break on. For Windows APIs, include the module prefix.",
                Examples = new[] { "0x140001000", "user32:MessageBoxW", "kernel32:CreateFileW" })]
            string target)
        {
            Console.WriteLine("----------------------------------------");
            Console.WriteLine($"METHOD: {nameof(ExecuteSetBreakpoint)}");
            Console.WriteLine($"  - {nameof(target)} : {target}");
            Console.WriteLine("----------------------------------------");
            try
            {
                if (string.IsNullOrWhiteSpace(target))
                    return "Error: Target parameter is required.";

                // Strip quotes if the LLM accidentally added them
                target = target.Replace("\"", "").Replace("'", "");

                // 1. Construct the x64dbg command
                string commandString = $"bp {target}";

                // 2. Use your existing DbgCmdExecFunction to run it and capture the output
                string result = DbgCmdExecFunction(commandString, 300);

                // 3. The "Friendly" Interceptor: Guide the LLM if it makes a syntax mistake
                if (result.Contains("Invalid addr") || result.Contains("Error setting breakpoint"))
                {
                    return $"Result: {result}\n\n" +
                           "HINT: If you are trying to target a Windows API, x64dbg requires the module prefix. " +
                           "Try prepending 'user32:', 'kernel32:', 'kernelbase:', or 'ntdll:' to the function name (e.g., 'user32:GetDlgItemTextW' or 'user32:SetWindowTextW'). " +
                           "If it still fails, the target DLL may not be loaded into the process memory yet.";
                }

                return result;
            }
            catch (Exception e)
            {
                return $"Exception occurred while setting breakpoint: {e.Message}";
            }
        }

        //[Command("DbgCmdExec", DebugOnly = false, MCPOnly = true, Category = CommandCategory.DebugFunctions, MCPCmdDescription = "Executes a native x64dbg command. WARNING: Use x64dbg syntax, NOT WinDbg syntax (e.g., use 'bp MessageBoxW' or 'bp user32:MessageBoxW', do NOT use '!').\\nExamples:\\n- 'run' (continue execution)\\n- 'step' (step into)\\n- 'bp GetDlgItemTextW' (set breakpoint)\\n- 'bc *' (clear all breakpoints)\\n- 'analx' (analyze executable)\"). Execute the command on the command processing thread.\r\n\r\nbool DbgCmdExec(const char* cmd);\r\nParameters\r\ncmd The command string in UTF-8 encoding\r\n\r\nReturn Value\r\ntrue if the command is sent to the command processing thread for asynchronous execution, false otherwise.\r\n\r\nExample\r\nDbgCmdExec(\"run\");")]
        public static string DbgCmdExecFunction(string command, int settleDelayMs = 200)
        {
            string tempFile = null;
            try
            {
                Console.WriteLine("Executing DbgCmdExec: " + command);

                tempFile = Path.Combine(Path.GetTempPath(), "x64dbg_cmd_" + Guid.NewGuid().ToString("N") + ".log");

                // Start redirection
                Bridge.DbgCmdExec($"LogRedirect \"{tempFile}\"");
                Thread.Sleep(50);

                // Execute the actual command
                var ok = Bridge.DbgCmdExec(command);
                Thread.Sleep(settleDelayMs);

                // Stop redirection
                Bridge.DbgCmdExec("LogRedirectStop");
                Thread.Sleep(100);

                // Read file with simple retries
                string output = string.Empty;
                for (int i = 0; i < 5; i++)
                {
                    if (File.Exists(tempFile))
                    {
                        try
                        {
                            var fi = new FileInfo(tempFile);
                            if (fi.Length > 0)
                            {
                                output = File.ReadAllText(tempFile, Encoding.UTF8);
                                break;
                            }
                        }
                        catch { }
                    }
                    Thread.Sleep(100);
                }

                // Filter common noise lines
                if (!string.IsNullOrEmpty(output))
                {
                    var lines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                        .Where(l => !l.Contains("Log will be redirected to")
                                 && !l.Contains("Log redirection stopped")
                                 && !l.Equals("Log cleared", StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    output = string.Join(Environment.NewLine, lines).Trim();
                }

                if (!string.IsNullOrEmpty(output))
                    return output;

                return ok ? "Command executed successfully (no output captured)" : "Command execution failed (no output captured)";
            }
            catch (Exception ex)
            {
                return $"[ExecuteDebuggerCommandWithOutput] Error: {ex.Message}";
            }
            finally
            {
                try { if (!string.IsNullOrEmpty(tempFile) && File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            }
        }

        [Command("GetBreakpointInfo", DebugOnly = true, MCPOnly = true, Category = CommandCategory.GeneralPurpose, MCPCmdDescription = "Returns information about all currently set breakpoints, including the total count and list. Takes no arguments.")]
        public static string GetBreakpointInfo()
        {
            Console.WriteLine("----------------------------------------");
            Console.WriteLine($"METHOD: {nameof(GetBreakpointInfo)}");
            Console.WriteLine("----------------------------------------");
            try
            {
                Console.WriteLine("Getting breakpoint information using alternative methods...");

                if (!Bridge.DbgIsDebugging())
                {
                    return "Debugger is not actively debugging";
                }

                var output = new StringBuilder();
                output.AppendLine("Breakpoint Information:");
                output.AppendLine("======================");

                // Try to get breakpoint count using debugger variables
                try
                {
                    var bpCount = Bridge.DbgValFromString("$bpcount");
                    output.AppendLine($"Breakpoint count: {bpCount}");
                }
                catch (Exception ex)
                {
                    output.AppendLine($"Could not get breakpoint count: {ex.Message}");
                }

                // Try to get breakpoint list using a different approach
                try
                {
                    // Use ExecuteDebuggerCommandWithOutput which has better error handling
                    var result = DbgCmdExecFunction("bplist", 500);
                    if (!string.IsNullOrEmpty(result))
                    {
                        output.AppendLine("Breakpoint list:");
                        output.AppendLine(result);
                    }
                    else
                    {
                        output.AppendLine("No breakpoints found or command failed");
                    }
                }
                catch (Exception ex)
                {
                    output.AppendLine($"Error getting breakpoint list: {ex.Message}");
                }

                return output.ToString();
            }
            catch (Exception ex)
            {
                return $"Error getting breakpoint info: {ex.Message}";
            }
        }


        [Command("ListCommandsByCategory", DebugOnly = false, MCPOnly = true, Category = CommandCategory.GeneralPurpose, MCPCmdDescription = "Lists the available MCP tools, optionally filtered to a single category. Call with no category to list the available categories first.")]
        public static string ListCommandsByCategory(
            [McpParam("Category to list commands for. Omit to list all available categories.",
                Required = false,
                Examples = new[] { "Searching", "DebugControl", "DebugFunctions" })]
            string category = "")
        {
            Console.WriteLine("----------------------------------------");
            Console.WriteLine($"METHOD: {nameof(ListCommandsByCategory)}");
            Console.WriteLine($"  - {nameof(category)} : {category}");
            Console.WriteLine("----------------------------------------");
            // 1. Explicitly fully-qualify System.Reflection.BindingFlags to prevent namespace collisions
            // with DotNetPlugin.NativeBindings.Script.Module. We use typeof(Plugin) since this is a static method.
            var pluginMethods = typeof(Plugin).GetMethods(
                System.Reflection.BindingFlags.DeclaredOnly |
                System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);

            // 2. Map the methods without relying on the System.Reflection extension methods
            var commandList = pluginMethods
                .SelectMany(method => method.GetCustomAttributes(typeof(CommandAttribute), false)
                    .Cast<CommandAttribute>()
                    .Select(attribute => new
                    {
                        Name = attribute.Name ?? method.Name,
                        Category = attribute.Category.ToString(),
                        Description = attribute.MCPCmdDescription ?? "No description provided.",
                        X64DbgOnly = attribute.X64DbgOnly
                    }))
                .Where(x => !x.X64DbgOnly)
                .ToList();

            // 3. Scenario A: No specific category provided. List populated categories.
            if (string.IsNullOrWhiteSpace(category))
            {
                var availableCategories = commandList
                    .Select(c => c.Category)
                    .Distinct()
                    .OrderBy(c => c)
                    .ToList();

                var output = new System.Text.StringBuilder();
                output.AppendLine("Available command categories:");

                foreach (var cat in availableCategories)
                {
                    output.AppendLine($"- {cat}");
                }

                output.AppendLine("\nExample usage:");
                output.AppendLine("ListCommandsByCategory category=Searching");

                return output.ToString();
            }

            // 4. Scenario B: A specific category was requested
            var targetCommands = commandList
                .Where(c => string.Equals(c.Category, category.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (targetCommands.Count > 0)
            {
                var output = new System.Text.StringBuilder();
                output.AppendLine($"Commands in category '{category}':");
                output.AppendLine(new string('-', 50));

                foreach (var cmd in targetCommands)
                {
                    output.AppendLine($"Command: {cmd.Name}");
                    output.AppendLine($"Description: {cmd.Description}");
                    output.AppendLine(); // Spacer
                }

                return output.ToString().TrimEnd();
            }

            // 5. Scenario C: Invalid or empty category requested
            return $"Unknown category '{category}'. Run ListCommandsByCategory without arguments to see available categories.";
        }

        /*
        [Command("ListDebuggerCommandsByResFile", DebugOnly = false, MCPOnly = true, Category = CommandCategory.GeneralPurpose, MCPCmdDescription = "Example: ListDebuggerCommands")]
        public static string ListDebuggerCommandsByResFile(string subject = "")
        {
            subject = subject?.Trim().ToLowerInvariant();

            // Mapping user input to resource keys
            var map = new Dictionary<string, string>
            {
                { "debugcontrol", Resources.DebugControl },
                { "gui", Resources.GUI },
                { "search", Resources.Search },
                { "threadcontrol", Resources.ThreadControl }
            };

            if (string.IsNullOrWhiteSpace(subject))
            {
                return "Available options:\n- debugcontrol\n- gui\n- search\n- threadcontrol\n\nExample:\nListDebuggerCommands subject=gui";
            }

            if (map.TryGetValue(subject, out string json))
            {
                return json;
            }

            return "Unknown subject group. Try one of:\n- DebugControl\n- GUI\n- Search\n- ThreadControl";
        }
        */

        //[Command("DbgValFromString", DebugOnly = false, MCPOnly = true, Category = CommandCategory.DebugFunctions, MCPCmdDescription = "Example: DbgValFromString value=$pid")]
        public static string DbgValFromString(string value)// = "$hProcess"
        {
            Console.WriteLine("Executing DbgValFromString: " + value);
            return "0x" + Bridge.DbgValFromString(value).ToHexString();
        }
        public static nuint DbgValFromStringAsNUInt(string value)// = "$hProcess"
        {
            Console.WriteLine("Executing DbgValFromString: " + value);
            return Bridge.DbgValFromString(value);
        }

        /*
        [Command("strref", DebugOnly = true, MCPOnly = true, Category = CommandCategory.Searching, MCPCmdDescription = "Alias for refstr. Finds referenced text strings.")]
        public static string ExecuteStrRef(string address = "", string size = "")
        {
            // Point the alias directly to the main function
            return ExecuteRefStr(address, size);
        }
        */

        [Command("refstr", DebugOnly = true, MCPOnly = true, Category = CommandCategory.Searching,
    MCPCmdDescription = "Finds referenced text strings within a memory region and returns the instruction addresses that reference them.")]
        public static string ExecuteRefStr(
            [McpParam("Hex start address to search from. Defaults to the current instruction pointer (CIP) when omitted.",
                Pattern = McpParamAttribute.HexAddressPattern, Required = false,
                Examples = new[] { "0x140001000" })]
            string address = "",
            [McpParam("Size of the memory region to scan (hex). Only applied when an address is also supplied.",
                Required = false, Examples = new[] { "0x1000" })]
            string size = "")
        {

            Console.WriteLine("----------------------------------------");
            Console.WriteLine($"METHOD: {nameof(ExecuteRefStr)}");
            Console.WriteLine($"  - {nameof(address)} : {address}");
            Console.WriteLine($"  - {nameof(size)}    : {size}");
            Console.WriteLine("----------------------------------------");

            try
            {
                // 1. Construct the command string dynamically
                string commandString = "refstr";

                var argsList = new System.Collections.Generic.List<string>();
                if (!string.IsNullOrWhiteSpace(address))
                    argsList.Add(address);

                // x64dbg usually requires the address if you are providing the size
                if (!string.IsNullOrWhiteSpace(size) && !string.IsNullOrWhiteSpace(address))
                    argsList.Add(size);

                if (argsList.Count > 0)
                {
                    // Join the arguments with commas as expected by x64dbg
                    commandString += " " + string.Join(", ", argsList);
                }

                // 2. Execute the command in the x64dbg engine
                bool success = Bridge.DbgCmdExecDirect(commandString);
                if (!success)
                {
                    return $"Error: x64dbg rejected the command syntax -> {commandString}";
                }

                // 3. Query x64dbg for the number of string references found
                nuint count = Bridge.DbgValFromString("ref.count()");

                if (count == 0)
                {
                    return "0 string references found.";
                }

                // 4. Iterate through the reference view to extract the addresses
                var foundAddresses = new System.Collections.Generic.List<string>();

                for (nuint i = 0; i < count; i++)
                {
                    nuint refAddress = Bridge.DbgValFromString($"ref.addr({i})");
                    foundAddresses.Add($"0x{refAddress:X}");

                    // Hard cap at 100 addresses so we don't nuke the LLM context window 
                    if (i >= 99)
                    {
                        foundAddresses.Add("... [Truncated for LLM context size]");
                        break;
                    }
                }

                return $"Found {count} string references at the following instruction addresses:\n" + string.Join("\n", foundAddresses);
            }
            catch (Exception e)
            {
                return $"Exception occurred while executing refstr: {e.Message}";
            }
        }

        [Command("FindXrefs", DebugOnly = true, MCPOnly = true, Category = CommandCategory.Searching,
    MCPCmdDescription = "Finds cross-references (xrefs) pointing TO a specific address, e.g. every location that calls a function or references a string. Returns each referencing address with its disassembly.")]
        public static string ExecuteFindXrefs(
            [McpParam("Hex address to find references to.",
                Pattern = McpParamAttribute.HexAddressPattern,
                Examples = new[] { "0x140001000" })]
            string targetAddress)
        {
            Console.WriteLine("----------------------------------------");
            Console.WriteLine($"METHOD: {nameof(ExecuteFindXrefs)}");
            Console.WriteLine($"  - {nameof(targetAddress)} : {targetAddress}");
            Console.WriteLine("----------------------------------------");
            try
            {
                if (string.IsNullOrWhiteSpace(targetAddress))
                    return "Error: targetAddress is required.";

                // 1. Execute the 'ref' command to find references TO the target address
                string commandString = $"ref {targetAddress}";
                bool success = Bridge.DbgCmdExecDirect(commandString);

                if (!success)
                {
                    return $"Error: x64dbg rejected the command -> {commandString}";
                }

                // 2. Query the number of references found in the GUI reference view
                nuint count = Bridge.DbgValFromString("ref.count()");

                if (count == 0)
                {
                    return $"0 cross-references found pointing to {targetAddress}.";
                }

                // 3. Extract the referencing addresses and their disassembly
                var foundAddresses = new System.Collections.Generic.List<string>();

                for (nuint i = 0; i < count; i++)
                {
                    nuint refAddress = Bridge.DbgValFromString($"ref.addr({i})");

                    // Disassemble at the referencing address to give the LLM immediate context
                    var disasm = new Bridge.BASIC_INSTRUCTION_INFO();
                    Bridge.DbgDisasmFastAt(refAddress, ref disasm);

                    string instruction = disasm.size > 0 ? disasm.instruction : "<unable to disassemble>";

                    foundAddresses.Add($"0x{refAddress:X} : {instruction}");

                    // Hard cap to prevent context window explosion (50 is usually plenty for xrefs)
                    if (i >= 49)
                    {
                        foundAddresses.Add("... [Truncated for LLM context size]");
                        break;
                    }
                }

                return $"Found {count} cross-references to {targetAddress}:\n" + string.Join("\n", foundAddresses);
            }
            catch (Exception e)
            {
                return $"Exception occurred while executing FindXrefs: {e.Message}";
            }
        }

        /*
        [Command("ExecuteDebuggerCommandDirect", DebugOnly = false)]
        public static bool ExecuteDebuggerCommandDirect(string[] args)
        {
            try
            {
                string commandString = args[0];

                // If there are arguments, append a space, then join the rest with commas
                if (args.Length > 1)
                {
                    commandString += " " + string.Join(", ", args.Skip(1));
                }
                Console.WriteLine(commandString);
                return Bridge.DbgCmdExecDirect(commandString);
            }
            catch(Exception e)
            {
                Console.WriteLine(e.ToString());
                return false;
            }
        }
        */

        //[Command("ReadMemory", DebugOnly = false)]
        //public static bool ReadMemory(string[] args)
        //{
        //    if (args.Length != 2)
        //    {
        //        Console.WriteLine("Usage: ReadMemory <address> <size>");
        //        return false;
        //    }

        //    try
        //    {
        //        // Parse address (supports hex or decimal)
        //        nuint address = (nuint)Convert.ToUInt64(
        //            args[0].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? args[0].Substring(2) : args[0],
        //            args[0].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? 16 : 10
        //        );

        //        // Parse size
        //        uint size = uint.Parse(args[1]);

        //        var memory = ReadMemory(address, size);

        //        if (memory == null)
        //        {
        //            Console.WriteLine($"[ReadMemory] Failed to read memory at 0x{address:X}");
        //            return false;
        //        }

        //        Console.WriteLine($"[ReadMemory] {size} bytes at 0x{address:X}:");

        //        for (int i = 0; i < memory.Length; i += 16)
        //        {
        //            var chunk = memory.Skip(i).Take(16).ToArray();
        //            string hex = BitConverter.ToString(chunk).Replace("-", " ").PadRight(48);
        //            string ascii = string.Concat(chunk.Select(b => b >= 32 && b <= 126 ? (char)b : '.'));
        //            Console.WriteLine($"{address + (nuint)i:X8}: {hex} {ascii}");
        //        }

        //        return true;
        //    }
        //    catch (Exception ex)
        //    {
        //        Console.WriteLine($"[ReadMemory] Error: {ex.Message}");
        //        return false;
        //    }
        //}


        public static byte[] ReadMemory(nuint address, uint size)
        {
            byte[] buffer = new byte[size];
            if (!Bridge.DbgMemRead(address, buffer, size)) // assume NativeBridge is a P/Invoke wrapper
                return null;
            return buffer;
        }

        //[Command("WriteMemory", DebugOnly = true, MCPOnly = true)]
        //public static bool WriteMemory(string[] args)
        //{
        //    if (args.Length < 2)
        //    {
        //        Console.WriteLine("Usage: WriteMemory <address> <byte1> <byte2> ...");
        //        Console.WriteLine("Example: WriteMemory 0x7FF600001000 48 8B 05");
        //        return false;
        //    }

        //    try
        //    {
        //        // Parse address (hex or decimal)
        //        nuint address = (nuint)Convert.ToUInt64(
        //            args[0].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? args[0].Substring(2) : args[0],
        //            args[0].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? 16 : 10
        //        );

        //        // Parse byte values (can be "48", "0x48", etc.)
        //        byte[] data = args.Skip(1).Select(b =>
        //        {
        //            b = b.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? b.Substring(2) : b;
        //            return byte.Parse(b, NumberStyles.HexNumber);
        //        }).ToArray();

        //        // Dump what we're about to write
        //        Console.WriteLine($"[WriteMemory] Writing {data.Length} bytes to 0x{address:X}:");
        //        Console.WriteLine(BitConverter.ToString(data).Replace("-", " "));

        //        // Perform the memory write
        //        if (!WriteMemory(address, data))
        //        {
        //            Console.WriteLine($"[WriteMemory] Failed to write to memory at 0x{address:X}");
        //            return false;
        //        }

        //        Console.WriteLine($"[WriteMemory] Successfully wrote to 0x{address:X}");
        //        return true;
        //    }
        //    catch (Exception ex)
        //    {
        //        Console.WriteLine($"[WriteMemory] Error: {ex.Message}");
        //        return false;
        //    }
        //}

        public static bool WriteMemory(nuint address, byte[] data)
        {
            return Bridge.DbgMemWrite(address, data, (uint)data.Length);
        }

        //[Command("WriteBytesToAddress", DebugOnly = true)]
        //public static bool WriteBytesToAddress(string[] args)
        //{
        //    if (args.Length < 2)
        //    {
        //        Console.WriteLine("Usage: WriteBytesToAddress <address> <byte1> <byte2> ...");
        //        Console.WriteLine("Example: WriteBytesToAddress 0x7FF600001000 48 8B 05");
        //        return false;
        //    }

        //    string addressStr = args[0];

        //    try
        //    {
        //        // Convert string[] to byte[]
        //        byte[] data = args.Skip(1).Select(b =>
        //        {
        //            b = b.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? b.Substring(2) : b;
        //            return byte.Parse(b, NumberStyles.HexNumber);
        //        }).ToArray();

        //        // Dump what we're about to write
        //        Console.WriteLine($"[WriteBytesToAddress] Writing {data.Length} bytes to {addressStr}:");
        //        Console.WriteLine(BitConverter.ToString(data).Replace("-", " "));

        //        // Call existing function
        //        return WriteBytesToAddress(addressStr, data);
        //    }
        //    catch (Exception ex)
        //    {
        //        Console.WriteLine($"[WriteBytesToAddress] Error: {ex.Message}");
        //        return false;
        //    }
        //}
        //public static bool WriteBytesToAddress(string addressStr, byte[] data)
        //{
        //    if (data == null || data.Length == 0)
        //    {
        //        Console.WriteLine("Data is null or empty.");
        //        return false;
        //    }

        //    if (!ulong.TryParse(addressStr.Replace("0x", ""), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong parsed))
        //    {
        //        Console.WriteLine($"Invalid address: {addressStr}");
        //        return false;
        //    }

        //    IntPtr ptr = new IntPtr((long)parsed);
        //    nuint address = (nuint)ptr.ToInt64();

        //    bool success = WriteMemory(address, data);

        //    if (success)
        //    {
        //        Console.WriteLine($"Successfully wrote {data.Length} bytes at 0x{address:X}");
        //    }
        //    else
        //    {
        //        Console.WriteLine($"Failed to write memory at 0x{address:X}");
        //    }

        //    return success;
        //}

        [Command("WriteMemToAddress", DebugOnly = true, MCPOnly = true, Category = CommandCategory.DebugFunctions, MCPCmdDescription = "Patches memory by writing raw hex bytes to an address. WARNING: this modifies the live process. Example: WriteMemToAddress address=0x12345678, byteString=0F FF 90")]
        public static string WriteMemToAddress(
            [McpParam("Hex address to write the bytes to.",
                Pattern = McpParamAttribute.HexAddressPattern,
                Examples = new[] { "0x12345678" })]
            string address,
            [McpParam("Space- or comma-separated hex bytes to write, in order.",
                Examples = new[] { "0F FF 90", "90 90 90" })]
            string byteString)
        {
            Console.WriteLine("----------------------------------------");
            Console.WriteLine($"METHOD: {nameof(WriteMemToAddress)}");
            Console.WriteLine($"  - {nameof(address)}    : {address}");
            Console.WriteLine($"  - {nameof(byteString)} : {byteString}");
            Console.WriteLine("----------------------------------------");
            try
            {
                if (string.IsNullOrWhiteSpace(byteString))
                    return "Error: Byte string is empty.";

                // Parse address
                if (!ulong.TryParse(address.Replace("0x", ""), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong parsed))
                    return $"Error: Invalid address: {address}";

                nuint MyAddresses = (nuint)parsed;

                // Parse byte string (e.g., "90 89 78")
                string[] byteParts = byteString.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                byte[] data = byteParts.Select(b =>
                {
                    if (b.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                        b = b.Substring(2);
                    return byte.Parse(b, NumberStyles.HexNumber);
                }).ToArray();

                if (data.Length == 0)
                    return "Error: No valid bytes found to write.";

                // Write memory
                bool success = WriteMemory(MyAddresses, data);

                if (success)
                {
                    return $"Successfully wrote {data.Length} byte(s) to 0x{MyAddresses:X}:\r\n{BitConverter.ToString(data)}";
                }
                else
                {
                    return $"Failed to write memory at 0x{(uint)MyAddresses:X}";
                }
            }
            catch (Exception ex)
            {
                return $"[WriteBytesToAddress] Error: {ex.Message}";
            }
        }

        [Command("CommentOrLabelAtAddress", DebugOnly = true, MCPOnly = true, Category = CommandCategory.DebugFunctions, MCPCmdDescription = "Adds a comment or a label at a specific address in the disassembly. Example: CommentOrLabelAtAddress address=0x12345678, value=DecryptRoutine, mode=Label")]
        public static string CommentOrLabelAtAddress(
            [McpParam("Hex address to annotate.",
                Pattern = McpParamAttribute.HexAddressPattern,
                Examples = new[] { "0x12345678" })]
            string address,
            [McpParam("The label or comment text to apply at the address.",
                Examples = new[] { "DecryptRoutine", "checks license flag" })]
            string value,
            [McpParam("Whether to write a renamable label or an inline comment.",
                Required = false, EnumValues = new[] { "Label", "Comment" })]
            string mode = "Label")
        {
            Console.WriteLine("----------------------------------------");
            Console.WriteLine($"METHOD: {nameof(CommentOrLabelAtAddress)}");
            Console.WriteLine($"  - {nameof(address)} : {address}");
            Console.WriteLine($"  - {nameof(value)}   : {value}");
            Console.WriteLine($"  - {nameof(mode)}    : {mode}");
            Console.WriteLine("----------------------------------------");
            try
            {
                bool success = false;
                // Parse address
                if (!ulong.TryParse(address.Replace("0x", ""), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong parsed))
                    return $"Error: Invalid address: {address}";

                nuint MyAddresses = (nuint)parsed;

                if (string.Equals(mode, "Label", StringComparison.OrdinalIgnoreCase))
                {
                    success = Bridge.DbgSetLabelAt(MyAddresses, value);
                    Console.WriteLine($"Label '{value}' added at {MyAddresses:X} (byte pattern match)");
                }
                else if (string.Equals(mode, "Comment", StringComparison.OrdinalIgnoreCase))
                {
                    success = Bridge.DbgSetCommentAt(MyAddresses, value);
                    Console.WriteLine($"Comment '{value}' added at {MyAddresses:X} (byte pattern match)");
                }
                if (success)
                {
                    return $"Successfully wrote {value} to addressStr as {mode}";
                }
                else
                {
                    return $"Failed to write memory at 0x{MyAddresses:X}";
                }
            }
            catch (Exception ex)
            {
                return $"[WriteBytesToAddress] Error: {ex.Message}";
            }
        }

        public static bool PatchWithNops(string[] args)
        {
            return PatchWithNops(args[0], Convert.ToInt32(args[1]));
        }
        public static bool PatchWithNops(string addressStr, int nopCount = 7)
        {
            if (!ulong.TryParse(addressStr.Replace("0x", ""), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong parsed))
            {
                Console.WriteLine($"Invalid address: {addressStr}");
                return false;
            }

            IntPtr ptr = new IntPtr((long)parsed);
            nuint address = (nuint)ptr.ToInt64();

            byte[] nops = Enumerable.Repeat((byte)0x90, nopCount).ToArray();
            bool success = WriteMemory(address, nops);

            if (success)
            {
                Console.WriteLine($"Successfully patched {nopCount} NOPs at 0x{address:X}");
            }
            else
            {
                Console.WriteLine($"Failed to write memory at 0x{address:X}");
            }

            return success;
        }

        /// <summary>
        /// Parses a string of hexadecimal byte values separated by hyphens into a byte array.
        /// </summary>
        /// <param name="pattern">
        /// A string containing hexadecimal byte values, e.g., "75-38" or "90-90-CC".
        /// Each byte must be two hex digits and separated by hyphens.
        /// </param>
        /// <returns>
        /// A byte array representing the parsed hex values.
        /// </returns>
        /// <example>
        /// byte[] bytes = ParseBytePattern("75-38"); // returns new byte[] { 0x75, 0x38 }
        /// </example>
        public static byte[] ParseBytePattern(string pattern)
        {
            return pattern.Split('-').Select(b => Convert.ToByte(b, 16)).ToArray();
        }

        //[Command("GetLabel", DebugOnly = true)]
        //public static bool GetLabel(string[] args)
        //{
        //    if (args.Length != 1)
        //    {
        //        Console.WriteLine("Usage: GetLabel <address>");
        //        Console.WriteLine("Example: GetLabel 0x7FF600001000");
        //        return false;
        //    }

        //    try
        //    {
        //        // Parse address (supports hex and decimal)
        //        nuint address = (nuint)Convert.ToUInt64(
        //            args[0].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? args[0].Substring(2) : args[0],
        //            args[0].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? 16 : 10
        //        );

        //        string label = GetLabel(address);

        //        if (label != null)
        //        {
        //            Console.WriteLine($"[GetLabel] Label at 0x{address:X}: {label}");
        //            return true;
        //        }
        //        else
        //        {
        //            Console.WriteLine($"[GetLabel] No label found at 0x{address:X}");
        //            return false;
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        Console.WriteLine($"[GetLabel] Error: {ex.Message}");
        //        return false;
        //    }
        //}

        [Command("GetLabel", DebugOnly = true, MCPOnly = true, Category = CommandCategory.DebugFunctions, MCPCmdDescription = "Returns the label (symbol name) currently assigned to an address, if any. Example: GetLabel addressStr=0x12345678")]
        public static string GetLabel(
            [McpParam("Hex (or decimal) address to look up the label for.",
                Pattern = McpParamAttribute.HexAddressPattern,
                Examples = new[] { "0x12345678" })]
            string addressStr)
        {
            Console.WriteLine("----------------------------------------");
            Console.WriteLine($"METHOD: {nameof(GetLabel)}");
            Console.WriteLine($"  - {nameof(addressStr)} : {addressStr}");
            Console.WriteLine("----------------------------------------");
            try
            {
                // Parse address (supports hex or decimal)
                nuint address = (nuint)Convert.ToUInt64(
                    addressStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? addressStr.Substring(2) : addressStr,
                    addressStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? 16 : 10
                );

                string label = GetLabel(address);

                if (!string.IsNullOrEmpty(label))
                    return $"[GetLabel] Label at 0x{address:X}: {label}";
                else
                    return $"[GetLabel] No label found at 0x{address:X}";
            }
            catch (Exception ex)
            {
                return $"[GetLabel] Error: {ex.Message}";
            }
        }

        public static string GetLabel(nuint address)
        {
            return Bridge.DbgGetLabelAt(address, SEGMENTREG.SEG_DEFAULT, out var label) ? label : null;
        }


        string TryGetDereferencedString(nuint address)
        {
            var data = ReadMemory(address, 64); // read 64 bytes (arbitrary)
            int end = Array.IndexOf(data, (byte)0);
            if (end <= 0) return null;
            return Encoding.ASCII.GetString(data, 0, end);
        }


        public static void LabelIfCallTargetMatches(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: LabelIfCallTargetMatches <address> <targetAddress> [labelOrComment] [mode: Label|Comment]");
                Console.WriteLine("Example: LabelIfCallTargetMatches 0x7FF600001000 0x7FF600002000 MyLabel Label");
                return;
            }

            try
            {
                // Parse input addresses
                nuint address = (nuint)Convert.ToUInt64(
                    args[0].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? args[0].Substring(2) : args[0],
                    args[0].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? 16 : 10
                );

                nuint targetAddress = (nuint)Convert.ToUInt64(
                    args[1].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? args[1].Substring(2) : args[1],
                    args[1].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? 16 : 10
                );

                // Optional label + mode
                string value = "test";
                string mode = "Label";

                if (args.Length == 3)
                {
                    value = args[2];
                }
                else if (args.Length >= 4)
                {
                    value = args[args.Length - 2];
                    mode = args[args.Length - 1];
                }

                // Disassemble at the given address
                Bridge.BASIC_INSTRUCTION_INFO disasm = new Bridge.BASIC_INSTRUCTION_INFO();
                Bridge.DbgDisasmFastAt(address, ref disasm);


                LabelIfCallTargetMatches(address, ref disasm, targetAddress, value, mode);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LabelIfCallTargetMatches] Error: {ex.Message}");
            }
        }
        public static void LabelIfCallTargetMatches(nuint address, ref Bridge.BASIC_INSTRUCTION_INFO disasm, nuint targetAddress, string value = "test", string mode = "Label")
        {
            if (disasm.addr == targetAddress)
            {
                if (string.Equals(mode, "Label", StringComparison.OrdinalIgnoreCase))
                {
                    Bridge.DbgSetLabelAt(address, value);
                    Console.WriteLine($"Label '{value}' added at {address:X}");
                }
                else if (string.Equals(mode, "Comment", StringComparison.OrdinalIgnoreCase))
                {
                    Bridge.DbgSetCommentAt(address, value);
                    Console.WriteLine($"Comment '{value}' added at {address:X}");
                }
            }
        }

        public static bool LabelMatchingInstruction(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: LabelMatchingInstruction <address> <instruction> [labelOrComment] [mode: Label|Comment]");
                Console.WriteLine("Example: LabelMatchingInstruction 0x7FF600001000 \"jnz 0x140001501\" MyLabel Label");
                return false;
            }

            try
            {
                // Parse address
                nuint address = (nuint)Convert.ToUInt64(
                    args[0].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? args[0].Substring(2) : args[0],
                    args[0].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? 16 : 10
                );

                string instruction = args[1];
                string label = "test";
                string mode = "Label";

                if (args.Length == 3)
                {
                    label = args[2];
                }
                else if (args.Length >= 4)
                {
                    label = args[args.Length - 2];
                    mode = args[args.Length - 1];
                }

                Bridge.BASIC_INSTRUCTION_INFO disasm = new Bridge.BASIC_INSTRUCTION_INFO();
                Bridge.DbgDisasmFastAt(address, ref disasm);

                LabelMatchingInstruction(address, ref disasm, instruction, label, mode);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LabelMatchingInstruction] Error: {ex.Message}");
                return false;
            }
        }
        public static void LabelMatchingInstruction(nuint address, ref Bridge.BASIC_INSTRUCTION_INFO disasm, string targetInstruction = "jnz 0x0000000140001501", string value = "test", string mode = "Label")
        {
            if (string.Equals(disasm.instruction, targetInstruction, StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(mode, "Label", StringComparison.OrdinalIgnoreCase))
                {
                    Bridge.DbgSetLabelAt(address, value);
                    Console.WriteLine($"Label 'test' added at {address:X}");
                }
                else if (string.Equals(mode, "Comment", StringComparison.OrdinalIgnoreCase))
                {
                    Bridge.DbgSetCommentAt(address, value);
                    Console.WriteLine($"Comment 'test' added at {address:X}");
                }
            }
        }

        public static void LabelMatchingBytes(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: LabelMatchingBytes <address> <byte1> <byte2> ... [labelOrComment] [mode: Label|Comment]");
                Console.WriteLine("Example: LabelMatchingBytes 0x7FF600001000 48 8B 05 MyLabel Label");
                return;
            }

            try
            {
                // Parse address
                nuint address = (nuint)Convert.ToUInt64(
                    args[0].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? args[0].Substring(2) : args[0],
                    args[0].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? 16 : 10
                );

                // Default values
                string value = "test";
                string mode = "Label";

                // Determine how many arguments belong to byte pattern
                int byteCount = args.Length - 1;

                if (args.Length >= 3)
                {
                    string lastArg = args[args.Length - 1];
                    string secondLastArg = args[args.Length - 2];

                    bool lastIsMode = lastArg.Equals("Label", StringComparison.OrdinalIgnoreCase)
                                   || lastArg.Equals("Comment", StringComparison.OrdinalIgnoreCase);

                    if (lastIsMode)
                    {
                        mode = lastArg;
                        value = secondLastArg;
                        byteCount -= 2;
                    }
                    else
                    {
                        value = lastArg;
                        byteCount -= 1;
                    }
                }

                // Parse bytes
                var pattern = args.Skip(1).Take(byteCount).Select(b =>
                {
                    if (b.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                        b = b.Substring(2);
                    return byte.Parse(b, NumberStyles.HexNumber);
                }).ToArray();

                // Call the memory-labeling function
                LabelMatchingBytes(address, pattern, value, mode);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LabelMatchingBytes] Error: {ex.Message}");
            }
        }




        public static void LabelMatchingBytes(nuint address, byte[] pattern, string value = "test", string mode = "Label")
        {
            try
            {
                byte[] actualBytes = ReadMemory(address, (uint)pattern.Length);

                if (actualBytes.Length != pattern.Length)
                    return;

                for (int i = 0; i < pattern.Length; i++)
                {
                    if (actualBytes[i] != pattern[i])
                        return;
                }

                if (string.Equals(mode, "Label", StringComparison.OrdinalIgnoreCase))
                {
                    Bridge.DbgSetLabelAt(address, value);
                    Console.WriteLine($"Label '{value}' added at {address:X} (byte pattern match)");
                }
                else if (string.Equals(mode, "Comment", StringComparison.OrdinalIgnoreCase))
                {
                    Bridge.DbgSetCommentAt(address, value);
                    Console.WriteLine($"Comment '{value}' added at {address:X} (byte pattern match)");
                }
            }
            catch
            {
                // Fail quietly on bad memory read
            }
        }

        // Function returns List of tuples: (Module Name, Full Path, Base Address, Total Size)
        public static List<(string Name, string Path, nuint Base, nuint Size)> GetAllModulesFromMemMapFunc()
        {
            // Update the list's tuple definition to include Path (string)
            var finalResult = new List<(string Name, string Path, nuint Base, nuint Size)>();
            MEMMAP_NATIVE nativeMemMap = new MEMMAP_NATIVE();
            var allocationRegions = new Dictionary<nuint, List<(nuint Base, nuint Size, string Info)>>();

            try
            {
                if (!DbgMemMap(ref nativeMemMap))
                {
                    Console.WriteLine("[GetAllModulesFromMemMapFunc] DbgMemMap call failed.");
                    return finalResult;
                }

                // Console.WriteLine($"[GetAllModulesFromMemMapFunc] DbgMemMap reported count: {nativeMemMap.count}"); // Optional

                if (nativeMemMap.page != IntPtr.Zero && nativeMemMap.count > 0)
                {
                    int sizeOfMemPage = Marshal.SizeOf<MEMPAGE>();

                    // --- Pass 1: Collect all MEM_IMAGE regions grouped by AllocationBase ---
                    for (int i = 0; i < nativeMemMap.count; i++)
                    {
                        IntPtr currentPagePtr = new IntPtr(nativeMemMap.page.ToInt64() + (long)i * sizeOfMemPage);
                        MEMPAGE memPage = Marshal.PtrToStructure<MEMPAGE>(currentPagePtr);

                        if ((memPage.mbi.Type & MEM_IMAGE) == MEM_IMAGE)
                        {
                            nuint allocBase = (nuint)memPage.mbi.AllocationBase.ToInt64();
                            nuint baseAddr = (nuint)memPage.mbi.BaseAddress.ToInt64();
                            nuint regionSize = memPage.mbi.RegionSize;
                            string infoString = memPage.info ?? string.Empty;

                            if (!allocationRegions.ContainsKey(allocBase))
                            {
                                allocationRegions[allocBase] = new List<(nuint Base, nuint Size, string Info)>();
                            }
                            allocationRegions[allocBase].Add((baseAddr, regionSize, infoString));
                        }
                    }

                    // --- Pass 2: Process collected regions for each allocation base ---
                    foreach (var kvp in allocationRegions)
                    {
                        nuint allocBase = kvp.Key;
                        var regions = kvp.Value;

                        if (regions.Count > 0)
                        {
                            // Find the actual module name/path.
                            string modulePath = "Unknown Module"; // Store the full path here
                            var mainRegion = regions.FirstOrDefault(r => r.Base == allocBase);

                            if (mainRegion.Info != null && !string.IsNullOrEmpty(mainRegion.Info))
                            {
                                modulePath = mainRegion.Info;
                            }
                            else
                            {
                                var firstInfoRegion = regions.FirstOrDefault(r => !string.IsNullOrEmpty(r.Info));
                                if (firstInfoRegion.Info != null)
                                {
                                    modulePath = firstInfoRegion.Info;
                                }
                                // If still no path, it remains "Unknown Module"
                            }

                            // Extract the file name for display
                            string finalModuleName = System.IO.Path.GetFileName(modulePath);
                            if (string.IsNullOrEmpty(finalModuleName))
                            {
                                finalModuleName = modulePath; // Use path if filename extraction fails
                                if (string.IsNullOrEmpty(finalModuleName)) // Final fallback
                                {
                                    finalModuleName = $"Module@0x{allocBase:X16}";
                                    modulePath = finalModuleName; // Assign fallback to path too
                                }
                            }

                            // --- Manual Min/Max Calculation ---
                            nuint minRegionBase = regions[0].Base;
                            nuint maxRegionEnd = regions[0].Base + regions[0].Size;
                            for (int i = 1; i < regions.Count; i++)
                            {
                                if (regions[i].Base < minRegionBase) minRegionBase = regions[i].Base;
                                nuint currentEnd = regions[i].Base + regions[i].Size;
                                if (currentEnd > maxRegionEnd) maxRegionEnd = currentEnd;
                            }
                            // --- End Manual Min/Max ---

                            nuint totalSize = maxRegionEnd - minRegionBase;

                            // Add the aggregated module info, including the full path
                            finalResult.Add((finalModuleName, modulePath, allocBase, totalSize));

                        } // End if (regions.Count > 0)
                    } // End Pass 2 Loop

                    // Sort the final list by base address
                    finalResult.Sort((a, b) => {
                        if (a.Base < b.Base) return -1;
                        if (a.Base > b.Base) return 1;
                        return 0;
                    });

                }
                // ... (rest of try block and error logging) ...
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GetAllModulesFromMemMapFunc] Exception: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
            finally
            {
                if (nativeMemMap.page != IntPtr.Zero)
                {
                    //BridgeFree(nativeMemMap.page); // Ensure this is called!
                }
            }
            return finalResult;
        }


        [Command("GetAllModulesFromMemMap", DebugOnly = true, MCPOnly = true, Category = CommandCategory.GeneralPurpose, MCPCmdDescription = "Lists every image module currently mapped into the target process, with name, path, base address, end address, and size. Takes no arguments.")]
        public static string GetAllModulesFromMemMap()
        {
            Console.WriteLine("GetAllModulesFromMemMap() called");
            try
            {
                // Update expected tuple type
                var modules = GetAllModulesFromMemMapFunc(); // Returns List<(string Name, string Path, nuint Base, nuint Size)>

                if (modules.Count == 0)
                    return "[GetAllModulesFromMemMap] No image modules found in memory map.";

                var output = new StringBuilder();
                output.AppendLine($"[GetAllModulesFromMemMap] Found {modules.Count} image modules:");

                // Update foreach destructuring and output line
                output.AppendLine($"{"Name",-30} {"Path",-70} {"Base Address",-18} {"End Address",-18} {"Size",-10}");
                output.AppendLine(new string('-', 150)); // Separator line

                foreach (var (Name, Path, Base, Size) in modules)
                {
                    nuint End = Base + Size;
                    // Add Path to the output, adjust spacing as needed
                    output.AppendLine($"{Name,-30} {Path,-70} 0x{Base:X16} 0x{End:X16} 0x{Size:X}");
                }

                return output.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                return $"[GetAllModulesFromMemMap] Error: {ex.Message}\n{ex.StackTrace}";
            }
        }














        public static bool VerboseLogging = true;

        private static void Log(string msg)
        {
            if (VerboseLogging) Console.WriteLine(msg);
        }

        // Always print hex WITH the value's true hex digits. (The stale build printed RSP
        // in decimal behind a "0x" prefix — that bug is impossible with these helpers.)
        private static string H(ulong v) => "0x" + v.ToString("X");
        private static string H(nuint v) => "0x" + ((ulong)v).ToString("X");

        // ------------------------------------------------------------------ public API
        public enum TargetArch { Auto = 0, X86 = 4, X64 = 8 } // value == pointer size

        public struct CallStackFrameInfo
        {
            public nuint FrameAddress;   // stack slot holding the return address (matches x64dbg "Address")
            public nuint ReturnAddress;  // value at that slot          (matches x64dbg "To")
            public nuint FrameSize;      // distance to caller frame    (matches x64dbg "Size")
            public bool IsHeuristic;    // true = scan-derived, NOT a verified unwind
        }

        // x64 general-purpose register indices (UNWIND_CODE OpInfo encoding)
        private const int RAX = 0, RCX = 1, RDX = 2, RBX = 3, RSP = 4, RBP = 5, RSI = 6, RDI = 7;
        private static readonly string[] RegName =
            { "rax","rcx","rdx","rbx","rsp","rbp","rsi","rdi","r8","r9","r10","r11","r12","r13","r14","r15" };

        [Command("GetCallStack", DebugOnly = true, MCPOnly = true, Category = CommandCategory.GeneralPurpose,
MCPCmdDescription = "Retrieves the current execution call stack by walking RBP frames. Use immediately after a breakpoint hits to trace which module/function originally called the current code. Requires execution to be paused.")]
        public static string GetCallStackFunc([McpParam("Maximum number of stack frames to walk before stopping. Defaults to 32.",
                Required = false, Minimum = 1, Maximum = 256, Examples = new[] { "32" })]
            int maxFrames = 32)
        {
            TargetArch arch = TargetArch.Auto;
            bool extendWithHeuristic = false;
            Log("==================================================================");
            Log($"[GetCallStackFunc] ENTER  maxFrames={maxFrames}  arch={arch}  extendWithHeuristic={extendWithHeuristic}");

            var callstack = new List<CallStackFrameInfo>();

            int pointerSize = ResolvePointerSize(arch);
            if (pointerSize != 4 && pointerSize != 8)
            {
                Log($"[GetCallStackFunc] Bad pointer size {pointerSize}. Aborting.");
                return $"[GetCallStackFunc] Bad pointer size {pointerSize}. Aborting.";
            }

            string bpReg = pointerSize == 8 ? "rbp" : "ebp";
            string spReg = pointerSize == 8 ? "rsp" : "esp";
            string ipReg = pointerSize == 8 ? "rip" : "eip";
            byte[] addrBuffer = new byte[8];

            nuint bp = DbgValFromStringAsNUInt(bpReg);
            nuint sp = DbgValFromStringAsNUInt(spReg);
            nuint ip = DbgValFromStringAsNUInt(ipReg);

            Log($"[GetCallStackFunc] {pointerSize * 8}-bit context:");
            Log($"    {ipReg} = {H(ip)}");
            Log($"    {spReg} = {H(sp)}   (decimal {(ulong)sp})");
            Log($"    {bpReg} = {H(bp)}");

            if (sp == 0)
            {
                Log("[GetCallStackFunc] Stack pointer is 0 — no usable context. Aborting.");
                return "[GetCallStackFunc] Stack pointer is 0 — no usable context. Aborting.";
            }

            // ---------------------------------------------------------------- dispatch
            if (pointerSize == 8)
            {
                // PRIMARY for x64: unwind-info walk. Works regardless of RBP (RBP==0 is normal here).
                Log("[GetCallStackFunc] x64 -> unwind-info walk (RtlVirtualUnwind-style).");
                callstack = WalkX64UnwindInfo(ip, sp, bp, addrBuffer, maxFrames);

                if (callstack.Count == 0)
                {
                    Log("[GetCallStackFunc] Unwind-info walk produced 0 frames. " +
                        "Check that mod.base(<addr>) resolves through your bridge. " +
                        "Falling back to heuristic scan (will NOT match the GUI).");
                    callstack = WalkStackHeuristic(sp, pointerSize, addrBuffer, maxFrames);
                }
            }
            else
            {
                // PRIMARY for x86: EBP chain.
                nuint alignMask = (nuint)(pointerSize - 1);
                bool bpValid = bp != 0 && bp >= sp && (bp & alignMask) == 0;
                Log($"[GetCallStackFunc] x86 EBP valid? {bpValid} " +
                    $"(nonzero={bp != 0}, ebp>=esp={bp >= sp}, aligned={(bp & alignMask) == 0})");

                if (bpValid)
                    callstack = WalkFramePointerChain(bp, sp, pointerSize, addrBuffer, maxFrames);

                if (callstack.Count == 0)
                {
                    Log("[GetCallStackFunc] EBP chain empty — heuristic scan fallback.");
                    callstack = WalkStackHeuristic(sp, pointerSize, addrBuffer, maxFrames);
                }
            }

            // ---------------------------------------------------------------- optional extension
            if (extendWithHeuristic && callstack.Count > 0 && callstack.Count < maxFrames)
            {
                nuint resumeFrom = callstack[callstack.Count - 1].FrameAddress + (nuint)pointerSize;
                Log($"[GetCallStackFunc] extendWithHeuristic: scanning from {H(resumeFrom)}");
                var extra = WalkStackHeuristic(resumeFrom, pointerSize, addrBuffer, maxFrames - callstack.Count);
                callstack.AddRange(extra);
            }

            DumpResult(callstack);
            Log($"[GetCallStackFunc] EXIT  returning {callstack.Count} frame(s).");
            Log("==================================================================");
            //return callstack;
            var output = new StringBuilder();
            output.AppendLine($"[GetCallStack] Retrieved {callstack.Count} frames:");
            output.AppendLine($"{"Frame",-5} {"Frame Addr",-18} {"Return Addr",-18} {"Size",-10} {"Module",-25} {"Label Symbol",-40} {"Comment"}");
            output.AppendLine(new string('-', 130));

            for (int i = 0; i < callstack.Count; i++)
            {
                var frame = callstack[i];

                // x64dbg labels each frame by the code it's executing IN (its "From"),
                // not the address it returns to. Innermost frame -> RIP; every outer
                // frame -> the return address captured one frame further in.
                nuint fromAddr = (i == 0) ? ip : callstack[i - 1].ReturnAddress;
                TryResolveSymbols(fromAddr, out AddressSymbols symbols);

                string frameAddrHex = ((ulong)frame.FrameAddress).ToString("X16");
                string returnAddrHex = ((ulong)frame.ReturnAddress).ToString("X16");
                string frameSizeHex = ((ulong)frame.FrameSize).ToString("X");

                output.AppendLine($"{$"[ {i}]",-5} 0x{frameAddrHex} 0x{returnAddrHex} 0x{frameSizeHex,-8} " +
                                  $"{symbols.Module,-20} {symbols.Symbolic,-45} {symbols.Comment}");
            }

            return output.ToString().TrimEnd(); // remove trailing newline
        }

        public class AddressSymbols
        {
            public string Module { get; set; } = "N/A";
            public string Label { get; set; } = "N/A";  // "RtlGetReturnAddressHijackTarget+6EE"
            public string Comment { get; set; } = "";      // instruction-level comment (usually empty here)
            public string Symbolic { get; set; } = "N/A";   // "ntdll.RtlGetReturnAddressHijackTarget+6EE"
        }

        private static bool TryResolveSymbols(nuint address, out AddressSymbols symbols)
        {
            symbols = new AddressSymbols();

            var info = new BRIDGE_ADDRINFO_NATIVE
            {
                // flaglabel -> symbol name; leaving flagNoFuncOffset UNSET keeps the "+offset" suffix.
                flags = (int)(ADDRINFOFLAGS.flagmodule
                            | ADDRINFOFLAGS.flaglabel
                            | ADDRINFOFLAGS.flagcomment)
            };

            bool ok = DbgAddrInfoGet(address, 0, ref info);

            string mod = info.module ?? "";
            string label = info.label ?? "";   // <-- the "function+offset" is HERE, not in comment
            string cmt = info.comment ?? "";

            symbols.Module = mod.Length > 0 ? mod : "N/A";
            symbols.Label = label.Length > 0 ? label : "N/A";

            if (cmt.Length > 0)
                symbols.Comment = cmt[0] == '\x01' ? cmt.Substring(1) : cmt; // strip auto-comment marker

            // Reproduce x64dbg's call-stack "Comment": module + "." + label
            if (mod.Length > 0 && label.Length > 0) symbols.Symbolic = $"{mod}.{label}";
            else if (label.Length > 0) symbols.Symbolic = label;
            else if (mod.Length > 0) symbols.Symbolic = mod;

            return ok;
        }
        private static bool TryGetInitialStackPointers(out nuint rbp, out nuint rsp)
        {
            rbp = DbgValFromStringAsNUInt("rbp");
            rsp = DbgValFromStringAsNUInt("rsp");

            if (rbp == 0 || rbp < rsp)
            {
                Console.WriteLine($"[GetCallStackFunc] Initial RBP is invalid or below RSP. RBP=0x{rbp:X} RSP=0x{rsp:X}");
                return false;
            }
            return true;
        }

        public static IEnumerable<CallStackFrameInfo> WalkStackFrames(int maxFrames = 32)
        {
            if (!TryGetInitialStackPointers(out nuint rbp, out nuint rsp))
            {
                yield break; // Stop iteration if initial pointers are invalid
            }

            nuint currentRbp = rbp;
            nuint previousRbp = 0;

            for (int i = 0; i < maxFrames; i++)
            {
                if (!TryGetFrameInfo(currentRbp, out nuint returnAddress, out nuint nextRbp))
                {
                    break; // Stop if we can't read frame info (end of stack or invalid memory)
                }

                nuint frameSize = CalculateFrameSize(currentRbp, previousRbp);

                yield return new CallStackFrameInfo
                {
                    FrameAddress = currentRbp,
                    ReturnAddress = returnAddress,
                    FrameSize = frameSize
                };

                previousRbp = currentRbp;
                currentRbp = nextRbp;

                if (!IsNextRbpValid(currentRbp, previousRbp, rsp))
                {
                    break; // Stop if the next frame pointer is invalid
                }
            }
        }
        private static bool IsNextRbpValid(nuint currentRbp, nuint previousRbp, nuint rsp)
        {
            if (currentRbp == 0 || currentRbp < rsp || currentRbp <= previousRbp)
            {
                Console.WriteLine($"[GetCallStackFunc] Invalid next RBP (0x{currentRbp:X}). Previous=0x{previousRbp:X}, RSP=0x{rsp:X}. Stopping walk.");
                return false;
            }
            return true;
        }
        private static nuint CalculateFrameSize(nuint currentRbp, nuint previousRbp)
        {
            if (previousRbp == 0)
            {
                return 0;
            }
            // Avoid nonsensical size if RBP decreased or is not what we expect
            if (currentRbp > previousRbp)
            {
                return 0;
            }
            return previousRbp - currentRbp;
        }

        private static bool TryReadNuintFromMemory(nuint address, out nuint value)
        {
            byte[] buffer = new byte[sizeof(ulong)];
            if (DbgMemRead(address, buffer, (nuint)sizeof(ulong)))
            {
                value = (nuint)BitConverter.ToUInt64(buffer, 0);
                return true;
            }
            value = 0;
            Console.WriteLine($"[GetCallStackFunc] Failed to read memory at 0x{address:X}");
            return false;
        }

        private static bool TryGetFrameInfo(nuint currentRbp, out nuint returnAddress, out nuint nextRbp)
        {
            returnAddress = 0;
            nextRbp = 0;

            if (!TryReadNuintFromMemory(currentRbp + (nuint)sizeof(ulong), out returnAddress))
            {
                return false;
            }

            if (returnAddress == 0)
            {
                Console.WriteLine("[GetCallStackFunc] Reached null return address.");
                return false;
            }

            return TryReadNuintFromMemory(currentRbp, out nextRbp);
        }
        private static bool TryResolveSymbols_retire(nuint address, out AddressSymbols symbols)
        {
            symbols = new AddressSymbols();

            var info = new BRIDGE_ADDRINFO_NATIVE
            {
                // flaglabel -> symbol name; leaving flagNoFuncOffset UNSET keeps the "+offset" suffix.
                flags = (int)(ADDRINFOFLAGS.flagmodule
                            | ADDRINFOFLAGS.flaglabel
                            | ADDRINFOFLAGS.flagcomment)
            };

            bool ok = DbgAddrInfoGet(address, 0, ref info);

            string mod = info.module ?? "";
            string label = info.label ?? "";   // <-- the "function+offset" is HERE, not in comment
            string cmt = info.comment ?? "";

            symbols.Module = mod.Length > 0 ? mod : "N/A";
            symbols.Label = label.Length > 0 ? label : "N/A";

            if (cmt.Length > 0)
                symbols.Comment = cmt[0] == '\x01' ? cmt.Substring(1) : cmt; // strip auto-comment marker

            // Reproduce x64dbg's call-stack "Comment": module + "." + label
            if (mod.Length > 0 && label.Length > 0) symbols.Symbolic = $"{mod}.{label}";
            else if (label.Length > 0) symbols.Symbolic = label;
            else if (mod.Length > 0) symbols.Symbolic = mod;

            return ok;
        }

        private static void DumpResult(List<CallStackFrameInfo> cs)
        {
            Log("[GetCallStackFunc] Resolved call stack (compare against x64dbg Call Stack view):");
            Log("    idx  Address(slot)      To(return)        Size     Source");
            for (int i = 0; i < cs.Count; i++)
            {
                var f = cs[i];
                Log($"    {i,3}  {("0x" + ((ulong)f.FrameAddress).ToString("X16"))}  " +
                    $"{("0x" + ((ulong)f.ReturnAddress).ToString("X16"))}  " +
                    $"{((ulong)f.FrameSize),6:X}   {(f.IsHeuristic ? "HEURISTIC" : "unwind/fp")}");
            }
        }

        // ============================================================== arch detection
        private static int ResolvePointerSize(TargetArch arch)
        {
            if (arch == TargetArch.X86) { Log("[ResolvePointerSize] Forced x86 (4)."); return 4; }
            if (arch == TargetArch.X64) { Log("[ResolvePointerSize] Forced x64 (8)."); return 8; }

            // Auto-detect. WARNING: unreliable under WOW64 — a 64-bit debugger may resolve
            // "rip" for a 32-bit thread. Pass arch explicitly when you know the target.
            Log("[ResolvePointerSize] Auto-detect (pass arch explicitly to avoid WOW64 ambiguity)...");
            try
            {
                nuint rip = DbgValFromStringAsNUInt("rip");
                Log($"    rip -> {H(rip)}");
                if (rip != 0) { Log("    -> 64-bit."); return 8; }

                nuint eip = DbgValFromStringAsNUInt("eip");
                Log($"    eip -> {H(eip)}");
                if (eip != 0) { Log("    -> 32-bit."); return 4; }
            }
            catch (Exception ex) { Log($"    auto-detect threw: {ex.Message}"); }

            Log("[ResolvePointerSize] Auto-detect failed; defaulting to 64-bit.");
            return 8;
        }

        // ============================================================== memory helpers
        private static bool ReadBytes(ulong va, byte[] buf, int size)
        {
            if (buf.Length < size) return false;
            bool ok = DbgMemRead((nuint)va, buf, (nuint)size);
            if (!ok) Log($"        [mem] read FAILED  va={H(va)} size={size}");
            return ok;
        }

        private static bool ReadU8(ulong va, out byte val)
        {
            val = 0; var b = new byte[1];
            if (!ReadBytes(va, b, 1)) return false;
            val = b[0]; return true;
        }

        private static bool ReadU16(ulong va, out ushort val)
        {
            val = 0; var b = new byte[2];
            if (!ReadBytes(va, b, 2)) return false;
            val = BitConverter.ToUInt16(b, 0); return true;
        }

        private static bool ReadU32(ulong va, out uint val)
        {
            val = 0; var b = new byte[4];
            if (!ReadBytes(va, b, 4)) return false;
            val = BitConverter.ToUInt32(b, 0); return true;
        }

        private static bool ReadPtr64(ulong va, out ulong val)
        {
            val = 0; var b = new byte[8];
            if (!ReadBytes(va, b, 8)) return false;
            val = BitConverter.ToUInt64(b, 0); return true;
        }

        private static bool TryReadPointer(nuint va, int pointerSize, byte[] buffer, out nuint value)
        {
            value = 0;
            Array.Clear(buffer, 0, pointerSize);
            if (!DbgMemRead(va, buffer, (nuint)pointerSize)) return false;
            value = pointerSize == 8
                ? (nuint)BitConverter.ToUInt64(buffer, 0)
                : (nuint)BitConverter.ToUInt32(buffer, 0);
            return true;
        }

        // ============================================================== x64 UNWIND WALK
        // Mirrors RtlVirtualUnwind: for each frame, find the module's exception directory,
        // locate the RUNTIME_FUNCTION covering RIP, replay the UNWIND_INFO to adjust RSP,
        // then the return address is at [RSP] and the slot is the frame's "Address".
        private static List<CallStackFrameInfo> WalkX64UnwindInfo(
            nuint startRip, nuint startRsp, nuint startRbp, byte[] addrBuffer, int maxFrames)
        {
            var stack = new List<CallStackFrameInfo>();

            var regs = new ulong[16];
            regs[RSP] = startRsp;
            regs[RBP] = startRbp; // 0 is fine; only matters if a SET_FPREG references it.

            ulong rip = startRip;

            for (int frame = 0; frame < maxFrames; frame++)
            {
                Log($"  ---------------------------------------------------------------");
                Log($"  [unwind] frame {frame}: RIP={H(rip)} RSP={H(regs[RSP])} RBP={H(regs[RBP])}");

                // 1) Module base for RIP.
                nuint modBaseN = DbgValFromStringAsNUInt($"mod.base(0x{rip:X})");
                ulong modBase = modBaseN;
                Log($"  [unwind] mod.base({H(rip)}) = {H(modBase)}");
                if (modBase == 0)
                {
                    Log("  [unwind] RIP not in a known module (JIT/dynamic/bad expr). Stopping.");
                    break;
                }

                uint funcRva = (uint)(rip - modBase);

                // 2) Exception directory -> RUNTIME_FUNCTION covering funcRva.
                if (!TryGetExceptionDirectory(modBase, out ulong exTableVa, out uint exTableSize))
                {
                    Log("  [unwind] No exception directory. Treating as leaf.");
                    if (!PopReturnAddress(regs, addrBuffer, stack)) break;
                    rip = stack[stack.Count - 1].ReturnAddress;
                    if (!ContinueChecks(regs, rip)) break;
                    continue;
                }

                if (!TryFindRuntimeFunction(modBase, exTableVa, exTableSize, funcRva,
                                            out uint rfBegin, out uint rfEnd, out uint rfUnwindRva))
                {
                    // No unwind data for this RVA => leaf function: return addr at [RSP].
                    Log($"  [unwind] No RUNTIME_FUNCTION for rva={H(funcRva)} -> leaf function.");
                    if (!PopReturnAddress(regs, addrBuffer, stack)) break;
                    rip = stack[stack.Count - 1].ReturnAddress;
                    if (!ContinueChecks(regs, rip)) break;
                    continue;
                }

                Log($"  [unwind] RUNTIME_FUNCTION begin={H(rfBegin)} end={H(rfEnd)} unwindRva={H(rfUnwindRva)} " +
                    $"(funcRva={H(funcRva)})");

                // 3) Replay the unwind info chain to adjust RSP (and recover RBP if pushed).
                bool machineFrame = false;
                ulong machineRip = 0;
                uint curUnwindRva = rfUnwindRva;
                uint prologOffset = funcRva - rfBegin; // only relevant if we're inside the prolog
                int chainGuard = 0;

                while (true)
                {
                    if (++chainGuard > 32) { Log("  [unwind] chain too deep, bailing."); break; }
                    if (!ApplyUnwindInfo(modBase, curUnwindRva, prologOffset, regs,
                                         ref machineFrame, ref machineRip, out bool chained, out uint nextUnwindRva))
                    {
                        Log("  [unwind] failed reading UNWIND_INFO. Stopping walk.");
                        return stack;
                    }
                    if (machineFrame) break;
                    if (!chained) break;
                    Log($"  [unwind] CHAININFO -> parent unwindRva={H(nextUnwindRva)}");
                    curUnwindRva = nextUnwindRva;
                    prologOffset = 0xFFFFFFFF; // parent codes always fully apply
                }

                // 4) Return address.
                ulong retSlot, retAddr;
                if (machineFrame)
                {
                    // PUSH_MACHFRAME already set RSP/RIP; record the machine-frame return.
                    retAddr = machineRip;
                    retSlot = regs[RSP]; // approximate; machine frames are rare
                    Log($"  [unwind] machine frame: RIP={H(retAddr)} newRSP={H(regs[RSP])}");
                }
                else
                {
                    retSlot = regs[RSP];
                    if (!ReadPtr64(retSlot, out retAddr))
                    {
                        Log($"  [unwind] failed reading return address at {H(retSlot)}. Stopping.");
                        break;
                    }
                    regs[RSP] = retSlot + 8;
                }

                Log($"  [unwind] frame {frame} RESULT: slot(Address)={H(retSlot)} return(To)={H(retAddr)} " +
                    $"newRSP={H(regs[RSP])}");

                // record (FrameSize is filled in on the next iteration once we know the next slot)
                stack.Add(new CallStackFrameInfo
                {
                    FrameAddress = (nuint)retSlot,
                    ReturnAddress = (nuint)retAddr,
                    FrameSize = 0,
                    IsHeuristic = false
                });

                if (retAddr == 0)
                {
                    Log("  [unwind] return address 0 -> top of stack (thread entry). Done.");
                    break;
                }

                rip = retAddr;
                if (!ContinueChecks(regs, rip)) break;
            }

            // Fill FrameSize = nextSlot - thisSlot (matches x64dbg "Size").
            for (int i = 0; i + 1 < stack.Count; i++)
            {
                var f = stack[i];
                ulong here = f.FrameAddress, next = stack[i + 1].FrameAddress;
                f.FrameSize = (nuint)(next > here ? next - here : 0);
                stack[i] = f;
            }

            return stack;
        }

        // Leaf helper: return address sits directly at [RSP].
        private static bool PopReturnAddress(ulong[] regs, byte[] _buf, List<CallStackFrameInfo> stack)
        {
            ulong slot = regs[RSP];
            if (!ReadPtr64(slot, out ulong ret)) return false;
            regs[RSP] = slot + 8;
            Log($"  [unwind] leaf pop: slot={H(slot)} return={H(ret)} newRSP={H(regs[RSP])}");
            stack.Add(new CallStackFrameInfo
            {
                FrameAddress = (nuint)slot,
                ReturnAddress = (nuint)ret,
                FrameSize = 0,
                IsHeuristic = false
            });
            return ret != 0;
        }

        // Sanity gates between frames.
        private static bool ContinueChecks(ulong[] regs, ulong nextRip)
        {
            if (regs[RSP] == 0) { Log("  [unwind] RSP became 0. Stop."); return false; }
            if ((regs[RSP] & 0x7) != 0) { Log($"  [unwind] RSP {H(regs[RSP])} misaligned. Stop."); return false; }
            if (nextRip < 0x10000) { Log($"  [unwind] next RIP {H(nextRip)} too low. Stop."); return false; }
            return true;
        }

        // ---- PE parsing: locate IMAGE_DIRECTORY_ENTRY_EXCEPTION (index 3) ----
        private static bool TryGetExceptionDirectory(ulong modBase, out ulong tableVa, out uint tableSize)
        {
            tableVa = 0; tableSize = 0;

            if (!ReadU32(modBase + 0x3C, out uint eLfanew)) return false;
            ulong nt = modBase + eLfanew;

            if (!ReadU32(nt, out uint sig) || sig != 0x00004550 /* "PE\0\0" */)
            {
                Log($"  [pe] bad PE signature at {H(nt)} (got {H(sig)})");
                return false;
            }

            ulong optHdr = nt + 24; // 4 (sig) + 20 (file header)
            if (!ReadU16(optHdr, out ushort magic)) return false;

            // DataDirectory offset within optional header: PE32+ => 112, PE32 => 96
            int dirOffset = (magic == 0x20B) ? 112 : 96;
            ulong dirEntry = optHdr + (ulong)dirOffset + (3 * 8); // entry #3 (exception)

            if (!ReadU32(dirEntry, out uint rva)) return false;
            if (!ReadU32(dirEntry + 4, out uint size)) return false;

            if (rva == 0 || size == 0)
            {
                Log("  [pe] exception directory empty (no .pdata).");
                return false;
            }

            tableVa = modBase + rva;
            tableSize = size;
            Log($"  [pe] exception dir rva={H(rva)} size={H(size)} -> va={H(tableVa)} " +
                $"({size / 12} RUNTIME_FUNCTION entries)");
            return true;
        }

        // Binary search the RUNTIME_FUNCTION array (sorted by BeginAddress).
        private static bool TryFindRuntimeFunction(
            ulong modBase, ulong tableVa, uint tableSize, uint funcRva,
            out uint begin, out uint end, out uint unwindRva)
        {
            begin = end = unwindRva = 0;
            int count = (int)(tableSize / 12);
            int lo = 0, hi = count - 1;

            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                ulong entry = tableVa + (ulong)mid * 12;
                if (!ReadU32(entry, out uint b)) return false;
                if (!ReadU32(entry + 4, out uint e)) return false;

                if (funcRva < b) hi = mid - 1;
                else if (funcRva >= e) lo = mid + 1;
                else
                {
                    if (!ReadU32(entry + 8, out uint u)) return false;
                    begin = b; end = e; unwindRva = u;
                    return true;
                }
            }
            return false;
        }

        // ---- UNWIND_INFO replay (the heart of RtlVirtualUnwind) ----
        private const int UNW_FLAG_CHAININFO = 0x4;

        private static bool ApplyUnwindInfo(
            ulong modBase, uint unwindRva, uint prologOffset, ulong[] regs,
            ref bool machineFrame, ref ulong machineRip,
            out bool chained, out uint nextUnwindRva)
        {
            chained = false; nextUnwindRva = 0;

            ulong infoVa = modBase + unwindRva;
            if (!ReadU8(infoVa + 0, out byte verFlags)) return false;
            if (!ReadU8(infoVa + 1, out byte sizeOfProlog)) return false;
            if (!ReadU8(infoVa + 2, out byte countOfCodes)) return false;
            if (!ReadU8(infoVa + 3, out byte frameRegInfo)) return false;

            int version = verFlags & 0x7;
            int flags = (verFlags >> 3) & 0x1F;
            int frameReg = frameRegInfo & 0xF;
            int frameOff = (frameRegInfo >> 4) & 0xF; // scaled by 16

            Log($"  [uinfo] @{H(infoVa)} ver={version} flags={H((ulong)flags)} prologSize={sizeOfProlog} " +
                $"codes={countOfCodes} frameReg={(frameReg == 0 ? "-" : RegName[frameReg])} frameOff={frameOff * 16}");

            // Read all code slots (2 bytes each).
            int codeBytes = countOfCodes * 2;
            byte[] codes = new byte[Math.Max(codeBytes, 2)];
            if (codeBytes > 0 && !ReadBytes(infoVa + 4, codes, codeBytes)) return false;

            bool inProlog = prologOffset < sizeOfProlog;
            if (inProlog)
                Log($"  [uinfo] RIP is INSIDE prolog (off={prologOffset} < {sizeOfProlog}); applying executed codes only.");

            int i = 0;
            while (i < countOfCodes)
            {
                byte codeOffset = codes[2 * i];
                byte b1 = codes[2 * i + 1];
                int op = b1 & 0xF;
                int opInfo = (b1 >> 4) & 0xF;
                int slots = UnwindSlotCount(op, opInfo, codes, i, countOfCodes);

                // If we're still in the prolog, ops that haven't executed yet (codeOffset >
                // current prolog offset) must be skipped — but still advance the index.
                if (inProlog && codeOffset > prologOffset)
                {
                    Log($"    [code] skip (not yet executed) op={op} opInfo={opInfo} at prologOff={codeOffset}");
                    i += slots;
                    continue;
                }

                ulong rspBefore = regs[RSP];
                switch (op)
                {
                    case 0: // UWOP_PUSH_NONVOL
                        if (ReadPtr64(regs[RSP], out ulong popped)) regs[opInfo] = popped;
                        regs[RSP] += 8;
                        Log($"    [code] PUSH_NONVOL {RegName[opInfo]}: RSP {H(rspBefore)}->{H(regs[RSP])} " +
                            $"({RegName[opInfo]}={H(regs[opInfo])})");
                        break;

                    case 1: // UWOP_ALLOC_LARGE
                        if (opInfo == 0)
                        {
                            uint sz = (uint)(codes[2 * (i + 1)] | (codes[2 * (i + 1) + 1] << 8)) * 8u;
                            regs[RSP] += sz;
                            Log($"    [code] ALLOC_LARGE(small form) +{H(sz)}: RSP {H(rspBefore)}->{H(regs[RSP])}");
                        }
                        else
                        {
                            uint sz = (uint)(codes[2 * (i + 1)] | (codes[2 * (i + 1) + 1] << 8)
                                           | (codes[2 * (i + 2)] << 16) | (codes[2 * (i + 2) + 1] << 24));
                            regs[RSP] += sz;
                            Log($"    [code] ALLOC_LARGE(large form) +{H(sz)}: RSP {H(rspBefore)}->{H(regs[RSP])}");
                        }
                        break;

                    case 2: // UWOP_ALLOC_SMALL
                        {
                            uint sz = (uint)(opInfo * 8 + 8);
                            regs[RSP] += sz;
                            Log($"    [code] ALLOC_SMALL +{H(sz)}: RSP {H(rspBefore)}->{H(regs[RSP])}");
                        }
                        break;

                    case 3: // UWOP_SET_FPREG: RSP = frameReg - frameOff*16
                        regs[RSP] = regs[frameReg] - (ulong)(frameOff * 16);
                        Log($"    [code] SET_FPREG: RSP = {RegName[frameReg]}({H(regs[frameReg])}) - {frameOff * 16} " +
                            $"= {H(regs[RSP])}");
                        break;

                    case 4: // UWOP_SAVE_NONVOL: reg saved at RSP + off*8 (RSP unchanged)
                        {
                            uint off = (uint)(codes[2 * (i + 1)] | (codes[2 * (i + 1) + 1] << 8)) * 8u;
                            if (ReadPtr64(regs[RSP] + off, out ulong v)) regs[opInfo] = v;
                            Log($"    [code] SAVE_NONVOL {RegName[opInfo]} @ RSP+{H(off)} -> {H(regs[opInfo])}");
                        }
                        break;

                    case 5: // UWOP_SAVE_NONVOL_FAR
                        {
                            uint off = (uint)(codes[2 * (i + 1)] | (codes[2 * (i + 1) + 1] << 8)
                                            | (codes[2 * (i + 2)] << 16) | (codes[2 * (i + 2) + 1] << 24));
                            if (ReadPtr64(regs[RSP] + off, out ulong v)) regs[opInfo] = v;
                            Log($"    [code] SAVE_NONVOL_FAR {RegName[opInfo]} @ RSP+{H(off)} -> {H(regs[opInfo])}");
                        }
                        break;

                    case 6: // v2 UWOP_EPILOG (no RSP effect for our purposes)
                        Log("    [code] EPILOG (v2) — ignored for walk");
                        break;
                    case 7: // v2 UWOP_SPARE_CODE — ignored
                        Log("    [code] SPARE (v2) — ignored");
                        break;

                    case 8: // UWOP_SAVE_XMM128 — XMM, no GP/RSP effect
                        Log("    [code] SAVE_XMM128 — no RSP effect");
                        break;
                    case 9: // UWOP_SAVE_XMM128_FAR — no GP/RSP effect
                        Log("    [code] SAVE_XMM128_FAR — no RSP effect");
                        break;

                    case 10: // UWOP_PUSH_MACHFRAME
                        {
                            if (opInfo == 1) regs[RSP] += 8; // error code present
                            ReadPtr64(regs[RSP] + 0, out ulong mRip);
                            ReadPtr64(regs[RSP] + 24, out ulong mRsp);
                            machineRip = mRip;
                            regs[RSP] = mRsp;
                            machineFrame = true;
                            Log($"    [code] PUSH_MACHFRAME: RIP={H(mRip)} newRSP={H(mRsp)}");
                        }
                        break;

                    default:
                        Log($"    [code] UNKNOWN op {op} — ignored");
                        break;
                }

                if (machineFrame) break;
                i += slots;
            }

            if ((flags & UNW_FLAG_CHAININFO) != 0 && !machineFrame)
            {
                // Chained RUNTIME_FUNCTION sits after the (even-padded) code array.
                int padded = (countOfCodes + 1) & ~1;
                ulong chainEntry = infoVa + 4 + (ulong)padded * 2;
                if (ReadU32(chainEntry + 8, out uint parentUnwind))
                {
                    chained = true;
                    nextUnwindRva = parentUnwind;
                }
            }

            return true;
        }

        private static int UnwindSlotCount(int op, int opInfo, byte[] codes, int i, int count)
        {
            switch (op)
            {
                case 0: return 1;                       // PUSH_NONVOL
                case 1: return opInfo == 0 ? 2 : 3;     // ALLOC_LARGE
                case 2: return 1;                       // ALLOC_SMALL
                case 3: return 1;                       // SET_FPREG
                case 4: return 2;                       // SAVE_NONVOL
                case 5: return 3;                       // SAVE_NONVOL_FAR
                case 6: return 1;                       // EPILOG (v2)
                case 7: return 3;                       // SPARE (v2)
                case 8: return 2;                       // SAVE_XMM128
                case 9: return 3;                       // SAVE_XMM128_FAR
                case 10: return 1;                       // PUSH_MACHFRAME
                default: return 1;
            }
        }

        // ============================================================== x86 EBP CHAIN
        private static List<CallStackFrameInfo> WalkFramePointerChain(
            nuint bp, nuint sp, int pointerSize, byte[] addrBuffer, int maxFrames)
        {
            Log($"[WalkFramePointerChain] start bp={H(bp)} sp={H(sp)} ptr={pointerSize}");
            var callstack = new List<CallStackFrameInfo>();
            nuint ptr = (nuint)pointerSize;
            nuint alignMask = (nuint)(pointerSize - 1);
            nuint currentBp = bp;
            nuint previousBp = 0;

            for (int i = 0; i < maxFrames; i++)
            {
                if (!TryReadPointer(currentBp + ptr, pointerSize, addrBuffer, out nuint returnAddress))
                {
                    Log($"[WalkFramePointerChain] failed reading return addr at {H(currentBp + ptr)}"); break;
                }
                if (returnAddress == 0) { Log("[WalkFramePointerChain] return addr 0 -> end."); break; }

                if (!TryReadPointer(currentBp, pointerSize, addrBuffer, out nuint nextBp))
                {
                    Log($"[WalkFramePointerChain] failed reading saved BP at {H(currentBp)}"); break;
                }

                nuint frameSize = nextBp > currentBp ? nextBp - currentBp : 0;
                Log($"[WalkFramePointerChain] frame {i}: bp={H(currentBp)} ret={H(returnAddress)} " +
                    $"nextBp={H(nextBp)} size={H(frameSize)}");

                callstack.Add(new CallStackFrameInfo
                {
                    FrameAddress = currentBp,
                    ReturnAddress = returnAddress,
                    FrameSize = frameSize,
                    IsHeuristic = false
                });

                previousBp = currentBp;
                currentBp = nextBp;

                if (currentBp == 0 || currentBp < sp || currentBp <= previousBp || (currentBp & alignMask) != 0)
                {
                    Log($"[WalkFramePointerChain] chain terminator at {H(currentBp)}."); break;
                }
            }
            return callstack;
        }

        // ============================================================== HEURISTIC (LAST RESORT)
        private static List<CallStackFrameInfo> WalkStackHeuristic(
            nuint scanStart, int pointerSize, byte[] addrBuffer, int maxFrames)
        {
            Log($"[WalkStackHeuristic] *** LAST-RESORT SCAN from {H(scanStart)} *** " +
                "(over-collects stale return addresses; will NOT match the GUI exactly)");
            var callstack = new List<CallStackFrameInfo>();
            nuint step = (nuint)pointerSize;
            nuint maxScanBytes = 0x4000;
            nuint limit = scanStart + maxScanBytes;
            nuint addr = scanStart;
            byte[] code = new byte[16];

            while (addr < limit && callstack.Count < maxFrames)
            {
                if (!TryReadPointer(addr, pointerSize, addrBuffer, out nuint candidate)) break;

                if (IsPlausibleReturnAddress(candidate, pointerSize) && IsPrecededByCall(candidate, code))
                {
                    Log($"[WalkStackHeuristic] hit @ {H(addr)} -> {H(candidate)}");
                    callstack.Add(new CallStackFrameInfo
                    {
                        FrameAddress = addr,
                        ReturnAddress = candidate,
                        FrameSize = 0,
                        IsHeuristic = true
                    });
                }
                addr += step;
            }
            Log($"[WalkStackHeuristic] collected {callstack.Count} candidate(s).");
            return callstack;
        }

        private static bool IsPrecededByCall(nuint returnAddr, byte[] code)
        {
            const int look = 16;
            if (returnAddr <= (nuint)look) return false;
            if (!DbgMemRead(returnAddr - (nuint)look, code, (nuint)look)) return false;

            if (code[look - 5] == 0xE8) return true; // E8 call rel32
            for (int back = 2; back <= 7; back++)    // FF /2 call r/m
            {
                if (code[look - back] == 0xFF)
                {
                    byte modrm = code[look - back + 1];
                    if (((modrm >> 3) & 0x7) == 0x2) return true;
                }
            }
            if (code[look - 7] == 0x9A) return true; // 9A far call
            return false;
        }

        private static bool IsPlausibleReturnAddress(nuint addr, int pointerSize)
        {
            ulong a = (ulong)addr;
            if (a < 0x10000) return false;
            if (pointerSize == 8)
            {
                ulong high = a & 0xFFFF_0000_0000_0000UL;
                if (high != 0 && high != 0xFFFF_0000_0000_0000UL) return false;
            }
            else
            {
                if (a > 0xFFFFFFFFUL) return false;
                if (a >= 0xFFFF0000UL) return false;
            }
            return true;
        }





















        [Command("run", DebugOnly = true, MCPOnly = true, Category = CommandCategory.DebugControl,
    MCPCmdDescription = "Resumes execution of the debugged process (equivalent to F9 / 'run'). Returns whether the process is now running or has paused at a breakpoint. Takes no arguments.")]
        public static string ContinueExecution()
        {
            Console.WriteLine("----------------------------------------");
            Console.WriteLine($"METHOD: {nameof(ContinueExecution)}");
            Console.WriteLine("----------------------------------------");
            try
            {
                // 1. Verify the debugger is actually attached and active
                if (!Bridge.DbgIsDebugging())
                {
                    return "Error: Debugger is not actively debugging a target process.";
                }

                // 2. Execute the native x64dbg 'run' command.
                // Bumped to 250ms to give immediate TLS callbacks/initial breakpoints time to register.
                string result = DbgCmdExecFunction("run", 250);

                // 3. Check the ACTUAL status of the CPU after the settle delay
                // If the process is still executing, explicitly tell the agent to halt and yield.
                if (Bridge.DbgIsRunning())
                {
                    return "STATUS: RUNNING. The target process is now in a running state.";
                }

                // 4. If it's NOT running, it hit an immediate breakpoint, exception, or stepped.
                if (string.IsNullOrEmpty(result) || result.Contains("Command executed successfully"))
                {
                    return "STATUS: PAUSED. Process resumed but hit an immediate breakpoint, TLS callback, or system event. Analyze the current address space.";
                }

                return $"Result: {result}";
            }
            catch (Exception ex)
            {
                return $"Exception occurred while trying to continue execution: {ex.Message}\n{ex.StackTrace}";
            }
        }

        [Command("StepInto", DebugOnly = true, MCPOnly = true, Category = CommandCategory.DebugControl,
            MCPCmdDescription = "Executes a single instruction, stepping INTO any call encountered (F7). Takes no arguments.")]
        public static string StepInto()
        {
            Console.WriteLine("----------------------------------------");
            Console.WriteLine($"METHOD: {nameof(StepInto)}");
            Console.WriteLine("----------------------------------------");
            try
            {
                if (!Bridge.DbgIsDebugging())
                {
                    return "Error: Process must be running and suspended to step.";
                }

                string result = DbgCmdExecFunction("sti", 100);
                return string.IsNullOrEmpty(result) || result.Contains("Command executed successfully")
                    ? "Success: Stepped into instruction."
                    : $"Result: {result}";
            }
            catch (Exception ex)
            {
                return $"Exception in StepInto: {ex.Message}";
            }
        }

        [Command("StepOver", DebugOnly = true, MCPOnly = true, Category = CommandCategory.DebugControl,
            MCPCmdDescription = "Executes a single instruction, stepping OVER any call/subroutine entirely (F8). Takes no arguments.")]
        public static string StepOver()
        {
            Console.WriteLine("----------------------------------------");
            Console.WriteLine($"METHOD: {nameof(StepOver)}");
            Console.WriteLine("----------------------------------------");
            try
            {
                if (!Bridge.DbgIsDebugging())
                {
                    return "Error: Process must be running and suspended to step.";
                }

                string result = DbgCmdExecFunction("sto", 100);
                return string.IsNullOrEmpty(result) || result.Contains("Command executed successfully")
                    ? "Success: Stepped over block frame."
                    : $"Result: {result}";
            }
            catch (Exception ex)
            {
                return $"Exception in StepOver: {ex.Message}";
            }
        }

        [Command("StepOut", DebugOnly = true, MCPOnly = true, Category = CommandCategory.DebugControl,
            MCPCmdDescription = "Runs until the current function returns to its caller (Ctrl+F9). Takes no arguments.")]
        public static string StepOut()
        {
            Console.WriteLine("----------------------------------------");
            Console.WriteLine($"METHOD: {nameof(StepOut)}");
            Console.WriteLine("----------------------------------------");
            try
            {
                if (!Bridge.DbgIsDebugging())
                {
                    return "Error: Process must be running and suspended to step out.";
                }

                string result = DbgCmdExecFunction("rtr", 150);
                return string.IsNullOrEmpty(result) || result.Contains("Command executed successfully")
                    ? "Success: Execution advanced to target return layout statement."
                    : $"Result: {result}";
            }
            catch (Exception ex)
            {
                return $"Exception in StepOut: {ex.Message}";
            }
        }

        [Command("GetAllActiveThreads", DebugOnly = true, MCPOnly = true, Category = CommandCategory.GeneralPurpose, MCPCmdDescription = "Lists all active threads in the target process with thread number, thread ID, entry point, TEB, and thread name. Takes no arguments.")]
        public static string GetAllActiveThreads()
        {
            Console.WriteLine("----------------------------------------");
            Console.WriteLine($"METHOD: {nameof(GetAllActiveThreads)}");
            Console.WriteLine("----------------------------------------");
            try
            {
                // Get the list of threads with the extended information
                var threads = GetAllActiveThreadsFunc(); // This now returns List<(int, uint, ulong, ulong, string)>
                var output = new StringBuilder();

                output.AppendLine($"[GetAllActiveThreads] Found {threads.Count} active threads:");

                // Update the foreach loop to destructure the new tuple elements
                foreach (var (ThreadNumber, ThreadId, EntryPoint, TEB, ThreadName) in threads)
                {
                    // Update the output line to include ThreadNumber and ThreadName
                    // Adjust formatting as desired
                    output.AppendLine($"Num: {ThreadNumber,3} | TID: {ThreadId,6} | EntryPoint: 0x{EntryPoint:X16} | TEB: 0x{TEB:X16} | Name: {ThreadName}");
                }

                return output.ToString().TrimEnd(); // Removes trailing newline
            }
            catch (Exception ex)
            {
                // Add more detail to the error if possible
                return $"[GetAllActiveThreads] Error: {ex.Message}\n{ex.StackTrace}";
            }
        }

        // Updated function signature and List type to include ThreadNumber and ThreadName
        public static List<(int ThreadNumber, uint ThreadId, ulong EntryPoint, ulong TEB, string ThreadName)> GetAllActiveThreadsFunc()
        {
            // Update the list's tuple definition
            var result = new List<(int ThreadNumber, uint ThreadId, ulong EntryPoint, ulong TEB, string ThreadName)>();
            THREADLIST_NATIVE nativeList = new THREADLIST_NATIVE();

            try
            {
                DbgGetThreadList(ref nativeList);

                if (nativeList.list != IntPtr.Zero && nativeList.count > 0)
                {
                    int sizeOfAllInfo = Marshal.SizeOf<THREADALLINFO>();
                    // Console.WriteLine($"DEBUG: Marshal.SizeOf<THREADALLINFO>() = {sizeOfAllInfo}"); // Keep for debugging

                    for (int i = 0; i < nativeList.count; i++)
                    {
                        IntPtr currentPtr = new IntPtr(nativeList.list.ToInt64() + (long)i * sizeOfAllInfo);
                        THREADALLINFO threadInfo = Marshal.PtrToStructure<THREADALLINFO>(currentPtr);

                        // Add the extended information to the result list
                        // This now matches the List's tuple definition
                        result.Add((
                            threadInfo.BasicInfo.ThreadNumber,
                            threadInfo.BasicInfo.ThreadId,
                            threadInfo.BasicInfo.ThreadStartAddress, // ulong
                            threadInfo.BasicInfo.ThreadLocalBase,    // ulong
                            threadInfo.BasicInfo.threadName          // string
                        ));
                    }
                }
                else if (nativeList.list == IntPtr.Zero && nativeList.count > 0)
                {
                    // Handle potential error case where count > 0 but list pointer is null
                    Console.WriteLine($"[GetAllActiveThreadsFunc] Warning: nativeList.count is {nativeList.count} but nativeList.list is IntPtr.Zero.");
                }
            }
            catch (Exception ex)
            {
                // Log or handle exceptions during marshalling/processing
                Console.WriteLine($"[GetAllActiveThreadsFunc] Exception during processing: {ex.Message}\n{ex.StackTrace}");
                // Optionally re-throw or return partial results depending on desired behavior
                throw; // Re-throwing is often appropriate unless you want to suppress errors
            }
            finally
            {
                if (nativeList.list != IntPtr.Zero)
                {
                    // Console.WriteLine($"DEBUG: Calling BridgeFree for IntPtr {nativeList.list}"); // Add debug log
                    //BridgeFree(nativeList.list); // Free the allocated memory - UNCOMMENT THIS!
                }
            }

            return result;
        }

        //public static List<(uint ThreadId, nuint EntryPoint, nuint TEB)> GetAllActiveThreadsFunc()
        //{
        //    var result = new List<(uint, nuint, nuint)>();

        //    THREADLIST threadList = new THREADLIST
        //    {
        //        Entries = new THREADENTRY[256]
        //    };

        //    DbgGetThreadList(ref threadList);

        //    for (int i = 0; i < threadList.Count; i++)
        //    {
        //        var t = threadList.Entries[i];
        //        result.Add((t.ThreadId, t.ThreadEntry, t.TebBase));
        //    }

        //    return result;
        //}



        [Command("GetAllRegisters", DebugOnly = true, MCPOnly = true, Category = CommandCategory.GeneralPurpose, MCPCmdDescription = "Returns the current values of all general-purpose registers (RAX–R15, RIP). Takes no arguments.")]
        public static string GetAllRegistersAsStrings()
        {
            Console.WriteLine("----------------------------------------");
            Console.WriteLine($"METHOD: {nameof(GetAllRegistersAsStrings)}");
            Console.WriteLine("----------------------------------------");
            string[] regNames = new[]
            {
                "rax", "rbx", "rcx", "rdx",
                "rsi", "rdi", "rbp", "rsp",
                "r8",  "r9",  "r10", "r11",
                "r12", "r13", "r14", "r15",
                "rip"
            };

            List<string> result = new List<string>();

            foreach (string reg in regNames)
            {
                try
                {
                    nuint val = Bridge.DbgValFromString(reg);
                    result.Add($"{reg.ToUpper(),-4}: {val.ToPtrString()}");
                }
                catch
                {
                    result.Add($"{reg.ToUpper(),-4}: <unavailable>");
                }
            }

            return string.Join("\r\n", result);
        }


        [Command("ReadDismAtAddress", DebugOnly = true, Category = CommandCategory.DebugFunctions, MCPOnly = true, MCPCmdDescription = "Disassembles instructions starting at an address and returns the listing (with bytes, labels, and any dereferenced strings) until the byte budget is reached. Example: ReadDismAtAddress address=0x12345678, byteCount=100")]
        public static string ReadDismAtAddress(
            [McpParam("Hex address to begin disassembling from.",
                Pattern = McpParamAttribute.HexAddressPattern,
                Examples = new[] { "0x12345678" })]
            string address,
            [McpParam("Number of bytes of code to disassemble starting at the address.",
                Minimum = 1, Examples = new[] { "100" })]
            int byteCount)
        {
            Console.WriteLine("----------------------------------------");
            Console.WriteLine($"METHOD: {nameof(ReadDismAtAddress)}");
            Console.WriteLine($"  - {nameof(address)}   : {address}");
            Console.WriteLine($"  - {nameof(byteCount)} : {byteCount}");
            Console.WriteLine("----------------------------------------");
            try
            {
                // Parse address string
                nuint MyAddresses = (nuint)Convert.ToUInt64(
                    address.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? address.Substring(2) : address,
                    address.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? 16 : 10
                );

                int instructionCount = 0;
                int bytesRead = 0;
                const int MAX_INSTRUCTIONS = 5000;

                var output = new StringBuilder();

                while (instructionCount < MAX_INSTRUCTIONS && bytesRead < byteCount)
                {
                    string label = GetLabel(MyAddresses);
                    if (!string.IsNullOrEmpty(label))
                    {
                        output.AppendLine();
                        output.AppendLine($"{label}:");
                    }

                    var disasm = new Bridge.BASIC_INSTRUCTION_INFO();
                    Bridge.DbgDisasmFastAt(MyAddresses, ref disasm);

                    if (disasm.size == 0)
                    {
                        MyAddresses += 1;
                        bytesRead += 1;
                        continue;
                    }

                    // Attempt string dereference
                    string inlineString = null;
                    nuint ptr = disasm.type == 1 ? disasm.value.value :
                                disasm.type == 2 ? disasm.addr : 0;

                    if (ptr != 0)
                    {
                        try
                        {
                            var strData = ReadMemory(ptr, 64);
                            int len = Array.IndexOf(strData, (byte)0);
                            if (len > 0)
                            {
                                var decoded = Encoding.ASCII.GetString(strData, 0, len);
                                if (decoded.All(c => c >= 0x20 && c < 0x7F))
                                {
                                    inlineString = decoded;
                                }
                            }
                        }
                        catch
                        {
                            // ignore bad memory access
                        }
                    }

                    string bytes = BitConverter.ToString(ReadMemory(MyAddresses, (uint)disasm.size));
                    output.Append($"{MyAddresses.ToPtrString()}  {bytes,-20}  {disasm.instruction}");
                    if (inlineString != null)
                        output.Append($"    ; \"{inlineString}\"");
                    output.AppendLine();

                    MyAddresses += (nuint)disasm.size;
                    bytesRead += disasm.size;
                    instructionCount++;
                }

                if (instructionCount >= MAX_INSTRUCTIONS)
                    output.AppendLine($"; Max instruction limit ({MAX_INSTRUCTIONS}) reached");

                if (bytesRead >= byteCount)
                    output.AppendLine($"; Byte read limit ({byteCount}) reached");

                return output.ToString();
            }
            catch (Exception ex)
            {
                return $"[GetDismAtAddress] Error: {ex.Message}";
            }
        }




        [Command("DumpModuleToFile", DebugOnly = true, MCPOnly = true, MCPCmdDescription = "Dumps the current module's register state and full disassembly (with labels and dereferenced strings) to a text file on disk. Example: DumpModuleToFile pfilepath=C:\\Output.txt")]
        public static void DumpModuleToFile(
            [McpParam("Absolute path of the output text file to write the dump to.",
                Examples = new[] { "C:\\Output.txt", "C:\\dump.txt" })]
            string pfilepath)
        {
            string filePath = pfilepath;//@"C:\dump.txt"; // Hardcoded file path as requested
            Console.WriteLine($"DumpModuleToFile: Attempting to dump module info to: {filePath}");

            try
            {
                // 1. Get current instruction pointer and module info
                var cip = Bridge.DbgValFromString("cip"); // Gets EIP or RIP depending on architecture
                var modInfo = new Module.ModuleInfo();

                if (!Module.InfoFromAddr(cip, ref modInfo))
                {
                    Console.Error.WriteLine($"Error: Could not find module information for address {cip.ToPtrString()}. Is the debugger attached and running?");
                    return;
                }


                var LoadedModules = GetAllModulesFromMemMapFunc();
                Console.WriteLine("Modules loaded Count: " + LoadedModules.Count);

                // Deconstruct into FOUR variables matching the tuple returned by the function
                foreach (var (name, path, baseAddr, size) in LoadedModules)
                {
                    // Calculate the end address correctly using baseAddr + size
                    nuint endAddr = baseAddr + size;
                    // Use the correct variables in the output string
                    // Added Path for context, and corrected End address calculation
                    Console.WriteLine($"{name,-30} Path: {path,-70} Base: 0x{baseAddr:X16} End: 0x{endAddr:X16} Size: 0x{size:X}");
                    // Or, if you only wanted the original 3 pieces of info (adjusting end calculation):
                    // Console.WriteLine($"{name,-20} 0x{baseAddr:X16} - 0x{endAddr:X16}");
                }

                IntPtr ptr = new IntPtr(0x14000140B); //Set to base address of module
                nuint address = (nuint)ptr.ToInt64();
                byte[] nops = Enumerable.Repeat((byte)0x90, 7).ToArray();

                bool success = WriteMemory(address, nops);

                if (success)
                {
                    Console.WriteLine($"Successfully patched {nops.Length} NOPs at 0x{address:X}");
                }
                else
                {
                    Console.WriteLine($"Failed to write memory at 0x{address:X}");
                }


                Console.WriteLine($"Found module '{modInfo.name}' at base {modInfo.@base.ToPtrString()}, size {modInfo.size:X}");

                // Use StreamWriter to write to the file
                using (var writer = new StreamWriter(filePath, false, Encoding.UTF8)) // Overwrite if exists
                {
                    // 2. Dump Registers
                    writer.WriteLine("--- Current Register State ---");
                    writer.WriteLine($"Module: {modInfo.name}");
                    writer.WriteLine($"Timestamp: {DateTime.Now}");
                    writer.WriteLine("-----------------------------");
                    // Add common registers (adjust for x86/x64 as needed, DbgValFromString handles it)
                    writer.WriteLine($"RAX: {Bridge.DbgValFromString("rax").ToPtrString()}");
                    writer.WriteLine($"RBX: {Bridge.DbgValFromString("rbx").ToPtrString()}");
                    writer.WriteLine($"RCX: {Bridge.DbgValFromString("rcx").ToPtrString()}");
                    writer.WriteLine($"RDX: {Bridge.DbgValFromString("rdx").ToPtrString()}");
                    writer.WriteLine($"RSI: {Bridge.DbgValFromString("rsi").ToPtrString()}");
                    writer.WriteLine($"RDI: {Bridge.DbgValFromString("rdi").ToPtrString()}");
                    writer.WriteLine($"RBP: {Bridge.DbgValFromString("rbp").ToPtrString()}");
                    writer.WriteLine($"RSP: {Bridge.DbgValFromString("rsp").ToPtrString()}");
                    writer.WriteLine($"RIP: {cip.ToPtrString()}"); // Use the 'cip' we already fetched
                    writer.WriteLine($"R8:  {Bridge.DbgValFromString("r8").ToPtrString()}");
                    writer.WriteLine($"R9:  {Bridge.DbgValFromString("r9").ToPtrString()}");
                    writer.WriteLine($"R10: {Bridge.DbgValFromString("r10").ToPtrString()}");
                    writer.WriteLine($"R11: {Bridge.DbgValFromString("r11").ToPtrString()}");
                    writer.WriteLine($"R12: {Bridge.DbgValFromString("r12").ToPtrString()}");
                    writer.WriteLine($"R13: {Bridge.DbgValFromString("r13").ToPtrString()}");
                    writer.WriteLine($"R14: {Bridge.DbgValFromString("r14").ToPtrString()}");
                    writer.WriteLine($"R15: {Bridge.DbgValFromString("r15").ToPtrString()}");
                    writer.WriteLine($"EFlags: {Bridge.DbgValFromString("eflags").ToPtrString()}"); // Or rflags
                    writer.WriteLine("-----------------------------");
                    writer.WriteLine(); // Add a blank line

                    // 3. Dump Disassembly and Labels
                    writer.WriteLine($"--- Disassembly for {modInfo.name} ({modInfo.@base.ToPtrString()} - {(modInfo.@base + modInfo.size).ToPtrString()}) ---");
                    writer.WriteLine("-----------------------------");

                    nuint currentAddr = modInfo.@base;
                    var endAddr = modInfo.@base + modInfo.size;
                    const int MAX_INSTRUCTIONS = 10000; // Limit number of instructions to prevent too large dumps
                    int instructionCount = 0;

                    // Write disassembly with labels
                    while (currentAddr < endAddr && instructionCount < MAX_INSTRUCTIONS)
                    {

                        // Get label at current address if exists
                        string label = GetLabel(currentAddr);
                        if (!string.IsNullOrEmpty(label))
                        {
                            writer.WriteLine();
                            writer.WriteLine($"{label}:");
                        }

                        // Disassemble instruction at current address
                        Bridge.BASIC_INSTRUCTION_INFO disasm = new Bridge.BASIC_INSTRUCTION_INFO();
                        Bridge.DbgDisasmFastAt(currentAddr, ref disasm);
                        if (disasm.size == 0)
                        {
                            // Failed to disassemble, move to next byte
                            currentAddr++;
                            continue;
                        }

                        //LabelMatchingInstruction(currentAddr, ref disasm);
                        //LabelMatchingBytes(currentAddr, new byte[] { 0x48, 0x85, 0xc0}, "Found Bytes");

                        // Attempt to dereference value or address for a potential string
                        string inlineString = null;
                        nuint possiblePtr = 0;

                        if (disasm.type == 1) // value (immediate)
                        {
                            possiblePtr = disasm.value.value;
                        }
                        else if (disasm.type == 2) // address
                        {
                            possiblePtr = disasm.addr;
                        }

                        if (possiblePtr != 0)
                        {
                            try
                            {
                                var strData = ReadMemory(possiblePtr, 64);
                                int len = Array.IndexOf(strData, (byte)0);
                                if (len > 0)
                                {
                                    inlineString = Encoding.ASCII.GetString(strData, 0, len);

                                    // Optional: filter printable ASCII
                                    if (inlineString.All(c => c >= 0x20 && c < 0x7F))
                                    {
                                        writer.WriteLine($"    ; \"{inlineString}\"");
                                    }
                                    else
                                    {
                                        inlineString = null;
                                    }
                                }
                            }
                            catch
                            {
                                // Ignore invalid memory
                            }
                        }

                        // Format and write instruction
                        string bytes = BitConverter.ToString(ReadMemory(currentAddr, (uint)disasm.size)); //.Replace("-", " ")
                        writer.WriteLine($"{currentAddr.ToPtrString()}  {bytes,-20}  {disasm.instruction}");

                        // Move to next instruction
                        currentAddr += (nuint)disasm.size;
                        instructionCount++;

                        // If we've hit a lot of instructions for one section, add a progress note
                        if (instructionCount % 1000 == 0)
                        {
                            //Console.WriteLine($"Dumped {instructionCount} instructions...");
                        }
                    }

                    if (instructionCount >= MAX_INSTRUCTIONS)
                    {
                        writer.WriteLine();
                        writer.WriteLine($"--- Instruction limit ({MAX_INSTRUCTIONS}) reached. Dump truncated. ---");
                    }

                    writer.WriteLine("-----------------------------");
                    writer.WriteLine("--- Dump Complete ---");
                } // StreamWriter is automatically flushed and closed here

                Console.WriteLine($"Successfully dumped module '{modInfo.name}' and registers to {filePath}");
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.Error.WriteLine($"Error: Access denied writing to '{filePath}'. Try running x64dbg as administrator or choose a different path. Details: {ex.Message}");
            }
            catch (IOException ex)
            {
                Console.Error.WriteLine($"Error: An I/O error occurred while writing to '{filePath}'. Details: {ex.Message}");
            }
            catch (Exception ex) // Catch-all for other unexpected errors
            {
                Console.Error.WriteLine($"An unexpected error occurred: {ex.GetType().Name} - {ex.Message}");
                Console.Error.WriteLine(ex.StackTrace); // Log stack trace for debugging
            }
        }
    }
}