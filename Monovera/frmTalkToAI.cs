using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using static System.Net.Mime.MediaTypeNames;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TrayNotify;
using static Monovera.frmMain;
using Application = System.Windows.Forms.Application;

namespace Monovera
{
    /// <summary>
    /// Search dialog for Monovera.
    /// Allows users to search Jira issues by text, project, type, and status.
    /// Displays results in a WebView2 browser and supports navigation to issues in the tree.
    /// </summary>
    public partial class frmTalkToAI : Form
    {
        private ListBox lstAutoComplete;
        public string AI_MODE = "test";

        /// <summary>
        /// Main constructor. Initializes UI, combo boxes, event handlers, and WebView2.
        /// </summary>
        /// <param name="tree">The TreeView control to use for navigation.</param>
        public frmTalkToAI()
        {
            InitializeComponent();

            string loadingAIHTML = $@"<!DOCTYPE AIResponse>
<AIResponse lang=""en"">
<head>
  <meta charset=""utf-8"" />
  <meta name=""viewport"" content=""width=device-width,initial-scale=1"" />
  <link href=""https://fonts.googleapis.com/css2?family=IBM+Plex+Sans:wght@400;600&display=swap"" rel=""stylesheet"">
  <style>
    :root{{ --bg: #ffffff; --muted:#6b7280; --accent:#1565c0; --shadow: 0 8px 24px rgba(8,30,50,0.08); }}
    AIResponse,body{{height:100%;margin:0;background:var(--bg);font-family:""IBM Plex Sans"", ""Segoe UI"", system-ui, -apple-system, sans-serif;}}
    .wrap{{height:100%;display:flex;align-items:center;justify-content:center;flex-direction:column;gap:24px;padding:24px;box-sizing:border-box;}}
    .gear-scene{{display:flex;align-items:center;gap:24px;}}
    .gear{{width:96px;height:96px;display:block;position:relative;filter:drop-shadow(var(--shadow));}}
    .gear svg{{width:100%;height:100%;display:block;transform-origin:center center;animation:spin 2.2s linear infinite;}}
    .gear.small svg{{width:56px;height:56px;animation-duration:1.6s;}}
    @keyframes spin{{ from{{transform:rotate(0deg);}} to{{transform:rotate(360deg);}} }}
    .gear-ring{{position:absolute;inset:-8px;border-radius:50%;display:block;background:linear-gradient(90deg, rgba(21,101,192,0.08), rgba(21,101,192,0.02));pointer-events:none;animation:breath 2.8s ease-in-out infinite;}}
    @keyframes breath{{0%{{transform:scale(0.98);opacity:0.7}}50%{{transform:scale(1.05);opacity:1}}100%{{transform:scale(0.98);opacity:0.7}}}}
    .label{{text-align:center;color:var(--muted);font-size:1.2rem;font-weight:500;}}
    .label strong{{display:block;color:var(--accent);font-size:1.4rem;margin-bottom:6px;}}
    .dots{{display:inline-block;margin-left:6px;}}
    .dot{{display:inline-block; width:8px;height:8px;margin:0 2px;border-radius:50%;background:var(--accent);opacity:0;transform:translateY(6px);animation:dot 1.2s infinite;}}
    .dot:nth-child(1){{animation-delay:0s}} .dot:nth-child(2){{animation-delay:0.12s}} .dot:nth-child(3){{animation-delay:0.24s}}
    @keyframes dot{{0%{{opacity:0;transform:translateY(6px) scale(0.9)}}40%{{opacity:1;transform:translateY(0) scale(1)}}80%{{opacity:0.4;transform:translateY(-2px) scale(0.95)}}100%{{opacity:0;transform:translateY(6px) scale(0.9)}}}}
    .hint{{color:#9aa4b2;font-size:0.9rem;margin-top:6px;}}
    @media (max-width:480px){{ .gear{{width:72px;height:72px}} .label strong{{font-size:1.15rem}} }}
  </style>
</head>
<body>
  <div class=""wrap"" role=""status"" aria-live=""polite"">
    <div class=""gear-scene"">
      <div class=""gear"" aria-hidden=""true"">
        <span class=""gear-ring""></span>
        <svg viewBox=""0 0 100 100"" xmlns=""http://www.w3.org/2000/svg"" aria-hidden=""true"">
          <defs>
            <linearGradient id=""g"" x1=""0"" x2=""1"">
              <stop offset=""0"" stop-color=""#2b6fb3""/>
              <stop offset=""1"" stop-color=""#124e93""/>
            </linearGradient>
          </defs>
          <g transform=""translate(50,50)"">
            <g fill=""none"" stroke=""url(#g)"" stroke-width=""4"" stroke-linejoin=""round"">
              <path d=""M0 -28 L6 -20 L16 -18 L20 -8 L30 -4 L30 6 L24 16 L26 26 L16 32 L8 40 L-8 40 L-16 32 L-26 26 L-24 16 L-30 6 L-30 -4 L-20 -8 L-16 -18 L-6 -20 Z"" fill=""#fff"" opacity=""0.02""/>
              <circle cx=""0"" cy=""0"" r=""18"" fill=""#fff"" opacity=""0.04""/>
              <circle cx=""0"" cy=""0"" r=""10"" fill=""url(#g)""/>
            </g>
          </g>
        </svg>
      </div>

      <div class=""gear small"" aria-hidden=""true"">
        <span class=""gear-ring""></span>
        <svg viewBox=""0 0 100 100"" xmlns=""http://www.w3.org/2000/svg"">
          <g transform=""translate(50,50)"">
            <g fill=""none"" stroke=""#94bff0"" stroke-width=""3"">
              <path d=""M0 -18 L4 -13 L10 -12 L12 -6 L18 -4 L18 4 L14 10 L16 16 L10 18 L6 24 L-6 24 L-10 18 L-16 16 L-14 10 L-18 4 L-18 -4 L-12 -6 L-10 -12 L-4 -13 Z"" fill=""#fff"" opacity=""0.02""/>
              <circle cx=""0"" cy=""0"" r=""7"" fill=""#cfe6ff""/>
            </g>
          </g>
        </svg>
      </div>
    </div>

    <div class=""label"">
      <strong>Asking AI...<span class=""dots"" aria-hidden=""true""><span class=""dot""></span><span class=""dot""></span><span class=""dot""></span></span></strong>
      <div class=""hint"">This may take a few moments — the AI is crafting something nice for you...</div>
      <div id=""ai-progress"" style=""margin-top:12px;color:#1565c0;font-weight:600;""></div>
    </div>
  </div>
</body>
</AIResponse>";

            // Initialize WebView2 and attach message handler
            webViewTestCases.EnsureCoreWebView2Async().ContinueWith(_ =>
            {
                webViewTestCases.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

                // Show welcome message on first open
                webViewTestCases.Invoke(() =>
                {
                    webViewTestCases.NavigateToString(loadingAIHTML);

                });
            }, TaskScheduler.FromCurrentSynchronizationContext());



        }

        public void UpdateAIProgress(string message)
        {
            if (webViewTestCases?.CoreWebView2 != null)
            {
                // Escape message for JS
                string js = $"document.getElementById('ai-progress').textContent = {JsonSerializer.Serialize(message)};";
                webViewTestCases.ExecuteScriptAsync(js);
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
        }

        /// <summary>
        /// Handles messages from the WebView2 browser.
        /// Selects and focuses the corresponding issue node in the tree when a result is clicked.
        /// </summary>
        private void CoreWebView2_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string key = e.TryGetWebMessageAsString();
        }

        public async void LoadAIContext(string key, string issueListSummary, string aiInput)
        {
            // Ensure WebView2 is initialized before using it
            if (webViewTestCases.CoreWebView2 == null)
                await webViewTestCases.EnsureCoreWebView2Async();

            string aiResult;
            try
            {
                aiResult = await AIService.AskAI(aiInput, AI_MODE);
            }
            catch (Exception ex)
            {
                aiResult = "Error: " + ex.Message;
            }

            // Read the CSS file content
            string cssPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "monovera.css");
            string cssContent = File.Exists(cssPath) ? File.ReadAllText(cssPath) : "";

            // Prepare the HTML with marked.js for Markdown and Prism.js for code highlighting
            string AIResponse = $@"
<!DOCTYPE html>
<html lang='en'>
<head>
  <meta charset='UTF-8'>
  <title>AI-Generated Context</title>
  <link href='https://cdn.jsdelivr.net/npm/prismjs@1.29.0/themes/prism.css' rel='stylesheet' />
  <script src='https://cdn.jsdelivr.net/npm/marked/marked.min.js'></script>
  <script src='https://cdn.jsdelivr.net/npm/prismjs@1.29.0/prism.js'></script>
  <script src='https://cdn.jsdelivr.net/npm/prismjs@1.29.0/components/prism-gherkin.min.js'></script>
  <script src='https://cdn.jsdelivr.net/npm/prismjs@1.29.0/components/prism-json.min.js'></script>
  <link href='https://fonts.googleapis.com/css2?family=IBM+Plex+Sans:wght@400;500;600&display=swap' rel='stylesheet' />
  <style>
    {cssContent}
    body {{ background: #fff; font-family: 'IBM Plex Sans', 'Segoe UI', system-ui, sans-serif; }}
    .panelContent {{ background: #fffae6; padding: 1em; border-radius: 6px; margin-bottom: 1em; }}
    .markdown-body {{ font-size: 1.05em; line-height: 1.7; }}
    pre code {{ font-size: 1em; }}
  </style>
</head>
<body>
  <details open>
    <summary>Analysis for {System.Web.HttpUtility.HtmlEncode(key)}</summary>
    <section>
      <div class='panelContent'>
        <strong>⚠️ Important:</strong>
        <p>Disclaimer: AI-generated content is intended solely to assist and inspire you. It should be treated as guidance, not absolute truth. Always verify the accuracy, completeness, and applicability of the information before using it in any decision-making or production environment.</p>
      </div>
      <details>
        <summary>List of Issues</summary>
        <section>{System.Web.HttpUtility.HtmlEncode(issueListSummary)}</section>
      </details>
        <details>
        <summary>Prompt</summary>
        <section>{System.Web.HttpUtility.HtmlEncode(aiInput)}</section>
      </details>
      <details open>
        <summary>Response</summary>
        <section>
          <div id='ai-markdown' class='markdown-body'></div>
        </section>
      </details>
    </section>
  </details>
  <script>
    // Render markdown to HTML
    document.getElementById('ai-markdown').innerHTML = marked.parse({JsonSerializer.Serialize(aiResult)});
    // Highlight code blocks
    Prism.highlightAll();
  </script>
</body>
</html>";

            // Write to temp file and navigate
            string tempFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp");
            Directory.CreateDirectory(tempFolder);
            string tempFile = Path.Combine(tempFolder, $"AIResponse_{Guid.NewGuid():N}.html");
            File.WriteAllText(tempFile, AIResponse, Encoding.UTF8);

            try
            {
                webViewTestCases.Source = new Uri(tempFile);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading AI response: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);

            }
        }
    }

    public static class AIService
    {
        private class AIConfig
        {
            public string Type { get; set; } = "";
            public string EndPoint { get; set; } = "";
            public string Token { get; set; } = "";
            public Dictionary<string, string> PromptPrefixes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        }

        private static AIConfig? _activeConfig;
        private static readonly object _lock = new();

        private static void LoadConfig()
        {
            if (_activeConfig != null) return;
            lock (_lock)
            {
                if (_activeConfig != null) return;
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AIConfiguration.json");
                if (!File.Exists(configPath))
                    throw new InvalidOperationException("AIConfiguration.json not found.");

                var json = File.ReadAllText(configPath);
                using var doc = JsonDocument.Parse(json);

                AIConfig? found = null;
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    var aiObj = prop.Value;
                    if (aiObj.TryGetProperty("default", out var def) && def.GetBoolean())
                    {
                        found = new AIConfig
                        {
                            Type = prop.Name,
                            EndPoint = aiObj.GetProperty("endPoint").GetString() ?? "",
                            Token = aiObj.GetProperty("token").GetString() ?? "",
                            PromptPrefixes = aiObj.TryGetProperty("promptPrefixes", out var prefixesElem) && prefixesElem.ValueKind == JsonValueKind.Object
                                ? prefixesElem.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "", StringComparer.OrdinalIgnoreCase)
                                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        };
                        break;
                    }
                }
                if (found == null)
                    throw new InvalidOperationException("No default AI configuration found in AIConfiguration.json.");
                _activeConfig = found;
            }
        }

        private static string PromptForPrefix(string prefix)
        {
            string result = prefix;
            using (var dlg = new Form())
            {
                dlg.Text = "Edit AI Prompt Prefix";
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.Width = 700;
                dlg.Height = 400;
                dlg.Font = new System.Drawing.Font("Segoe UI", 10);

                var txtPrompt = new TextBox
                {
                    Dock = DockStyle.Fill,
                    Font = new System.Drawing.Font("Consolas", 11),
                    Multiline = true,
                    ScrollBars = ScrollBars.Vertical,
                    Text = prefix,
                    Height = 240
                };

                var btnContinue = new Button { Text = "Continue", DialogResult = DialogResult.OK, Width = 100, Height = 40, Font = new System.Drawing.Font("Segoe UI", 10, FontStyle.Bold) };
                var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 100, Height = 40, Font = new System.Drawing.Font("Segoe UI", 10) };

                var buttonPanel = new FlowLayoutPanel
                {
                    FlowDirection = FlowDirection.RightToLeft,
                    Dock = DockStyle.Bottom,
                    Padding = new Padding(0, 8, 0, 0)
                };
                buttonPanel.Controls.Add(btnContinue);
                buttonPanel.Controls.Add(btnCancel);

                dlg.Controls.Add(txtPrompt);
                dlg.Controls.Add(buttonPanel);

                dlg.AcceptButton = btnContinue;
                dlg.CancelButton = btnCancel;

                if (dlg.ShowDialog() == DialogResult.OK)
                    result = txtPrompt.Text;
                else
                    result = null;
            }
            return result;
        }

        public static async Task<string> AskAI(string prompt, string promptType = "")
        {
            LoadConfig();
            if (_activeConfig == null)
                return "AI configuration is missing or invalid.";

            string prefix = "";
            if (!string.IsNullOrWhiteSpace(promptType) && _activeConfig.PromptPrefixes.TryGetValue(promptType, out var p))
                prefix = p;

            // Show dialog for user to review/edit the prefix
            prefix = PromptForPrefix(prefix);
            if (string.IsNullOrWhiteSpace(prefix))
                return "Cancelled by user.";

            string fullPrompt = (prefix + (string.IsNullOrWhiteSpace(prefix) ? "" : "\n\n") + prompt).Trim();

            return _activeConfig.Type.Equals("GemniAI", StringComparison.OrdinalIgnoreCase)
                ? await AskGeminiAI(fullPrompt)
                : await AskOllamaAI(fullPrompt);
        }

        private static async Task<string> AskGeminiAI(string prompt)
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("X-goog-api-key", _activeConfig!.Token);

            var requestBody = new
            {
                contents = new[]
                {
                new
                {
                    parts = new[]
                    {
                        new { text = prompt }
                    }
                }
            }
            };

            var jsonRequest = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");

            using var response = await client.PostAsync(_activeConfig.EndPoint, content);
            response.EnsureSuccessStatusCode();

            var jsonResponse = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(jsonResponse);
            var root = doc.RootElement;

            if (root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
            {
                var text = candidates[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString();

                return text ?? "";
            }
            return "";
        }

        private static async Task<string> AskOllamaAI(string prompt)
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _activeConfig!.Token);

            var requestBody = new
            {
                model = "llama3", // or whatever model you want to use
                prompt = prompt,
                stream = false
            };

            var jsonRequest = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");

            using var response = await client.PostAsync(_activeConfig.EndPoint + "/api/generate", content);
            response.EnsureSuccessStatusCode();

            var jsonResponse = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(jsonResponse);
            var root = doc.RootElement;

            if (root.TryGetProperty("response", out var resp))
                return resp.GetString() ?? "";

            return "";
        }
    }
}
