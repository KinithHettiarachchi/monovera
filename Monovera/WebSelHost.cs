using HtmlAgilityPack;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Monovera
{
    // Self-host for browser UI using IWebHost/WebHostBuilder (no minimal hosting, no Host.CreateDefaultBuilder)
    public sealed class WebSelfHost
    {
        private readonly int port;
        private IWebHost webHost;

        public string BaseUrl => $"http://localhost:{port}";

        public WebSelfHost(int port = 5178)
        {
            this.port = port;
        }

        public async Task StartAsync()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var wwwroot = Path.Combine(baseDir, "wwwroot");
            Directory.CreateDirectory(wwwroot);

            // Generate index.html and monovera.web.js at runtime with embedded monovera.css
            await EnsureWebAssetsAsync(wwwroot);

            webHost = new WebHostBuilder()
                .UseKestrel()
                .UseContentRoot(baseDir)
                .UseUrls(BaseUrl)
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddCors();
                })
                .Configure(app =>
                {
                    // Static files for SPA
                    app.UseDefaultFiles(new DefaultFilesOptions
                    {
                        DefaultFileNames = new List<string> { "index.html" },
                        FileProvider = new PhysicalFileProvider(wwwroot)
                    });
                    app.UseStaticFiles(new StaticFileOptions
                    {
                        FileProvider = new PhysicalFileProvider(wwwroot),
                        RequestPath = ""
                    });

                    // Expose images/ as static so tree icons can be used by the web UI
                    var imagesDir = Path.Combine(baseDir, "images");
                    if (Directory.Exists(imagesDir))
                    {
                        app.UseStaticFiles(new StaticFileOptions
                        {
                            FileProvider = new PhysicalFileProvider(imagesDir),
                            RequestPath = "/static/images"
                        });
                    }

                    // Expose attachments/ so description and attachment images load inside iframe
                    var attachmentsDir = Path.Combine(baseDir, "attachments");
                    if (Directory.Exists(attachmentsDir))
                    {
                        app.UseStaticFiles(new StaticFileOptions
                        {
                            FileProvider = new PhysicalFileProvider(attachmentsDir),
                            RequestPath = "/attachments"
                        });
                    }

                    app.UseRouting();
                    app.UseCors(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());

                    app.UseEndpoints(endpoints =>
                    {
                        // Serve desktop CSS directly (not used by SPA; SPA embeds CSS)
                        endpoints.MapGet("/static/monovera.css", async context =>
                        {
                            context.Response.ContentType = "text/css; charset=utf-8";
                            var cssPath = frmMain.cssPath;
                            if (File.Exists(cssPath))
                            {
                                var css = await File.ReadAllTextAsync(cssPath);
                                await context.Response.WriteAsync(css);
                            }
                            else
                            {
                                context.Response.Redirect(frmMain.cssHref ?? "");
                            }
                        });

                        // Status info
                        endpoints.MapGet("/api/status", async context =>
                        {
                            var payload = new
                            {
                                connectedUser = frmMain.jiraUserName,
                                offline = frmMain.OFFLINE_MODE,
                                projects = frmMain.projectList,
                                lastDbUpdated = GetMaxUpdatedTimeFromDbWeb()
                            };
                            context.Response.ContentType = "application/json; charset=utf-8";
                            await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
                        });

                        // Root nodes (camelCase + icon + robust roots)
                        endpoints.MapGet("/api/tree/roots", async context =>
                        {
                            var configuredRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            if (frmMain.config?.Projects != null)
                            {
                                foreach (var p in frmMain.config.Projects)
                                    if (!string.IsNullOrWhiteSpace(p?.Root))
                                        configuredRoots.Add(p.Root.Trim());
                            }
                            if (configuredRoots.Count == 0 && !string.IsNullOrWhiteSpace(frmMain.root_key))
                            {
                                foreach (var k in frmMain.root_key.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                                    configuredRoots.Add(k);
                            }

                            IEnumerable<object> payload = Enumerable.Empty<object>();

                            if (frmMain.issueDict != null && frmMain.issueDict.Count > 0)
                            {
                                payload = frmMain.issueDict.Values
                                    .Where(issue =>
                                    {
                                        var noParentOrMissing = string.IsNullOrEmpty(issue.ParentKey) || !frmMain.issueDict.ContainsKey(issue.ParentKey);
                                        if (!noParentOrMissing) return false;
                                        if (configuredRoots.Count > 0 && !configuredRoots.Contains(issue.Key)) return false;
                                        return true;
                                    })
                                    .Select(issue =>
                                    {
                                        string iconUrl = ResolveTypeIconUrl(issue.Type);
                                        bool hasChildren = frmMain.childrenByParent.TryGetValue(issue.Key, out var kids) && (kids?.Count > 0);
                                        return new
                                        {
                                            key = issue.Key,
                                            text = $"{WebUtility.HtmlEncode(issue.Summary)} [{issue.Key}]",
                                            hasChildren,
                                            icon = iconUrl
                                        };
                                    })
                                    .ToList();
                            }
                            else
                            {
                                payload = configuredRoots.Select(k =>
                                {
                                    frmMain.JiraIssue issue = null;
                                    frmMain.issueDict?.TryGetValue(k, out issue);
                                    string iconUrl = ResolveTypeIconUrl(issue?.Type);
                                    bool hasChildren = frmMain.childrenByParent.TryGetValue(k, out var kids) && (kids?.Count > 0);
                                    return new
                                    {
                                        key = k,
                                        text = $"{WebUtility.HtmlEncode(issue?.Summary ?? k)} [{k}]",
                                        hasChildren,
                                        icon = iconUrl
                                    };
                                }).ToList();
                            }

                            context.Response.ContentType = "application/json; charset=utf-8";
                            await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
                        });

                        // Children of a parent key (sorted + camelCase + icon)
                        endpoints.MapGet("/api/tree/children/{parentKey}", async context =>
                        {
                            var parentKey = context.Request.RouteValues["parentKey"]?.ToString() ?? "";
                            var children = frmMain.childrenByParent.TryGetValue(parentKey, out var list)
                                ? list
                                : new List<frmMain.JiraIssue>();

                            var cmp = Comparer<string>.Create((a, b) => new frmMain.AlphanumericComparer().Compare(a, b));
                            var sorted = children.OrderBy(ch =>
                            {
                                if (frmMain.FlatJiraIssueDictionary != null &&
                                    frmMain.FlatJiraIssueDictionary.TryGetValue(ch.Key, out var dto) &&
                                    !string.IsNullOrWhiteSpace(dto.SortingField))
                                {
                                    return dto.SortingField;
                                }
                                return ch.SortingField ?? ch.Summary ?? "";
                            }, cmp).ToList();

                            var payload = sorted.Select(ch =>
                            {
                                string iconUrl = ResolveTypeIconUrl(ch.Type);
                                bool hasChildren = frmMain.childrenByParent.ContainsKey(ch.Key) && frmMain.childrenByParent[ch.Key].Count > 0;
                                return new
                                {
                                    key = ch.Key,
                                    text = $"{WebUtility.HtmlEncode(ch.Summary)} [{ch.Key}]",
                                    hasChildren,
                                    icon = iconUrl
                                };
                            });

                            context.Response.ContentType = "application/json; charset=utf-8";
                            await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
                        });

                        // Full rendered issue HTML (uses your builders to match desktop)
                        endpoints.MapGet("/api/issue/{key}/html", async context =>
                        {
                            var key = context.Request.RouteValues["key"]?.ToString() ?? "";
                            var html = BuildIssuePageHtml(key);
                            context.Response.ContentType = "text/html; charset=utf-8";
                            await context.Response.WriteAsync(html);
                        });
                    });
                })
                .Build();

            await webHost.StartAsync();
        }

        public async Task StopAsync()
        {
            if (webHost != null)
            {
                try
                {
                    await webHost.StopAsync(TimeSpan.FromSeconds(2));
                    webHost.Dispose();
                }
                catch { /* ignore */ }
            }
        }

        private static string ResolveTypeIconUrl(string issueType)
        {
            if (string.IsNullOrWhiteSpace(issueType)) return null;
            try
            {
                var key = frmMain.GetIconForType(issueType);
                if (!string.IsNullOrWhiteSpace(key) && frmMain.typeIcons != null && frmMain.typeIcons.TryGetValue(key, out var fileName) && !string.IsNullOrWhiteSpace(fileName))
                {
                    return "/static/images/" + fileName;
                }
            }
            catch { }
            return null;
        }

        private sealed class WebNode
        {
            public string Key { get; set; }
            public string Text { get; set; }
            public bool HasChildren { get; set; }
        }

        // Build a full issue page HTML (header + tabs) for web
        private static string BuildIssuePageHtml(string key)
        {
            string summary = frmMain.GetFieldValueByKey(key, "SUMMARY") ?? frmMain.SUMMARY_MISSING;
            string issueType = frmMain.GetFieldValueByKey(key, "ISSUETYPE") ?? "";
            string status = frmMain.GetFieldValueByKey(key, "STATUS") ?? "";
            string createdRaw = frmMain.GetFieldValueByKey(key, "CREATEDTIME");
            string updatedRaw = frmMain.GetFieldValueByKey(key, "UPDATEDTIME");

            string created = TryFormatDbTime(createdRaw);
            string updated = TryFormatDbTime(updatedRaw);

            string issueUrl = $"{frmMain.jiraBaseUrl}/browse/{key}";
            string headerLine = $"<h2>{WebUtility.HtmlEncode(summary)} [{key}]</h2>";

            // Description
            string descOriginal = frmMain.GetFieldValueByKey(key, "DESCRIPTION") ?? "";
            string descriptionHtml = frmMain.BuildHTMLSection_DESCRIPTION(descOriginal, key);
            descriptionHtml = FixOfflineAttachmentUrlsLocal(descriptionHtml);

            // Attachments
            string attachmentsHtml = frmMain.GetFieldValueByKey(key, "ATTACHMENTS")
                ?? "<div class='no-attachments'>No attachments found.</div>";
            attachmentsHtml = FixOfflineAttachmentUrlsLocal(attachmentsHtml);

            // Links offline
            string linksHtml = BuildLinksOffline(key);

            // History
            string histRaw = frmMain.GetFieldValueByKey(key, "HISTORY") ?? "[]";
            string historyHtml = "";
            try
            {
                using var doc = JsonDocument.Parse(histRaw);
                historyHtml = frmMain.BuildHTMLSection_HISTORY(doc.RootElement);
            }
            catch
            {
                historyHtml = "<div class='no-links'>No history found.</div>";
            }

            // Always serve CSS over HTTP so it loads inside iframe srcdoc
            var cssHref = "/static/monovera.css";

            var sb = new StringBuilder();
            sb.Append($@"
<!DOCTYPE html>
<html>
<head>
  <meta charset='UTF-8'>
  <link href='https://cdn.jsdelivr.net/npm/prismjs@1.29.0/themes/prism.css' rel='stylesheet' />
  <script src='https://cdn.jsdelivr.net/npm/prismjs@1.29.0/prism.js'></script>
  <script src='https://cdn.jsdelivr.net/npm/prismjs@1.29.0/components/prism-gherkin.min.js'></script>
  <script src='https://cdn.jsdelivr.net/npm/prismjs@1.29.0/components/prism-json.min.js'></script>
  <link href='https://fonts.googleapis.com/css2?family=IBM+Plex+Sans:wght@400;500;600&display=swap' rel='stylesheet' />
  <link rel='stylesheet' href='{cssHref}' />
</head>
<body>
  {headerLine}
  <div style='margin-bottom: 20px; font-size: 0.95em; color: #444; display: flex; gap: 40px; align-items: center;'>
    <div>🧰 <strong>Type:</strong> {WebUtility.HtmlEncode(issueType)}</div>
    <div><strong>Status:</strong> {WebUtility.HtmlEncode(status)}</div>
    <div>📅 <strong>Created:</strong> {WebUtility.HtmlEncode(created)}</div>
    <div>📅 <strong>Updated:</strong> {WebUtility.HtmlEncode(updated)}</div>
    <div>🔗 <a href='{issueUrl}' target='_blank' rel='noopener'>Open in Browser</a></div>
  </div>
  <hr/>
  <details open>
    <summary>📜 Description</summary>
    <section>
        {descriptionHtml}
    </section>
  </details>

  <div class='tab-bar'>
    <button class='tab-btn active' data-tab='linksTab'>⛓ Links</button>
    <button class='tab-btn' data-tab='historyTab'>🕰️ History</button>
    <button class='tab-btn' data-tab='attachmentsTab'>📎 Attachments</button>
  </div>
  <div class='tab-content' id='linksTab' style='display:block;'>
    {linksHtml}
  </div>
  <div class='tab-content' id='historyTab' style='display:none;'>
    {historyHtml}
  </div>
  <div class='tab-content' id='attachmentsTab' style='display:none;'>
    {attachmentsHtml}
  </div>

  <script>
    document.querySelectorAll('.tab-btn').forEach(btn => {{
      btn.addEventListener('click', function() {{
        document.querySelectorAll('.tab-btn').forEach(b => b.classList.remove('active'));
        btn.classList.add('active');
        document.querySelectorAll('.tab-content').forEach(tc => tc.style.display = 'none');
        const tgt = document.getElementById(btn.dataset.tab);
        if (tgt) tgt.style.display = 'block';
      }});
    }});
    Prism.highlightAll();
  </script>
</body>
</html>");
            return sb.ToString();
        }
// Static clone of BuildHTMLSection_LINKS_Offline (with icons like frmMain)
        private static string BuildLinksOffline(string issueKey)
        {
            var sb = new StringBuilder();

            (string summary, string type, string sortingField) GetIssueInfo(string key)
            {
                string summary = frmMain.GetFieldValueByKey(key, "SUMMARY") ?? frmMain.SUMMARY_MISSING;
                string type = frmMain.GetFieldValueByKey(key, "ISSUETYPE") ?? "";
                string sortingField = frmMain.GetFieldValueByKey(key, "SORTINGFIELD") ?? "0";

                if (summary == frmMain.SUMMARY_MISSING)
                {
                    try
                    {
                        string url = $"{frmMain.jiraBaseUrl}/rest/api/3/issue/{key}?fields=summary,issuetype";
                        using var client = new HttpClient();
                        var authToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{frmMain.jiraEmail}:{frmMain.jiraToken}"));
                        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authToken);

                        var response = client.GetAsync(url).Result;
                        if (response.IsSuccessStatusCode)
                        {
                            var json = response.Content.ReadAsStringAsync().Result;
                            using var doc = JsonDocument.Parse(json);
                            var fields = doc.RootElement.GetProperty("fields");

                            if (fields.TryGetProperty("summary", out var summaryProp) && summaryProp.ValueKind == JsonValueKind.String)
                                summary = summaryProp.GetString() ?? frmMain.SUMMARY_MISSING;

                            if (fields.TryGetProperty("issuetype", out var typeProp) &&
                                typeProp.TryGetProperty("name", out var typeNameProp) &&
                                typeNameProp.ValueKind == JsonValueKind.String)
                                type = typeNameProp.GetString() ?? "";
                        }
                    }
                    catch
                    {
                        summary = key;
                    }
                }
                return (summary, type, sortingField);
            }

            string BuildTable(string title, List<string> keys, bool sortByField = false, bool showPath = false)
            {
                var rows = new List<(string key, string summary, string type, string sortingField)>();
                foreach (var key in keys)
                {
                    var info = GetIssueInfo(key);
                    rows.Add((key, info.summary, info.type, info.sortingField));
                }
                if (sortByField)
                {
                    var comparer = Comparer<string>.Create((a, b) => new frmMain.AlphanumericComparer().Compare(a, b));
                    rows = rows.OrderBy(r => r.sortingField, comparer).ToList();
                }

                string IconImgHtml(string key, string issueType)
                {
                    // Resolve project config for this key to map type -> image file (same logic as frmMain)
                    var keyPrefix = key.Split('-')[0];
                    var projectConfig = frmMain.config?.Projects?.FirstOrDefault(p => !string.IsNullOrEmpty(p.Root) &&
                                                                                     p.Root.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase));
                    if (projectConfig == null || string.IsNullOrWhiteSpace(issueType))
                        return "<span style='font-size:22px; vertical-align:middle; margin-right:8px;'>🟥</span>";

                    // Exact or case-insensitive map
                    string fileName = null;
                    if (!projectConfig.Types.TryGetValue(issueType, out fileName))
                    {
                        var match = projectConfig.Types.FirstOrDefault(kvp => kvp.Key.Equals(issueType, StringComparison.OrdinalIgnoreCase));
                        fileName = match.Value;
                    }

                    if (string.IsNullOrWhiteSpace(fileName))
                        return "<span style='font-size:22px; vertical-align:middle; margin-right:8px;'>🟥</span>";

                    var fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", fileName);
                    if (!File.Exists(fullPath))
                        return "<span style='font-size:22px; vertical-align:middle; margin-right:8px;'>🟥</span>";

                    try
                    {
                        var bytes = File.ReadAllBytes(fullPath);
                        var base64 = Convert.ToBase64String(bytes);
                        return $"<img src='data:image/png;base64,{base64}' style='height:24px; width:24px; vertical-align:middle; margin-right:8px; border-radius:4px;' title='{WebUtility.HtmlEncode(issueType)}' />";
                    }
                    catch
                    {
                        return "<span style='font-size:22px; vertical-align:middle; margin-right:8px;'>🟥</span>";
                    }
                }

                var tableRows = new StringBuilder();
                foreach (var r in rows)
                {
                    string pathHtml = "";
                    if (showPath)
                    {
                        string path = frmMain.GetRequirementPath(r.key);
                        if (!string.IsNullOrEmpty(path))
                            pathHtml = $"<div style='font-size:0.7em;color:#888;margin-left:48px;margin-top:1px;'>{path}</div>";
                    }

                    var iconImgInner = IconImgHtml(r.key, r.type);

                    tableRows.AppendLine($@"
<tr>
  <td class='confluenceTd'>
    <a href='#' data-key='{WebUtility.HtmlEncode(r.key)}'>
      {iconImgInner} {WebUtility.HtmlEncode(r.summary)} [{WebUtility.HtmlEncode(r.key)}]
    </a>
    {pathHtml}
  </td>
</tr>");
                }

                return $@"
<table class='confluenceTable' style='width:100%; border-collapse:collapse; margin-bottom:10px;'>
  <thead>
    <tr>
      <th class='confluenceTh' style='width:60px;'>{WebUtility.HtmlEncode(title)}</th>
    </tr>
  </thead>
  <tbody>
    {(rows.Count == 0
                ? $"<tr><td class='confluenceTd' style='text-align:left; color:#888;'>No {WebUtility.HtmlEncode(title)} issues found.</td></tr>"
                : tableRows.ToString())}
  </tbody>
</table>";
            }

            // Children
            string childrenRaw = frmMain.GetFieldValueByKey(issueKey, "CHILDRENKEYS");
            var childrenKeys = !string.IsNullOrWhiteSpace(childrenRaw)
                ? childrenRaw.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(k => k.Trim()).ToList()
                : new List<string>();
            sb.AppendLine(BuildTable("Children", childrenKeys, sortByField: true));

            // Parent
            string parentKey = frmMain.GetFieldValueByKey(issueKey, "PARENTKEY");
            var parentKeys = !string.IsNullOrWhiteSpace(parentKey) ? new List<string> { parentKey } : new List<string>();
            sb.AppendLine(BuildTable("Parent", parentKeys));

            // Related
            string relatesRaw = frmMain.GetFieldValueByKey(issueKey, "RELATESKEYS");
            var relatesKeys = !string.IsNullOrWhiteSpace(relatesRaw)
                ? relatesRaw.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(k => k.Trim()).ToList()
                : new List<string>();
            sb.AppendLine(BuildTable("Related", relatesKeys, showPath: true));

            return sb.ToString();
        }

        private static string TryFormatDbTime(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            if (DateTime.TryParseExact(raw, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                return dt.ToString("yyyy-MM-dd HH:mm");
            return raw;
        }

        private static string? GetMaxUpdatedTimeFromDbWeb()
        {
            try
            {
                string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "monovera.sqlite");
                string connStr = $"Data Source={dbPath};";
                using var conn = new Microsoft.Data.Sqlite.SqliteConnection(connStr);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT MAX(UPDATEDTIME) FROM issue";
                var result = cmd.ExecuteScalar();
                if (result != DBNull.Value && result != null)
                {
                    if (DateTime.TryParseExact(result.ToString(), "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                        return dt.ToString("yyyy-MM-dd HH:mm:ss");
                }
                return null;
            }
            catch { return null; }
        }

        // Local copy of frmMain.FixOfflineAttachmentUrls (frmMain version is private)
        // Local copy of frmMain.FixOfflineAttachmentUrls (web version: rewrite to HTTP)
        private static string FixOfflineAttachmentUrlsLocal(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return html;

            try
            {
                var doc = new HtmlAgilityPack.HtmlDocument();
                doc.LoadHtml(html);

                static bool IsRelativeAttachment(string s)
                    => !string.IsNullOrWhiteSpace(s)
                       && !Uri.IsWellFormedUriString(s, UriKind.Absolute)
                       && (s.StartsWith("attachments/", StringComparison.OrdinalIgnoreCase)
                           || s.StartsWith("./attachments/", StringComparison.OrdinalIgnoreCase));

                static string ToHttpPath(string rel)
                {
                    // Normalize ./attachments/... => /attachments/...
                    var p = rel.TrimStart('.', '/');
                    return "/" + p.Replace('\\', '/');
                }

                foreach (var node in doc.DocumentNode.SelectNodes("//*[@src]") ?? Enumerable.Empty<HtmlNode>())
                {
                    var src = node.GetAttributeValue("src", null);
                    if (IsRelativeAttachment(src))
                        node.SetAttributeValue("src", ToHttpPath(src));
                }

                foreach (var node in doc.DocumentNode.SelectNodes("//*[@href]") ?? Enumerable.Empty<HtmlNode>())
                {
                    var href = node.GetAttributeValue("href", null);
                    if (IsRelativeAttachment(href))
                        node.SetAttributeValue("href", ToHttpPath(href));
                }

                foreach (var node in doc.DocumentNode.SelectNodes("//*[@data-src]") ?? Enumerable.Empty<HtmlNode>())
                {
                    var ds = node.GetAttributeValue("data-src", null);
                    if (IsRelativeAttachment(ds))
                        node.SetAttributeValue("data-src", ToHttpPath(ds));
                }

                return doc.DocumentNode.InnerHtml;
            }
            catch
            {
                return html; // best effort
            }
        }

        // Write index.html (embedded monovera.css) and monovera.web.js to wwwroot
        private static async Task EnsureWebAssetsAsync(string wwwroot)
        {
            string css = "";
            try
            {
                if (!string.IsNullOrWhiteSpace(frmMain.cssPath) && File.Exists(frmMain.cssPath))
                {
                    css = await File.ReadAllTextAsync(frmMain.cssPath, Encoding.UTF8);
                }
                else if (!string.IsNullOrWhiteSpace(frmMain.cssHref))
                {
                    using var hc = new HttpClient();
                    css = await hc.GetStringAsync(frmMain.cssHref);
                }
            }
            catch { css = ""; }

            // index.html with embedded CSS
            string indexHtml = $@"<!DOCTYPE html>
<html>
<head>
  <meta charset='utf-8' />
  <title>Monovera (Web)</title>
  <style>
{css}

/* Layout: prevent page scroll; keep status visible */
html, body {{ height: 100%; margin: 0; }}
body {{ overflow: hidden; }}              /* stop page scrollbars */
.layout {{ display: grid; grid-template-rows: 1fr 32px; height: 100vh; }} /* lock to viewport */
.main {{ display: grid; grid-template-columns: 1fr 2fr; gap: 8px; padding: 8px; box-sizing: border-box; min-height: 0; }}

/* Sidebar: make tree area scroll, not the page */
.sidebar {{
  border: 1px solid #c0daf3; border-radius: 8px; background: #f5faff;
  display: flex; flex-direction: column; min-height: 0; overflow: hidden;
}}
/* Tree overrides (isolate from monovera.css bullets) */
#tree, #tree ul, #tree li {{
  list-style: none !important;
  list-style-type: none !important;
  list-style-image: none !important;
  margin: 0;
  padding-left: 12px;
}}
#tree {{ padding: 8px; white-space: nowrap; flex: 1 1 auto; overflow: auto; }}  /* scrollbars here */
#tree li::marker {{ content: '' !important; color: transparent !important; }}
#tree li::before {{ content: none !important; }}
#tree li {{ background: none !important; margin: 2px 0; }}
#tree a {{ cursor: pointer; text-decoration: none; color: #1565c0; }}
#tree .expander {{
  display:inline-block; width: 16px; text-align:center; margin-right: 6px;
  cursor: pointer; user-select: none; color: #0d47a1; font-weight: 700; font-family: Consolas, monospace;
}}
.node-icon {{ width: 18px; height: 18px; vertical-align: middle; margin-right: 6px; border-radius: 3px; }}

