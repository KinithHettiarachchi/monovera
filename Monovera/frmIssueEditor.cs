using System;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;

namespace Monovera
{
    public partial class frmIssueEditor : Form
    {
        private readonly string issueKey, jiraBaseUrl, jiraEmail, jiraToken;

        public frmIssueEditor(string issueKey, string summary, string description, string jiraBaseUrl, string jiraEmail, string jiraToken)
        {
            InitializeComponent();
            this.issueKey = issueKey;
            this.jiraBaseUrl = jiraBaseUrl;
            this.jiraEmail = jiraEmail;
            this.jiraToken = jiraToken;

            var webView = new Microsoft.Web.WebView2.WinForms.WebView2 { Dock = DockStyle.Fill };
            panel.Controls.Add(webView);

            string localPath = Path.Combine(Application.StartupPath, "Resources", "AdfKitchen", "index.html");
            webView.Source = new Uri("file:///" + localPath.Replace("\\", "/"));

            webView.EnsureCoreWebView2Async().ContinueWith(_ =>
            {
                webView.Invoke(() =>
                {
                    webView.CoreWebView2.WebMessageReceived += async (s, e) =>
                    {
                        var msg = e.TryGetWebMessageAsString();

                        if (msg == null)
                            return;

                        try
                        {
                            var data = JsonSerializer.Deserialize<Dictionary<string, object>>(msg);

                            if (data == null || !data.ContainsKey("type"))
                                return;

                            string type = data["type"]?.ToString();

                            if (type == "cancel")
                            {
                                DialogResult = DialogResult.Cancel;
                                Close();
                                return;
                            }

                            if (type == "save" && data.TryGetValue("content", out var contentObj))
                            {
                                string adfJson = contentObj.ToString();

                                if (MessageBox.Show("Save changes to JIRA?", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                                {
                                    var authToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{jiraEmail}:{jiraToken}"));
                                    using var client = new HttpClient();
                                    client.BaseAddress = new Uri(jiraBaseUrl);
                                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);

                                    var payload = new
                                    {
                                        fields = new
                                        {
                                            summary = summary,
                                            description = JsonSerializer.Deserialize<object>(adfJson) // Send ADF JSON to Jira
                                        }
                                    };

                                    var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                                    var resp = await client.PutAsync($"/rest/api/3/issue/{issueKey}", content);
                                    if (resp.IsSuccessStatusCode)
                                    {
                                        DialogResult = DialogResult.OK;
                                        Close();
                                    }
                                    else
                                    {
                                        MessageBox.Show("Failed to update issue: " + await resp.Content.ReadAsStringAsync());
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show("Error while processing WebView message: " + ex.Message);
                        }
                    };

                    // Send initial data to editor after DOM is loaded (if supported by adf-kitchen)
                    webView.CoreWebView2.DOMContentLoaded += async (s2, e2) =>
                    {
                        object contentObject = null;
                        try
                        {
                            if (!string.IsNullOrWhiteSpace(description))
                                contentObject = JsonSerializer.Deserialize<object>(description);
                        }
                        catch
                        {
                            // ignore deserialization errors, fallback to empty doc
                        }

                        if (contentObject == null)
                        {
                            contentObject = new
                            {
                                type = "doc",
                                version = 1,
                                content = Array.Empty<object>()
                            };
                        }

                        var initMessage = new
                        {
                            type = "setContent",
                            content = contentObject
                        };

                        string script = $"window.postMessage({JsonSerializer.Serialize(initMessage)}, '*');";
                        await webView.CoreWebView2.ExecuteScriptAsync(script);

                    };
                });
            });
        }

    }
}
