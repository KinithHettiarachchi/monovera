using System;
using System.Drawing;
using System.Windows.Forms;
using System.Threading.Tasks;

namespace Monovera
{
    /// <summary>
    /// ChatGPT-like chat interface for MonoveraBot
    /// </summary>
    public partial class MonoveraChatControl : UserControl
    {
        private RichTextBox txtChat;
        private TextBox txtInput;
        private Button btnSend;
        private MonoveraBot? _bot;
        private readonly string _databasePath;

        public MonoveraChatControl(string databasePath)
        {
            _databasePath = databasePath;
            InitializeComponent();
            InitializeBot();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();

            // Chat display area (RichTextBox for formatted text)
            txtChat = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                Font = new Font("Segoe UI", 10F),
                BackColor = Color.White,
                BorderStyle = BorderStyle.None,
                ScrollBars = RichTextBoxScrollBars.Vertical
            };

            // Input panel at bottom
            var inputPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 80,
                Padding = new Padding(10),
                BackColor = Color.FromArgb(240, 240, 240)
            };

            // Input textbox
            txtInput = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                Font = new Font("Segoe UI", 10F),
                BorderStyle = BorderStyle.FixedSingle,
                ScrollBars = ScrollBars.Vertical
            };
            txtInput.KeyDown += TxtInput_KeyDown;

            // Send button
            btnSend = new Button
            {
                Text = "Send",
                Dock = DockStyle.Right,
                Width = 80,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(33, 150, 243),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            btnSend.FlatAppearance.BorderSize = 0;
            btnSend.Click += BtnSend_Click;

            inputPanel.Controls.Add(txtInput);
            inputPanel.Controls.Add(btnSend);

            this.Controls.Add(txtChat);
            this.Controls.Add(inputPanel);

            this.ResumeLayout(false);
        }

        private void InitializeBot()
        {
            try
            {
                _bot = new MonoveraBot(_databasePath);
                
                if (_bot.IsTrained)
                {
                    AppendMessage("Monovera Bot", "Hello! I'm Monovera Bot, your AI assistant. I can help you with:\n\n" +
                        "• Finding test cases and requirements\n" +
                        "• Explaining relationships between items\n" +
                        "• Checking status and progress\n" +
                        "• Understanding project structure (TST, REQ, STF)\n\n" +
                        "Try asking me questions like:\n" +
                        "- \"What test cases are related to login?\"\n" +
                        "- \"Show me all requirements for authentication\"\n" +
                        "- \"What is the status of payment testing?\"\n" +
                        "- \"How many test cases are in progress?\"\n\n" +
                        "What would you like to know?", 
                        Color.FromArgb(33, 150, 243));
                }
                else
                {
                    AppendMessage("Monovera Bot", "Hello! I'm Monovera Bot. I need to be trained first to learn about your projects (TST, REQ, STF).\n\n" +
                        "Please go to: AI Assistant > Train Local Model\n\n" +
                        "This will read your database and build my knowledge base so I can answer your questions.", 
                        Color.FromArgb(200, 100, 0));
                }
            }
            catch (Exception ex)
            {
                AppendMessage("System", $"Error initializing bot: {ex.Message}", Color.Red);
            }
        }

        private void TxtInput_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && !e.Shift)
            {
                e.SuppressKeyPress = true;
                SendMessage();
            }
        }

        private void BtnSend_Click(object? sender, EventArgs e)
        {
            SendMessage();
        }

        private async void SendMessage()
        {
            string question = txtInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(question))
                return;

            // Display user message
            AppendMessage("You", question, Color.FromArgb(60, 60, 60));
            txtInput.Clear();

            // Disable input while processing
            txtInput.Enabled = false;
            btnSend.Enabled = false;

            try
            {
                if (_bot == null || !_bot.IsTrained)
                {
                    AppendMessage("Monovera Bot", "I'm not trained yet. Please train me first by going to AI Assistant > Train.", Color.Red);
                    return;
                }

                // Show thinking indicator
                AppendMessage("Monovera Bot", "Thinking...", Color.Gray);

                // Get response
                string answer = await Task.Run(() => _bot.Ask(question));

                // Remove thinking indicator and show actual response
                RemoveLastMessage();
                AppendMessage("Monovera Bot", answer, Color.FromArgb(33, 150, 243));
            }
            catch (Exception ex)
            {
                RemoveLastMessage();
                AppendMessage("Monovera Bot", $"Error: {ex.Message}", Color.Red);
            }
            finally
            {
                txtInput.Enabled = true;
                btnSend.Enabled = true;
                txtInput.Focus();
            }
        }

        private void AppendMessage(string sender, string message, Color senderColor)
        {
            if (txtChat.InvokeRequired)
            {
                txtChat.Invoke(new Action(() => AppendMessage(sender, message, senderColor)));
                return;
            }

            txtChat.SelectionStart = txtChat.TextLength;
            txtChat.SelectionLength = 0;

            // Sender name
            txtChat.SelectionFont = new Font(txtChat.Font, FontStyle.Bold);
            txtChat.SelectionColor = senderColor;
            txtChat.AppendText($"{sender}\n");

            // Message content
            txtChat.SelectionFont = new Font(txtChat.Font, FontStyle.Regular);
            txtChat.SelectionColor = Color.Black;
            txtChat.AppendText($"{message}\n\n");

            // Auto-scroll to bottom
            txtChat.SelectionStart = txtChat.TextLength;
            txtChat.ScrollToCaret();
        }

        private void RemoveLastMessage()
        {
            if (txtChat.InvokeRequired)
            {
                txtChat.Invoke(new Action(RemoveLastMessage));
                return;
            }

            // Find the last occurrence of "Monovera Bot\n"
            string text = txtChat.Text;
            int lastMonoveraBot = text.LastIndexOf("Monovera Bot\n");
            
            if (lastMonoveraBot >= 0)
            {
                txtChat.Text = text.Substring(0, lastMonoveraBot);
                txtChat.SelectionStart = txtChat.TextLength;
                txtChat.ScrollToCaret();
            }
        }

        public void RefreshBot()
        {
            InitializeBot();
        }
    }
}
