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

        public WebSelfHost(int port = 8090)
        {
            this.port = port;
        }

        public async Task StartAsync()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;

            // WebAppRoot under app base
            var WebAppRoot = Path.Combine(baseDir, "WebAppRoot");
            Directory.CreateDirectory(WebAppRoot);

            // Ensure Data/attachments
            var dataDir = Path.Combine(baseDir, "Data");
            var attachmentsPhysical = Path.Combine(dataDir, "attachments");
            Directory.CreateDirectory(attachmentsPhysical);

            await EnsureWebAssetsAsync(WebAppRoot);

            webHost = new WebHostBuilder()
                .UseKestrel(options =>
                {
                    options.ListenAnyIP(port);
                })
                .UseContentRoot(baseDir)
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddCors();
                })
                .Configure(app =>
                {
                    app.UseDefaultFiles(new DefaultFilesOptions
                    {
                        DefaultFileNames = new List<string> { "index.html" },
                        FileProvider = new PhysicalFileProvider(WebAppRoot)
                    });
                    app.UseStaticFiles(new StaticFileOptions
                    {
                        FileProvider = new PhysicalFileProvider(WebAppRoot),
                        RequestPath = ""
                    });

                    var imagesDir = Path.Combine(baseDir, "images");
                    if (Directory.Exists(imagesDir))
                    {
                        app.UseStaticFiles(new StaticFileOptions
                        {
                            FileProvider = new PhysicalFileProvider(imagesDir),
                            RequestPath = "/static/images"
                        });
                    }

                    // Serve attachments from Data/attachments
                    if (Directory.Exists(attachmentsPhysical))
                    {
                        app.UseStaticFiles(new StaticFileOptions
                        {
                            FileProvider = new PhysicalFileProvider(attachmentsPhysical),
                            RequestPath = "/attachments"
                        });
                    }

                    app.UseRouting();
                    app.UseCors(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());

                    app.UseEndpoints(endpoints =>
                    {
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

                        // Recent updates HTML (for the SPA Recent Updates tab)
                        endpoints.MapGet("/api/recent/updated/html", async context =>
                        {
                            int days = 14;
                            if (context.Request.Query.TryGetValue("days", out var vals))
                                int.TryParse(vals.FirstOrDefault(), out days);

                            // 1) Return loader page (spinner + JS) immediately
                            string loader = BuildRecentUpdatesHtml(days);
                            context.Response.ContentType = "text/html; charset=utf-8";
                            await context.Response.WriteAsync(loader);
                        });

                        // Final heavy page; the loader fetches this and replaces the document
                        endpoints.MapGet("/api/recent/updated/final", async context =>
                        {
                            int days = 14;
                            if (context.Request.Query.TryGetValue("days", out var vals))
                                int.TryParse(vals.FirstOrDefault(), out days);

                            string html = BuildRecentUpdatesHtmlFinal(days);
                            context.Response.ContentType = "text/html; charset=utf-8";
                            await context.Response.WriteAsync(html);
                        });

                        // New: returns [rootKey, ..., targetKey] for SPA expansion
                        endpoints.MapGet("/api/tree/path/{key}", async context =>
                        {
                            var targetKey = context.Request.RouteValues["key"]?.ToString() ?? "";
                            var chain = new List<string>();
                            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            var cur = targetKey;

                            while (!string.IsNullOrWhiteSpace(cur) && seen.Add(cur))
                            {
                                if (frmMain.issueDict != null && frmMain.issueDict.TryGetValue(cur, out var issue))
                                {
                                    chain.Add(issue.Key);
                                    cur = issue.ParentKey;
                                }
                                else
                                {
                                    chain.Add(cur);
                                    break;
                                }
                            }
                            chain.Reverse();

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
                            if (configuredRoots.Count > 0)
                            {
                                int idx = chain.FindIndex(k => configuredRoots.Contains(k));
                                if (idx > 0) chain = chain.Skip(idx).ToList();
                            }

                            context.Response.ContentType = "application/json; charset=utf-8";
                            await context.Response.WriteAsync(JsonSerializer.Serialize(chain));
                        });

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

        // Loader page: shows spinner (same look as desktop) then swaps in the final HTML
        private static string BuildRecentUpdatesHtml(int days)
        {
            var cssHref = "/static/monovera.css";
            return $@"<!DOCTYPE html>
<html>
<head>
  <meta charset='UTF-8'>
  <link rel='stylesheet' href='{cssHref}' />
  <style>
    html,body {{ height:100%; margin:0; }}
    body {{ display:flex; align-items:center; justify-content:center; background:#fff; }}
  </style>
</head>
<body>
  <div class='spinner' aria-label='Loading Recent Updates...' title='Loading Recent Updates...'></div>
  <script>
    (function() {{
      var url = '/api/recent/updated/final?days=' + encodeURIComponent({days});
      fetch(url).then(r => r.text()).then(html => {{
        document.open(); document.write(html); document.close();
      }}).catch(err => {{
        document.body.innerHTML = ""<div style='padding:20px;color:#b00;font:14px Segoe UI'>Failed to load Recent Updates: "" +
          (err && err.message ? err.message : 'Unknown error') + ""</div>"";
      }});
    }})();
  </script>
</body>
</html>";
        }

        // Builds the HTML for the Recent Updates tab using Jira REST (mirrors frmMain.ShowRecentlyUpdatedIssuesAsync)
        private static string BuildRecentUpdatesHtmlFinal(int days)
        {

            // Search Jira for recently created/updated issues across configured projects
            // JQL: (project = "P1" OR project = "P2") AND (created >= -{days}d OR updated >= -{days}d) ORDER BY updated DESC
            var rows = new List<(string Key, string Summary, string Type, string Status, DateTime Updated, DateTime? Created, List<string> Tags)>();
            try
            {
                string jql = $"({string.Join(" OR ", frmMain.projectList.Select(p => $"project = \"{p}\""))}) AND (created >= -{days}d OR updated >= -{days}d) ORDER BY updated DESC";
                string baseUrl = frmMain.jiraBaseUrl?.TrimEnd('/') ?? "";
                string authToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{frmMain.jiraEmail}:{frmMain.jiraToken}"));

                using var client = new HttpClient();
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authToken);

                // Paginate /rest/api/3/search
                int startAt = 0;
                const int maxResults = 100;
                var searchIssues = new List<(string Key, string Summary, string Type, string Status, DateTime Updated, DateTime? Created)>();

                while (true)
                {
                    var url = $"{baseUrl}/rest/api/3/search?jql={WebUtility.UrlEncode(jql)}&fields=summary,issuetype,status,updated,created&startAt={startAt}&maxResults={maxResults}";
                    var resp = client.GetAsync(url).Result;
                    if (!resp.IsSuccessStatusCode) break;

                    var json = resp.Content.ReadAsStringAsync().Result;
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    int total = root.TryGetProperty("total", out var totalProp) ? totalProp.GetInt32() : 0;
                    if (!root.TryGetProperty("issues", out var issues) || issues.ValueKind != JsonValueKind.Array) break;
                    int count = 0;

                    foreach (var issue in issues.EnumerateArray())
                    {
                        count++;
                        var key = issue.TryGetProperty("key", out var k) ? k.GetString() ?? "" : "";

                        string summary = "";
                        string type = "";
                        string status = "";
                        DateTime updated = DateTime.MinValue;
                        DateTime? created = null;

                        if (issue.TryGetProperty("fields", out var fields))
                        {
                            if (fields.TryGetProperty("summary", out var s) && s.ValueKind == JsonValueKind.String)
                                summary = s.GetString() ?? "";

                            if (fields.TryGetProperty("issuetype", out var it) && it.TryGetProperty("name", out var itn) && itn.ValueKind == JsonValueKind.String)
                                type = itn.GetString() ?? "";

                            if (fields.TryGetProperty("status", out var st) && st.TryGetProperty("name", out var stn) && stn.ValueKind == JsonValueKind.String)
                                status = stn.GetString() ?? "";

                            if (fields.TryGetProperty("updated", out var up) && up.ValueKind == JsonValueKind.String && DateTime.TryParse(up.GetString(), out var dtUp))
                                updated = dtUp;

                            if (fields.TryGetProperty("created", out var cr) && cr.ValueKind == JsonValueKind.String && DateTime.TryParse(cr.GetString(), out var dtCr))
                                created = dtCr;
                        }

                        if (!string.IsNullOrWhiteSpace(key) && updated != DateTime.MinValue)
                            searchIssues.Add((key, summary, type, status, updated, created));
                    }

                    startAt += count;
                    if (count == 0 || startAt >= total) break;
                }

                // For each issue: fetch changelog to derive "Changes" tags for the day of its Updated date
                foreach (var it in searchIssues)
                {
                    var tags = new List<string>();
                    try
                    {
                        var issueUrl = $"{baseUrl}/rest/api/3/issue/{WebUtility.UrlEncode(it.Key)}?expand=changelog&fields=created";
                        var resp = client.GetAsync(issueUrl).Result;
                        if (resp.IsSuccessStatusCode)
                        {
                            var json = resp.Content.ReadAsStringAsync().Result;
                            using var doc = JsonDocument.Parse(json);
                            var root = doc.RootElement;

                            var updatedLocalDate = it.Updated.ToLocalTime().Date;
                            DateTime? createdUtc = it.Created;

                            // Created tag if created same local date as update day
                            if (createdUtc.HasValue && createdUtc.Value.ToLocalTime().Date == updatedLocalDate)
                                tags.Add("Created");

                            if (root.TryGetProperty("changelog", out var changelog) &&
                                changelog.TryGetProperty("histories", out var histories) &&
                                histories.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var h in histories.EnumerateArray())
                                {
                                    if (!h.TryGetProperty("created", out var hCreated) || hCreated.ValueKind != JsonValueKind.String) continue;
                                    if (!DateTime.TryParse(hCreated.GetString(), out var histCreated)) continue;
                                    if (histCreated.Date != updatedLocalDate) continue;

                                    if (!h.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) continue;
                                    foreach (var item in items.EnumerateArray())
                                    {
                                        if (!item.TryGetProperty("field", out var fieldProp) || fieldProp.ValueKind != JsonValueKind.String) continue;
                                        var field = fieldProp.GetString() ?? "";
                                        var lower = field.ToLowerInvariant();
                                        if (lower.Contains("issue sequence"))
                                            tags.Add("order");
                                        else if (lower.Contains("issuetype"))
                                            tags.Add("type");
                                        else if (!string.IsNullOrWhiteSpace(field))
                                            tags.Add(field);
                                    }
                                }
                            }
                        }
                    }
                    catch { /* best effort */ }

                    // Distinct + normalize
                    tags = tags
                        .Where(t => !string.IsNullOrWhiteSpace(t))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    rows.Add((it.Key, it.Summary, it.Type, it.Status, it.Updated, it.Created, tags));
                }
            }
            catch
            {
                // fall through; rows may be empty
            }

            var sb = new StringBuilder();
            string cssHref = "/static/monovera.css";

            sb.Append($@"<!DOCTYPE html>
<html>
<head>
  <meta charset='UTF-8'>
  <link rel='stylesheet' href='{cssHref}' />
  <style>
    .recent-update-tag {{
      display:inline-block; padding:2px 6px; margin:2px 4px 0 0; border-radius:4px;
      background:#e3f2fd; color:#0d47a1; font-size:.85em; border:1px solid #b3d4f6;
    }}
  </style>
</head>
<body>
  <h2>Recent Updates</h2>
");

            if (rows.Count == 0)
            {
                sb.Append("<div style='padding:12px;color:#888;'>No recent updates.</div></body></html>");
                return sb.ToString();
            }

            // Collect filters
            var allIssueTypesGlobal = rows
                .Select(r => r.Type)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var allChangeTypesGlobal = rows
                .SelectMany(r => r.Tags)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Filter panel: hidden by default; toggled by button
            sb.Append($@"
<button id='show-filter-btn'>Apply Filter</button>

<div id='floating-filter-container' style='display:none; position:fixed; left:10px; right:10px; top:10px; z-index:9999; padding:8px; background:#ffffffcc; backdrop-filter:saturate(1.2) blur(2px); border:1px solid #b3d4f6; border-radius:8px;'>
  <div style='display:flex; gap:12px; flex-wrap:wrap; align-items:flex-start;'>

    <div class='filter-panel' style='display:inline-block; padding:8px; border:1px solid #b3d4f6; background:#f9fcff; border-radius:6px;'>
      <div class='filter-panel-title' style='font-weight:600;color:#1565c0;margin-bottom:6px;'>Issue Types</div>
      <div id='issue-type-checkboxes' class='checkbox-container' style='display:flex;gap:10px;flex-wrap:wrap;max-height:140px;overflow:auto;'>
        <label><input type='checkbox' class='issue-type-checkbox-all change-type-checkbox-all' checked /> <span style='margin-left:6px;'>All</span></label>
        {string.Join("\n", allIssueTypesGlobal.Select(t =>
                    $"<label style='display:inline-flex;align-items:center;'><input type='checkbox' class='issue-type-checkbox change-type-checkbox' value='{WebUtility.HtmlEncode(t)}' checked /> <span style='margin-left:6px;'>{WebUtility.HtmlEncode(t)}</span></label>"))}
      </div>
    </div>

    {(allChangeTypesGlobal.Count == 0 ? "" : $@"
    <div class='filter-panel' style='display:inline-block; padding:8px; border:1px solid #b3d4f6; background:#f9fcff; border-radius:6px;'>
      <div class='filter-panel-title' style='font-weight:600;color:#1565c0;margin-bottom:6px;'>Change Types</div>
      <div id='change-type-checkboxes' class='checkbox-container' style='display:flex;gap:10px;flex-wrap:wrap;max-height:140px;overflow:auto;'>
        <label><input type='checkbox' class='change-type-checkbox-all' checked /> <span style='margin-left:6px;'>All</span></label>
        {string.Join("\n", allChangeTypesGlobal.Select(t =>
                    $"<label style='display:inline-flex;align-items:center;'><input type='checkbox' class='change-type-checkbox' value='{WebUtility.HtmlEncode(t)}' checked /> <span style='margin-left:6px;'>{WebUtility.HtmlEncode(t)}</span></label>"))}
      </div>
    </div>")}

    <div style='display:flex; align-items:center; gap:8px;'>
      <button id='hide-filter-btn'>Close</button>
    </div>

  </div>
</div>

<script>
  const panel = document.getElementById('floating-filter-container');
  const showBtn = document.getElementById('show-filter-btn');
  const hideBtn = document.getElementById('hide-filter-btn');
  function showPanel() {{ panel.style.display = 'block'; }}
  function hidePanel() {{ panel.style.display = 'none'; }}
  showBtn.addEventListener('click', (e) => {{ e.stopPropagation(); panel.style.display === 'none' || panel.style.display === '' ? showPanel() : hidePanel(); }});
  if (hideBtn) hideBtn.addEventListener('click', (e) => {{ e.stopPropagation(); hidePanel(); }});
  document.addEventListener('click', (event) => {{
    if (!panel.contains(event.target) && !showBtn.contains(event.target)) hidePanel();
  }});
</script>
");

            // Group by updated date (local)
            foreach (var group in rows
                .GroupBy(x => x.Updated.ToLocalTime().Date)
                .OrderByDescending(g => g.Key))
            {
                sb.Append($@"
<details open>
  <summary>{group.Key:yyyy-MM-dd} ({group.Count()} issues)</summary>
  <section>
    <div class='subsection'>
      <table class='confluenceTable' style='width:100%;border-collapse:collapse;'>
        <thead>
          <tr>
            <th class='confluenceTh' style='width:36px;'>Type</th>
            <th class='confluenceTh'>Summary</th>
            <th class='confluenceTh'>Changes</th>
            <th class='confluenceTh' style='width:110px;'>Updated</th>
          </tr>
        </thead>
        <tbody>");

                foreach (var item in group)
                {
                    string iconUrl = ResolveTypeIconUrl(item.Type);
                    string iconHtml = !string.IsNullOrWhiteSpace(iconUrl)
                        ? $"<img src='{iconUrl}' style='height:24px;width:24px;vertical-align:middle;margin-right:8px;border-radius:4px;' title='{WebUtility.HtmlEncode(item.Type)}' />"
                        : "<span style='font-size:22px; vertical-align:middle; margin-right:8px;'>🟥</span>";

                    string updatedLocal = item.Updated.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

                    string pathHtml = "";
                    try
                    {
                        var path = frmMain.GetRequirementPath(item.Key);
                        if (!string.IsNullOrWhiteSpace(path))
                            pathHtml = $"<div style='font-size:0.7em;color:#888;margin-left:1px;margin-top:1px;'>{path}</div>";
                    }
                    catch { }

                    string tagsHtml = item.Tags.Count > 0
                        ? $"<div class='recent-update-tags'>{string.Join(" ", item.Tags.Select(t => $"<span class='recent-update-tag' data-changetype='{WebUtility.HtmlEncode(t)}'>{WebUtility.HtmlEncode(t)}</span>"))}</div>"
                        : "";

                    string changeTypeAttr = item.Tags.Count > 0
                        ? $"data-changetypes='{WebUtility.HtmlEncode(string.Join(",", item.Tags))}'"
                        : "data-changetypes=''";

                    string issueTypeAttr = $"data-issuetype='{WebUtility.HtmlEncode(item.Type ?? "")}'";

                    sb.Append($@"
<tr {issueTypeAttr} {changeTypeAttr}>
  <td class='confluenceTd'>{iconHtml}</td>
  <td class='confluenceTd'>
    <a href='#' data-key='{WebUtility.HtmlEncode(item.Key)}' class='recent-link'>
      {WebUtility.HtmlEncode(item.Summary)} [{WebUtility.HtmlEncode(item.Key)}]
    </a>
    {pathHtml}
  </td>
  <td class='confluenceTd'>{tagsHtml}</td>
  <td class='confluenceTd'>{WebUtility.HtmlEncode(updatedLocal)}</td>
</tr>");
                }

                sb.Append(@"
        </tbody>
      </table>
    </div>
  </section>
</details>");
            }

            // Filtering + navigation to SPA
            sb.Append(@"
<script>
function applyGlobalFilter() {
  var typeBoxes = Array.from(document.querySelectorAll('#issue-type-checkboxes .change-type-checkbox'));
  var checkedIssueTypes = typeBoxes.filter(x => x.checked).map(x => x.value);
  var changeTypeBoxes = Array.from(document.querySelectorAll('#change-type-checkboxes .change-type-checkbox'));
  var checkedChangeTypes = changeTypeBoxes.filter(x => x.checked).map(x => x.value);

  document.querySelectorAll('table.confluenceTable tbody tr').forEach(function(row) {
    var rowIssueType = row.getAttribute('data-issuetype') || '';
    var rowChangeTypes = (row.getAttribute('data-changetypes') || '').split(',').filter(Boolean);

    var show = true;
    if (typeBoxes.length > 0 && checkedIssueTypes.length > 0 && !checkedIssueTypes.includes(rowIssueType)) show = false;

    if (changeTypeBoxes.length > 0 && checkedChangeTypes.length > 0) {
      var anyMatch = rowChangeTypes.some(t => checkedChangeTypes.includes(t));
      if (!anyMatch) show = false;
    }

    row.style.display = show ? '' : 'none';
  });
}

// Wire up 'All' + individual checkboxes
(function(){
  const typeAll = document.querySelector('#issue-type-checkboxes .change-type-checkbox-all');
  const typeBoxes = document.querySelectorAll('#issue-type-checkboxes .change-type-checkbox');
  if (typeAll) {
    typeAll.addEventListener('change', function () {
      const checked = this.checked; typeBoxes.forEach(cb => cb.checked = checked); applyGlobalFilter();
    });
  }
  typeBoxes.forEach(cb => {
    cb.addEventListener('change', function () {
      if (typeAll) typeAll.checked = Array.from(typeBoxes).every(x => x.checked);
      applyGlobalFilter();
    });
  });

  const changeAll = document.querySelector('#change-type-checkboxes .change-type-checkbox-all');
  const changeBoxes = document.querySelectorAll('#change-type-checkboxes .change-type-checkbox');
  if (changeAll) {
    changeAll.addEventListener('change', function () {
      const checked = this.checked; changeBoxes.forEach(cb => cb.checked = checked); applyGlobalFilter();
    });
  }
  changeBoxes.forEach(cb => {
    cb.addEventListener('change', function () {
      if (changeAll) changeAll.checked = Array.from(changeBoxes).every(x => x.checked);
      applyGlobalFilter();
    });
  });

  // Initial apply
  applyGlobalFilter();
})();

// Bridge clicks to parent SPA
document.querySelectorAll('a.recent-link[data-key]').forEach(link => {
  link.addEventListener('click', e => {
    e.preventDefault();
    const key = link.dataset.key;
    try {
      if (window.parent && window.parent !== window) {
        window.parent.postMessage({ type: 'open-issue', key: key, title: link.innerText }, '*');
      }
    } catch {}
  });
});
</script>");

            sb.Append("</body></html>");
            return sb.ToString();
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

    // Bridge clicks on issue links to parent SPA to open/select that issue
    (function() {{
      function findKeyFromAnchor(a) {{
        if (!a) return null;
        if (a.dataset && a.dataset.key) return a.dataset.key;
        const t = (a.textContent || '').trim();
        let m = t.match(/([A-Z][A-Z0-9]+-\\d+)/);
        if (m && m[1]) return m[1];
        const h = a.getAttribute('href') || '';
        m = h.match(/([A-Z][A-Z0-9]+-\\d+)/);
        if (m && m[1]) return m[1];
        return null;
      }}
      document.addEventListener('click', function(ev) {{
        const a = ev.target && ev.target.closest ? ev.target.closest('a') : null;
        if (!a) return;
        if (a.target === '_blank') return; // allow external 'Open in Browser'
        const key = findKeyFromAnchor(a);
        if (!key) return;
        const title = (a.textContent || ('[' + key + ']')).trim();
        try {{
          if (window.parent && window.parent !== window) {{
            window.parent.postMessage({{ type: 'open-issue', key: key, title: title }}, '*');
            ev.preventDefault();
            ev.stopPropagation();
          }}
        }} catch (e) {{}}
      }}, true);
    }})();
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
                string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "monovera.sqlite");
                string connStr = $"Data Source={dbPath};";
                using var conn = new Microsoft.Data.Sqlite.SqliteConnection(connStr);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT MAX(UPDATEDTIME) FROM issue";
                var result = cmd.ExecuteScalar();
                if (result != DBNull.Value && result != null &&
                    DateTime.TryParseExact(result.ToString(), "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                {
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

        // Write index.html (embedded monovera.css) and monovera.web.js to WebAppRoot
        private static async Task EnsureWebAssetsAsync(string WebAppRoot)
        {
            string css = "";
            try
            {
                if (!string.IsNullOrWhiteSpace(frmMain.cssPath) && File.Exists(frmMain.cssPath))
                    css = await File.ReadAllTextAsync(frmMain.cssPath, Encoding.UTF8);
                else if (!string.IsNullOrWhiteSpace(frmMain.cssHref))
                    using (var hc = new HttpClient()) css = await hc.GetStringAsync(frmMain.cssHref);
            }
            catch { css = ""; }

            // Namespaced CSS to avoid collisions (mv-*)
            string indexHtml = $@"<!DOCTYPE html>
<html>
<head>
  <meta charset='utf-8' />
  <title>Monovera (Web)</title>
  <style>
{css}
html, body {{ height: 100%; margin: 0; }}
body {{ overflow: hidden; }}
.layout {{ display: grid; grid-template-rows: 1fr 32px; height: 100vh; }}
.main {{ --left: 33%; display: grid; grid-template-columns: var(--left) 6px 1fr; gap: 8px; padding: 8px; box-sizing: border-box; min-height: 0; }}
.splitter {{ grid-column: 2; background: linear-gradient(to right, transparent, #cbd8ea, transparent); cursor: col-resize; user-select: none; }}
.splitter:hover {{ background: linear-gradient(to right, transparent, #b6c9e4, transparent); }}
.sidebar {{ grid-column: 1; border: 1px solid #c0daf3; border-radius: 8px; background: #f5faff; display: flex; flex-direction: column; min-height: 0; overflow: hidden; }}

/* Tree (force no bullets) */
#tree, #tree ul, #tree li {{
  list-style: none !important;
  list-style-type: none !important;
  list-style-image: none !important;
  margin: 0;
  padding-left: 12px;
}}
#tree li::marker {{ content: '' !important; color: transparent !important; }}
#tree li::before {{ content: none !important; }}
#tree {{ padding: 8px; white-space: nowrap; flex: 1 1 auto; overflow: auto; }}
#tree li {{ margin: 2px 0; }}
#tree a {{ cursor: pointer; text-decoration: none; color: #1565c0; padding: 2px 6px; border-radius: 4px; display: inline-flex; align-items: center; gap: 6px; }}
#tree a.selected {{ background:#e3f2fd; color:#0d47a1; outline:1px solid #b3d4f6; }}
#tree .expander {{ display:inline-block; width:16px; text-align:center; margin-right:6px; cursor:pointer; user-select:none; color:#0d47a1; font-weight:700; font-family:Consolas,monospace; }}
.node-icon {{ width:18px; height:18px; vertical-align:middle; border-radius:3px; }}

/* Right workspace */
.workspace {{ grid-column: 3; display: flex; flex-direction: column; min-width: 0; min-height: 0; overflow: hidden; }}

/* Tabs (namespaced) */
.mv-tabs-bar {{
  display: grid;
  grid-template-columns: auto 1fr auto;
  align-items: center;
  gap: 6px;
  border-bottom: 1px solid #b3d4f6;
  background: #f2faff;
  padding: 6px 6px 0 6px;
}}
.mv-tabs-viewport {{ overflow: hidden; }}
#mv-tabs {{
  position: relative;
  display: inline-flex;
  gap: 4px;
  white-space: nowrap;
  overflow-x: auto;
  overflow-y: hidden;
  scrollbar-width: none;
  -ms-overflow-style: none;
}}
#mv-tabs::-webkit-scrollbar {{ display: none; }}

.mv-tab {{
  background:#fff;
  border:1px solid #b3d4f6; border-bottom:none;
  border-radius:6px 6px 0 0;
  padding:6px 10px;
  cursor:pointer;
  display:flex; align-items:center; gap:8px;
  max-width: 360px;
}}
.mv-tab.active {{ font-weight:600; color:#1565c0; border-bottom:2px solid #1565c0; }}
.mv-tab-key {{ font-weight:600; }}
.mv-tab-close {{
  margin-left:6px;
  width:16px; height:16px;
  display:inline-flex; align-items:center; justify-content:center;
  font-weight:700; font-size:12px; line-height:1;
  color:#fff; background:#d32f2f; border:1px solid #b71c1c;
  border-radius:3px; cursor:pointer; box-shadow:0 1px 2px rgba(0,0,0,.2);
  flex: 0 0 auto;
}}
.mv-tab-close:hover {{ background:#b71c1c; border-color:#8a1111; }}

/* Scroll buttons */
.mv-tab-scroll {{
  appearance: none; -webkit-appearance: none;
  border: 1px solid #b3d4f6; background: #ffffff; color: #1565c0;
  width: 28px; height: 24px; border-radius: 4px; display: none;
  align-items: center; justify-content: center; cursor: pointer;
  user-select: none;
}}
.mv-tab-scroll[disabled] {{ opacity: .5; cursor: default; }}

/* Views */
#mv-views {{ flex: 1 1 auto; position: relative; min-height: 0; overflow: hidden; }}
.mv-view {{ position: absolute; inset: 0; display: none; background:#fff; }}
.mv-view.active {{ display: block; }}
.mv-view iframe {{ width: 100%; height: 100%; border: none; background: #fff; }}

/* Home splash */
.home-splash {{ width:100%; height:100%; display:flex; align-items:center; justify-content:center; background:#fff; }}
.home-splash img {{ max-width:100%; max-height:100%; object-fit: contain; }}

.status {{ display: flex; align-items: center; padding: 0 12px; border-top: 1px solid #b3d4f6; background: #f2faff; color: #1565c0; gap: 16px; }}
  </style>
</head>
<body>
  <div class='layout'>
    <div class='main'>
      <aside class='sidebar'>
        <ul id='tree'></ul>
      </aside>
      <div id='splitter' class='splitter' role='separator' aria-orientation='vertical' tabindex='0' title='Drag to resize'></div>
      <section class='workspace'>
        <div class='mv-tabs-bar'>
          <button id='mv-tabPrev' class='mv-tab-scroll' title='Scroll left' aria-label='Scroll left'>&lsaquo;</button>
          <div class='mv-tabs-viewport'><div id='mv-tabs'></div></div>
          <button id='mv-tabNext' class='mv-tab-scroll' title='Scroll right' aria-label='Scroll right'>&rsaquo;</button>
        </div>
        <div id='mv-views'>
          <!-- Default Home view (shows background image when no tabs are open) -->
          <div id='mv-home' class='mv-view active'>
            <div class='home-splash'>
              <img src='/static/images/MonoveraBackground.png' alt='Monovera' onerror=""this.outerHTML='<div style=\'color:#b00;font:14px Segoe UI\'>Missing images/MonoveraBackground.png</div>'"" />
            </div>
          </div>
        </div>
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

            string webJs = @"(async function () {
  const treeEl = document.getElementById('tree');

  // Tabs
  const tabsEl = document.getElementById('mv-tabs');               // scrollable strip
  const tabsViewport = document.querySelector('.mv-tabs-viewport'); // viewport
  const prevBtn = document.getElementById('mv-tabPrev');
  const nextBtn = document.getElementById('mv-tabNext');
  const viewsEl = document.getElementById('mv-views');
  const homeView = document.getElementById('mv-home');

  // Splitter
  const mainEl = document.querySelector('.main');
  const splitter = document.getElementById('splitter');

  // --- Resizer ---
  const MIN_LEFT = 220, MIN_RIGHT = 360;
  function setLeftWidth(px){ mainEl.style.setProperty('--left', px + 'px'); splitter.setAttribute('aria-valuenow', String(px)); }
  function clampWidth(px){ const r = mainEl.getBoundingClientRect(); const max = Math.max(MIN_LEFT, r.width - MIN_RIGHT); return Math.max(MIN_LEFT, Math.min(px, max)); }
  function startDrag(e){ e.preventDefault(); const r = mainEl.getBoundingClientRect(); document.body.classList.add('resizing');
    const move = (ev)=>{ const x = (ev.touches?.[0]?.clientX ?? ev.clientX) - r.left; setLeftWidth(clampWidth(x)); requestAnimationFrame(updateTabScrollButtons); };
    const up = ()=>{ document.body.classList.remove('resizing'); window.removeEventListener('mousemove', move); window.removeEventListener('mouseup', up); window.removeEventListener('touchmove', move); window.removeEventListener('touchend', up); };
    window.addEventListener('mousemove', move); window.addEventListener('mouseup', up); window.addEventListener('touchmove', move, { passive:false }); window.addEventListener('touchend', up);
  }
  splitter.addEventListener('mousedown', startDrag);
  splitter.addEventListener('touchstart', startDrag, { passive:false });

  // --- Status ---
  async function refreshStatus(){
    try {
      const s = await (await fetch('/api/status')).json();
      document.getElementById('statusUser').textContent = '👤 User: ' + (s.connectedUser || '-');
      document.getElementById('statusMode').textContent = '🌐 Mode: ' + (s.offline ? 'Offline' : 'Online');
      document.getElementById('statusUpdated').textContent = '🕒 DB Updated: ' + (s.lastDbUpdated || 'N/A');
    } catch {}
  }
  refreshStatus(); setInterval(refreshStatus, 10000);

  // --- Tree selection helpers ---
  let selectedAnchor = null;
  function setSelected(a){ if (selectedAnchor){ selectedAnchor.classList.remove('selected'); selectedAnchor.setAttribute('aria-selected','false'); } selectedAnchor = a; if (a){ a.classList.add('selected'); a.setAttribute('aria-selected','true'); a.scrollIntoView({block:'nearest', inline:'nearest'}); } }
  function highlightTreeSelection(key){ const a = document.querySelector(`#tree a[data-key='${key}']`); if (a) setSelected(a); }

  function liNode({ key, text, hasChildren, icon }) {
    const li = document.createElement('li');

    const exp = document.createElement('span'); exp.className='expander'; exp.textContent = hasChildren ? '+' : ''; exp.dataset.state='collapsed'; exp.style.visibility = hasChildren ? 'visible' : 'hidden';
    const a = document.createElement('a'); a.href='#'; a.dataset.key=key;
    if (icon) { const img=document.createElement('img'); img.src=icon; img.className='node-icon'; img.alt=''; a.appendChild(img); }
    a.appendChild(document.createTextNode(text));
    a.addEventListener('click', (e) => { e.preventDefault(); setSelected(a); openTab(key, text, icon); });

    const ul = document.createElement('ul'); ul.style.display='none';
    exp.addEventListener('click', async () => { if (exp.dataset.state === 'collapsed') { await expandNode(li, key); } else { collapseNode(li); } });

    li.appendChild(exp); li.appendChild(a); li.appendChild(ul);
    return li;
  }

  async function expandNode(li, key){
    const exp = li.querySelector('span.expander');
    const ul = li.querySelector('ul');
    if (!exp || !ul) return;
    if (exp.dataset.state === 'expanded') return;
    const children = await (await fetch(`/api/tree/children/${encodeURIComponent(key)}`)).json();
    ul.innerHTML = '';
    children.forEach(c => ul.appendChild(liNode(c)));
    ul.style.display = 'block';
    exp.textContent = '-';
    exp.dataset.state = 'expanded';
  }

  function collapseNode(li){
    const exp = li.querySelector('span.expander');
    const ul = li.querySelector('ul');
    if (!exp || !ul) return;
    ul.style.display = 'none';
    exp.textContent = '+';
    exp.dataset.state = 'collapsed';
  }

  async function loadRoots() {
    const roots = await (await fetch('/api/tree/roots')).json();
    treeEl.innerHTML = '';
    roots.forEach(r => treeEl.appendChild(liNode(r)));
  }

  // One-time root expansion on load
  let expandedRootsOnce = false;
  async function expandRootLevelOnce() {
    if (expandedRootsOnce) return;
    expandedRootsOnce = true;
    const roots = Array.from(treeEl.children);
    for (const li of roots) {
      const a = li.querySelector('a[data-key]');
      const exp = li.querySelector('span.expander');
      const key = a?.dataset.key;
      if (key && exp && exp.dataset.state === 'collapsed' && exp.style.visibility !== 'hidden') {
        await expandNode(li, key);
      }
    }
  }

  // Expand to key and select within SPA tree
  async function expandAndSelect(key) {
    try {
      if (!treeEl.children.length) {
        await loadRoots();
        await expandRootLevelOnce();
      }
      const res = await fetch(`/api/tree/path/${encodeURIComponent(key)}`);
      if (!res.ok) return;
      const path = await res.json();
      if (!Array.isArray(path) || !path.length) return;
      for (let i = 0; i < path.length; i++) {
        const k = path[i];
        let a = document.querySelector(`#tree a[data-key='${k}']`);
        if (!a && i > 0) {
          const prevA = document.querySelector(`#tree a[data-key='${path[i - 1]}']`);
          const liPrev = prevA ? prevA.parentElement : null;
          if (liPrev) await expandNode(liPrev, path[i - 1]);
          a = document.querySelector(`#tree a[data-key='${k}']`);
        }
        if (i < path.length - 1) {
          const li = a ? a.parentElement : null;
          if (li) await expandNode(li, k);
        } else {
          if (a) setSelected(a);
        }
      }
    } catch {}
  }

  // --- Tabs: scrolling logic (namespaced) ---
  function updateTabScrollButtons(){
    const canScroll = tabsEl.scrollWidth > tabsViewport.clientWidth + 1;
    prevBtn.style.display = canScroll ? 'inline-flex' : 'none';
    nextBtn.style.display = canScroll ? 'inline-flex' : 'none';
    prevBtn.disabled = !canScroll || tabsEl.scrollLeft <= 0;
    nextBtn.disabled = !canScroll || (tabsEl.scrollLeft + tabsViewport.clientWidth >= tabsEl.scrollWidth - 1);
  }
  function scrollTabsBy(delta){
    tabsEl.scrollBy({ left: delta, behavior: 'smooth' });
  }
  prevBtn.addEventListener('click', () => scrollTabsBy(-Math.max(200, tabsViewport.clientWidth * 0.6)));
  nextBtn.addEventListener('click', () => scrollTabsBy(+Math.max(200, tabsViewport.clientWidth * 0.6)));
  tabsEl.addEventListener('scroll', () => requestAnimationFrame(updateTabScrollButtons));
  window.addEventListener('resize', () => requestAnimationFrame(updateTabScrollButtons));

  // Keep active tab in view
  function ensureActiveTabVisible(key){
    const tab = document.getElementById(makeTabId(key));
    if (!tab) return;
    const tabRect = tab.getBoundingClientRect();
    const viewRect = tabsViewport.getBoundingClientRect();
    if (tabRect.right > viewRect.right - 8) {
      const diff = tabRect.right - viewRect.right + 8;
      tabsEl.scrollBy({ left: diff, behavior: 'smooth' });
    } else if (tabRect.left < viewRect.left + 8) {
      const diff = tabRect.left - viewRect.left - 8;
      tabsEl.scrollBy({ left: diff, behavior: 'smooth' });
    }
  }

  // Helpers for views/tabs
  function makeTabId(key){ return 'tab-' + key; }
  function makeViewId(key){ return 'view-' + key; }

  function showHomeIfNoTabs() {
    if (!tabsEl.children.length) {
      [...viewsEl.children].forEach(ch => ch.classList.remove('active'));
      if (homeView) homeView.classList.add('active');
    }
  }

  function activate(key) {
    const id = makeTabId(key);
    const vid = makeViewId(key);
    [...tabsEl.children].forEach(ch => ch.classList.toggle('active', ch.id === id));
    [...viewsEl.children].forEach(ch => ch.classList.toggle('active', ch.id === vid));
    if (homeView) homeView.classList.remove('active');
    highlightTreeSelection(key);
    ensureActiveTabVisible(key);
    updateTabScrollButtons();
  }

  function getIconForKey(key){
    const img = document.querySelector(`#tree a[data-key='${key}'] img.node-icon`);
    return img ? img.src : null;
  }

  async function openTab(key, title, icon, { activateTab = true } = {}) {
    const tabId = makeTabId(key);
    const viewId = makeViewId(key);

    if (!document.getElementById(tabId)) {
      const tab = document.createElement('div');
      tab.className='mv-tab'; tab.id=tabId; tab.dataset.key=key; tab.title=title;

      const iconSrc = icon || getIconForKey(key);
      if (iconSrc){ const img=document.createElement('img'); img.src=iconSrc; img.className='node-icon'; img.alt=''; tab.appendChild(img); }
      const keySpan = document.createElement('span'); keySpan.className='mv-tab-key'; keySpan.textContent='[' + key + ']'; tab.appendChild(keySpan);

      const close = document.createElement('span');
      close.className='mv-tab-close'; close.textContent='×'; close.title='Close'; close.setAttribute('aria-label','Close');
      close.addEventListener('click', (e) => {
        e.stopPropagation();
        const t=document.getElementById(tabId), v=document.getElementById(viewId);
        if (t) tabsEl.removeChild(t);
        if (v) viewsEl.removeChild(v);
        const last=tabsEl.lastElementChild;
        updateTabScrollButtons();
        if (last){ const lastKey=last.dataset.key || last.id.replace(/^tab-/,''); activate(lastKey); }
        else { showHomeIfNoTabs(); }
      });

      tab.addEventListener('click', () => { activate(key); });
      tab.appendChild(close);
      tabsEl.appendChild(tab);

      const view = document.createElement('div'); view.className='mv-view'; view.id=viewId;
      const iframe = document.createElement('iframe'); iframe.setAttribute('title', key); view.appendChild(iframe);
      viewsEl.appendChild(view);

      try {
        // Set loading page first
        iframe.srcdoc = `<html><body><div style='display:flex;align-items:center;justify-content:center;height:100%;font:14px Segoe UI;color:#1565c0;'>Loading...</div></body></html>`;
        const html = await (await fetch(`/api/issue/${encodeURIComponent(key)}/html`)).text();
        iframe.srcdoc = html;
      } catch {
        iframe.srcdoc = `<html><body><div style='padding: 20px; color:#b00;'>Failed to load ${key}</div></body></html>`;
      }
    }
    if (activateTab) activate(key);
  }

  // Special: open Recent Updates tab (HTML provided by server)
  async function openRecentUpdatesTab({ days = 14, activateTab = true } = {}) {
    const key = 'RECENT-UPDATES';
    const tabId = makeTabId(key);
    const viewId = makeViewId(key);
    if (!document.getElementById(tabId)) {
      const tab = document.createElement('div');
      tab.className='mv-tab'; tab.id=tabId; tab.dataset.key=key; tab.title='Recent Updates!';
      const keySpan = document.createElement('span'); keySpan.className='mv-tab-key'; keySpan.textContent='[Recent Updates]';
      tab.appendChild(keySpan);

      const close = document.createElement('span');
      close.className='mv-tab-close'; close.textContent='×'; close.title='Close'; close.setAttribute('aria-label','Close');
      close.addEventListener('click', (e) => {
        e.stopPropagation();
        const t=document.getElementById(tabId), v=document.getElementById(viewId);
        if (t) tabsEl.removeChild(t);
        if (v) viewsEl.removeChild(v);
        const last=tabsEl.lastElementChild;
        updateTabScrollButtons();
        if (last){ const lastKey=last.dataset.key || last.id.replace(/^tab-/,''); activate(lastKey); }
        else { showHomeIfNoTabs(); }
      });

      tab.addEventListener('click', () => { activate(key); });
      tab.appendChild(close);
      tabsEl.appendChild(tab);

      const view = document.createElement('div'); view.className='mv-view'; view.id=viewId;
      const iframe = document.createElement('iframe'); iframe.setAttribute('title', 'Recent Updates'); view.appendChild(iframe);
      viewsEl.appendChild(view);

      try {
        // Show loading placeholder first
        iframe.srcdoc = `<html><body><div style='display:flex;align-items:center;justify-content:center;height:100%;font:14px Segoe UI;color:#1565c0;'>Loading Recent Updates...</div></body></html>`;
        const html = await (await fetch(`/api/recent/updated/html?days=${encodeURIComponent(days)}`)).text();
        iframe.srcdoc = html;
      } catch {
        iframe.srcdoc = `<html><body><div style='padding: 20px; color:#b00;'>Failed to load Recent Updates</div></body></html>`;
      }
    }
    if (activateTab) activate(key);
  }

  // Messages from iframes: open tab AND expand/select tree
  window.addEventListener('message', (ev) => {
    try {
      const d = ev.data || {};
      if (d.type === 'open-issue' && d.key) {
        (async () => {
          await expandAndSelect(d.key);
          await openTab(d.key, d.title || ('[' + d.key + ']'), null);
        })();
      }
    } catch {}
  });

  await loadRoots();
  await expandRootLevelOnce();

  // Auto-open Recent Updates (same behavior as desktop)
  await openRecentUpdatesTab({ days: 14, activateTab: true });

  updateTabScrollButtons();
})();";

            Directory.CreateDirectory(WebAppRoot);
            await File.WriteAllTextAsync(Path.Combine(WebAppRoot, "index.html"), indexHtml, Encoding.UTF8);
            await File.WriteAllTextAsync(Path.Combine(WebAppRoot, "monovera.web.js"), webJs, Encoding.UTF8);
        }

    }
}