/* Right side: prevent it from forcing page scroll */
.workspace {{ display: flex; flex-direction: column; min-width: 0; min-height: 0; overflow: hidden; }}
.tabs {{ display: flex; gap: 4px; border-bottom: 1px solid #b3d4f6; padding: 6px 6px 0 6px; background: #f2faff; }}
.tab {{ background: #ffffff; border: 1px solid #b3d4f6; border-bottom: none; border-radius: 6px 6px 0 0; padding: 6px 10px; cursor: pointer; display: flex; align-items: center; gap: 8px; }}
.tab.active {{ background: #fff; color: #1565c0; font-weight: 600; border-bottom: 2px solid #1565c0; }}
.tab .close {{ color: #888; cursor: pointer; }}
.tab .tab-key {{ font-weight: 600; }}

.views {{ flex: 1 1 auto; position: relative; min-height: 0; overflow: hidden; }}
.view {{ position: absolute; inset: 0; display: none; }}
.view.active {{ display: block; }}
.view iframe {{ width: 100%; height: 100%; border: none; background: #fff; }}

.status {{ display: flex; align-items: center; padding: 0 12px; border-top: 1px solid #b3d4f6; background: #f2faff; color: #1565c0; gap: 16px; }}
  </style>
</head>
<body>
  <div class='layout'>
    <div class='main'>
      <aside class='sidebar'>
        <ul id='tree'></ul>
      </aside>
      <section class='workspace'>
        <div id='tabs' class='tabs'></div>
        <div id='views' class='views'></div>
      </section>
    </div>
    <footer class='status'>
      <span id='statusUser'>👤 User: -</span>
      <span id='statusMode'>🌐 Mode: -</span>
      <span id='statusUpdated'>🕒 DB Updated: -</span>
      <span style='margin-left:auto;'>Monovera Web</span>
    </footer>
  </div>
  <script src='monovera.web.js'></script>
</body>
</html>";

            // JS unchanged
            string webJs = @"(async function () {
  const treeEl = document.getElementById('tree');
  const tabsEl = document.getElementById('tabs');
  const viewsEl = document.getElementById('views');

  async function refreshStatus() {
    try {
      const s = await (await fetch('/api/status')).json();
      document.getElementById('statusUser').textContent = '👤 User: ' + (s.connectedUser || '-');
      document.getElementById('statusMode').textContent = '🌐 Mode: ' + (s.offline ? 'Offline' : 'Online');
      document.getElementById('statusUpdated').textContent = '🕒 DB Updated: ' + (s.lastDbUpdated || 'N/A');
    } catch {}
  }
  refreshStatus();
  setInterval(refreshStatus, 10000);

  function liNode({ key, text, hasChildren, icon }) {
    const li = document.createElement('li');

    const exp = document.createElement('span');
    exp.className = 'expander';
    exp.textContent = hasChildren ? '+' : '';
    exp.dataset.state = 'collapsed';
    exp.style.visibility = hasChildren ? 'visible' : 'hidden';

    const a = document.createElement('a');
    a.href = '#';
    a.dataset.key = key;

    if (icon) {
      const img = document.createElement('img');
      img.src = icon;
      img.className = 'node-icon';
      img.alt = '';
      a.appendChild(img);
    }
    a.appendChild(document.createTextNode(text));

    a.addEventListener('click', (e) => {
      e.preventDefault();
      openTab(key, text, icon);
    });

    const ul = document.createElement('ul');
    ul.className = 'tree';
    ul.style.display = 'none';

    exp.addEventListener('click', async () => {
      if (!hasChildren) return;
      if (exp.dataset.state === 'collapsed') {
        const children = await (await fetch(`/api/tree/children/${encodeURIComponent(key)}`)).json();
        ul.innerHTML = '';
        children.forEach(c => ul.appendChild(liNode(c)));
        ul.style.display = 'block';
        exp.textContent = '-';
        exp.dataset.state = 'expanded';
      } else {
        ul.style.display = 'none';
        exp.textContent = '+';
        exp.dataset.state = 'collapsed';
      }
    });

    li.appendChild(exp);
    li.appendChild(a);
    li.appendChild(ul);
    return li;
  }

  async function loadRoots() {
    const roots = await (await fetch('/api/tree/roots')).json();
    treeEl.innerHTML = '';
    roots.forEach(r => treeEl.appendChild(liNode(r)));
  }

  function makeTabId(key) { return 'tab-' + key; }
  function makeViewId(key) { return 'view-' + key; }

  function activate(key) {
    const id = makeTabId(key);
    const vid = makeViewId(key);
    [...tabsEl.children].forEach(ch => ch.classList.toggle('active', ch.id === id));
    [...viewsEl.children].forEach(ch => ch.classList.toggle('active', ch.id === vid));
  }

  async function openTab(key, title, icon) {
    const tabId = makeTabId(key);
    const viewId = makeViewId(key);

    if (!document.getElementById(tabId)) {
      const tab = document.createElement('div');
      tab.className = 'tab';
      tab.id = tabId;
      tab.title = title;

      if (icon) {
        const img = document.createElement('img');
        img.src = icon;
        img.className = 'node-icon';
        img.alt = '';
        tab.appendChild(img);
      }

      const keySpan = document.createElement('span');
      keySpan.className = 'tab-key';
      keySpan.textContent = '[' + key + ']';
      tab.appendChild(keySpan);

      const close = document.createElement('span');
      close.className = 'close';
      close.textContent = '×';
      close.title = 'Close';
      close.addEventListener('click', (e) => {
        e.stopPropagation();
        const t = document.getElementById(tabId);
        const v = document.getElementById(viewId);
        if (t) tabsEl.removeChild(t);
        if (v) viewsEl.removeChild(v);
        const last = tabsEl.lastElementChild;
        if (last) activate(last.id.replace(/^tab-/, ''));
      });

      tab.appendChild(close);
      tab.addEventListener('click', () => activate(key));
      tabsEl.appendChild(tab);

      const view = document.createElement('div');
      view.className = 'view';
      view.id = viewId;
      const iframe = document.createElement('iframe');
      iframe.setAttribute('title', key);
      view.appendChild(iframe);
      viewsEl.appendChild(view);

      try {
        const html = await (await fetch(`/api/issue/${encodeURIComponent(key)}/html`)).text();
        iframe.srcdoc = html;
      } catch {
        iframe.srcdoc = '<html><body><div style=""padding: 20px; color:#b00;"">Failed to load ' + key + '</div></body></html>';
      }
    }
    activate(key);
  }

  await loadRoots();
})();";

            Directory.CreateDirectory(wwwroot);
            await File.WriteAllTextAsync(Path.Combine(wwwroot, "index.html"), indexHtml, Encoding.UTF8);
            await File.WriteAllTextAsync(Path.Combine(wwwroot, "monovera.web.js"), webJs, Encoding.UTF8);
        }
    }
}