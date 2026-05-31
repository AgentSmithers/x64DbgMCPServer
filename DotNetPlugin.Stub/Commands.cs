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
