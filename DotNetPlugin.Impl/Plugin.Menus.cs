using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using DotNetPlugin.NativeBindings.SDK;
using DotNetPlugin.Properties;

namespace DotNetPlugin
{
    partial class Plugin
    {
        protected override void SetupMenu(Menus menus)
        {
            // Set main plugin menu icon (PNG resource only)
            try
            {
                menus.Main.SetIcon(Resources.MainIcon);
            }
            catch
            {
                // Last resort: keep default without icon
            }

            menus.Main
                .AddAndConfigureItem("&Start MCP Server", StartMCPServer).SetIcon(Resources.AboutIcon).Parent
                .AddAndConfigureItem("&Stop MCP Server", StopMCPServer).SetIcon(Resources.AboutIcon).Parent
                .AddSeparator()
                .AddAndConfigureItem("&Configure MCP Server...", ConfigureMCPServer).SetIcon(Resources.AboutIcon).Parent
                .AddSeparator()
                .AddAndConfigureItem("&About...", OnAboutMenuItem).SetIcon(Resources.AboutIcon);
        }

        public void OnAboutMenuItem(MenuItem menuItem)
        {
            MessageBox.Show(HostWindow, "x64DbgMCPServer Plugin For x64dbg\nCoded By AgentSmithers", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public static void OnDumpMenuItem(MenuItem menuItem)
        {
            if (!Bridge.DbgIsDebugging())
            {
                Console.WriteLine("You need to be debugging to use this Command");
                return;
            }
            Bridge.DbgCmdExec("DotNetDumpProcess");
        }

        public static void ExecuteCustomCommand(MenuItem menuItem)
        {
            if (!Bridge.DbgIsDebugging())
            {
                Console.WriteLine("You need to be debugging to use this Command");
                return;
            }
            Bridge.DbgCmdExec("DumpModuleToFile");
        }
        public static void StartMCPServer(MenuItem menuItem)
        {
            Bridge.DbgCmdExec("StartMCPServer");
        }
        public static void StopMCPServer(MenuItem menuItem)
        {
            Bridge.DbgCmdExec("StopMCPServer");
        }

        public static void ConfigureMCPServer(MenuItem menuItem)
        {
            // Load current configuration
            var config = Plugin.GetMcpServerConfig();

            // Show dialog on STA thread (required for Windows Forms)
            var t = new Thread(() =>
            {
                using (var dialog = new McpConfigDialog(config))
                {
                    if (dialog.ShowDialog() == DialogResult.OK)
                    {
                        // Update and save configuration
                        config.IpAddress = dialog.IpAddress;
                        config.Port = dialog.Port;
                        Plugin.SetMcpServerConfig(config);

                        MessageBox.Show(
                            $"Configuration saved.\n\nNew URL: {config.GetDisplayUrl()}\n\nPlease restart the MCP server for changes to take effect.",
                            "MCP Server Configuration",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
        }
    }
}
