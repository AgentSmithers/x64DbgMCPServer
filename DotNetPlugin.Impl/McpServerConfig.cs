using System;
using System.IO;
using System.Web.Script.Serialization;

namespace DotNetPlugin
{
    /// <summary>
    /// Configuration for the MCP server including IP address and port settings.
    /// Settings are persisted to a JSON file in the plugin directory.
    /// </summary>
    public class McpServerConfig
    {
        public string IpAddress { get; set; } = "+";
        public int Port { get; set; } = 50300;

        private static string _configPath;

        /// <summary>
        /// Gets the path to the configuration file in the plugin directory.
        /// </summary>
        public static string ConfigPath
        {
            get
            {
                if (_configPath == null)
                {
                    var assemblyDir = Path.GetDirectoryName(typeof(McpServerConfig).Assembly.Location);
                    _configPath = Path.Combine(assemblyDir, "mcp_config.json");
                }
                return _configPath;
            }
        }

        /// <summary>
        /// Gets the base URL for the HTTP listener (e.g., "http://+:50300/").
        /// </summary>
        public string GetBaseUrl()
        {
            return $"http://{IpAddress}:{Port}/";
        }

        /// <summary>
        /// Gets the SSE endpoint URL for display purposes.
        /// </summary>
        public string GetDisplayUrl()
        {
            string displayIp = IpAddress == "+" ? "127.0.0.1" : IpAddress;
            return $"http://{displayIp}:{Port}/sse";
        }

        /// <summary>
        /// Loads the configuration from the JSON file, or returns default settings if the file doesn't exist.
        /// </summary>
        public static McpServerConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    var serializer = new JavaScriptSerializer();
                    var config = serializer.Deserialize<McpServerConfig>(json);
                    if (config != null)
                    {
                        // Validate loaded values
                        if (string.IsNullOrWhiteSpace(config.IpAddress))
                            config.IpAddress = "+";
                        if (config.Port < 1 || config.Port > 65535)
                            config.Port = 50300;
                        return config;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[McpServerConfig] Error loading config: {ex.Message}. Using defaults.");
            }

            return new McpServerConfig();
        }

        /// <summary>
        /// Saves the current configuration to the JSON file.
        /// </summary>
        public void Save()
        {
            try
            {
                var serializer = new JavaScriptSerializer();
                var json = serializer.Serialize(this);

                // Ensure directory exists
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(ConfigPath, json);
                Console.WriteLine($"[McpServerConfig] Configuration saved to: {ConfigPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[McpServerConfig] Error saving config: {ex.Message}");
            }
        }

        /// <summary>
        /// Validates the IP address format.
        /// </summary>
        public static bool IsValidIpAddress(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip))
                return false;

            // Special values for HttpListener
            if (ip == "+" || ip == "*" || ip == "localhost")
                return true;

            // Check for valid IP address format
            System.Net.IPAddress address;
            return System.Net.IPAddress.TryParse(ip, out address);
        }

        /// <summary>
        /// Validates the port number.
        /// </summary>
        public static bool IsValidPort(int port)
        {
            return port >= 1 && port <= 65535;
        }
    }
}
