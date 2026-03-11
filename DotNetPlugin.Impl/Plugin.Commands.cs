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
using DotNetPlugin.NativeBindings;
using DotNetPlugin.NativeBindings.Script;
using DotNetPlugin.NativeBindings.SDK;
using DotNetPlugin.Properties;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using static DotNetPlugin.NativeBindings.SDK.Bridge;

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

        [Command("StartMCPServer", DebugOnly = false)]
        public static void cbStartMCPServer(string[] args)
        {
            Console.WriteLine("Starting MCPServer");
            GMcpServerConfig = McpServerConfig.Load();
            GSimpleMcpServer = new SimpleMcpServer(typeof(DotNetPlugin.Plugin), GMcpServerConfig);
            GSimpleMcpServer.Start();
            Console.WriteLine("MCPServer Started");
            Console.WriteLine($"MCP Server URL: {GMcpServerConfig.GetDisplayUrl()}");
        }

        [Command("StopMCPServer", DebugOnly = false)]
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

        [Command("ExecuteDebuggerCommandWithVar", DebugOnly = false, MCPOnly = true, MCPCmdDescription = "Execute a command then return a debugger variable. Example: ExecuteDebuggerCommandWithVar command=init notepad.exe, resultVar=$pid, pollMs=100, pollTimeoutMs=5000")]
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

        [Command("ExecuteDebuggerCommandWithOutput", DebugOnly = false, MCPOnly = true, MCPCmdDescription = "Execute a command and return captured log output. Example: ExecuteDebuggerCommandWithOutput command=\"bplist\"")]
        public static string ExecuteDebuggerCommandWithOutput(string command, int settleDelayMs = 200)
        {
            string tempFile = null;
            try
            {
                Console.WriteLine("Executing DebuggerCommandWithOutput: " + command);

                tempFile = Path.Combine(Path.GetTempPath(), "x64dbg_cmd_" + Guid.NewGuid().ToString("N") + ".log");

                // Start redirection
                DbgCmdExec($"LogRedirect \"{tempFile}\"");
                Thread.Sleep(50);

                // Execute the actual command
                var ok = DbgCmdExec(command);
                Thread.Sleep(settleDelayMs);

                // Stop redirection
                DbgCmdExec("LogRedirectStop");
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

        [Command("GetBreakpointInfo", DebugOnly = true, MCPOnly = true, MCPCmdDescription = "Get breakpoint information using alternative methods. Example: GetBreakpointInfo")]
        public static string GetBreakpointInfo()
        {
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
                    var result = ExecuteDebuggerCommandWithOutput("bplist", 500);
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

        [Command("ListDebuggerCommands", DebugOnly = false, MCPOnly = true, MCPCmdDescription = "Example: ListDebuggerCommands")]
        public static string ListDebuggerCommands(string subject = "")
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

        [Command("DbgValFromString", DebugOnly = false, MCPOnly = true, MCPCmdDescription = "Example: DbgValFromString value=$pid")]
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


        [Command("ExecuteDebuggerCommandDirect", DebugOnly = false)]
        public static bool ExecuteDebuggerCommandDirect(string[] args)
        {
            return ExecuteDebuggerCommandDirect(args);
        }

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

        [Command("WriteMemToAddress", DebugOnly = true, MCPOnly = true, MCPCmdDescription = "Example: WriteMemToAddress address=0x12345678, byteString=0F FF 90")]
        public static string WriteMemToAddress(string address, string byteString)
        {
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

        [Command("CommentOrLabelAtAddress", DebugOnly = true, MCPOnly = true, MCPCmdDescription = "Example: CommentOrLabelAtAddress address=0x12345678, value=LabelTextGoeshere, mode=Label\r\nExample: CommentOrLabelAtAddress address=0x12345678, value=LabelTextGoeshere, mode=Comment\r\n")]
        public static string CommentOrLabelAtAddress(string address, string value, string mode = "Label")
        {
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

        [Command("GetLabel", DebugOnly = true, MCPOnly = true, MCPCmdDescription = "Example: GetLabel addressStr=0x12345678")]
        public static string GetLabel(string addressStr)
        {
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


        [Command("GetAllModulesFromMemMap", DebugOnly = true, MCPOnly = true, MCPCmdDescription = "Example: GetAllModulesFromMemMap")]
        public static string GetAllModulesFromMemMap()
        {
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


        // Define a struct to hold the frame info we can gather
        public struct CallStackFrameInfo
        {
            public nuint FrameAddress; // Value of RBP for this frame
            public nuint ReturnAddress; // Address execution returns to
            public nuint FrameSize;     // Calculated size (approx)
        }

        // Modified function to return richer frame info
        public static List<CallStackFrameInfo> GetCallStackFunc(int maxFrames = 32)
        {
            var callstack = new List<CallStackFrameInfo>();
            byte[] addrBuffer = new byte[sizeof(ulong)]; // Buffer for reading addresses (nuint size)

            // Get initial stack pointers from the debugger
            // Ensure DbgValFromString is correctly implemented via P/Invoke
            nuint rbp = DbgValFromStringAsNUInt("rbp");
            nuint rsp = DbgValFromStringAsNUInt("rsp");
            nuint currentRbp = rbp;
            nuint previousRbp = 0; // To calculate frame size

            if (rbp == 0 || rbp < rsp) // Initial check if RBP is valid
            {
                Console.WriteLine("[GetCallStackFunc] Initial RBP is invalid or below RSP.");
                return callstack;
            }

            for (int i = 0; i < maxFrames; i++)
            {
                // 1. Read Return Address from [RBP + 8] (or [RBP + sizeof(nuint)])
                if (!DbgMemRead(currentRbp + (nuint)sizeof(ulong), addrBuffer, (nuint)sizeof(ulong)))
                {
                    Console.WriteLine($"[GetCallStackFunc] Failed to read return address at 0x{currentRbp + (nuint)sizeof(ulong):X}");
                    break; // Stop if memory read fails
                }
                nuint returnAddress = (nuint)BitConverter.ToUInt64(addrBuffer, 0);

                // Stop if return address is null (often end of chain)
                if (returnAddress == 0)
                {
                    Console.WriteLine("[GetCallStackFunc] Reached null return address.");
                    break;
                }

                // 2. Read Saved RBP value from [RBP]
                if (!DbgMemRead(currentRbp, addrBuffer, (nuint)sizeof(ulong)))
                {
                    Console.WriteLine($"[GetCallStackFunc] Failed to read saved RBP at 0x{currentRbp:X}");
                    break; // Stop if memory read fails
                }
                nuint nextRbp = (nuint)BitConverter.ToUInt64(addrBuffer, 0);

                // Calculate frame size (difference between current and previous RBP)
                // Size is only meaningful after the first frame
                nuint frameSize = (previousRbp > 0 && currentRbp > previousRbp) ? 0 : // Avoid nonsensical size if RBP decreased
                                  (previousRbp > 0) ? previousRbp - currentRbp : 0;


                // Add collected info for this frame
                callstack.Add(new CallStackFrameInfo
                {
                    FrameAddress = currentRbp,
                    ReturnAddress = returnAddress,
                    FrameSize = frameSize
                });

                // Update RBP for the next iteration
                previousRbp = currentRbp; // Store current RBP before updating
                currentRbp = nextRbp;

                // Validate the next RBP value
                if (currentRbp == 0 || currentRbp < rsp || currentRbp <= previousRbp) // RBP must be > RSP and generally increase (move down stack)
                {
                    Console.WriteLine($"[GetCallStackFunc] Invalid next RBP (0x{currentRbp:X}). Previous=0x{previousRbp:X}, RSP=0x{rsp:X}. Stopping walk.");
                    break; // Stop if RBP becomes null, goes below RSP, or doesn't advance
                }
            }

            return callstack;
        }


        [Command("GetCallStack", DebugOnly = true, MCPOnly = true, MCPCmdDescription = "Example: GetCallStack\r\nExample: GetCallStack, maxFrames=32")]
        public static string GetCallStack(int maxFrames = 32)
        {
            // Define buffer sizes matching C++ MAX_ defines
            const int MAX_MODULE_SIZE_BUFF = 256;
            const int MAX_LABEL_SIZE_BUFF = 256;
            const int MAX_COMMENT_SIZE_BUFF = 512;

            try
            {
                var callstackFrames = GetCallStackFunc(maxFrames); // This still returns List<CallStackFrameInfo>

                if (callstackFrames.Count == 0)
                    return "[GetCallStack] Call stack could not be retrieved (check RBP validity or use debugger UI).";

                var output = new StringBuilder();
                output.AppendLine($"[GetCallStack] Retrieved {callstackFrames.Count} frames (RBP walk, may be inaccurate):");
                output.AppendLine($"{"Frame",-5} {"Frame Addr",-18} {"Return Addr",-18} {"Size",-10} {"Module",-25} {"Label/Symbol",-40} {"Comment"}");
                output.AppendLine(new string('-', 130));

                // --- Manual Marshalling Setup ---
                IntPtr ptrModule = IntPtr.Zero;
                IntPtr ptrLabel = IntPtr.Zero;
                IntPtr ptrComment = IntPtr.Zero;
                BRIDGE_ADDRINFO_NATIVE addrInfo = new BRIDGE_ADDRINFO_NATIVE(); // Must be NATIVE struct

                for (int i = 0; i < callstackFrames.Count; i++)
                {
                    var frame = callstackFrames[i];
                    string moduleStr = "N/A";
                    string labelStr = "N/A";
                    string commentStr = "";

                    try // Use try/finally to guarantee freeing allocated memory
                    {
                        // 1. Allocate native buffers
                        ptrModule = Marshal.AllocHGlobal(MAX_MODULE_SIZE_BUFF);
                        ptrLabel = Marshal.AllocHGlobal(MAX_LABEL_SIZE_BUFF);
                        ptrComment = Marshal.AllocHGlobal(MAX_COMMENT_SIZE_BUFF);

                        // Initialize buffers slightly for safety (optional, helps debugging)
                        Marshal.WriteByte(ptrModule, 0, 0);
                        Marshal.WriteByte(ptrLabel, 0, 0);
                        Marshal.WriteByte(ptrComment, 0, 0);

                        // 2. Prepare the struct
                        addrInfo.module = ptrModule;
                        addrInfo.label = ptrLabel;
                        addrInfo.comment = ptrComment;
                        // Set flags for desired info
                        addrInfo.flags = ADDRINFOFLAGS.flagmodule | ADDRINFOFLAGS.flaglabel | ADDRINFOFLAGS.flagcomment;

                        // 3. Call the native function (use correct struct type)
                        bool success = DbgAddrInfoGet(frame.ReturnAddress, 0, ref addrInfo); // Pass NATIVE struct

                        // 4. Read results back from native buffers if call succeeded
                        if (success)
                        {
                            moduleStr = Marshal.PtrToStringAnsi(addrInfo.module) ?? "N/A"; // Read from buffer
                            labelStr = Marshal.PtrToStringAnsi(addrInfo.label) ?? "N/A";   // Read from buffer

                            string retrievedComment = Marshal.PtrToStringAnsi(addrInfo.comment) ?? "";
                            if (!string.IsNullOrEmpty(retrievedComment))
                            {
                                // Handle auto-comment marker (\1) if present
                                if (retrievedComment.Length > 0 && retrievedComment[0] == '\x01')
                                {
                                    commentStr = retrievedComment.Length > 1 ? retrievedComment.Substring(1) : "";
                                }
                                else
                                {
                                    commentStr = retrievedComment;
                                }
                            }
                        }
                        else
                        {
                            // Fallback if DbgAddrInfoGet fails
                            var modInfoOnly = new BRIDGE_ADDRINFO_NATIVE { flags = ADDRINFOFLAGS.flagmodule, module = ptrModule };
                            Marshal.WriteByte(ptrModule, 0, 0); // Clear buffer before reuse
                            if (DbgAddrInfoGet(frame.ReturnAddress, 0, ref modInfoOnly))
                            {
                                moduleStr = Marshal.PtrToStringAnsi(modInfoOnly.module) ?? "Lookup Failed";
                            }
                            else
                            {
                                moduleStr = "Lookup Failed";
                            }
                            labelStr = ""; // Clear label/comment if lookup failed
                            commentStr = "";
                        }
                    }
                    finally // 5. CRITICAL: Free allocated native memory
                    {
                        if (ptrModule != IntPtr.Zero) Marshal.FreeHGlobal(ptrModule);
                        if (ptrLabel != IntPtr.Zero) Marshal.FreeHGlobal(ptrLabel);
                        if (ptrComment != IntPtr.Zero) Marshal.FreeHGlobal(ptrComment);
                    }
                    // --- End Manual Marshalling ---

                    // Format the output line
                    output.AppendLine($"{$"[{i}]",-5} 0x{frame.FrameAddress:X16} 0x{frame.ReturnAddress:X16} {($"0x{frame.FrameSize:X}"),-10} {moduleStr,-25} {labelStr,-40} {commentStr}");
                } // End for loop

                return output.ToString().TrimEnd(); // remove trailing newline
            }
            catch (Exception ex)
            {
                return $"[GetCallStack] Error: {ex.Message}\n{ex.StackTrace}";
            }
        }

        // GetCallStackFunc remains the same as the previous version, returning List<CallStackFrameInfo>
        // public static List<CallStackFrameInfo> GetCallStackFunc(int maxFrames = 32) { ... }


        //[Command("GetCallStack", DebugOnly = true, MCPOnly = true)]
        //public static string GetCallStack(int maxFrames = 32)
        //{
        //    try
        //    {
        //        var callstack = GetCallStackFunc(maxFrames);

        //        if (callstack.Count == 0)
        //            return "[GetCallStack] No call stack could be retrieved.";

        //        var output = new StringBuilder();
        //        output.AppendLine($"[GetCallStack] Retrieved {callstack.Count} frames:");

        //        for (int i = 0; i < callstack.Count; i++)
        //        {
        //            output.AppendLine($"Frame {i,2}: 0x{callstack[i]:X}");
        //        }

        //        return output.ToString().TrimEnd(); // remove trailing newline
        //    }
        //    catch (Exception ex)
        //    {
        //        return $"[GetCallStack] Error: {ex.Message}";
        //    }
        //}

        //public static List<nuint> GetCallStackFunc(int maxFrames = 32)
        //{
        //    List<nuint> callstack = new List<nuint>();

        //    nuint rbp = Bridge.DbgValFromString("rbp");
        //    nuint rsp = Bridge.DbgValFromString("rsp");

        //    for (int i = 0; i < maxFrames; i++)
        //    {
        //        // Read return address (next value after saved RBP)
        //        byte[] addrBuffer = new byte[8]; // 64-bit address
        //        if (!Bridge.DbgMemRead(rbp + 8, addrBuffer, 8))
        //            break;

        //        nuint returnAddress = (nuint)BitConverter.ToUInt64(addrBuffer, 0);
        //        if (returnAddress == 0)
        //            break;

        //        callstack.Add(returnAddress);

        //        // Read the previous RBP
        //        if (!Bridge.DbgMemRead(rbp, addrBuffer, 8))
        //            break;

        //        rbp = (nuint)BitConverter.ToUInt64(addrBuffer, 0);
        //        if (rbp == 0 || rbp < rsp)
        //            break; // Invalid frame or stack unwound
        //    }

        //    return callstack;
        //}

        [Command("GetAllActiveThreads", DebugOnly = true, MCPOnly = true, MCPCmdDescription = "Example: GetAllActiveThreads")]
        public static string GetAllActiveThreads()
        {
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



        [Command("GetAllRegisters", DebugOnly = true, MCPOnly = true, MCPCmdDescription = "Example: GetAllRegisters")]
        public static string GetAllRegistersAsStrings()
        {
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


        [Command("ReadDismAtAddress", DebugOnly = true, MCPOnly = true, MCPCmdDescription = "Example: ReadDismAtAddress address=0x12345678, byteCount=100")]
        public static string ReadDismAtAddress(string address, int byteCount)
        {
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




        [Command("DumpModuleToFile", DebugOnly = true, MCPOnly = true, MCPCmdDescription = "Example: DumpModuleToFile pfilepath=C:\\Output.txt")]
        public static void DumpModuleToFile(string[] pfilepath)
        {
            string filePath = pfilepath[0];//@"C:\dump.txt"; // Hardcoded file path as requested
            Console.WriteLine($"Attempting to dump module info to: {filePath}");

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
