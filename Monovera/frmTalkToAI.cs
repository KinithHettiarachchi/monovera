using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;
using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Monovera
{
    /// <summary>
    /// Talk to AI dialog. Hosts an Ollama RAG UI in WebView2 and streams progress/answers.
    /// </summary>
    public partial class frmTalkToAI : Form
    {
        private ListBox lstAutoComplete;
        public string AI_MODE = "test";

        private string _dbPath = "";
        private string? _ollamaBaseUrl = "http://localhost:11434";
        private CancellationTokenSource? _ctsIndex;
        private CancellationTokenSource? _ctsAsk;

        // Guards initial navigation so the loading page doesn't override the Ollama UI
        private volatile bool _loadOllamaUiRequested = false;

        // Call this from frmMain after creating the form (mnuOllama click)
        public async void InitializeOllamaRagUI(string dbPath, string? ollamaBaseUrl = "http://localhost:11434")
        {
            _dbPath = dbPath;
            _ollamaBaseUrl = ollamaBaseUrl;
            _loadOllamaUiRequested = true; // set early to win races

            var wv = webViewTestCases ?? throw new InvalidOperationException("webViewTestCases is not initialized on frmTalkToAI.");
            await wv.EnsureCoreWebView2Async();

            // Wire Ollama message handler
            wv.CoreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived_ForOllama;
            wv.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived_ForOllama;

            // Navigate to the Ollama UI
            wv.CoreWebView2.NavigateToString(BuildOllamaHtml());
        }

        private void CoreWebView2_WebMessageReceived_ForOllama(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var msgText = e.TryGetWebMessageAsString();
                if (string.IsNullOrWhiteSpace(msgText)) return;

                dynamic msg = JsonConvert.DeserializeObject(msgText)!;
                string cmd = (string?)msg.cmd ?? "";

                switch (cmd)
                {
                    case "index":
                        {
                            int chunk = (int?)(msg.chunksize ?? 1500) ?? 1500;
                            int hop = (int?)(msg.hop ?? 2) ?? 2;
                            StartIndex(chunk, hop);
                            break;
                        }
                    case "ask":
                        {
                            string q = (string?)msg.q ?? "";
                            int topk = (int?)(msg.topk ?? 12) ?? 12;
                            string? seed = (string?)msg.seed;
                            string? sys = (string?)msg.sys;
                            StartAsk(q, topk, seed, sys);
                            break;
                        }
                    case "cancel":
                        {
                            _ctsIndex?.Cancel();
                            _ctsAsk?.Cancel();
                            PostStatus("Canceled.");
                            break;
                        }
                }
            }
            catch (Exception ex)
            {
                PostStatus($"Error: {ex.Message}");
            }
        }

        private void StartIndex(int chunkSize, int hop)
        {
            _ctsIndex?.Cancel();
            _ctsIndex = new CancellationTokenSource();

            var progress = new Progress<string>(text => PostProgress(text));
            PostStatus($"Indexing started (chunksize={chunkSize}, hop={hop})");

            _ = Task.Run(async () =>
            {
                try
                {
                    await OllamaRAG.BuildIndexAsync(_dbPath, chunkSize, hop, _ollamaBaseUrl, progress, _ctsIndex!.Token);
                    PostStatus("Indexing completed.");
                }
                catch (OperationCanceledException)
                {
                    PostStatus("Indexing canceled.");
                }
                catch (MissingMethodException)
                {
                    await OllamaRAG.BuildIndexAsync(_dbPath, chunkSize, hop, _ollamaBaseUrl);
                    PostStatus("Indexing completed (fallback).");
                }
                catch (Exception ex)
                {
                    PostStatus($"Index error: {ex.Message}");
                }
            });
        }

        private void StartAsk(string question, int topk, string? seedKey, string? systemStyle)
        {
            _ctsAsk?.Cancel();
            _ctsAsk = new CancellationTokenSource();

            PostAnswerReset();
            PostStatus("Asking...");

            _ = Task.Run(async () =>
            {
                try
                {
                    var gotAnyDelta = false;
                    await OllamaRAG.AskStreamAsync(
                        dbPath: _dbPath,
                        question: question,
                        onDelta: delta => { gotAnyDelta = true; PostAnswerDelta(delta); },
                        topk: topk,
                        seedKey: seedKey,
                        systemStyle: systemStyle,
                        ollamaBaseUrl: _ollamaBaseUrl,
                        cancellationToken: _ctsAsk!.Token
                    );

                    if (!gotAnyDelta)
                    {
                        // Fallback to non-stream if stream produced no output
                        var answer = await OllamaRAG.AskAsync(_dbPath, question, topk, seedKey, systemStyle, _ollamaBaseUrl);
                        if (!string.IsNullOrWhiteSpace(answer))
                            PostAnswerDelta(answer);
                    }
                    PostStatus("Ask completed.");
                }
                catch (OperationCanceledException)
                {
                    PostStatus("Ask canceled.");
                }
                catch (MissingMethodException)
                {
                    var answer = await OllamaRAG.AskAsync(_dbPath, question, topk, seedKey, systemStyle, _ollamaBaseUrl);
                    PostAnswerDelta(answer);
                    PostStatus("Ask completed (fallback).");
                }
                catch (Exception ex)
                {
                    PostStatus($"Ask error: {ex.Message}");
                }
            });
        }

        private void PostProgress(string text) => PostToWeb(new { type = "progress", text });
        private void PostStatus(string text) => PostToWeb(new { type = "status", text });
        private void PostAnswerReset() => PostToWeb(new { type = "answer-reset" });
        private void PostAnswerDelta(string delta) => PostToWeb(new { type = "answer-delta", delta });

        private void PostToWeb(object payload)
        {
            if (webViewTestCases?.CoreWebView2 == null) return;
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(payload);

            if (webViewTestCases.InvokeRequired)
            {
                webViewTestCases.Invoke(new Action(() =>
                {
                    if (webViewTestCases?.CoreWebView2 != null)
                        webViewTestCases.CoreWebView2.PostWebMessageAsString(json);
                }));
            }
            else
            {
                webViewTestCases.CoreWebView2.PostWebMessageAsString(json);
            }
        }

        private static string BuildOllamaHtml()
        {
            var sb = new StringBuilder();
            sb.Append(@"<!doctype html>
<html>
<head>
<meta charset='utf-8'>
<title>Ollama RAG</title>
<style>
 body{font-family:Segoe UI,Arial,sans-serif;margin:16px}
 h2{color:#1565c0}
 .row{display:flex;gap:8px;align-items:center;margin:6px 0}
 textarea,input[type=text]{width:100%;box-sizing:border-box}
 textarea{height:120px}
 .progress{height:90px}
 button{padding:6px 10px}
 .muted{color:#666}
</style>
</head>
<body>
<h2>Ollama RAG</h2>

<div class='row'>
  <button id='btnIndex'>Index Now</button>
  <input id='chunksize' type='number' value='1500' min='200' step='100' style='width:110px' />
  <input id='hop' type='number' value='2' min='0' max='5' step='1' style='width:70px' />
  <button id='btnCancel' title='Cancel current operation'>Cancel</button>
</div>
<div class='row'>
  <textarea id='progress' class='progress' placeholder='Progress...'></textarea>
</div>

<hr />

<div class='row'>
  <textarea id='q' placeholder='Type your question...'></textarea>
</div>
<div class='row'>
  <input id='seed' type='text' placeholder='Optional seed key (e.g., DEV-123)' />
  <input id='sys' type='text' placeholder='Optional system style rules' />
  <input id='topk' type='number' value='12' min='1' max='50' step='1' style='width:80px' />
  <button id='btnAsk'>Ask</button>
</div>
<div class='row'>
  <textarea id='answer' placeholder='Answer appears here in real time...'></textarea>
</div>

<div class='row muted' id='status'></div>

<script>
const btnIndex = document.getElementById('btnIndex');
const btnCancel = document.getElementById('btnCancel');
const progress = document.getElementById('progress');
const q = document.getElementById('q');
const topk = document.getElementById('topk');
const sys = document.getElementById('sys');
const seed = document.getElementById('seed');
const chunksize = document.getElementById('chunksize');
const hop = document.getElementById('hop');
const btnAsk = document.getElementById('btnAsk');
const answer = document.getElementById('answer');
const statusEl = document.getElementById('status');

function post(msg) {
  if (window.chrome && window.chrome.webview) {
    window.chrome.webview.postMessage(JSON.stringify(msg));
  }
}

btnIndex.addEventListener('click', () => {
  progress.value = '';
  statusEl.textContent = 'Starting indexing...';
  post({ cmd: 'index', chunksize: parseInt(chunksize.value || '1500'), hop: parseInt(hop.value || '2') });
});

btnCancel.addEventListener('click', () => {
  post({ cmd: 'cancel' });
});

btnAsk.addEventListener('click', () => {
  answer.value = '';
  statusEl.textContent = 'Asking...';
  post({ cmd: 'ask', q: q.value || '', topk: parseInt(topk.value || '12'), seed: seed.value || null, sys: sys.value || null });
});

if (window.chrome && window.chrome.webview) {
  window.chrome.webview.addEventListener('message', e => {
    const data = e.data;
    try {
      const msg = (typeof data === 'string') ? JSON.parse(data) : data;
      switch (msg.type) {
        case 'progress':
          progress.value += (msg.text || '') + '\n';
          progress.scrollTop = progress.scrollHeight;
          break;
        case 'status':
          statusEl.textContent = msg.text || '';
          break;
        case 'answer-reset':
          answer.value = '';
          break;
        case 'answer-delta':
          answer.value += (msg.delta || '');
          answer.scrollTop = answer.scrollHeight;
          break;
      }
    } catch {}
  });
}
</script>
</body>
</html>");
            return sb.ToString();
        }

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

            // Initialize WebView2 and attach generic message handler
            webViewTestCases.EnsureCoreWebView2Async().ContinueWith(_ =>
            {
                webViewTestCases.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

                // Navigate once ready; choose page based on requested mode
                webViewTestCases.Invoke(() =>
                {
                    var html = _loadOllamaUiRequested ? BuildOllamaHtml() : loadingAIHTML;
                    webViewTestCases.NavigateToString(html);
                });
            }, TaskScheduler.FromCurrentSynchronizationContext());

            // Clean up on close
            this.FormClosed += (_, __) =>
            {
                try
                {
                    _ctsAsk?.Cancel();
                    _ctsAsk?.Dispose();
                    _ctsAsk = null;

                    _ctsIndex?.Cancel();
                    _ctsIndex?.Dispose();
                    _ctsIndex = null;

                    if (webViewTestCases?.CoreWebView2 != null)
                    {
                        webViewTestCases.CoreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived;
                        webViewTestCases.CoreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived_ForOllama;
                    }
                }
                catch { /* ignore */ }
            };
        }

        public void UpdateAIProgress(string message)
        {
            if (webViewTestCases?.CoreWebView2 != null)
            {
                string js = $"document.getElementById('ai-progress').textContent = {System.Text.Json.JsonSerializer.Serialize(message)};";
                webViewTestCases.ExecuteScriptAsync(js);
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
        }

        // Generic WebView2 messages (used by other pages)
        private void CoreWebView2_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string _ = e.TryGetWebMessageAsString();
        }

        // Existing (non-RAG) AI context renderer remains available
        public async void LoadAIContext(string key, string issueListSummary, string aiInput)
        {
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

            string cssPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "monovera.css");
            string cssContent = System.IO.File.Exists(cssPath) ? System.IO.File.ReadAllText(cssPath) : "";

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
    document.getElementById('ai-markdown').innerHTML = marked.parse({System.Text.Json.JsonSerializer.Serialize(aiResult)});
    Prism.highlightAll();
  </script>
</body>
</html>";

            string tempFolder = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp");
            System.IO.Directory.CreateDirectory(tempFolder);
            string tempFile = System.IO.Path.Combine(tempFolder, $"AIResponse_{Guid.NewGuid():N}.html");
            System.IO.File.WriteAllText(tempFile, AIResponse, Encoding.UTF8);

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
}