using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using DotNetPlugin.NativeBindings.SDK;
using RGiesecke.DllExport;
using System.Drawing;

namespace DotNetPlugin
{
    /// <summary>
    /// Contains entry points for plugin lifecycle and debugger event callbacks.
    /// </summary>
    internal static class PluginMain
    {
#if ALLOW_UNLOADING
        private static readonly Lazy<IPluginSession> NullSession = new Lazy<IPluginSession>(() => PluginSession.Null, LazyThreadSafetyMode.PublicationOnly);
        private static volatile Lazy<IPluginSession> s_session = NullSession;
        private static IPluginSession Session => s_session.Value;

        private static readonly string s_controlCommand = PluginBase.PluginName.Replace(' ', '_');

        internal static readonly string ImplAssemblyLocation;
#else
        private static PluginSession Session = PluginSession.Null;
#endif

        private static int s_pluginHandle;
        private static Plugins.PLUG_SETUPSTRUCT s_setupStruct;

        private static Assembly TryLoadAssemblyFrom(AssemblyName assemblyName, string location, bool tryLoadFromMemory = false)
        {
            var pluginBasePath = Path.GetDirectoryName(location);
            var dllPath = Path.Combine(pluginBasePath, assemblyName.Name + ".dll");

            if (!File.Exists(dllPath))
                return null;

            if (tryLoadFromMemory)
            {
                var assemblyBytes = File.ReadAllBytes(dllPath);
                // first we try to load the assembly from memory so that it doesn't get locked
                try { return Assembly.Load(assemblyBytes); }
                // mixed-mode assemblies can't be loaded from memory, so we resort to loading it from the disk
                catch { }
            }

            return Assembly.LoadFile(dllPath);
        }

        static PluginMain()
        {
            if (AppDomain.CurrentDomain.IsDefaultAppDomain())
            {
                AppDomain.CurrentDomain.UnhandledException += (s, e) => LogUnhandledException(e.ExceptionObject);

                // by default the runtime will look for referenced assemblies in the directory of the host application,
                // not in the plugin's dictionary, so we need to customize assembly resolving to fix this
                AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
                {
                    var assemblyName = new AssemblyName(e.Name);

                    if (assemblyName.Name == typeof(PluginMain).Assembly.GetName().Name)
                        return typeof(PluginMain).Assembly;

                    return TryLoadAssemblyFrom(assemblyName, typeof(PluginMain).Assembly.Location);
                };
            }
#if ALLOW_UNLOADING
            else
            {
                AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
                {
                    var assemblyName = new AssemblyName(e.Name);

                    if (assemblyName.Name == typeof(PluginMain).Assembly.GetName().Name)
                        return typeof(PluginMain).Assembly;

                    return
                        (ImplAssemblyLocation != null ? TryLoadAssemblyFrom(assemblyName, ImplAssemblyLocation, tryLoadFromMemory: true) : null) ??
                        TryLoadAssemblyFrom(assemblyName, typeof(PluginMain).Assembly.Location, tryLoadFromMemory: true);
                };
            }

            using (var resourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("build.meta"))
            {
                if (resourceStream == null)
                    return;

                ImplAssemblyLocation = new StreamReader(resourceStream).ReadLine();
            }
#endif
        }

        public static void LogUnhandledException(object exceptionObject)
        {
            var location = typeof(PluginMain).Assembly.Location;
            var logPath = Path.ChangeExtension(location, ".log");

            var errorMessage = exceptionObject?.ToString();
            if (errorMessage != null)
            {
                errorMessage += Environment.NewLine;
                File.AppendAllText(logPath, errorMessage);
                PluginBase.LogError(errorMessage);
            }
        }

#if ALLOW_UNLOADING
        private static void HandleImplChanged(object sender)
        {
            var session = s_session;
            if (ReferenceEquals(session.Value, sender) && UnloadPlugin(session))
                LoadPlugin(session);
        }
        
        private static bool LoadPlugin(Lazy<IPluginSession> reloadedSession = null)
        {
            if (!TryLoadPlugin(isInitial: false, reloadedSession))
            {
                PluginBase.LogError("Failed to load the implementation assembly.");
                return false;
            }

            Session.PluginHandle = s_pluginHandle;

            if (!Session.Init())
            {
                PluginBase.LogError("Failed to initialize the implementation assembly.");
                TryUnloadPlugin();
                return false;
            }

            Session.Setup(ref s_setupStruct);

            PluginBase.LogInfo("Successfully loaded the implementation assembly.");
            return true;
        }

