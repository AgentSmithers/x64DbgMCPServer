using System;
using System.Drawing;
using System.Windows.Forms;

namespace DotNetPlugin
{
    /// <summary>
    /// Configuration dialog for the MCP server settings.
    /// Allows users to configure the IP address and port for the HTTP listener.
    /// </summary>
    public class McpConfigDialog : Form
    {
        private TextBox txtIpAddress;
        private NumericUpDown numPort;
        private Button btnSave;
        private Button btnCancel;
        private Label lblHelp;
        private Label lblCurrentUrl;

        public string IpAddress => txtIpAddress.Text.Trim();
        public int Port => (int)numPort.Value;

        public McpConfigDialog(McpServerConfig currentConfig)
        {
            InitializeComponents(currentConfig);
            UpdateUrlPreview();
        }

        private void InitializeComponents(McpServerConfig config)
        {
            // Form settings
            Text = "MCP Server Configuration";
            Width = 420;
            Height = 280;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;

            // IP Address Label
            var lblIp = new Label
            {
                Text = "IP Address:",
                Left = 15,
                Top = 20,
                Width = 80,
                TextAlign = ContentAlignment.MiddleRight
            };

            // IP Address TextBox
            txtIpAddress = new TextBox
            {
                Left = 100,
                Top = 18,
                Width = 180,
                Text = config.IpAddress
            };
            txtIpAddress.TextChanged += (s, e) => UpdateUrlPreview();

            // IP Help Label
            var lblIpHelp = new Label
            {
                Text = "(+, *, localhost, or IP)",
                Left = 285,
                Top = 20,
                Width = 110,
                ForeColor = Color.Gray,
                Font = new Font(Font.FontFamily, 8)
            };

            // Port Label
            var lblPort = new Label
            {
                Text = "Port:",
                Left = 15,
                Top = 55,
                Width = 80,
                TextAlign = ContentAlignment.MiddleRight
            };

            // Port NumericUpDown
            numPort = new NumericUpDown
            {
                Left = 100,
                Top = 53,
                Width = 100,
                Minimum = 1,
                Maximum = 65535,
                Value = config.Port
            };
            numPort.ValueChanged += (s, e) => UpdateUrlPreview();

            // URL Preview Label
            var lblUrlTitle = new Label
            {
                Text = "Server URL:",
                Left = 15,
                Top = 95,
                Width = 80,
                TextAlign = ContentAlignment.MiddleRight
            };

            lblCurrentUrl = new Label
            {
                Left = 100,
                Top = 95,
                Width = 280,
                Height = 20,
                ForeColor = Color.DarkBlue,
                Font = new Font(Font.FontFamily, 9, FontStyle.Bold)
            };

            // Help text
            lblHelp = new Label
            {
                Left = 15,
                Top = 125,
                Width = 375,
                Height = 60,
                Text = "Notes:\n" +
                       "• Use '+' or '*' to listen on all interfaces (requires admin or urlacl)\n" +
                       "• Use '127.0.0.1' or 'localhost' for local-only access\n" +
                       "• Restart the MCP server after changing settings",
                ForeColor = Color.DimGray,
                Font = new Font(Font.FontFamily, 8)
            };

            // Save Button
            btnSave = new Button
            {
                Text = "Save",
                Left = 210,
                Top = 195,
                Width = 85,
                Height = 30,
                DialogResult = DialogResult.OK
            };
            btnSave.Click += BtnSave_Click;

            // Cancel Button
            btnCancel = new Button
            {
                Text = "Cancel",
                Left = 305,
                Top = 195,
                Width = 85,
                Height = 30,
                DialogResult = DialogResult.Cancel
            };

            // Add controls
            Controls.AddRange(new Control[]
            {
                lblIp, txtIpAddress, lblIpHelp,
                lblPort, numPort,
                lblUrlTitle, lblCurrentUrl,
                lblHelp,
                btnSave, btnCancel
            });

            AcceptButton = btnSave;
            CancelButton = btnCancel;
        }

        private void UpdateUrlPreview()
        {
            string ip = string.IsNullOrWhiteSpace(txtIpAddress.Text) ? "+" : txtIpAddress.Text.Trim();
            string displayIp = (ip == "+" || ip == "*") ? "127.0.0.1" : ip;
            lblCurrentUrl.Text = $"http://{displayIp}:{(int)numPort.Value}/sse";
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            // Validate IP address
            if (!McpServerConfig.IsValidIpAddress(txtIpAddress.Text.Trim()))
            {
                MessageBox.Show(
                    "Invalid IP address format.\n\nValid values:\n• + (all interfaces)\n• * (all interfaces)\n• localhost\n• A valid IP address (e.g., 127.0.0.1, 192.168.1.100)",
                    "Validation Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                txtIpAddress.Focus();
                txtIpAddress.SelectAll();
                DialogResult = DialogResult.None;
                return;
            }

            // Port validation is handled by NumericUpDown control
            DialogResult = DialogResult.OK;
        }
    }
}