        private static bool UnloadPlugin(Lazy<IPluginSession> reloadedSession = null)
        {
            if (!TryUnloadPlugin(reloadedSession))
            {
                PluginBase.LogError("Failed to unload the implementation assembly.");
                return false;
            }

            PluginBase.LogInfo("Successfully unloaded the implementation assembly.");
            return true;
        }

        private static bool TryLoadPlugin(bool isInitial, Lazy<IPluginSession> reloadedSession = null)
        {
            var expectedSession = reloadedSession ?? NullSession;
            var newSession = new Lazy<IPluginSession>(() => new PluginSessionProxy(HandleImplChanged), LazyThreadSafetyMode.ExecutionAndPublication);
            var originalSession = Interlocked.CompareExchange(ref s_session, newSession, expectedSession);
            if (originalSession == expectedSession)
            {
                _ = newSession.Value; // forces creation of session

                return true;
            }

            return false;
        }

        private static bool TryUnloadPlugin(Lazy<IPluginSession> reloadedSession = null)
        {
            Lazy<IPluginSession> originalSession;

            if (reloadedSession == null)
            {
                originalSession = Interlocked.Exchange(ref s_session, NullSession);
            }
            else
            {
                originalSession =
                    Interlocked.CompareExchange(ref s_session, reloadedSession, reloadedSession) == reloadedSession ?
                    reloadedSession :
                    NullSession;
            }

            if (originalSession != NullSession)
            {
                originalSession.Value.Dispose();
                return true;
            }

            return false;
        }

#else
        private static bool TryLoadPlugin(bool isInitial)
        {
            if (isInitial)
            {
                Session = new PluginSession();
                return true;
            }

            return false;
        }

        private static bool TryUnloadPlugin()
        {
            return false;
        }
#endif

        [RGiesecke.DllExport.DllExport("pluginit", CallingConvention.Cdecl)]
        public static bool pluginit(ref Plugins.PLUG_INITSTRUCT initStruct)
        {
            if (!TryLoadPlugin(isInitial: true))
                return false;

            initStruct.sdkVersion = Plugins.PLUG_SDKVERSION;
            initStruct.pluginVersion = PluginBase.PluginVersion;
            initStruct.pluginName = PluginBase.PluginName;
            Session.PluginHandle = s_pluginHandle = initStruct.pluginHandle;

#if ALLOW_UNLOADING
            if (!Plugins._plugin_registercommand(s_pluginHandle, s_controlCommand, ControlCommand, false))
            {
                PluginBase.LogError($"Failed to register the \"'{s_controlCommand}'\" command.");
                TryUnloadPlugin();
                return false;
            }
#endif

            if (!Session.Init())
            {
                PluginBase.LogError("Failed to initialize the implementation assembly.");
                TryUnloadPlugin();
                return false;
            }

            return true;
        }

        [RGiesecke.DllExport.DllExport("plugsetup", CallingConvention.Cdecl)]
        private static void plugsetup(ref Plugins.PLUG_SETUPSTRUCT setupStruct)
        {
            s_setupStruct = setupStruct;

            Session.Setup(ref setupStruct);
        }

        [RGiesecke.DllExport.DllExport("plugstop", CallingConvention.Cdecl)]
        private static bool plugstop()
        {
            var success = Session.Stop();

#if ALLOW_UNLOADING
            Plugins._plugin_unregistercommand(s_pluginHandle, s_controlCommand);
#endif

            s_setupStruct = default;
            s_pluginHandle = default;

            return success;
        }

#if ALLOW_UNLOADING
        private static bool ControlCommand(string[] args)
        {
            if (args.Length > 1)
            {
                if ("load".Equals(args[1], StringComparison.OrdinalIgnoreCase))
                {
                    return LoadPlugin();
                }
                else if ("unload".Equals(args[1], StringComparison.OrdinalIgnoreCase))
                {
                    return UnloadPlugin();
                }
            }

            PluginBase.LogError($"Invalid syntax. Usage: {s_controlCommand} [load|unload]");
            return false;
        }
#endif

        [RGiesecke.DllExport.DllExport("CBMENUENTRY", CallingConvention.Cdecl)]
        public static void CBMENUENTRY(Plugins.CBTYPE cbType, ref Plugins.PLUG_CB_MENUENTRY info)
        {
            Session.OnMenuEntry(ref info);
        }

        [RGiesecke.DllExport.DllExport("plugingeticon", CallingConvention.Cdecl)]
        public static IntPtr plugingeticon()
        {
            // Icon handled via managed menu icon in Impl; no stub icon.
            return IntPtr.Zero;
        }
    }
}
