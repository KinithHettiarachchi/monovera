using Atlassian.Jira;
using HtmlAgilityPack;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.Security;
using Microsoft.VisualBasic.FileIO;
using NAudio.Gui;
using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Net.Mime.MediaTypeNames;
using static System.Net.WebRequestMethods;
using static System.Runtime.InteropServices.JavaScript.JSType;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Tab;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TrayNotify;

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

            // Ensure reports directory (served to browser)
            var reportsDirPhysical = Path.Combine(baseDir, "reports");
            Directory.CreateDirectory(reportsDirPhysical);

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

                    // Serve generated reports at /reports
                    if (Directory.Exists(reportsDirPhysical))
                    {
                        app.UseStaticFiles(new StaticFileOptions
                        {
                            FileProvider = new PhysicalFileProvider(reportsDirPhysical),
                            RequestPath = "/reports"
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
                            if (System.IO.File.Exists(cssPath))
                            {
                                var css = await System.IO.File.ReadAllTextAsync(cssPath);
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
                                lastDbUpdated = GetMaxUpdatedTimeFromDbWeb(),
                                syncStatus = frmMain.syncStatusCode,
                                pendingUpdates = frmMain.pendingUpdateCount
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

                            // Return loader page (spinner + JS) immediately
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

                            // Await the async builder to get the HTML string
                            string html = await BuildRecentUpdatesHtmlFinalAsync(days);
                            context.Response.ContentType = "text/html; charset=utf-8";
                            await context.Response.WriteAsync(html);
                        });

                        // Returns [rootKey, ..., targetKey] for SPA expansion
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

                        // Generate Report: produces offline HTML report and returns its URL
                        endpoints.MapPost("/api/report/{rootKey}", async context =>
                        {
                            var rootKey = context.Request.RouteValues["rootKey"]?.ToString() ?? "";
                            if (string.IsNullOrWhiteSpace(rootKey))
                            {
                                context.Response.StatusCode = 400;
                                await context.Response.WriteAsync("Missing rootKey");
                                return;
                            }

                            // Run work on STA with a WinForms message loop to satisfy any UI/COM/WebBrowser dependencies
                            static Task<string> RunOnStaWithMessageLoopAsync(Func<Task<string>> func)
                            {
                                var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

                                var th = new System.Threading.Thread(() =>
                                {
                                    try
                                    {
                                        // Start after the loop is alive
                                        System.EventHandler handler = null;
                                        handler = async (s, e) =>
                                        {
                                            // Run once
                                            System.Windows.Forms.Application.Idle -= handler;
                                            try
                                            {
                                                // Keep continuations on this STA ctx (message loop present)
                                                var result = await func().ConfigureAwait(true);
                                                tcs.TrySetResult(result);
                                            }
                                            catch (Exception ex)
                                            {
                                                tcs.TrySetException(ex);
                                            }
                                            finally
                                            {
                                                // Exit the message loop/thread
                                                try { System.Windows.Forms.Application.ExitThread(); } catch { }
                                            }
                                        };
                                        System.Windows.Forms.Application.Idle += handler;
                                        System.Windows.Forms.Application.Run();
                                    }
                                    catch (Exception ex)
                                    {
                                        tcs.TrySetException(ex);
                                    }
                                });

                                th.IsBackground = true;
                                th.SetApartmentState(System.Threading.ApartmentState.STA);
                                th.Start();
                                return tcs.Task;
                            }

                            try
                            {
                                // Optional: server-side timeout to avoid hanging forever
                                using var cts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
                                cts.CancelAfter(TimeSpan.FromMinutes(3));

                                var genTask = RunOnStaWithMessageLoopAsync(async () =>
                                {
                                    var generator = new JiraHtmlReportGenerator(
                                        frmMain.issueDict,
                                        frmMain.childrenByParent,
                                        frmMain.jiraEmail,
                                        frmMain.jiraToken,
                                        frmMain.jiraBaseUrl,
                                        new System.Windows.Forms.TreeView() // placeholder; generator may rely on WinForms context
                                    );
                                    // If your generator supports a token, pass cts.Token here
                                    var path = await generator.GenerateAsync(rootKey);
                                    return path;
                                });

                                var completed = await Task.WhenAny(genTask, Task.Delay(Timeout.InfiniteTimeSpan, cts.Token));
                                if (completed != genTask)
                                {
                                    context.Response.StatusCode = 504;
                                    await context.Response.WriteAsync("Report generation timed out.");
                                    return;
                                }

                                var filePath = await genTask; // propagate exceptions
                                if (string.IsNullOrWhiteSpace(filePath) || !System.IO.File.Exists(filePath))
                                {
                                    context.Response.StatusCode = 500;
                                    await context.Response.WriteAsync("Report generation failed: output file missing.");
                                    return;
                                }

                                // Ensure the result is available under /reports
                                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                                string reportsDirPhysical = Path.Combine(baseDir, "reports");
                                Directory.CreateDirectory(reportsDirPhysical);

                                string targetName = Path.GetFileName(filePath);
                                if (string.IsNullOrWhiteSpace(targetName))
                                    targetName = $"{rootKey}-{DateTime.Now:yyyyMMddHHmmss}.html";

                                string targetPath = Path.Combine(reportsDirPhysical, targetName);

                                try
                                {
                                    var srcFull = Path.GetFullPath(filePath);
                                    var dstFull = Path.GetFullPath(targetPath);
                                    if (!srcFull.Equals(dstFull, StringComparison.OrdinalIgnoreCase))
                                    {
                                        System.IO.File.Copy(srcFull, dstFull, overwrite: true);
                                    }
                                }
                                catch (Exception copyEx)
                                {
                                    context.Response.StatusCode = 500;
                                    await context.Response.WriteAsync($"Report generated but failed to move into /reports: {copyEx.Message}");
                                    return;
                                }

                                var url = "/reports/" + targetName;
                                context.Response.ContentType = "application/json; charset=utf-8";
                                context.Response.Headers["Cache-Control"] = "no-store";
                                await context.Response.WriteAsync(JsonSerializer.Serialize(new { url, file = targetName }));
                            }
                            catch (Exception ex)
                            {
                                context.Response.StatusCode = 500;
                                await context.Response.WriteAsync($"Report generation failed: {ex.Message}");
                            }
                        });

                        // AI Chat: Get bot status and training info
                        endpoints.MapGet("/api/ai/status", async context =>
                        {
                            try
                            {
                                var dbPath = frmMain.DatabasePath;
                                var modelDir = Path.GetDirectoryName(dbPath) ?? Environment.CurrentDirectory;
                                var knowledgeIndexPath = Path.Combine(modelDir, "monovera_knowledge.idx");
                                bool isTrained = System.IO.File.Exists(knowledgeIndexPath);

                                var payload = new
                                {
                                    trained = isTrained,
                                    databasePath = dbPath
                                };

                                context.Response.ContentType = "application/json; charset=utf-8";
                                await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
                            }
                            catch (Exception ex)
                            {
                                context.Response.StatusCode = 500;
                                await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = ex.Message }));
                            }
                        });

                        // AI Chat: Ask a question
                        endpoints.MapPost("/api/ai/ask", async context =>
                        {
                            try
                            {
                                using var reader = new StreamReader(context.Request.Body);
                                var body = await reader.ReadToEndAsync();
                                var request = JsonSerializer.Deserialize<JsonElement>(body);

                                if (!request.TryGetProperty("question", out var questionProp))
                                {
                                    context.Response.StatusCode = 400;
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = "Missing question" }));
                                    return;
                                }

                                string question = questionProp.GetString() ?? "";
                                if (string.IsNullOrWhiteSpace(question))
                                {
                                    context.Response.StatusCode = 400;
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = "Question cannot be empty" }));
                                    return;
                                }

                                // Create MonoveraBot instance
                                using var bot = new MonoveraBot(frmMain.DatabasePath);

                                if (!bot.IsTrained)
                                {
                                    var trainResponse = new { answer = "MonoveraBot is not trained yet. Please train it first from the desktop app (AI Assistant > Train Local Model) or wait for automatic training to complete." };
                                    context.Response.ContentType = "application/json; charset=utf-8";
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(trainResponse));
                                    return;
                                }

                                // Generate answer
                                string answer = await bot.AskAsync(question);

                                var successResponse = new { answer };
                                context.Response.ContentType = "application/json; charset=utf-8";
                                await context.Response.WriteAsync(JsonSerializer.Serialize(successResponse));
                            }
                            catch (Exception ex)
                            {
                                context.Response.StatusCode = 500;
                                await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = $"Error: {ex.Message}" }));
                            }
                        });

                        // AI Chat: HTML page
                        endpoints.MapGet("/api/ai/chat", async context =>
                        {
                            var html = BuildAIChatHtml();
                            context.Response.ContentType = "text/html; charset=utf-8";
                            await context.Response.WriteAsync(html);
                        });

                        // Search options for the dialog (projects/types/status)
                        endpoints.MapGet("/api/search/options", async context =>
                        {
                            var projects = new List<object>();
                            try
                            {
                                var cfgProjects = frmMain.config?.Projects ?? new List<frmMain.JiraProjectConfig>();
                                foreach (var p in cfgProjects)
                                {
                                    var types = p?.Types?.Keys?.OrderBy(k => k, StringComparer.OrdinalIgnoreCase)?.ToArray() ?? Array.Empty<string>();
                                    var statuses = p?.Status?.Keys?.OrderBy(k => k, StringComparer.OrdinalIgnoreCase)?.ToArray() ?? Array.Empty<string>();
                                    projects.Add(new
                                    {
                                        project = p?.Project ?? "",
                                        types,
                                        statuses
                                    });
                                }
                            }
                            catch
                            {
                            }

                            var payload = new { projects };
                            context.Response.ContentType = "application/json; charset=utf-8";
                            await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
                        });

                        // Search results HTML using Jira JQL (mirrors desktop search behavior)
                        endpoints.MapGet("/api/search/html", async context =>
                        {
                            var jql = context.Request.Query["jql"].ToString() ?? "";
                            context.Response.ContentType = "text/html; charset=utf-8";
                            if (string.IsNullOrWhiteSpace(jql))
                            {
                                await context.Response.WriteAsync("<!DOCTYPE html><html><body style='font:14px Segoe UI;padding:16px;color:#b00;'>Missing jql parameter.</body></html>");
                                return;
                            }
                            var html = await BuildSearchResultsHtml(jql);
                            await context.Response.WriteAsync(html);
                        });

                        // ── Configuration API ─────────────────────────────────────────────────
                        endpoints.MapGet("/api/config", async context =>
                        {
                            try
                            {
                                var cfgPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "configuration.json");
                                if (!System.IO.File.Exists(cfgPath))
                                {
                                    context.Response.ContentType = "application/json; charset=utf-8";
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new
                                    {
                                        jira = new { url = "", email = "", token = "", offlineMode = false },
                                        projects = new object[0]
                                    }));
                                    return;
                                }
                                var raw = await System.IO.File.ReadAllTextAsync(cfgPath, Encoding.UTF8);
                                context.Response.ContentType = "application/json; charset=utf-8";
                                await context.Response.WriteAsync(raw);
                            }
                            catch (Exception ex)
                            {
                                context.Response.StatusCode = 500;
                                await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = ex.Message }));
                            }
                        });

                        endpoints.MapPost("/api/config", async context =>
                        {
                            try
                            {
                                using var sr = new StreamReader(context.Request.Body, Encoding.UTF8);
                                var body = await sr.ReadToEndAsync();
                                // Validate JSON
                                using var doc = JsonDocument.Parse(body);
                                var cfgPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "configuration.json");
                                // Pretty-print before writing
                                var pretty = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
                                await System.IO.File.WriteAllTextAsync(cfgPath, pretty, Encoding.UTF8);
                                // Reload in-memory values (best-effort, full config reload requires restart)
                                try
                                {
                                    var reloaded = JsonDocument.Parse(pretty).RootElement;
                                    if (reloaded.TryGetProperty("Jira", out var jiraEl))
                                    {
                                        if (jiraEl.TryGetProperty("Url", out var urlEl))
                                            frmMain.jiraBaseUrl = urlEl.GetString() ?? frmMain.jiraBaseUrl;
                                        if (jiraEl.TryGetProperty("OfflineMode", out var omEl))
                                            frmMain.OFFLINE_MODE = omEl.GetBoolean();
                                    }
                                }
                                catch { /* reload is best-effort */ }
                                context.Response.ContentType = "application/json; charset=utf-8";
                                await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = true }));
                            }
                            catch (Exception ex)
                            {
                                context.Response.StatusCode = 500;
                                await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = ex.Message }));
                            }
                        });

                        // ── Hierarchy Update API ──────────────────────────────────────────────
                        endpoints.MapPost("/api/hierarchy/update", async context =>
                        {
                            try
                            {
                                using var sr = new StreamReader(context.Request.Body, Encoding.UTF8);
                                var body = await sr.ReadToEndAsync();
                                using var doc = JsonDocument.Parse(body);
                                var root = doc.RootElement;
                                string updateType = root.TryGetProperty("updateType", out var ut) ? (ut.GetString() ?? "Difference") : "Difference";
                                string project = root.TryGetProperty("project", out var proj) ? (proj.GetString() ?? "All") : "All";

                                // Run on STA thread (LoadAllProjectsToTreeAsync requires UI thread access)
                                var tcs = new System.Threading.Tasks.TaskCompletionSource<string>();
                                var th = new System.Threading.Thread(() =>
                                {
                                    try
                                    {
                                        var main = System.Windows.Forms.Application.OpenForms.OfType<frmMain>().FirstOrDefault();
                                        if (main == null) { tcs.TrySetResult("no_form"); return; }
                                        main.Invoke(async () =>
                                        {
                                            try
                                            {
                                                string selectedProject = project == "All" ? null : project;
                                                await main.LoadAllProjectsToTreeAsync(true, updateType, selectedProject);
                                                tcs.TrySetResult("ok");
                                            }
                                            catch (Exception ex2) { tcs.TrySetException(ex2); }
                                        });
                                    }
                                    catch (Exception ex) { tcs.TrySetException(ex); }
                                });
                                th.IsBackground = true;
                                th.SetApartmentState(System.Threading.ApartmentState.STA);
                                th.Start();

                                var result = await tcs.Task;
                                context.Response.ContentType = "application/json; charset=utf-8";
                                await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = true, result }));
                            }
                            catch (Exception ex)
                            {
                                context.Response.StatusCode = 500;
                                await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = ex.Message }));
                            }
                        });

                        // ── Hierarchy Progress (polling) ──────────────────────────────────
                        endpoints.MapGet("/api/hierarchy/progress", async context =>
                        {
                            var payload = new
                            {
                                inProgress = frmMain.updateInProgress,
                                project    = frmMain.updateProgressProject,
                                completed  = frmMain.updateProgressCompleted,
                                total      = frmMain.updateProgressTotal,
                                percent    = Math.Round(frmMain.updateProgressPercent, 1)
                            };
                            context.Response.ContentType = "application/json; charset=utf-8";
                            await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
                        });

                        // ── AI Train API ──────────────────────────────────────────────────────
                        endpoints.MapPost("/api/ai/train", async context =>
                        {
                            try
                            {
                                var bot = new MonoveraBot(frmMain.DatabasePath);
                                var messages = new System.Collections.Concurrent.ConcurrentQueue<string>();
                                var progress = new Progress<string>(msg => messages.Enqueue(msg));
                                await bot.TrainAsync(progress);
                                context.Response.ContentType = "application/json; charset=utf-8";
                                await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = true }));
                            }
                            catch (Exception ex)
                            {
                                context.Response.StatusCode = 500;
                                await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = ex.Message }));
                            }
                        });

                        // ── Projects list (for hierarchy update dropdown) ──────────────────────
                        endpoints.MapGet("/api/projects", async context =>
                        {
                            var list = frmMain.config?.Projects?.Select(p => p.Project).Where(p => !string.IsNullOrWhiteSpace(p)).ToList()
                                       ?? new List<string>();
                            context.Response.ContentType = "application/json; charset=utf-8";
                            await context.Response.WriteAsync(JsonSerializer.Serialize(list));
                        });

                        // ── Last DB updated time ──────────────────────────────────────────────
                        endpoints.MapGet("/api/db/updated", async context =>
                        {
                            var t = GetMaxUpdatedTimeFromDbWeb();
                            context.Response.ContentType = "application/json; charset=utf-8";
                            await context.Response.WriteAsync(JsonSerializer.Serialize(new { updated = t }));
                        });

                        // ── Editor mode flag ──────────────────────────────────────────────────
                        endpoints.MapGet("/api/editor/mode", async context =>
                        {
                            context.Response.ContentType = "application/json; charset=utf-8";
                            var projects = frmMain.config?.Projects?.Select(p => new
                            {
                                projectKey  = p.Root?.Split('-')[0] ?? p.Project,
                                projectName = p.Project,
                                canCreate   = p.HasCreatePermission,
                                canEdit     = p.HasEditPermission,
                                issueTypes  = p.Types?.Keys.ToList() ?? new List<string>()
                            }).ToList();
                            await context.Response.WriteAsync(JsonSerializer.Serialize(new { editorMode = true, projects }));
                        });

                        // ── All issue keys + summaries for autocomplete ───────────────────────
                        endpoints.MapGet("/api/issue/keys", async context =>
                        {
                            context.Response.ContentType = "application/json; charset=utf-8";
                            var dict = frmMain.FlatJiraIssueDictionary.Select(kvp => new { key = kvp.Key, summary = kvp.Value.Summary ?? "" }).ToList();
                            await context.Response.WriteAsync(JsonSerializer.Serialize(dict));
                        });

                        // ── Add child / sibling issue ─────────────────────────────────────────
                        endpoints.MapPost("/api/issue/create", async context =>
                        {
                            try
                            {
                                var body = await JsonSerializer.DeserializeAsync<JsonElement>(context.Request.Body);
                                string baseKey = body.GetProperty("baseKey").GetString() ?? "";
                                string mode = body.GetProperty("mode").GetString() ?? "Child";
                                string issueType = body.GetProperty("issueType").GetString() ?? "";
                                string summary = body.GetProperty("summary").GetString() ?? "";

                                string selectedKey = mode == "Sibling"
                                    ? (frmMain.FlatJiraIssueDictionary.TryGetValue(baseKey, out var bi) ? bi.ParentKey ?? baseKey : baseKey)
                                    : baseKey;

                                string? newKey = await frmMain.jiraService.CreateAndLinkJiraIssueAsync(selectedKey, mode, issueType, summary, frmMain.config);
                                context.Response.ContentType = "application/json; charset=utf-8";
                                await context.Response.WriteAsync(JsonSerializer.Serialize(new { success = true, newKey }));
                            }
                            catch (Exception ex)
                            {
                                context.Response.StatusCode = 500;
                                await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = ex.Message }));
                            }
                        });

                        // ── Link related issues ───────────────────────────────────────────────
                        endpoints.MapPost("/api/issue/link-related", async context =>
                        {
                            try
                            {
                                var body = await JsonSerializer.DeserializeAsync<JsonElement>(context.Request.Body);
                                string baseKey = body.GetProperty("baseKey").GetString() ?? "";
                                var keys = body.GetProperty("keys").EnumerateArray().Select(e => e.GetString() ?? "").Where(k => !string.IsNullOrWhiteSpace(k)).ToList();
                                await frmMain.jiraService.LinkRelatedIssuesAsync(baseKey, keys);
                                context.Response.ContentType = "application/json; charset=utf-8";
                                await context.Response.WriteAsync(JsonSerializer.Serialize(new { success = true }));
                            }
                            catch (Exception ex)
                            {
                                context.Response.StatusCode = 500;
                                await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = ex.Message }));
                            }
                        });

                        // ── Change parent ─────────────────────────────────────────────────────
                        endpoints.MapPost("/api/issue/change-parent", async context =>
                        {
                            try
                            {
                                var body = await JsonSerializer.DeserializeAsync<JsonElement>(context.Request.Body);
                                string childKey = body.GetProperty("childKey").GetString() ?? "";
                                string oldParentKey = body.TryGetProperty("oldParentKey", out var op) ? op.GetString() ?? "" : "";
                                string newParentKey = body.GetProperty("newParentKey").GetString() ?? "";
                                var dashIndex = childKey.IndexOf('-');
                                var keyPrefix = dashIndex > 0 ? childKey.Substring(0, dashIndex) : childKey;
                                var projectConfig = frmMain.config?.Projects?.FirstOrDefault(p => p.Root.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase));
                                string linkTypeName = projectConfig?.LinkTypeName ?? frmMain.hierarchyLinkTypeName.Split(',')[0];
                                await frmMain.jiraService.UpdateParentLinkAsync(childKey, oldParentKey, newParentKey, linkTypeName);
                                context.Response.ContentType = "application/json; charset=utf-8";
                                await context.Response.WriteAsync(JsonSerializer.Serialize(new { success = true }));
                            }
                            catch (Exception ex)
                            {
                                context.Response.StatusCode = 500;
                                await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = ex.Message }));
                            }
                        });

                        // ── Move node (up/down sequence) ──────────────────────────────────────
                        endpoints.MapPost("/api/issue/move", async context =>
                        {
                            try
                            {
                                var body = await JsonSerializer.DeserializeAsync<JsonElement>(context.Request.Body);
                                string key = body.GetProperty("key").GetString() ?? "";
                                int direction = body.GetProperty("direction").GetInt32(); // -1 = up, 1 = down
                                if (!frmMain.FlatJiraIssueDictionary.TryGetValue(key, out var issue))
                                {
                                    context.Response.StatusCode = 404;
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = "Key not found" }));
                                    return;
                                }
                                string? parentKey = issue.ParentKey;
                                var siblings = frmMain.FlatJiraIssueDictionary
                                    .Where(kvp => kvp.Value.ParentKey == parentKey)
                                    .Select(kvp => kvp.Key)
                                    .ToList();
                                int idx = siblings.IndexOf(key);
                                int newIdx = idx + direction;
                                if (idx < 0 || newIdx < 0 || newIdx >= siblings.Count)
                                {
                                    context.Response.StatusCode = 400;
                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = "Cannot move in that direction" }));
                                    return;
                                }
                                siblings.RemoveAt(idx);
                                siblings.Insert(newIdx, key);
                                for (int i = 0; i < siblings.Count; i++)
                                    await frmMain.jiraService.UpdateSequenceFieldAsync(siblings[i], i + 1);
                                context.Response.ContentType = "application/json; charset=utf-8";
                                await context.Response.WriteAsync(JsonSerializer.Serialize(new { success = true }));
                            }
                            catch (Exception ex)
                            {
                                context.Response.StatusCode = 500;
                                                         await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = ex.Message }));
                                                    }
                                                });

                                                // ── Folder structure preview ──────────────────────────────────────
                                                endpoints.MapGet("/api/folder/preview", async context =>
                                                {
                                                    string key = context.Request.Query["key"].FirstOrDefault() ?? "";
                                                    if (string.IsNullOrWhiteSpace(key))
                                                    {
                                                        context.Response.StatusCode = 400;
                                                        await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = "key required" }));
                                                        return;
                                                    }
                                                    var todo = BuildFolderTodo(key);
                                                    context.Response.ContentType = "application/json; charset=utf-8";
                                                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { baseRoot = @"C:\manual\Release", items = todo }));
                                                });

                                                // ── Folder structure create ───────────────────────────────────────
                                                endpoints.MapPost("/api/folder/create", async context =>
                                                {
                                                    try
                                                    {
                                                        var body = await JsonSerializer.DeserializeAsync<JsonElement>(context.Request.Body);
                                                        string key = body.GetProperty("key").GetString() ?? "";
                                                        if (string.IsNullOrWhiteSpace(key))
                                                        {
                                                            context.Response.StatusCode = 400;
                                                            await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = "key required" }));
                                                            return;
                                                        }
                                                        var todo = BuildFolderTodo(key);
                                                        string lastFolder = "";
                                                        foreach (var item in todo)
                                                        {
                                                            if (item.type == "Folder")
                                                            {
                                                                if (!Directory.Exists(item.path))
                                                                    Directory.CreateDirectory(item.path);
                                                                lastFolder = item.path;
                                                            }
                                                            else
                                                            {
                                                                if (!System.IO.File.Exists(item.path))
                                                                {
                                                                    var name = Path.GetFileNameWithoutExtension(item.path);
                                                                    var sb = new StringBuilder();
                                                                    sb.AppendLine($"Feature: {name}");
                                                                    sb.AppendLine();
                                                                    sb.AppendLine("  Scenario: TBD");
                                                                    sb.AppendLine("    Given TBD");
                                                                    await System.IO.File.WriteAllTextAsync(item.path, sb.ToString(), Encoding.UTF8);
                                                                }
                                                            }
                                                        }
                                                        if (!string.IsNullOrEmpty(lastFolder))
                                                        {
                                                            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(lastFolder) { UseShellExecute = true }); } catch { }
                                                        }
                                                        context.Response.ContentType = "application/json; charset=utf-8";
                                                        await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = true, created = todo.Count }));
                                                    }
                                                    catch (Exception ex)
                                                    {
                                                        context.Response.StatusCode = 500;
                                                        await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = ex.Message }));
                                                    }
                                                });
                                            });
                                        })
                                        .Build();

                                    await webHost.StartAsync();
                                }

                                // ── Folder-structure path helpers (mirrors frmMain.CreateLocalFolderStructureAsync) ──
                                private static string FolderNormalizeTitle(string title)
                                {
                                    if (string.IsNullOrWhiteSpace(title)) return "";
                                    int idx = title.LastIndexOf('[');
                                    string head = idx > 0 ? title.Substring(0, idx).Trim() : title.Trim();
                                    head = System.Text.RegularExpressions.Regex.Replace(head, "[^A-Za-z0-9]+", " ");
                                    head = System.Text.RegularExpressions.Regex.Replace(head, @"\s+", " ").Trim();
                                    return head;
                                }

                                private static string FolderBuildFsName(string issueKey, string summary)
                                {
                                    // Replicate BuildFsNameFromNodeText: node text is "{summary} [{key}]"
                                    string nodeText = $"{summary} [{issueKey}]";
                                    string key = issueKey ?? "";
                                    string id = key.Replace("-", "");
                                    string title = FolderNormalizeTitle(nodeText);

                                    string ToPascalCase(string input)
                                    {
                                        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
                                        var words = System.Text.RegularExpressions.Regex.Matches(input, @"[A-Za-z0-9]+").Select(m => m.Value);
                                        var sb = new StringBuilder();
                                        foreach (var word in words)
                                        {
                                            var lower = word.ToLowerInvariant();
                                            sb.Append(char.ToUpperInvariant(lower[0]));
                                            if (lower.Length > 1) sb.Append(lower.Substring(1));
                                        }
                                        return sb.ToString();
                                    }

                                    string camel = ToPascalCase(title);
                                    if (string.IsNullOrWhiteSpace(id)) return camel;
                                    var num = System.Text.RegularExpressions.Regex.Match(id, @"\d+").Value;
                                    string prefix = string.IsNullOrWhiteSpace(num) ? id : $"TST{num}";
                                    return prefix + "_" + camel;
                                }

                                private static List<(string path, string type)> BuildFolderTodo(string rootKey)
                                {
                                    const string baseRoot = @"C:\manual\Release";
                                    var todo = new List<(string path, string type)>();

                                    // Build ancestor chain from root -> rootKey using ParentKey chain
                                    // to compute parentPath (skip first 2 tree levels, replicate native logic)
                                    var chain = new List<string>();
                                    string cur = rootKey;
                                    while (!string.IsNullOrEmpty(cur) && frmMain.FlatJiraIssueDictionary.TryGetValue(cur, out var ci))
                                    {
                                        chain.Insert(0, cur);
                                        cur = ci.ParentKey;
                                    }

                                    int skip = Math.Min(2, chain.Count);
                                    var ancestors = chain.Skip(skip).Take(Math.Max(0, chain.Count - skip - 1)).ToList();

                                    string parentPath = baseRoot;
                                    foreach (var ak in ancestors)
                                    {
                                        if (frmMain.FlatJiraIssueDictionary.TryGetValue(ak, out var ai))
                                        {
                                            string name = FolderBuildFsName(ak, ai.Summary ?? "");
                                            if (!string.IsNullOrWhiteSpace(name))
                                                parentPath = Path.Combine(parentPath, name);
                                        }
                                    }

                                    // Walk selected node and all descendants
                                    void Visit(string nodeKey, string currentPath)
                                    {
                                        if (!frmMain.FlatJiraIssueDictionary.TryGetValue(nodeKey, out var issue)) return;
                                        string nodeName = FolderBuildFsName(nodeKey, issue.Summary ?? "");
                                        string nodePath = Path.Combine(currentPath, nodeName);
                                        var children = frmMain.childrenByParent.TryGetValue(nodeKey, out var ch)
                                            ? ch.Select(c => c.Key).ToList()
                                            : new List<string>();
                                        if (children.Count > 0)
                                        {
                                            todo.Add((nodePath, "Folder"));
                                            foreach (var childKey in children)
                                                Visit(childKey, nodePath);
                                        }
                                        else
                                        {
                                            todo.Add((nodePath + ".feature", "Feature"));
                                        }
                                    }

                                    if (frmMain.FlatJiraIssueDictionary.TryGetValue(rootKey, out var rootIssue))
                                    {
                                        string selectedFsName = FolderBuildFsName(rootKey, rootIssue.Summary ?? "");
                                        string creationRoot = Path.Combine(parentPath, selectedFsName);
                                        todo.Insert(0, (creationRoot, "Folder"));
                                        var children = frmMain.childrenByParent.TryGetValue(rootKey, out var ch)
                                            ? ch.Select(c => c.Key).ToList()
                                            : new List<string>();
                                        foreach (var childKey in children)
                                            Visit(childKey, creationRoot);
                                    }

                                    return todo;
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
        private static async Task<string> BuildRecentUpdatesHtmlFinalAsync(int days)
        {
            var projects = frmMain.projectList?.Where(p => !string.IsNullOrWhiteSpace(p)).ToList() ?? new List<string>();
            if (projects.Count == 0 ||
                string.IsNullOrWhiteSpace(frmMain.jiraBaseUrl) ||
                string.IsNullOrWhiteSpace(frmMain.jiraEmail) ||
                string.IsNullOrWhiteSpace(frmMain.jiraToken))
            {
                return @"<!DOCTYPE html><html><head><meta charset='UTF-8'></head>
<body><div style='padding:12px;color:#888;font:14px Segoe UI'>
No recent updates: Jira configuration is missing (projects/base URL/credentials).
</div></body></html>";
            }

            // Search Jira for recently created/updated issues across configured projects
            var rows = new List<(string Key, string Summary, string Type, string Status, DateTime Updated, DateTime? Created, List<string> Tags)>();
            try
            {
                // New JQL Search API (GET /rest/api/3/search/jql with nextPageToken pagination)
                string jql = $"({string.Join(" OR ", frmMain.projectList.Select(p => $"project = \"{p}\""))}) AND (created >= -{days}d OR updated >= -{days}d) ORDER BY updated DESC";
                string baseUrl = frmMain.jiraBaseUrl?.TrimEnd('/') ?? "";
                string authToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{frmMain.jiraEmail}:{frmMain.jiraToken}"));

                var searchIssues = new List<(string Key, string Summary, string Type, string Status, DateTime Updated, DateTime? Created)>();

                using var client = new HttpClient();
                client.BaseAddress = new Uri(baseUrl);
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authToken);
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                client.Timeout = TimeSpan.FromSeconds(100);

                const int pageSize = 100; // typical upper bound for this API
                string nextPageToken = null;

                do
                {
                    var fieldsCsv = "summary,issuetype,status,updated,created";
                    var qs = new List<string>
            {
                "jql=" + Uri.EscapeDataString(jql),
                "maxResults=" + pageSize,
                "fields=" + Uri.EscapeDataString(fieldsCsv)
            };
                    if (!string.IsNullOrWhiteSpace(nextPageToken))
                        qs.Add("nextPageToken=" + Uri.EscapeDataString(nextPageToken));

                    var res = await client.GetAsync("/rest/api/3/search/jql?" + string.Join("&", qs));
                    if (!res.IsSuccessStatusCode)
                    {
                        var errBody = await res.Content.ReadAsStringAsync();
                        throw new HttpRequestException($"Jira search failed: {(int)res.StatusCode} {res.ReasonPhrase}. {errBody}");
                    }

                    var json = await res.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    // The response may be: { issues, nextPageToken } or { results: [ { issues, nextPageToken } ] }
                    JsonElement page = root;
                    if (root.TryGetProperty("results", out var resultsArr) && resultsArr.ValueKind == JsonValueKind.Array && resultsArr.GetArrayLength() > 0)
                        page = resultsArr[0];

                    if (!page.TryGetProperty("issues", out var issuesEl) || issuesEl.ValueKind != JsonValueKind.Array)
                        break;

                    int count = 0;
                    foreach (var issue in issuesEl.EnumerateArray())
                    {
                        count++;

                        string key = issue.TryGetProperty("key", out var k) ? k.GetString() ?? "" : "";
                        if (string.IsNullOrWhiteSpace(key)) continue;

                        string summary = "";
                        string type = "";
                        string status = "";
                        DateTime updated = DateTime.MinValue;
                        DateTime? created = null;

                        if (issue.TryGetProperty("fields", out var fields) && fields.ValueKind == JsonValueKind.Object)
                        {
                            if (fields.TryGetProperty("summary", out var s) && s.ValueKind == JsonValueKind.String)
                                summary = s.GetString() ?? "";

                            if (fields.TryGetProperty("issuetype", out var it) &&
                                it.TryGetProperty("name", out var itn) && itn.ValueKind == JsonValueKind.String)
                                type = itn.GetString() ?? "";

                            if (fields.TryGetProperty("status", out var st) &&
                                st.TryGetProperty("name", out var stn) && stn.ValueKind == JsonValueKind.String)
                                status = stn.GetString() ?? "";

                            if (fields.TryGetProperty("updated", out var up) &&
                                up.ValueKind == JsonValueKind.String && DateTime.TryParse(up.GetString(), out var dtUp))
                                updated = dtUp;

                            if (fields.TryGetProperty("created", out var cr) &&
                                cr.ValueKind == JsonValueKind.String && DateTime.TryParse(cr.GetString(), out var dtCr))
                                created = dtCr;
                        }

                        if (updated != DateTime.MinValue)
                            searchIssues.Add((key, summary, type, status, updated, created));
                    }

                    nextPageToken = page.TryGetProperty("nextPageToken", out var tokenEl) && tokenEl.ValueKind == JsonValueKind.String
                        ? tokenEl.GetString()
                        : null;

                    if (count == 0) break;
                } while (!string.IsNullOrWhiteSpace(nextPageToken));

                // For each issue: fetch changelog to derive "Changes" tags for the day of its Updated date
                foreach (var it in searchIssues)
                {
                    var tags = new List<string>();
                    try
                    {
                        var issueUrl = $"/rest/api/3/issue/{WebUtility.UrlEncode(it.Key)}?expand=changelog&fields=created";
                        var resp = await client.GetAsync(issueUrl);
                        if (resp.IsSuccessStatusCode)
                        {
                            var json = await resp.Content.ReadAsStringAsync();
                            using var doc = JsonDocument.Parse(json);
                            var root = doc.RootElement;

                            var updatedLocalDate = it.Updated.ToLocalTime().Date;
                            DateTime? createdUtc = it.Created;

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

            // Filter panel
            sb.Append($@"
<button id='show-filter-btn'>Apply Filter</button>

<div id='floating-filter-container' style='display:none; position:fixed; left:10px; right:10px; top:10px; z-index:9999; padding:8px; background:#ffffffcc; backdrop-filter:saturate(1.2) blur(2px); border:1px solid #b3d4f6; border-radius:8px;'>
  <div style='display:flex; gap:12px; flex-wrap:wrap; align-items:flex-start;'>

    <div class='filter-panel' style='display:inline-block; padding:8px; border:1px solid #b3d4f6; background:#f9fcff; border-radius:6px;'>
      <div class='filter-panel-title' style='font-weight:600;color:#1565c0;margin-bottom:6px;'>Issue Types</div>
      <div id='issue-type-checkboxes' class='checkbox-container' style='display:flex;gap:10px;flex-wrap:wrap;max-height:140px;overflow:auto;'>
        <label><input type='checkbox' class='change-type-checkbox-all' checked /> <span style='margin-left:6px;'>All</span></label>
        {string.Join("\n", allIssueTypesGlobal.Select(t =>
                            $"<label style='display:inline-flex;align-items:center;'><input type='checkbox' class='change-type-checkbox' value='{WebUtility.HtmlEncode(t)}' checked /> <span style='margin-left:6px;'>{WebUtility.HtmlEncode(t)}</span></label>"))}
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

            // Group by date
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


            // Count attachments for header
            int attachmentCount = 0;
            try
            {
                if (!string.IsNullOrWhiteSpace(attachmentsHtml))
                {
                    if (attachmentsHtml.IndexOf("class='no-attachments'", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        attachmentsHtml.IndexOf("class=\"no-attachments\"", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        attachmentCount = 0;
                    }
                    else
                    {
                        var attDoc = new HtmlAgilityPack.HtmlDocument();
                        attDoc.LoadHtml(attachmentsHtml);
                        var nodes = attDoc.DocumentNode.SelectNodes("//div[contains(@class,'attachment-card')]");
                        attachmentCount = nodes?.Count ?? 0;
                    }
                }
            }
            catch { attachmentCount = 0; }

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
    <button class='tab-btn' data-tab='attachmentsTab'>📎 Attachments [#{attachmentCount}]</button>
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
                    var keyPrefix = key.Split('-')[0];
                    var projectConfig = frmMain.config?.Projects?.FirstOrDefault(p => !string.IsNullOrEmpty(p.Root) &&
                                                                                     p.Root.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase));
                    if (projectConfig == null || string.IsNullOrWhiteSpace(issueType))
                        return "<span style='font-size:22px; vertical-align:middle; margin-right:8px;'>🟥</span>";

                    string fileName = null;
                    if (!projectConfig.Types.TryGetValue(issueType, out fileName))
                    {
                        var match = projectConfig.Types.FirstOrDefault(kvp => kvp.Key.Equals(issueType, StringComparison.OrdinalIgnoreCase));
                        fileName = match.Value;
                    }

                    if (string.IsNullOrWhiteSpace(fileName))
                        return "<span style='font-size:22px; vertical-align:middle; margin-right:8px;'>🟥</span>";

                    var fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", fileName);
                    if (!System.IO.File.Exists(fullPath))
                        return "<span style='font-size:22px; vertical-align:middle; margin-right:8px;'>🟥</span>";

                    try
                    {
                        var bytes = System.IO.File.ReadAllBytes(fullPath);
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

        /// <summary>
        /// Builds the HTML page for the AI Chat interface
        /// </summary>
        private static string BuildAIChatHtml()
        {
            var sb = new StringBuilder();
            sb.Append($@"
<!DOCTYPE html>
<html>
<head>
  <meta charset='UTF-8'>
  <title>Ask Me - Monovera AI Chat</title>
  <link href='https://fonts.googleapis.com/css2?family=IBM+Plex+Sans:wght@400;500;600&display=swap' rel='stylesheet' />
  <style>
    * {{
      box-sizing: border-box;
      margin: 0;
      padding: 0;
    }}
    body {{
      font-family: 'IBM Plex Sans', 'Segoe UI', sans-serif;
      background: #f5f5f5;
      height: 100vh;
      display: flex;
      flex-direction: column;
      overflow: hidden;
    }}
    #chat-container {{
      flex: 1;
      overflow-y: auto;
      padding: 20px;
      background: #fff;
    }}
    .message {{
      margin-bottom: 16px;
      padding: 12px 16px;
      border-radius: 8px;
      max-width: 85%;
      word-wrap: break-word;
      white-space: pre-wrap;
    }}
    .message-user {{
      background: #e3f2fd;
      color: #0d47a1;
      margin-left: auto;
      text-align: right;
    }}
    .message-bot {{
      background: #f5f5f5;
      color: #333;
      border-left: 4px solid #1565c0;
    }}
    .message-error {{
      background: #ffebee;
      color: #c62828;
      border-left: 4px solid #d32f2f;
    }}
    .message-label {{
      font-weight: 600;
      margin-bottom: 4px;
      font-size: 0.85em;
      opacity: 0.7;
    }}
    #input-container {{
      display: flex;
      gap: 10px;
      padding: 16px;
      background: #fafafa;
      border-top: 1px solid #e0e0e0;
    }}
    #input-box {{
      flex: 1;
      padding: 12px;
      border: 1px solid #ccc;
      border-radius: 6px;
      font-family: 'IBM Plex Sans', 'Segoe UI', sans-serif;
      font-size: 14px;
      resize: none;
      min-height: 60px;
      max-height: 120px;
    }}
    #send-btn {{
      padding: 12px 24px;
      background: #1565c0;
      color: white;
      border: none;
      border-radius: 6px;
      font-weight: 600;
      cursor: pointer;
      transition: background 0.2s;
      font-size: 14px;
    }}
    #send-btn:hover:not(:disabled) {{
      background: #0d47a1;
    }}
    #send-btn:disabled {{
      background: #ccc;
      cursor: not-allowed;
    }}
    .loading {{
      display: inline-block;
      width: 12px;
      height: 12px;
      border: 2px solid #1565c0;
      border-top-color: transparent;
      border-radius: 50%;
      animation: spin 0.6s linear infinite;
      margin-right: 8px;
    }}
    @keyframes spin {{
      to {{ transform: rotate(360deg); }}
    }}
    .welcome-message {{
      background: #e8f5e9;
      border-left: 4px solid #4caf50;
      padding: 16px;
      margin-bottom: 16px;
      border-radius: 6px;
    }}
    .welcome-message h3 {{
      margin-bottom: 8px;
      color: #2e7d32;
    }}
    .welcome-message ul {{
      margin-left: 20px;
      margin-top: 8px;
    }}
    .welcome-message li {{
      margin: 4px 0;
      color: #555;
    }}
  </style>
</head>
<body>
  <div id='chat-container'></div>
  <div id='input-container'>
    <textarea id='input-box' placeholder='Ask me about your test cases, requirements, or projects...'></textarea>
    <button id='send-btn'>Send</button>
  </div>

  <script>
    const chatContainer = document.getElementById('chat-container');
    const inputBox = document.getElementById('input-box');
    const sendBtn = document.getElementById('send-btn');

    // Check bot status and show welcome
    async function init() {{
      try {{
        const response = await fetch('/api/ai/status');
        const data = await response.json();

        if (data.trained) {{
          addBotMessage(`Hello! I'm Monovera Bot, your AI assistant. I can help you with:

• Finding test cases and requirements
• Explaining relationships between items
• Checking status and progress
• Understanding project structure (TST, REQ, STF)

Try asking me questions like:
- ""What test cases are related to login?""
- ""Show me all requirements for authentication""
- ""What is the status of payment testing?""
- ""How many test cases are in progress?""

What would you like to know?`, 'welcome');
        }} else {{
          addBotMessage('Hello! I\'m Monovera Bot. I need to be trained first to learn about your projects.\n\nPlease train me from the desktop app:\nAI Assistant > Train Local Model\n\nThis will read your database and build my knowledge base so I can answer your questions.', 'error');
          inputBox.disabled = true;
          sendBtn.disabled = true;
        }}
      }} catch (err) {{
        addBotMessage('Error: Could not connect to Monovera Bot. ' + err.message, 'error');
        inputBox.disabled = true;
        sendBtn.disabled = true;
      }}
    }}

    function addMessage(sender, text, className = '') {{
      const messageDiv = document.createElement('div');
      messageDiv.className = 'message ' + (className || (sender === 'You' ? 'message-user' : 'message-bot'));

      const label = document.createElement('div');
      label.className = 'message-label';
      label.textContent = sender;

      const content = document.createElement('div');
      content.textContent = text;

      messageDiv.appendChild(label);
      messageDiv.appendChild(content);
      chatContainer.appendChild(messageDiv);
      chatContainer.scrollTop = chatContainer.scrollHeight;

      return messageDiv;
    }}

    function addUserMessage(text) {{
      addMessage('You', text);
    }}

    function addBotMessage(text, type = 'bot') {{
      if (type === 'welcome') {{
        const div = document.createElement('div');
        div.className = 'welcome-message';
        div.innerHTML = '<h3>🤖 Monovera Bot</h3><div>' + text.replace(/\n/g, '<br>') + '</div>';
        chatContainer.appendChild(div);
        chatContainer.scrollTop = chatContainer.scrollHeight;
        return div;
      }}
      return addMessage('Monovera Bot', text, type === 'error' ? 'message-error' : 'message-bot');
    }}

    function addLoadingMessage() {{
      const div = addMessage('Monovera Bot', '', 'message-bot');
      div.querySelector('div:last-child').innerHTML = '<span class=""loading""></span>Thinking...';
      return div;
    }}

    async function sendMessage() {{
      const question = inputBox.value.trim();
      if (!question) return;

      addUserMessage(question);
      inputBox.value = '';
      inputBox.disabled = true;
      sendBtn.disabled = true;

      const loadingDiv = addLoadingMessage();

      try {{
        const response = await fetch('/api/ai/ask', {{
          method: 'POST',
          headers: {{ 'Content-Type': 'application/json' }},
          body: JSON.stringify({{ question }})
        }});

        const data = await response.json();

        chatContainer.removeChild(loadingDiv);

        if (data.error) {{
          addBotMessage(data.error, 'error');
        }} else {{
          addBotMessage(data.answer);
        }}
      }} catch (err) {{
        chatContainer.removeChild(loadingDiv);
        addBotMessage('Error: ' + err.message, 'error');
      }} finally {{
        inputBox.disabled = false;
        sendBtn.disabled = false;
        inputBox.focus();
      }}
    }}

    sendBtn.addEventListener('click', sendMessage);
    inputBox.addEventListener('keydown', (e) => {{
      if (e.key === 'Enter' && !e.shiftKey) {{
        e.preventDefault();
        sendMessage();
      }}
    }});

    init();
  </script>
</body>
</html>");

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

        // Build Search Results HTML for the SPA (triggered by /api/search/html)
        // Build Search Results HTML for the SPA (triggered by /api/search/html)
        private static async Task<string> BuildSearchResultsHtml(string jql)
        {
            var list = new List<(string Key, string Summary, string Type, DateTime? Updated)>();
            try
            {
                var authToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{frmMain.jiraEmail}:{frmMain.jiraToken}"));
                using var client = new HttpClient();
                client.BaseAddress = new Uri(frmMain.jiraBaseUrl?.TrimEnd('/') ?? "");
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authToken);
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                client.Timeout = TimeSpan.FromSeconds(100);

                const int pageSize = 100;
                string nextPageToken = null;

                do
                {
                    var fieldsCsv = "summary,issuetype,updated";
                    var qs = new List<string>
                    {
                        "jql=" + Uri.EscapeDataString(jql),
                        "maxResults=" + pageSize,
                        "fields=" + Uri.EscapeDataString(fieldsCsv)
                    };
                    if (!string.IsNullOrWhiteSpace(nextPageToken))
                        qs.Add("nextPageToken=" + Uri.EscapeDataString(nextPageToken));

                    // Only use the new JQL search endpoint; no fallback to classic (avoids 410 Gone)
                    HttpResponseMessage res = await client.GetAsync("/rest/api/3/search/jql?" + string.Join("&", qs));
                    if (!res.IsSuccessStatusCode)
                    {
                        // Retry once with POST to the same JQL endpoint (some tenants/proxies prefer POST)
                        var body = new { jql, maxResults = pageSize, fields = new[] { "summary", "issuetype", "updated" } };
                        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
                        var postUrl = "/rest/api/3/search/jql" + (string.IsNullOrWhiteSpace(nextPageToken) ? "" : "?nextPageToken=" + Uri.EscapeDataString(nextPageToken));
                        res = await client.PostAsync(postUrl, content);
                        if (!res.IsSuccessStatusCode)
                        {
                            var err = await res.Content.ReadAsStringAsync();
                            throw new HttpRequestException($"Jira search/jql failed: {(int)res.StatusCode} {res.ReasonPhrase}. {err}");
                        }
                    }

                    string json = await res.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    // Two possible shapes: top-level { issues, nextPageToken } or { results: [ { issues, nextPageToken } ] }
                    JsonElement page;
                    if (root.TryGetProperty("results", out var resultsArr) && resultsArr.ValueKind == JsonValueKind.Array && resultsArr.GetArrayLength() > 0)
                        page = resultsArr[0];
                    else
                        page = root;

                    if (!page.TryGetProperty("issues", out var issues) || issues.ValueKind != JsonValueKind.Array)
                        break;

                    int count = 0;
                    foreach (var issue in issues.EnumerateArray())
                    {
                        count++;

                        string key = issue.TryGetProperty("key", out var k) ? k.GetString() ?? "" : "";
                        if (string.IsNullOrWhiteSpace(key)) continue;

                        string summary = "";
                        string type = "";
                        DateTime? updated = null;

                        if (issue.TryGetProperty("fields", out var fields) && fields.ValueKind == JsonValueKind.Object)
                        {
                            if (fields.TryGetProperty("summary", out var s) && s.ValueKind == JsonValueKind.String)
                                summary = s.GetString() ?? "";

                            if (fields.TryGetProperty("issuetype", out var it) &&
                                it.TryGetProperty("name", out var itn) && itn.ValueKind == JsonValueKind.String)
                                type = itn.GetString() ?? "";

                            if (fields.TryGetProperty("updated", out var up) &&
                                up.ValueKind == JsonValueKind.String &&
                                DateTime.TryParse(up.GetString(), out var dt))
                                updated = dt;
                        }

                        list.Add((key, summary, type, updated));
                    }

                    nextPageToken = page.TryGetProperty("nextPageToken", out var tokenEl) && tokenEl.ValueKind == JsonValueKind.String
                        ? tokenEl.GetString()
                        : null;

                    if (count == 0) break;
                }
                while (!string.IsNullOrWhiteSpace(nextPageToken));
            }
            catch
            {
                // ignore; will show empty
            }

            var cssHref = "/static/monovera.css";
            var sb = new StringBuilder();
            sb.Append($@"<!DOCTYPE html>
<html>
<head>
  <meta charset='UTF-8'>
  <link rel='stylesheet' href='{cssHref}' />
</head>
<body>
  <details open>
    <summary>Search Results ({list.Count})</summary>
    <section>
      <table class='confluenceTable' style='width:100%; border-collapse:collapse; margin-top:6px;'>
        <tbody>
");
            foreach (var item in list)
            {
                string iconUrl = ResolveTypeIconUrl(item.Type);
                string iconHtml = !string.IsNullOrWhiteSpace(iconUrl)
                    ? $"<img src='{iconUrl}' width='24' height='24' style='vertical-align:middle;margin-right:8px;border-radius:4px;' title='{WebUtility.HtmlEncode(item.Type)}' />"
                    : "<span style='font-size:20px; vertical-align:middle; margin-right:8px;'>🟥</span>";

                string pathHtml = "";
                try
                {
                    var path = frmMain.GetRequirementPath(item.Key);
                    if (!string.IsNullOrWhiteSpace(path))
                        pathHtml = $"<div style='font-size:0.7em;color:#888;margin-left:48px;margin-top:1px;'>{WebUtility.HtmlEncode(path)}</div>";
                }
                catch { }

                sb.Append($@"
  <tr>
    <td class='confluenceTd'>
      <a href='#' data-key='{WebUtility.HtmlEncode(item.Key)}'>{iconHtml}{WebUtility.HtmlEncode(item.Summary)} [{WebUtility.HtmlEncode(item.Key)}]</a>
      {pathHtml}
    </td>
  </tr>");
            }

            if (list.Count == 0)
            {
                sb.Append("<tr><td class='confluenceTd' style='color:#888;'>No results.</td></tr>");
            }

            sb.Append(@"
        </tbody>
      </table>
    </section>
  </details>
  <script>
    // Bridge clicks back to SPA to open and select issue
    document.querySelectorAll('a[data-key]').forEach(link => {
      link.addEventListener('click', e => {
        e.preventDefault();
        const key = link.dataset.key;
        const title = link.textContent || '[' + key + ']';
        try {
          if (window.parent && window.parent !== window) {
            window.parent.postMessage({ type: 'open-issue', key, title }, '*');
          }
        } catch {}
      });
    });
  </script>
</body>
</html>");
            return sb.ToString();
        }

        // Write index.html (embedded monovera.css) and monovera.web.js to WebAppRoot
        private static async Task EnsureWebAssetsAsync(string WebAppRoot)
        {
            string css = "";
            try
            {
                if (!string.IsNullOrWhiteSpace(frmMain.cssPath) && System.IO.File.Exists(frmMain.cssPath))
                    css = await System.IO.File.ReadAllTextAsync(frmMain.cssPath, Encoding.UTF8);
                else if (!string.IsNullOrWhiteSpace(frmMain.cssHref))
                    using (var hc = new HttpClient()) css = await hc.GetStringAsync(frmMain.cssHref);
            }
            catch { css = ""; }

            string indexHtml = $@"<!DOCTYPE html>
<html>
<head>
  <meta charset='utf-8' />
  <title>Monovera</title>
  <link href='https://fonts.googleapis.com/css2?family=Inter:wght@300;400;500;600&display=swap' rel='stylesheet'>
  <style>
{css}
/* ── Professional White + Light-Blue palette ────────────────────── */
:root {{
  --c-bg:        #f0f6ff;
  --c-sidebar:   #e8f0fc;
  --c-border:    #c2d8f5;
  --c-accent:    #1a6bbf;
  --c-accent2:   #3d8fd6;
  --c-text:      #0d2340;
  --c-text-soft: #4a6a8a;
  --c-surface:   #ffffff;
  --c-hover:     #dceeff;
  --c-active:    #c5e0fa;
  --c-danger:    #c0392b;
  --c-success:   #1e8449;
  --c-warn:      #b7770d;
  --radius:      10px;
  --shadow:      0 2px 16px rgba(26,107,191,.10);
  --trans:       0.18s ease;
}}
*, *::before, *::after {{ box-sizing: border-box; }}
html, body {{ height:100%; margin:0; font-family:'Inter',sans-serif; background:var(--c-bg); color:var(--c-text); overflow:hidden; }}

/* ── Layout ─────────────────────────────────────────────────────── */
.layout {{ display:grid; grid-template-rows:1fr 30px; height:100vh; }}
.main {{
  --left:300px;
  display:grid;
  grid-template-columns:var(--left) 5px 1fr;
  gap:0;
  padding:10px;
  box-sizing:border-box;
  min-height:0;
  gap:8px;
}}

/* ── Splitter ────────────────────────────────────────────────────── */
.splitter {{
  grid-column:2;
  cursor:col-resize;
  border-radius:4px;
  background:var(--c-border);
  transition:background var(--trans);
  margin:4px 0;
}}
.splitter:hover {{ background:var(--c-accent2); }}

/* ── Sidebar ─────────────────────────────────────────────────────── */
.sidebar {{
  grid-column:1;
  background:var(--c-sidebar);
  border:1px solid var(--c-border);
  border-radius:var(--radius);
  display:flex;
  flex-direction:column;
  min-height:0;
  overflow:hidden;
  box-shadow:var(--shadow);
}}
.sidebar-toolbar {{
  display:flex;
  align-items:center;
  gap:4px;
  padding:6px 8px;
  border-bottom:1px solid var(--c-border);
  background:rgba(255,255,255,.7);
  flex-shrink:0;
}}
.sb-btn {{
  appearance:none;
  border:1px solid var(--c-border);
  background:var(--c-surface);
  color:var(--c-accent);
  border-radius:6px;
  padding:4px 8px;
  font-size:13px;
  cursor:pointer;
  display:inline-flex;
  align-items:center;
  gap:4px;
  transition:background var(--trans),border-color var(--trans),box-shadow var(--trans);
  white-space:nowrap;
}}
.sb-btn:hover {{ background:var(--c-hover); border-color:var(--c-accent2); box-shadow:0 2px 8px rgba(26,107,191,.14); }}
.sb-btn:active {{ background:var(--c-active); }}
.sb-btn-icon {{ font-size:14px; }}

/* ── Tree ────────────────────────────────────────────────────────── */
#tree, #tree ul, #tree li {{ list-style:none!important; list-style-type:none!important; list-style-image:none!important; margin:0; padding-left:14px; }}
#tree li::marker, #tree li::before {{ content:none!important; color:transparent!important; }}
#tree {{ padding:8px 6px; white-space:nowrap; flex:1 1 auto; overflow:auto; font-size:12px; }}
#tree li {{ margin:1px 0; }}
#tree a {{
  cursor:pointer; text-decoration:none; color:var(--c-accent);
  padding:3px 6px; border-radius:6px;
  display:inline-flex; align-items:center; gap:5px;
  transition:background var(--trans);
}}
#tree a:hover {{ background:var(--c-hover); }}
#tree a.selected {{
  background:var(--c-active);
  color:var(--c-text);
  outline:1.5px solid var(--c-accent2);
  font-weight:500;
}}
#tree .expander {{
  display:inline-block; width:16px; text-align:center;
  cursor:pointer; user-select:none;
  color:var(--c-accent2); font-weight:700; font-family:Consolas,monospace;
  transition:transform var(--trans);
}}
.node-icon {{ width:16px; height:16px; vertical-align:middle; border-radius:3px; }}

/* ── Workspace ───────────────────────────────────────────────────── */
.workspace {{
  grid-column:3;
  display:flex;
  flex-direction:column;
  min-width:0;
  min-height:0;
  overflow:hidden;
  background:var(--c-surface);
  border:1px solid var(--c-border);
  border-radius:var(--radius);
  box-shadow:var(--shadow);
}}

/* ── Tabs bar ────────────────────────────────────────────────────── */
.mv-tabs-bar {{
  display:grid;
  grid-template-columns:auto 1fr auto;
  align-items:center;
  gap:4px;
  border-bottom:1px solid var(--c-border);
  background:rgba(240,246,255,.9);
  padding:5px 6px 0;
  flex-shrink:0;
}}
.mv-tabs-viewport {{ overflow:hidden; position:relative; }}
#mv-tabs {{
  --tab-width:200px;
  position:relative;
  display:inline-flex;
  gap:3px;
  white-space:nowrap;
}}
.mv-tab {{
  background:rgba(255,255,255,.7);
  border:1px solid var(--c-border); border-bottom:none;
  border-radius:7px 7px 0 0;
  padding:5px 8px;
  cursor:pointer;
  display:flex; align-items:center; gap:6px;
  width:var(--tab-width); min-width:var(--tab-width); max-width:var(--tab-width);
  flex:0 0 var(--tab-width);
  overflow:hidden;
  transition:background var(--trans),border-color var(--trans);
}}
.mv-tab:hover {{ background:var(--c-hover); }}
.mv-tab .mv-tab-label {{ overflow:hidden; text-overflow:ellipsis; white-space:nowrap; flex:1 1 auto; font-size:12px; }}
.mv-tab.active {{
  background:var(--c-surface);
  border-bottom:2px solid var(--c-accent);
  font-weight:600; color:var(--c-accent);
}}
.mv-tab-close {{
  margin-left:4px; width:14px; height:14px;
  display:inline-flex; align-items:center; justify-content:center;
  font-weight:700; font-size:11px; line-height:1;
  color:var(--c-text-soft);
  border-radius:3px; cursor:pointer;
  transition:background var(--trans),color var(--trans);
  flex:0 0 auto;
}}
.mv-tab-close:hover {{ background:var(--c-danger); color:#fff; }}
.mv-tab-scroll {{
  appearance:none; -webkit-appearance:none;
  border:1px solid var(--c-border); background:var(--c-surface); color:var(--c-accent);
  width:26px; height:22px; border-radius:5px; display:none;
  align-items:center; justify-content:center; cursor:pointer;
  transition:background var(--trans);
}}
.mv-tab-scroll:hover {{ background:var(--c-hover); }}
.mv-tab-scroll[disabled] {{ opacity:.4; cursor:default; }}

/* ── Views ───────────────────────────────────────────────────────── */
#mv-views {{ flex:1 1 auto; position:relative; min-height:0; overflow:hidden; }}
.mv-view {{ position:absolute; inset:0; display:none; background:var(--c-surface); animation:fadeIn .15s ease; }}
.mv-view.active {{ display:block; }}
.mv-view iframe {{ width:100%; height:100%; border:none; background:var(--c-surface); }}
.home-splash {{ width:100%; height:100%; display:flex; align-items:center; justify-content:center; background:var(--c-surface); }}
.home-splash img {{ max-width:100%; max-height:100%; object-fit:contain; opacity:.85; }}
@keyframes fadeIn {{ from {{ opacity:0; transform:translateY(4px); }} to {{ opacity:1; transform:none; }} }}

/* ── Status bar ──────────────────────────────────────────────────── */
.status {{
  display:flex; align-items:center; padding:0 14px;
  border-top:1px solid var(--c-border); background:rgba(232,240,252,.95);
  color:var(--c-text-soft); font-size:12px; gap:16px;
}}
.sync-indicator {{ display:flex; align-items:center; gap:5px; }}
.sync-dot {{
  width:9px; height:9px; border-radius:50%;
  flex-shrink:0;
  box-shadow:0 0 0 2px rgba(0,0,0,.06);
  transition:background .4s;
}}
.sync-dot-ok      {{ background:#2ecc71; }}
.sync-dot-updates {{ background:#e74c3c; box-shadow:0 0 0 2px rgba(231,76,60,.22); }}
.sync-dot-offline {{ background:#b0bec5; }}
.sync-dot-checking {{ background:#f39c12; }}

/* ── Overlay backdrop ────────────────────────────────────────────── */
.mv-overlay {{
  position:fixed; inset:0;
  background:rgba(30,20,60,.25);
  backdrop-filter:blur(4px) saturate(1.2);
  z-index:9000;
  display:flex; align-items:center; justify-content:center;
  animation:ovFadeIn .15s ease;
}}
@keyframes ovFadeIn {{ from {{ opacity:0; }} to {{ opacity:1; }} }}
.mv-overlay-panel {{
  background:var(--c-surface);
  border:1px solid var(--c-border);
  border-radius:14px;
  box-shadow:0 12px 48px rgba(26,107,191,.18);
  overflow:hidden;
  animation:panelPop .18s cubic-bezier(.34,1.56,.64,1);
}}
@keyframes panelPop {{ from {{ opacity:0; transform:scale(.94) translateY(12px); }} to {{ opacity:1; transform:none; }} }}
.mv-overlay-header {{
  display:flex; align-items:center; justify-content:space-between; gap:8px;
  padding:12px 16px;
  background:linear-gradient(135deg,#dceeff,#f0f6ff);
  border-bottom:1px solid var(--c-border);
}}
.mv-overlay-title {{ font-weight:600; font-size:14px; color:var(--c-text); }}
.mv-overlay-close {{
  appearance:none; border:1px solid var(--c-border); background:var(--c-surface);
  color:var(--c-text-soft); border-radius:6px;
  width:28px; height:28px; display:flex; align-items:center; justify-content:center;
  cursor:pointer; font-size:16px; line-height:1;
  transition:background var(--trans),color var(--trans);
}}
.mv-overlay-close:hover {{ background:var(--c-danger); color:#fff; border-color:var(--c-danger); }}
.mv-overlay-body {{ padding:16px; }}
.mv-overlay-footer {{
  display:flex; justify-content:flex-end; gap:8px;
  padding:10px 16px;
  background:rgba(240,246,255,.7);
  border-top:1px solid var(--c-border);
}}

/* ── Buttons ─────────────────────────────────────────────────────── */
.mv-btn {{
  appearance:none;
  border:1px solid var(--c-border); background:var(--c-surface); color:var(--c-accent);
  border-radius:7px; padding:7px 14px; font-size:13px; font-weight:500;
  cursor:pointer; transition:background var(--trans),border-color var(--trans),box-shadow var(--trans);
}}
.mv-btn:hover {{ background:var(--c-hover); border-color:var(--c-accent2); box-shadow:0 2px 8px rgba(124,92,191,.12); }}
.mv-btn-primary {{ background:var(--c-accent); color:#fff; border-color:var(--c-accent); }}
.mv-btn-primary:hover {{ background:var(--c-accent2); border-color:var(--c-accent2); box-shadow:0 4px 12px rgba(26,107,191,.22); }}
.mv-btn-danger {{ background:var(--c-danger); color:#fff; border-color:var(--c-danger); }}

/* ── Form fields ─────────────────────────────────────────────────── */
.mv-field {{ display:flex; flex-direction:column; gap:4px; margin-bottom:12px; }}
.mv-field label {{ font-size:12px; font-weight:500; color:var(--c-text-soft); }}
.mv-field input, .mv-field select, .mv-field textarea {{
  padding:7px 10px;
  border:1px solid var(--c-border);
  border-radius:7px;
  font-size:13px;
  color:var(--c-text);
  background:var(--c-bg);
  transition:border-color var(--trans), box-shadow var(--trans);
  outline:none;
}}
.mv-field input:focus, .mv-field select:focus, .mv-field textarea:focus {{
  border-color:var(--c-accent2);
  box-shadow:0 0 0 3px rgba(26,107,191,.14);
}}
.mv-field .mv-hint {{ font-size:11px; color:var(--c-text-soft); }}

/* ── Context menu ────────────────────────────────────────────────── */
.ctx-menu {{
  position:fixed; display:none; z-index:10000; min-width:210px;
  background:var(--c-surface);
  border:1px solid var(--c-border); border-radius:var(--radius);
  box-shadow:0 8px 28px rgba(26,107,191,.16);
  font-size:12px; padding:4px;
  animation:panelPop .14s cubic-bezier(.34,1.56,.64,1);
}}
.ctx-menu ul {{ margin:0; padding:0; list-style:none!important; }}
.ctx-menu li::marker, .ctx-menu li::before {{ content:none!important; }}
.ctx-menu li {{
  padding:7px 12px; cursor:pointer; border-radius:6px;
  color:var(--c-accent); display:flex; align-items:center; gap:8px;
  transition:background var(--trans);
}}
.ctx-menu li:hover {{ background:var(--c-hover); }}
.ctx-menu .ctx-sep {{ height:1px; background:var(--c-border); margin:3px 4px; padding:0; cursor:default; }}

/* ── Search overlay ──────────────────────────────────────────────── */
#mv-search {{
  position:fixed; inset:0; z-index:9100;
  background:rgba(30,20,60,.25); backdrop-filter:blur(4px);
  display:none;
}}
.mv-search-panel {{
  position:absolute; top:48px; left:50%; transform:translateX(-50%);
  width:min(1040px,calc(100% - 20px));
  background:var(--c-surface);
  border:1px solid var(--c-border);
  border-radius:14px;
  box-shadow:0 12px 48px rgba(26,107,191,.18);
  overflow:hidden;
  animation:panelPop .18s cubic-bezier(.34,1.56,.64,1);
}}
.mv-search-header {{
  display:flex; align-items:center; justify-content:space-between;
  padding:10px 14px;
  background:linear-gradient(135deg,#dceeff,#f0f6ff);
  border-bottom:1px solid var(--c-border);
}}
.mv-search-title {{ font-weight:600; color:var(--c-text); }}
.mv-search-close {{ appearance:none; border:1px solid var(--c-border); background:var(--c-surface); color:var(--c-text-soft); border-radius:6px; padding:4px 9px; cursor:pointer; transition:background var(--trans); }}
.mv-search-close:hover {{ background:var(--c-danger); color:#fff; }}
.mv-search-body {{ padding:10px 14px; display:flex; flex-direction:column; gap:8px; }}
.mv-search-row {{ display:flex; gap:10px; align-items:center; flex-wrap:wrap; }}
.mv-search-row label {{ font-size:12px; font-weight:500; color:var(--c-text-soft); min-width:48px; }}
.mv-search-row select, .mv-search-row input[type='text'] {{ padding:6px 9px; border:1px solid var(--c-border); border-radius:7px; font-size:13px; min-width:140px; flex:1 1 auto; transition:border-color var(--trans); outline:none; }}
.mv-search-row select:focus, .mv-search-row input:focus {{ border-color:var(--c-accent2); box-shadow:0 0 0 3px rgba(124,92,191,.10); }}
.mv-search-hint {{ font-size:11px; color:var(--c-text-soft); }}
.mv-search-actions {{ display:flex; gap:8px; justify-content:flex-end; }}
.mv-search-btn {{ appearance:none; border:1px solid var(--c-border); background:var(--c-accent); color:#fff; border-radius:7px; padding:6px 16px; font-size:13px; cursor:pointer; transition:background var(--trans); }}
.mv-search-btn:hover {{ background:var(--c-accent2); }}
.mv-search-results {{ height:420px; border-top:1px solid var(--c-border); }}
.mv-search-results iframe {{ width:100%; height:100%; border:none; background:var(--c-surface); }}

/* ── Toast notifications ─────────────────────────────────────────── */
#mv-toast-area {{ position:fixed; bottom:40px; left:50%; transform:translateX(-50%); z-index:11000; display:flex; flex-direction:column; gap:8px; pointer-events:none; }}
.mv-toast {{
  background:var(--c-text); color:#fff;
  padding:9px 18px; border-radius:8px;
  font-size:13px; box-shadow:0 4px 16px rgba(0,0,0,.18);
  animation:toastIn .2s ease; pointer-events:none; white-space:nowrap;
}}
.mv-toast.success {{ background:var(--c-success); }}
.mv-toast.warn {{ background:var(--c-warn); }}
.mv-toast.error {{ background:var(--c-danger); }}
@keyframes toastIn {{ from {{ opacity:0; transform:translateY(10px); }} to {{ opacity:1; transform:none; }} }}

/* ── Config overlay ──────────────────────────────────────────────── */
.mv-cfg-panel {{ width:min(680px,calc(100% - 24px)); }}
.mv-cfg-tabs-bar {{ display:flex; gap:3px; border-bottom:1px solid var(--c-border); padding:0 12px; background:rgba(240,246,255,.6); }}
.mv-cfg-tab {{ appearance:none; border:none; background:none; padding:8px 14px; font-size:13px; font-weight:500; color:var(--c-text-soft); cursor:pointer; border-bottom:2px solid transparent; transition:color var(--trans), border-color var(--trans); }}
.mv-cfg-tab.active {{ color:var(--c-accent); border-bottom-color:var(--c-accent); font-weight:600; }}
.mv-cfg-pane {{ display:none; padding:14px; }}
.mv-cfg-pane.active {{ display:block; }}
.mv-proj-list {{ list-style:none; padding:0; margin:0 0 10px; max-height:200px; overflow:auto; }}
.mv-proj-item {{ display:flex; align-items:center; justify-content:space-between; padding:7px 10px; border:1px solid var(--c-border); border-radius:7px; margin-bottom:5px; background:var(--c-bg); font-size:13px; }}
.mv-proj-item span {{ font-weight:500; color:var(--c-text); }}
.mv-proj-actions {{ display:flex; gap:4px; }}
.mv-proj-btn {{ appearance:none; border:1px solid var(--c-border); background:var(--c-surface); color:var(--c-text-soft); border-radius:5px; padding:3px 8px; font-size:11px; cursor:pointer; transition:background var(--trans); }}
.mv-proj-btn:hover {{ background:var(--c-hover); }}
.mv-proj-btn.danger:hover {{ background:var(--c-danger); color:#fff; border-color:var(--c-danger); }}

/* ── Hierarchy update overlay ────────────────────────────────────── */
.mv-hier-panel {{ width:min(460px,calc(100% - 24px)); }}

/* ── AI train overlay ────────────────────────────────────────────── */
.mv-train-panel {{ width:min(420px,calc(100% - 24px)); }}
.mv-progress {{ width:100%; height:8px; border-radius:4px; background:var(--c-border); margin-top:10px; overflow:hidden; }}
.mv-progress-bar {{ height:100%; width:0; background:var(--c-accent); border-radius:4px; transition:width .3s ease; animation:progressPulse 1.5s ease-in-out infinite; }}
@keyframes progressPulse {{ 0%,100% {{ opacity:1; }} 50% {{ opacity:.65; }} }}

/* ── Confirmation dialog ─────────────────────────────────────────── */
.mv-confirm-panel {{ width:min(480px,calc(100% - 24px)); }}
  </style>
</head>
<body>
<div id='mv-toast-area'></div>

<div class='layout'>
  <div class='main'>
    <!-- Sidebar -->
    <aside class='sidebar'>
      <div class='sidebar-toolbar'>
        <button class='sb-btn' id='btn-search' title='Search (Ctrl+Q)'><span class='sb-btn-icon'>🔎</span></button>
        <button class='sb-btn' id='btn-recent' title='Recent Updates'><span class='sb-btn-icon'>🕒</span></button>
        <button class='sb-btn' id='btn-ai' title='Ask Me AI (Ctrl+M)'><span class='sb-btn-icon'>🤖</span></button>
        <button class='sb-btn' id='btn-config' title='Configuration'><span class='sb-btn-icon'>⚙️</span></button>
        <button class='sb-btn' id='btn-update' title='Update Hierarchy'><span class='sb-btn-icon'>🔄</span></button>
        <button class='sb-btn mv-btn-primary' id='btn-report' title='Generate Report (Ctrl+P)' style='margin-left:auto;'><span class='sb-btn-icon'>📄</span></button>
      </div>
      <ul id='tree'></ul>
      <div id='treeMenu' class='ctx-menu' role='menu' aria-hidden='true'>
        <ul>
          <li data-action='search'>🔎 Search… <span class='mv-search-hint'>(Ctrl+Q)</span></li>
          <li data-action='report'>📄 Generate Report… <span class='mv-search-hint'>(Ctrl+P)</span></li>
          <li data-action='ask-ai'>🤖 Ask Me… <span class='mv-search-hint'>(Ctrl+M)</span></li>
          <li data-action='recent'>🕒 Recent Updates…</li>
          <li data-action='folder-structure'>📁 Create Folder Structure…</li>
          <li class='ctx-sep'></li>
          <li data-action='edit' id='ctx-edit'>✏️ Edit…</li>
          <li data-action='link-related' id='ctx-link'>🔗 Link Related…</li>
          <li data-action='change-parent' id='ctx-chparent'>🌳 Change Parent…</li>
          <li class='ctx-sep'></li>
          <li data-action='add-child' id='ctx-add-child'>🌱 Add Child…</li>
          <li data-action='add-sibling' id='ctx-add-sibling'>🌳 Add Sibling…</li>
          <li class='ctx-sep'></li>
          <li data-action='move-up' id='ctx-move-up'>🔼 Move Up</li>
          <li data-action='move-down' id='ctx-move-down'>🔽 Move Down</li>
          <li class='ctx-sep'></li>
          <li data-action='config'>⚙️ Configuration…</li>
          <li data-action='update-hierarchy'>🔄 Update Hierarchy…</li>
          <li data-action='train-ai'>🧠 Train AI Index…</li>
        </ul>
      </div>
    </aside>

    <div id='splitter' class='splitter' role='separator' aria-orientation='vertical' tabindex='0'></div>

    <!-- Workspace -->
    <section class='workspace'>
      <div class='mv-tabs-bar'>
        <button id='mv-tabPrev' class='mv-tab-scroll' title='Scroll left'>&lsaquo;</button>
        <div class='mv-tabs-viewport'><div id='mv-tabs'></div></div>
        <button id='mv-tabNext' class='mv-tab-scroll' title='Scroll right'>&rsaquo;</button>
      </div>
      <div id='mv-views'>
        <div id='mv-home' class='mv-view active'>
          <div class='home-splash'>
            <img src='/static/images/MonoveraBackground.png' alt='Monovera' onerror=""this.style.display='none'"" />
          </div>
        </div>
      </div>
    </section>
  </div>

  <footer class='status'>
    <span id='statusUpdated'>🕒 Last Synced: —</span>
    <span id='statusSync' class='sync-indicator'>
      <span id='syncDot' class='sync-dot sync-dot-offline'></span>
      <span id='syncText'>Checking…</span>
    </span>
    <span id='statusUser' style='display:none'></span>
    <span style='margin-left:auto;font-size:11px;opacity:.6;'>Monovera</span>
  </footer>
</div>

<!-- Tab context menu -->
<div id='tabMenu' class='ctx-menu' role='menu' aria-hidden='true'>
  <ul>
    <li data-action='close'>Close Tab</li>
    <li data-action='close-others'>Close Other Tabs</li>
    <li data-action='close-left'>Close Tabs on Left</li>
    <li data-action='close-right'>Close Tabs on Right</li>
    <li class='ctx-sep'></li>
    <li data-action='close-all'>Close All Tabs</li>
  </ul>
</div>

<!-- Search overlay -->
<div id='mv-search' aria-hidden='true'>
  <div class='mv-search-panel' role='dialog' aria-modal='true'>
    <div class='mv-search-header'>
      <div class='mv-search-title'>🔎 Search</div>
      <button id='mv-search-close' class='mv-search-close' aria-label='Close'>✕</button>
    </div>
    <div class='mv-search-body'>
      <div class='mv-search-row'>
        <label style='display:flex;align-items:center;gap:6px;'><input type='checkbox' id='mv-search-jql' style='accent-color:var(--c-accent)'/> JQL mode</label>
        <span class='mv-search-hint'>Toggle to type raw Jira queries</span>
      </div>
      <div id='mv-search-filters' class='mv-search-row'>
        <label for='mv-search-project'>Project</label><select id='mv-search-project'></select>
        <label for='mv-search-type'>Type</label><select id='mv-search-type'></select>
        <label for='mv-search-status'>Status</label><select id='mv-search-status'></select>
      </div>
      <div class='mv-search-row'>
        <label for='mv-search-text'>Query</label>
        <input id='mv-search-text' type='text' placeholder='Issue key or search text…' />
      </div>
      <div class='mv-search-actions'>
        <button id='mv-search-run' class='mv-search-btn'>Search</button>
      </div>
    </div>
    <div class='mv-search-results'><iframe id='mv-search-results' title='Search results'></iframe></div>
  </div>
</div>

<script src='monovera.web.js'></script>
</body>
</html>";

            string webJs = @"(async function () {
  const treeEl = document.getElementById('tree');

  // Tabs
  const tabsEl = document.getElementById('mv-tabs');
  const tabsViewport = document.querySelector('.mv-tabs-viewport');
  const prevBtn = document.getElementById('mv-tabPrev');
  const nextBtn = document.getElementById('mv-tabNext');
  const viewsEl = document.getElementById('mv-views');
  const homeView = document.getElementById('mv-home');
  const tabMenu = document.getElementById('tabMenu');

  // Splitter
  const mainEl = document.querySelector('.main');
  const splitter = document.getElementById('splitter');

  // Context menu + Search dialog
  const treeMenu = document.getElementById('treeMenu');
  const searchOverlay = document.getElementById('mv-search');
  const searchClose = document.getElementById('mv-search-close');
  const searchText = document.getElementById('mv-search-text');
  const searchRun = document.getElementById('mv-search-run');
  const searchJql = document.getElementById('mv-search-jql');
  const searchFilters = document.getElementById('mv-search-filters');
  const ddlProject = document.getElementById('mv-search-project');
  const ddlType = document.getElementById('mv-search-type');
  const ddlStatus = document.getElementById('mv-search-status');
  const resultsFrame = document.getElementById('mv-search-results');

  // Sidebar toolbar buttons
  const btnSearch = document.getElementById('btn-search');
  const btnRecent = document.getElementById('btn-recent');
  const btnAI    = document.getElementById('btn-ai');
  const btnConfig = document.getElementById('btn-config');
  const btnUpdate = document.getElementById('btn-update');
  const btnReport = document.getElementById('btn-report');

  // ── Toast notifications ─────────────────────────────────────────
  function showToast(msg, type = '') {
    const area = document.getElementById('mv-toast-area');
    if (!area) return;
    const t = document.createElement('div');
    t.className = 'mv-toast' + (type ? ' ' + type : '');
    t.textContent = msg;
    area.appendChild(t);
    setTimeout(() => { t.style.transition = 'opacity .35s'; t.style.opacity = '0'; setTimeout(() => t.remove(), 380); }, 2800);
  }

  // ── Generic overlay builder ──────────────────────────────────────
  function buildOverlay({ id, titleText, bodyHtml, footerHtml, panelClass = '' }) {
    const ov = document.createElement('div');
    ov.className = 'mv-overlay'; ov.id = id;
    ov.setAttribute('aria-hidden', 'false');
    const panel = document.createElement('div');
    panel.className = 'mv-overlay-panel' + (panelClass ? ' ' + panelClass : '');
    panel.innerHTML = `
      <div class='mv-overlay-header'>
        <span class='mv-overlay-title'>${titleText}</span>
        <button class='mv-overlay-close' aria-label='Close'>✕</button>
      </div>
      <div class='mv-overlay-body'>${bodyHtml}</div>
      ${footerHtml ? `<div class='mv-overlay-footer'>${footerHtml}</div>` : ''}`;
    ov.appendChild(panel);
    document.body.appendChild(ov);
    panel.querySelector('.mv-overlay-close').addEventListener('click', () => ov.remove());
    ov.addEventListener('click', (e) => { if (!panel.contains(e.target)) ov.remove(); });
    window.addEventListener('keydown', function onKey(e) {
      if (e.key === 'Escape') { window.removeEventListener('keydown', onKey); ov.remove(); }
    });
    return { ov, panel };
  }

  // ── Resizer ──────────────────────────────────────────────────────
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

  // Status
  const syncDot  = document.getElementById('syncDot');
  const syncText = document.getElementById('syncText');
  function setSyncUI(code, pending) {
    syncDot.className = 'sync-dot';
    if (code === 'ok') {
      syncDot.classList.add('sync-dot-ok');
      syncText.textContent = '✅ Up to date';
      syncText.style.color = 'var(--c-ok, #2ecc71)';
    } else if (code === 'updates') {
      syncDot.classList.add('sync-dot-updates');
      syncText.textContent = pending > 0
        ? `⚠ ${pending} update${pending !== 1 ? 's' : ''} available`
        : '⚠ Updates available';
      syncText.style.color = 'var(--c-danger, #e74c3c)';
    } else if (code === 'offline') {
      syncDot.classList.add('sync-dot-offline');
      syncText.textContent = '⏸ Offline';
      syncText.style.color = 'var(--c-text-soft)';
    } else {
      syncDot.classList.add('sync-dot-checking');
      syncText.textContent = 'Checking…';
      syncText.style.color = 'var(--c-text-soft)';
    }
  }
  async function refreshStatus(){
    setSyncUI('checking', 0);
    try {
      const s = await (await fetch('/api/status')).json();
      document.getElementById('statusUpdated').textContent =
        '🕒 Last Synced: ' + (s.lastDbUpdated || 'N/A');
      setSyncUI(s.syncStatus || (s.offline ? 'offline' : 'ok'), s.pendingUpdates || 0);
    } catch {
      setSyncUI('offline', 0);
    }
  }
  refreshStatus(); setInterval(refreshStatus, 15000);

  // Tree selection
  let selectedAnchor = null;
  function setSelected(a){ if (selectedAnchor){ selectedAnchor.classList.remove('selected'); selectedAnchor.setAttribute('aria-selected','false'); } selectedAnchor = a; if (a){ a.classList.add('selected'); a.setAttribute('aria-selected','true'); a.scrollIntoView({block:'nearest', inline:'nearest'}); } }
  function highlightTreeSelection(key){ const a = document.querySelector(`#tree a[data-key='${key}']`); if (a) setSelected(a); }
  window.__ctxKey = null;

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

  // Expand to key
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

  // Tabs scroll (scroll the viewport, not the strip)
  function updateTabScrollButtons(){
    const canScroll = tabsEl.scrollWidth > tabsViewport.clientWidth + 1;
    prevBtn.style.display = canScroll ? 'inline-flex' : 'none';
    nextBtn.style.display = canScroll ? 'inline-flex' : 'none';
    prevBtn.disabled = !canScroll || tabsViewport.scrollLeft <= 0;
    nextBtn.disabled = !canScroll || (tabsViewport.scrollLeft + tabsViewport.clientWidth >= tabsEl.scrollWidth - 1);
  }
  function scrollTabsBy(delta){ tabsViewport.scrollBy({ left: delta, behavior: 'smooth' }); }
  prevBtn.addEventListener('click', () => scrollTabsBy(-Math.max(200, tabsViewport.clientWidth * 0.6)));
  nextBtn.addEventListener('click', () => scrollTabsBy(+Math.max(200, tabsViewport.clientWidth * 0.6)));
  tabsViewport.addEventListener('scroll', () => requestAnimationFrame(updateTabScrollButtons));
  window.addEventListener('resize', () => requestAnimationFrame(updateTabScrollButtons));

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
    const tab = document.getElementById(id);
    if (tab) ensureTabVisible(tab);
    updateTabScrollButtons();
  }
  function getIconForKey(key){
    const img = document.querySelector(`#tree a[data-key='${key}'] img.node-icon`);
    return img ? img.src : null;
  }
  function ensureTabVisible(tab){
    const vpLeft = tabsViewport.scrollLeft;
    const vpRight = vpLeft + tabsViewport.clientWidth;
    const tabLeft = tab.offsetLeft;
    const tabRight = tabLeft + tab.offsetWidth;
    if (tabLeft < vpLeft) tabsViewport.scrollTo({ left: Math.max(0, tabLeft - 8), behavior: 'smooth' });
    else if (tabRight > vpRight) tabsViewport.scrollTo({ left: tabRight - tabsViewport.clientWidth + 8, behavior: 'smooth' });
  }

  function closeTabByKey(key) {
    const tabId = makeTabId(key);
    const viewId = makeViewId(key);
    const t = document.getElementById(tabId), v = document.getElementById(viewId);
    let nextToActivateKey = null;
    if (t) {
      const tabs = Array.from(tabsEl.children);
      const idx = tabs.indexOf(t);
      if (t.classList.contains('active')) {
        const neighbor = tabs[idx - 1] || tabs[idx + 1];
        nextToActivateKey = neighbor ? (neighbor.dataset.key || neighbor.id.replace(/^tab-/,'')) : null;
      }
      tabsEl.removeChild(t);
    }
    if (v) viewsEl.removeChild(v);
    updateTabScrollButtons();
    if (nextToActivateKey) activate(nextToActivateKey);
    else showHomeIfNoTabs();
  }

  async function openTab(key, title, icon, { activateTab = true } = {}) {
    const tabId = makeTabId(key);
    const viewId = makeViewId(key);

    if (!document.getElementById(tabId)) {
      const tab = document.createElement('div');
      tab.className='mv-tab'; tab.id=tabId; tab.dataset.key=key; tab.title=title || key;

      const iconSrc = icon || getIconForKey(key);
      if (iconSrc){ const img=document.createElement('img'); img.src=iconSrc; img.className='node-icon'; img.alt=''; tab.appendChild(img); }

      const label = document.createElement('span'); label.className='mv-tab-label'; label.textContent=key; tab.appendChild(label);

      const close = document.createElement('span');
      close.className='mv-tab-close'; close.textContent='×'; close.title='Close'; close.setAttribute('aria-label','Close');
      close.addEventListener('click', (e) => {
        e.stopPropagation();
        closeTabByKey(key);
      });

      tab.addEventListener('click', () => { activate(key); });
      tab.appendChild(close);
      tabsEl.appendChild(tab);

      const view = document.createElement('div'); view.className='mv-view'; view.id=viewId;
      const iframe = document.createElement('iframe'); iframe.setAttribute('title', key); view.appendChild(iframe);
      viewsEl.appendChild(view);

      try {
        iframe.srcdoc = `<html><body style='display:flex;align-items:center;justify-content:center;height:100%;font:14px Inter,sans-serif;color:#7c5cbf;background:#f7f4fb;'>Loading…</body></html>`;
        const html = await (await fetch(`/api/issue/${encodeURIComponent(key)}/html`)).text();
        iframe.srcdoc = html;
      } catch {
        iframe.srcdoc = `<html><body style='padding:20px;color:#c94040;font:13px Inter,sans-serif;'>Failed to load ${key}</body></html>`;
      }

      // Make the newly added tab visible
      ensureTabVisible(tab);
    }
    if (activateTab) activate(key);
  }

  // Recent updates tab (clock icon, no brackets)
  async function openRecentUpdatesTab({ days = 14, activateTab = true } = {}) {
    const key = 'RECENT-UPDATES';
    const tabId = makeTabId(key);
    const viewId = makeViewId(key);
    if (!document.getElementById(tabId)) {
      const tab = document.createElement('div');
      tab.className='mv-tab'; tab.id=tabId; tab.dataset.key=key; tab.title='Recent Updates';
      // clock icon
      const clock = document.createElement('span'); clock.textContent='🕒'; tab.appendChild(clock);
      const keySpan = document.createElement('span'); keySpan.className='mv-tab-label'; keySpan.textContent='Recent Updates';
      tab.appendChild(keySpan);

      const close = document.createElement('span');
      close.className='mv-tab-close'; close.textContent='×'; close.title='Close'; close.setAttribute('aria-label','Close');
      close.addEventListener('click', (e) => { e.stopPropagation(); closeTabByKey(key); });

      tab.addEventListener('click', () => { activate(key); });
      tab.appendChild(close);
      tabsEl.appendChild(tab);

      const view = document.createElement('div'); view.className='mv-view'; view.id=viewId;
      const iframe = document.createElement('iframe'); iframe.setAttribute('title', 'Recent Updates'); view.appendChild(iframe);
      viewsEl.appendChild(view);

      try {
        iframe.srcdoc = `<html><body style='display:flex;align-items:center;justify-content:center;height:100%;font:14px Inter,sans-serif;color:#7c5cbf;background:#f7f4fb;'>Loading Recent Updates…</body></html>`;
        const html = await (await fetch(`/api/recent/updated/html?days=${encodeURIComponent(days)}`)).text();
        iframe.srcdoc = html;
      } catch {
        iframe.srcdoc = `<html><body style='padding:20px;color:#c94040;font:13px Inter,sans-serif;'>Failed to load Recent Updates</body></html>`;
      }

      ensureTabVisible(tab);
    }
    if (activateTab) activate(key);
  }

  // AI Chat Tab (robot icon, no brackets)
  async function openAIChatTab({ activateTab = true } = {}) {
    const key = 'AI-CHAT';
    const tabId = makeTabId(key);
    const viewId = makeViewId(key);
    if (!document.getElementById(tabId)) {
      const tab = document.createElement('div');
      tab.className='mv-tab'; tab.id=tabId; tab.dataset.key=key; tab.title='Ask Me - AI Chat';
      // robot icon
      const robot = document.createElement('span'); robot.textContent='🤖'; tab.appendChild(robot);
      const keySpan = document.createElement('span'); keySpan.className='mv-tab-label'; keySpan.textContent='Ask Me';
      tab.appendChild(keySpan);

      const close = document.createElement('span');
      close.className='mv-tab-close'; close.textContent='×'; close.title='Close'; close.setAttribute('aria-label','Close');
      close.addEventListener('click', (e) => { e.stopPropagation(); closeTabByKey(key); });

      tab.addEventListener('click', () => { activate(key); });
      tab.appendChild(close);
      tabsEl.appendChild(tab);

      const view = document.createElement('div'); view.className='mv-view'; view.id=viewId;
      const iframe = document.createElement('iframe'); iframe.setAttribute('title', 'AI Chat'); view.appendChild(iframe);
      viewsEl.appendChild(view);

      try {
        iframe.srcdoc = `<html><body style='display:flex;align-items:center;justify-content:center;height:100%;font:14px Inter,sans-serif;color:#7c5cbf;background:#f7f4fb;'>Loading AI Chat…</body></html>`;
        const html = await (await fetch('/api/ai/chat')).text();
        iframe.srcdoc = html;
      } catch {
        iframe.srcdoc = `<html><body style='padding:20px;color:#c94040;font:13px Inter,sans-serif;'>Failed to load AI Chat</body></html>`;
      }

      ensureTabVisible(tab);
    }
    if (activateTab) activate(key);
  }

  // Message bridge
  window.addEventListener('message', (ev) => {
    try {
      const d = ev.data || {};
      if (d.type === 'open-issue' && d.key) {
        (async () => {
          await expandAndSelect(d.key);
          await openTab(d.key, d.title || d.key, null);
        })();
      }
    } catch {}
  });

  // Tree context menu
  function hideTreeMenu(){ treeMenu.style.display='none'; treeMenu.setAttribute('aria-hidden','true'); }
  function showTreeMenu(x, y){
    // Show immediately — no blocking fetch
    treeMenu.style.display='block';
    treeMenu.style.left = Math.max(2, Math.min(x, window.innerWidth - treeMenu.offsetWidth - 2)) + 'px';
    treeMenu.style.top  = Math.max(2, Math.min(y, window.innerHeight - treeMenu.offsetHeight - 2)) + 'px';
    treeMenu.setAttribute('aria-hidden','false');
    // Make sure all items are visible first (reset any previous state)
    treeMenu.querySelectorAll('li[data-action]').forEach(li => { li.style.display=''; li.style.opacity='1'; li.style.pointerEvents=''; });
    // Non-blocking permission refinement
    getEditorInfo().then(info => {
      if (!treeMenu.style.display || treeMenu.style.display === 'none') return; // menu closed already
      const key = window.__ctxKey;
      const prefix = key ? key.split('-')[0] : '';
      const proj = (info.projects || []).find(p => p.projectKey === prefix);
      // Default to true when project not matched (avoids incorrectly locking out the user)
      const canCreate = proj ? (proj.canCreate ?? true) : true;
      const canEdit   = proj ? (proj.canEdit   ?? true) : true;
      const dim = (id, allowed) => {
        const el = document.getElementById(id);
        if (!el) return;
        el.style.opacity = allowed ? '1' : '0.45';
        el.style.pointerEvents = allowed ? '' : 'none';
      };
      dim('ctx-add-child',   canCreate);
      dim('ctx-add-sibling', canCreate);
      dim('ctx-edit',        canEdit);
      dim('ctx-link',        canEdit);
      dim('ctx-chparent',    canEdit);
      dim('ctx-move-up',     canEdit);
      dim('ctx-move-down',   canEdit);
    }).catch(() => {});
  }
  treeEl.addEventListener('contextmenu', (e) => {
    e.preventDefault();
    const a = e.target && e.target.closest ? e.target.closest('a[data-key]') : null;
    if (a) { setSelected(a); window.__ctxKey = a.dataset.key; }
    showTreeMenu(e.clientX, e.clientY);
  });  document.addEventListener('click', (e) => {
    if (!treeMenu.contains(e.target)) { hideTreeMenu(); window.__ctxKey = null; }
  });
  treeMenu.addEventListener('click', (e) => {
    const li = e.target.closest('li[data-action]');
    if (!li) return;
    const action = li.getAttribute('data-action');
    hideTreeMenu();
    if (action === 'search')           openSearchDialog();
    if (action === 'report')           generateReport();
    if (action === 'ask-ai')           openAIChatTab();
    if (action === 'recent')           openRecentUpdatesTab();
    if (action === 'config')           openConfigOverlay();
    if (action === 'update-hierarchy') openHierarchyUpdateOverlay();
    if (action === 'edit')             { const k = window.__ctxKey || selectedAnchor?.dataset?.key; if (k) openEditIssue(k); }
    if (action === 'add-child')        openAddIssueOverlay('Child');
    if (action === 'add-sibling')      openAddIssueOverlay('Sibling');
    if (action === 'link-related')     openLinkRelatedOverlay();
    if (action === 'change-parent')    openChangeParentOverlay();
    if (action === 'move-up')          moveNode(-1);
    if (action === 'move-down')        moveNode(1);
    if (action === 'folder-structure') openCreateFolderOverlay();
    if (action === 'train-ai')         openAITrainOverlay();
  });

  // Tab context menu
  let ctxTab = null;
  function hideTabMenu(){ tabMenu.style.display='none'; tabMenu.setAttribute('aria-hidden','true'); ctxTab = null; }
  function showTabMenu(x, y){
    tabMenu.style.display='block';
    tabMenu.style.left = Math.max(2, Math.min(x, window.innerWidth - tabMenu.offsetWidth - 2)) + 'px';
    tabMenu.style.top = Math.max(2, Math.min(y, window.innerHeight - tabMenu.offsetHeight - 2)) + 'px';
    tabMenu.setAttribute('aria-hidden','false');
  }
  tabsEl.addEventListener('contextmenu', (e) => {
    const tab = e.target && e.target.closest ? e.target.closest('.mv-tab') : null;
    if (!tab) return;
    e.preventDefault();
    ctxTab = tab;
    showTabMenu(e.clientX, e.clientY);
  });
  document.addEventListener('click', (e) => { if (!tabMenu.contains(e.target)) hideTabMenu(); });

 tabMenu.addEventListener('click', (e) => {
    const li = e.target.closest('li[data-action]');
    if (!li || !ctxTab) return;

    // Capture current tab BEFORE hiding menu (hideTabMenu clears ctxTab)
    const currentTab = ctxTab;
    const action = li.getAttribute('data-action');
    const tabs = Array.from(tabsEl.children);
    const idx = tabs.indexOf(currentTab);
    const keyOf = (t) => t?.dataset?.key || t?.id?.replace(/^tab-/, '');
    const activeKey = keyOf(currentTab);

    hideTabMenu(); // ok to hide now

    if (action === 'close') {
      closeTabByKey(activeKey);
    } else if (action === 'close-others') {
      tabs.forEach(t => { if (t !== currentTab) closeTabByKey(keyOf(t)); });
      activate(activeKey);
    } else if (action === 'close-left') {
      for (let i = 0; i < idx; i++) closeTabByKey(keyOf(tabs[i]));
      activate(activeKey);
    } else if (action === 'close-right') {
      for (let i = tabs.length - 1; i > idx; i--) closeTabByKey(keyOf(tabs[i]));
      activate(activeKey);
    } else if (action === 'close-all') {
      tabs.forEach(t => closeTabByKey(keyOf(t)));
      showHomeIfNoTabs();
    }
  });

  // ── Pastel confirm dialog ────────────────────────────────────────
  function mvConfirm(message, { confirmLabel = 'OK', cancelLabel = 'Cancel', danger = false } = {}) {
    return new Promise((resolve) => {
      const { ov, panel } = buildOverlay({
        id: 'mv-confirm-ov',
        titleText: '⚠️ Confirm',
        panelClass: 'mv-confirm-panel',
        bodyHtml: `<div style='font-size:13px;line-height:1.6;color:var(--c-text);'>${message}</div>`,
        footerHtml: `<button id='mv-conf-cancel' class='mv-btn'>${cancelLabel}</button>
                     <button id='mv-conf-ok' class='mv-btn ${danger ? 'mv-btn-danger' : 'mv-btn-primary'}'>${confirmLabel}</button>`
      });
      const cleanup = (val) => { ov.remove(); resolve(val); };
      panel.querySelector('#mv-conf-ok').addEventListener('click', () => cleanup(true));
      panel.querySelector('#mv-conf-cancel').addEventListener('click', () => cleanup(false));
    });
  }

  // ── Report generation ────────────────────────────────────────────
  async function generateReport() {
    const key = window.__ctxKey || (selectedAnchor && selectedAnchor.dataset.key) || null;
    if (!key) { showToast('Please select an item in the tree.', 'warn'); return; }

    const ok = await mvConfirm('Generate hierarchical HTML report for <strong>' + key + '</strong> and its children?', { confirmLabel: 'Generate' });
    if (!ok) return;

    let popup = null;
    try {
      popup = window.open('about:blank', '_blank');
      if (popup && !popup.closed) {
        popup.document.write(`<html><head><title>Generating report\u2026</title></head>
          <body style='font:14px Inter,sans-serif;display:flex;align-items:center;justify-content:center;height:100vh;color:var(--c-accent,#7c5cbf);'>
            Generating report for ${key}\u2026</body></html>`);
        popup.document.close();
        try { popup.focus(); } catch {}
      }
    } catch {}

    try {
      const res = await fetch(`/api/report/${encodeURIComponent(key)}`, { method: 'POST' });
      if (!res.ok) throw new Error('HTTP ' + res.status);
      const data = await res.json();
      const targetUrl = data && data.url ? new URL(data.url, window.location.origin).href : null;
      if (targetUrl) {
        if (popup && !popup.closed) popup.location.replace(targetUrl);
        else window.open(targetUrl, '_blank');
      } else {
        if (popup && !popup.closed) popup.document.body.innerHTML = `<div style='padding:12px;color:var(--c-danger,#c94040);'>Report generated but no URL was returned.</div>`;
      }
    } catch (err) {
      showToast('Failed to generate report: ' + String(err?.message || err), 'error');
      if (popup && !popup.closed) popup.document.body.innerHTML = `<div style='padding:12px;color:var(--c-danger,#c94040);'>Failed: ${String(err?.message || err)}</div>`;
    }
  }

  // ── Configuration overlay ────────────────────────────────────────
  async function openConfigOverlay() {
    let cfg = { Jira: {}, Projects: [] };
    try { cfg = await (await fetch('/api/config')).json(); } catch {}
    const jira     = cfg.Jira     || cfg.jira     || {};
    const projects = cfg.Projects || cfg.projects || [];

    // safe: escape single-quotes for inline HTML attribute values
    function esc(v) { return String(v||'').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/'/g,'&#39;'); }

    function projRowHtml(p, i) {
      return '<tr data-pidx=' + i + '>' +
        '<td style=\'padding:4px 6px;font-weight:500;color:var(--c-accent);\'>' + esc(p.Project||p.project) + '</td>' +
        '<td style=\'padding:4px 6px;font-size:12px;color:var(--c-text-soft);\'>' + esc(p.Root||p.root) + '</td>' +
        '<td style=\'padding:4px 6px;font-size:12px;\'>' + esc(p.LinkTypeName||p.linkTypeName) + '</td>' +
        '<td style=\'padding:4px 6px;font-size:12px;\'>' + esc(p.SortingField||p.sortingField) + '</td>' +
        '<td style=\'padding:4px 6px;text-align:center;\'>' +
          '<button class=\'mv-proj-btn\' data-edit=' + i + ' title=\'Edit\'>✏️</button> ' +
          '<button class=\'mv-proj-btn danger\' data-del=' + i + ' title=\'Delete\'>✕</button>' +
        '</td></tr>';
    }

    function mapRowHtml(name, icon) {
      return '<tr>' +
        '<td><input style=\'width:100%;padding:3px 5px;border:1px solid var(--c-border);border-radius:5px;font-size:12px;\' value=\'' + esc(name) + '\' data-col=\'k\'/></td>' +
        '<td><input style=\'width:100%;padding:3px 5px;border:1px solid var(--c-border);border-radius:5px;font-size:12px;\' value=\'' + esc(icon) + '\' data-col=\'v\'/></td>' +
        '<td><button class=\'mv-proj-btn danger\' data-del-row=\'1\' style=\'padding:2px 6px;\'>✕</button></td></tr>';
    }

    const connHtml =
      '<div class=\'mv-field\'><label>Jira Base URL</label>' +
        '<input id=\'cfg-url\' value=\'' + esc(jira.Url||jira.url) + '\' placeholder=\'https://yourorg.atlassian.net\' /></div>' +
      '<div class=\'mv-field\'><label>Email / Username</label>' +
        '<input id=\'cfg-email\' value=\'' + esc(jira.Email||jira.email) + '\' placeholder=\'user@example.com\' /></div>' +
      '<div class=\'mv-field\'><label>API Token</label>' +
        '<input id=\'cfg-token\' type=\'password\' value=\'' + esc(jira.Token||jira.token) + '\' placeholder=\'Jira API token\' /></div>' +
      '<div style=\'display:flex;align-items:center;gap:8px;margin-top:4px;\'>' +
        '<input type=\'checkbox\' id=\'cfg-offline\' style=\'accent-color:var(--c-accent);width:16px;height:16px;\' ' + ((jira.OfflineMode||jira.offlineMode) ? 'checked' : '') + ' />' +
        '<label for=\'cfg-offline\' style=\'font-size:13px;cursor:pointer;\'>Offline mode (skip live Jira sync)</label>' +
      '</div>';

    const projTabHtml =
      '<div style=\'overflow:auto;max-height:220px;border:1px solid var(--c-border);border-radius:7px;margin-bottom:10px;\'>' +
        '<table id=\'cfg-proj-table\' style=\'width:100%;border-collapse:collapse;font-size:12px;\'>' +
          '<thead style=\'background:var(--c-sidebar);position:sticky;top:0;\'><tr>' +
            '<th style=\'padding:6px 8px;text-align:left;color:var(--c-text-soft);font-weight:600;\'>Project Key</th>' +
            '<th style=\'padding:6px 8px;text-align:left;color:var(--c-text-soft);font-weight:600;\'>Root Issue</th>' +
            '<th style=\'padding:6px 8px;text-align:left;color:var(--c-text-soft);font-weight:600;\'>Link Type</th>' +
            '<th style=\'padding:6px 8px;text-align:left;color:var(--c-text-soft);font-weight:600;\'>Sort Field</th>' +
            '<th style=\'padding:6px 8px;text-align:center;color:var(--c-text-soft);font-weight:600;\'>Actions</th>' +
          '</tr></thead>' +
          '<tbody id=\'cfg-proj-tbody\'>' + projects.map(projRowHtml).join('') + '</tbody>' +
        '</table>' +
      '</div>' +
      '<button class=\'mv-btn mv-btn-primary\' id=\'cfg-add-proj\' style=\'width:100%;\'>+ Add Project</button>' +
      '<div id=\'cfg-proj-editor\' style=\'display:none;margin-top:10px;border:1px solid var(--c-accent2);border-radius:8px;padding:10px;background:var(--c-bg);\'>' +
        '<div style=\'font-weight:600;font-size:13px;color:var(--c-accent);margin-bottom:8px;\' id=\'cfg-proj-editor-title\'>New Project</div>' +
        '<div style=\'display:grid;grid-template-columns:1fr 1fr;gap:8px;\'>' +
          '<div class=\'mv-field\' style=\'margin:0\'><label>Project Key</label><input id=\'ped-key\' placeholder=\'e.g. ABC\' style=\'text-transform:uppercase\'/></div>' +
          '<div class=\'mv-field\' style=\'margin:0\'><label>Root Issue Key</label><input id=\'ped-root\' placeholder=\'e.g. ABC-1\'/></div>' +
          '<div class=\'mv-field\' style=\'margin:0\'><label>Link Type Name</label><input id=\'ped-link\' placeholder=\'e.g. Blocks\'/></div>' +
          '<div class=\'mv-field\' style=\'margin:0\'><label>Sorting Field</label><input id=\'ped-sort\' placeholder=\'e.g. Priority\'/></div>' +
        '</div>' +
        '<div style=\'display:grid;grid-template-columns:1fr 1fr;gap:8px;margin-top:8px;\'>' +
          '<div>' +
            '<div style=\'font-size:12px;font-weight:600;color:var(--c-text-soft);margin-bottom:4px;\'>Issue Types (Name \u2192 Icon file)</div>' +
            '<div style=\'overflow:auto;max-height:120px;border:1px solid var(--c-border);border-radius:6px;\'>' +
              '<table style=\'width:100%;border-collapse:collapse;font-size:12px;\'>' +
                '<thead style=\'background:var(--c-sidebar);\'><tr>' +
                  '<th style=\'padding:4px 6px;text-align:left;\'>Type Name</th>' +
                  '<th style=\'padding:4px 6px;text-align:left;\'>Icon File</th>' +
                  '<th style=\'padding:4px 6px;\'></th>' +
                '</tr></thead>' +
                '<tbody id=\'ped-types-body\'></tbody>' +
              '</table>' +
            '</div>' +
            '<button class=\'mv-proj-btn\' id=\'ped-add-type\' style=\'margin-top:4px;width:100%;\'>+ Add Type</button>' +
          '</div>' +
          '<div>' +
            '<div style=\'font-size:12px;font-weight:600;color:var(--c-text-soft);margin-bottom:4px;\'>Statuses (Name \u2192 Icon file)</div>' +
            '<div style=\'overflow:auto;max-height:120px;border:1px solid var(--c-border);border-radius:6px;\'>' +
              '<table style=\'width:100%;border-collapse:collapse;font-size:12px;\'>' +
                '<thead style=\'background:var(--c-sidebar);\'><tr>' +
                  '<th style=\'padding:4px 6px;text-align:left;\'>Status Name</th>' +
                  '<th style=\'padding:4px 6px;text-align:left;\'>Icon File</th>' +
                  '<th style=\'padding:4px 6px;\'></th>' +
                '</tr></thead>' +
                '<tbody id=\'ped-status-body\'></tbody>' +
              '</table>' +
            '</div>' +
            '<button class=\'mv-proj-btn\' id=\'ped-add-status\' style=\'margin-top:4px;width:100%;\'>+ Add Status</button>' +
          '</div>' +
        '</div>' +
        '<div style=\'display:flex;gap:8px;justify-content:flex-end;margin-top:10px;\'>' +
          '<button class=\'mv-btn\' id=\'ped-cancel\'>Cancel</button>' +
          '<button class=\'mv-btn mv-btn-primary\' id=\'ped-save\'>\uD83D\uDCBE Save Project</button>' +
        '</div>' +
      '</div>';

    const { ov, panel } = buildOverlay({
      id: 'mv-config-ov',
      titleText: '\u2699\uFE0F Configuration',
      panelClass: 'mv-cfg-panel',
      bodyHtml:
        '<div class=\'mv-cfg-tabs-bar\'>' +
          '<button class=\'mv-cfg-tab active\' data-tab=\'conn\'>\uD83D\uDD0C Connection</button>' +
          '<button class=\'mv-cfg-tab\' data-tab=\'projects\'>\uD83D\uDCC1 Projects</button>' +
        '</div>' +
        '<div class=\'mv-cfg-pane active\' id=\'cfgpane-conn\'>' + connHtml + '</div>' +
        '<div class=\'mv-cfg-pane\' id=\'cfgpane-projects\'>' + projTabHtml + '</div>',
      footerHtml:
        '<button id=\'cfg-cancel\' class=\'mv-btn\'>Cancel</button>' +
        '<button id=\'cfg-save\' class=\'mv-btn mv-btn-primary\'>\uD83D\uDCBE Save Configuration</button>'
    });

    let localProjects = projects.map(p => ({
      Project:      p.Project      || p.project      || '',
      Root:         p.Root         || p.root         || '',
      LinkTypeName: p.LinkTypeName || p.linkTypeName || '',
      SortingField: p.SortingField || p.sortingField || '',
      Types:        p.Types        || p.types        || {},
      Status:       p.Status       || p.status       || {}
    }));
    let editingIdx = -1;

    const tbody = panel.querySelector('#cfg-proj-tbody');
    const editor = panel.querySelector('#cfg-proj-editor');

    function readMapFromTable(tbodyEl) {
      const dict = {};
      tbodyEl.querySelectorAll('tr').forEach(tr => {
        const k = (tr.querySelector('[data-col=\'k\']')?.value || '').trim();
        const v = (tr.querySelector('[data-col=\'v\']')?.value || '').trim();
        if (k) dict[k] = v;
      });
      return dict;
    }

    function bindDelRows(tbodyEl) {
      tbodyEl.querySelectorAll('[data-del-row]').forEach(btn => {
        btn.addEventListener('click', () => btn.closest('tr').remove());
      });
    }

    function openEditor(idx) {
      editingIdx = idx;
      const isNew = idx < 0;
      const p = isNew ? { Project:'', Root:'', LinkTypeName:'', SortingField:'', Types:{}, Status:{} } : localProjects[idx];
      panel.querySelector('#cfg-proj-editor-title').textContent = isNew ? 'New Project' : 'Edit: ' + p.Project;
      panel.querySelector('#ped-key').value   = p.Project;
      panel.querySelector('#ped-root').value  = p.Root;
      panel.querySelector('#ped-link').value  = p.LinkTypeName;
      panel.querySelector('#ped-sort').value  = p.SortingField;
      const typesTbody  = panel.querySelector('#ped-types-body');
      const statusTbody = panel.querySelector('#ped-status-body');
      typesTbody.innerHTML  = Object.entries(p.Types  || {}).map(([k,v]) => mapRowHtml(k,v)).join('');
      statusTbody.innerHTML = Object.entries(p.Status || {}).map(([k,v]) => mapRowHtml(k,v)).join('');
      bindDelRows(typesTbody); bindDelRows(statusTbody);
      editor.style.display = 'block';
      panel.querySelector('#ped-key').focus();
    }

    function refreshProjTable() {
      tbody.innerHTML = localProjects.map(projRowHtml).join('');
      tbody.querySelectorAll('[data-edit]').forEach(btn => {
        btn.addEventListener('click', () => openEditor(Number(btn.dataset.edit)));
      });
      tbody.querySelectorAll('[data-del]').forEach(btn => {
        btn.addEventListener('click', async () => {
          const nm = localProjects[Number(btn.dataset.del)]?.Project || '';
          if (await mvConfirm('Delete project <strong>' + nm + '</strong>?', { confirmLabel:'Delete', danger:true }))
            { localProjects.splice(Number(btn.dataset.del), 1); refreshProjTable(); }
        });
      });
    }
    refreshProjTable();

    panel.querySelector('#ped-add-type').addEventListener('click', () => {
      const tb = panel.querySelector('#ped-types-body');
      tb.insertAdjacentHTML('beforeend', mapRowHtml('',''));
      bindDelRows(tb);
    });
    panel.querySelector('#ped-add-status').addEventListener('click', () => {
      const tb = panel.querySelector('#ped-status-body');
      tb.insertAdjacentHTML('beforeend', mapRowHtml('',''));
      bindDelRows(tb);
    });

    panel.querySelector('#cfg-add-proj').addEventListener('click', () => openEditor(-1));
    panel.querySelector('#ped-cancel').addEventListener('click', () => { editor.style.display = 'none'; });
    panel.querySelector('#ped-save').addEventListener('click', () => {
      const key = (panel.querySelector('#ped-key').value || '').trim().toUpperCase();
      if (!key) { showToast('Project Key is required.', 'warn'); return; }
      const proj = {
        Project:      key,
        Root:         (panel.querySelector('#ped-root').value || '').trim(),
        LinkTypeName: (panel.querySelector('#ped-link').value || '').trim(),
        SortingField: (panel.querySelector('#ped-sort').value || '').trim(),
        Types:        readMapFromTable(panel.querySelector('#ped-types-body')),
        Status:       readMapFromTable(panel.querySelector('#ped-status-body'))
      };
      if (editingIdx < 0) localProjects.push(proj);
      else localProjects[editingIdx] = proj;
      editor.style.display = 'none';
      refreshProjTable();
    });

    panel.querySelectorAll('.mv-cfg-tab').forEach(tab => {
      tab.addEventListener('click', () => {
        panel.querySelectorAll('.mv-cfg-tab').forEach(t => t.classList.remove('active'));
        panel.querySelectorAll('.mv-cfg-pane').forEach(p => p.classList.remove('active'));
        tab.classList.add('active');
        panel.querySelector('#cfgpane-' + tab.dataset.tab).classList.add('active');
      });
    });

    panel.querySelector('#cfg-cancel').addEventListener('click', () => ov.remove());
    panel.querySelector('#cfg-save').addEventListener('click', async () => {
      const payload = {
        Jira: {
          Url:         (panel.querySelector('#cfg-url').value   || '').trim(),
          Email:       (panel.querySelector('#cfg-email').value || '').trim(),
          Token:       (panel.querySelector('#cfg-token').value || '').trim(),
          OfflineMode: panel.querySelector('#cfg-offline').checked
        },
        Projects: localProjects
      };
      const btn = panel.querySelector('#cfg-save');
      btn.disabled = true; btn.textContent = 'Saving\u2026';
      try {
        const res = await fetch('/api/config', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(payload)
        });
        if (!res.ok) throw new Error('HTTP ' + res.status);
        showToast('Configuration saved. Restart may be needed for full effect.', 'success');
        editorInfo = null;
        ov.remove();
      } catch (err) {
        showToast('Failed to save: ' + String(err?.message || err), 'error');
        btn.disabled = false; btn.textContent = '\uD83D\uDCBE Save Configuration';
      }
    });
  }

  // ── Hierarchy Update overlay ─────────────────────────────────────
  async function openHierarchyUpdateOverlay() {
    let projectList = [];
    let lastUpdated = 'N/A';
    try {
      const data = await (await fetch('/api/projects')).json();
      projectList = data?.projects || data || [];
    } catch {}
    try {
      const st = await (await fetch('/api/status')).json();
      lastUpdated = st.lastDbUpdated || 'N/A';
    } catch {}

    const projOptions = projectList.map(p =>
      `<option value='${p.projectKey || p}'>${p.projectKey || p}${p.projectName ? ' \u2014 ' + p.projectName : ''}</option>`
    ).join('');

    const { ov, panel } = buildOverlay({
      id: 'mv-hier-ov',
      titleText: '\uD83D\uDD04 Update Hierarchy',
      panelClass: 'mv-hier-panel',
      bodyHtml: `
        <div style='text-align:center;margin-bottom:10px;color:var(--c-text-soft);font-size:12px;'>
          Last updated: <strong style='color:var(--c-accent);'>${lastUpdated}</strong>
        </div>
        <div class='mv-field'>
          <label>Update Type</label>
          <select id='hier-type'>
            <option value='Difference' selected>Difference (recent changes)</option>
            <option value='Complete'>Complete (full refresh)</option>
          </select>
        </div>
        <div class='mv-field'>
          <label>Project (optional)</label>
          <select id='hier-project'><option value=''>All projects</option>${projOptions}</select>
        </div>
        <div class='mv-field mv-hint' style='color:var(--c-text-soft);font-size:12px;'>
          <em>Difference</em> fetches only recent Jira changes. <em>Complete</em> re-syncs everything.
        </div>
        <div id='hier-progress-area' style='display:none;margin-top:12px;'>
          <div style='display:flex;justify-content:space-between;align-items:center;margin-bottom:4px;'>
            <span id='hier-progress-project' style='font-size:12px;font-weight:600;color:var(--c-accent);'></span>
            <span id='hier-progress-count' style='font-size:12px;color:var(--c-text-soft);'></span>
          </div>
          <div class='mv-progress' style='height:10px;border-radius:6px;overflow:hidden;background:var(--c-border);'>
            <div class='mv-progress-bar' id='hier-progress-bar' style='width:0%;transition:width .4s ease;'></div>
          </div>
          <div style='display:flex;justify-content:space-between;margin-top:4px;'>
            <span id='hier-progress-msg' style='font-size:11px;color:var(--c-text-soft);'>Preparing\u2026</span>
            <span id='hier-progress-pct' style='font-size:11px;color:var(--c-text-soft);'></span>
          </div>
        </div>`,
      footerHtml: `<button id='hier-cancel' class='mv-btn'>Cancel</button>
                   <button id='hier-run' class='mv-btn mv-btn-primary'>Start Update</button>`
    });

    panel.querySelector('#hier-cancel').addEventListener('click', () => ov.remove());
    panel.querySelector('#hier-run').addEventListener('click', async () => {
      const updateType = panel.querySelector('#hier-type').value;
      const project    = panel.querySelector('#hier-project').value || null;
      panel.querySelector('#hier-run').disabled    = true;
      panel.querySelector('#hier-cancel').disabled = true;
      panel.querySelector('#hier-progress-area').style.display = 'block';

      const bar      = panel.querySelector('#hier-progress-bar');
      const msgEl    = panel.querySelector('#hier-progress-msg');
      const projEl   = panel.querySelector('#hier-progress-project');
      const countEl  = panel.querySelector('#hier-progress-count');
      const pctEl    = panel.querySelector('#hier-progress-pct');

      msgEl.textContent = 'Starting update\u2026';

      // ── Polling loop ──────────────────────────────────────────────
      let pollTimer = null;
      let lastProject = '';
      async function pollProgress() {
        try {
          const p = await (await fetch('/api/hierarchy/progress')).json();
          if (p.inProgress) {
            if (p.project && p.project !== lastProject) {
              lastProject = p.project;
              projEl.textContent = '\uD83D\uDCC2 ' + p.project;
            }
            const pct = Math.min(100, p.percent || 0);
            bar.style.width = pct + '%';
            pctEl.textContent = pct.toFixed(1) + '%';
            if (p.total > 0) {
              countEl.textContent = p.completed + ' / ' + p.total + ' issues';
              msgEl.textContent   = 'Syncing issues\u2026';
            } else {
              countEl.textContent = '';
              msgEl.textContent   = 'Fetching from Jira\u2026';
            }
          }
        } catch { /* ignore mid-update errors */ }
      }
      pollTimer = setInterval(pollProgress, 600);

      // ── POST the actual update ────────────────────────────────────
      try {
        const res = await fetch('/api/hierarchy/update', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ updateType, project, forceSync: true })
        });
        clearInterval(pollTimer);
        if (!res.ok) throw new Error('HTTP ' + res.status);
        bar.style.width         = '100%';
        pctEl.textContent       = '100%';
        msgEl.textContent       = 'Update complete!';
        countEl.textContent     = '';
        projEl.textContent      = '';
        showToast('Hierarchy updated successfully.', 'success');
        await refreshStatus();
        setTimeout(() => ov.remove(), 1200);
        await loadRoots();
        await expandRootLevelOnce();
      } catch (err) {
        clearInterval(pollTimer);
        panel.querySelector('#hier-progress-area').style.display = 'none';
        panel.querySelector('#hier-run').disabled    = false;
        panel.querySelector('#hier-cancel').disabled = false;
        showToast('Update failed: ' + String(err?.message || err), 'error');
      }
    });
  }

  // ── AI Train overlay ─────────────────────────────────────────────
  async function openAITrainOverlay() {
    const { ov, panel } = buildOverlay({
      id: 'mv-train-ov',
      titleText: '🧠 Train AI Index',
      panelClass: 'mv-train-panel',
      bodyHtml: `
        <p style='font-size:13px;line-height:1.6;color:var(--c-text);margin:0 0 10px;'>
          This will build the local AI knowledge index from your cached Jira data.
          Depending on the number of issues, this may take a few minutes.
        </p>
        <div id='train-progress-area' style='display:none;'>
          <div style='font-size:12px;color:var(--c-text-soft);margin-bottom:4px;' id='train-progress-msg'>Indexing\u2026</div>
          <div class='mv-progress'><div class='mv-progress-bar' id='train-progress-bar' style='width:70%'></div></div>
        </div>`,
      footerHtml: `<button id='train-cancel' class='mv-btn'>Cancel</button>
                   <button id='train-run' class='mv-btn mv-btn-primary'>🧠 Start Training</button>`
    });

    panel.querySelector('#train-cancel').addEventListener('click', () => ov.remove());
    panel.querySelector('#train-run').addEventListener('click', async () => {
      panel.querySelector('#train-run').disabled = true;
      panel.querySelector('#train-cancel').disabled = true;
      panel.querySelector('#train-progress-area').style.display = 'block';
      try {
        const res = await fetch('/api/ai/train', { method: 'POST' });
        if (!res.ok) throw new Error('HTTP ' + res.status);
        panel.querySelector('#train-progress-bar').style.width = '100%';
        panel.querySelector('#train-progress-msg').textContent = 'Training complete!';
        showToast('AI index trained successfully.', 'success');
        setTimeout(() => ov.remove(), 900);
      } catch (err) {
        panel.querySelector('#train-progress-area').style.display = 'none';
        panel.querySelector('#train-run').disabled = false;
        panel.querySelector('#train-cancel').disabled = false;
        showToast('Training failed: ' + String(err?.message || err), 'error');
      }
    });
  }

  // ── Edit issue (open Jira browse in new tab) ────────────────────
  function openEditIssue(key) {
    const url = `${window.__jiraBaseUrl || ''}/browse/${encodeURIComponent(key)}`;
    window.open(url, '_blank');
  }

  // ── Fetch editor mode / project config ──────────────────────────
  let editorInfo = null;
  async function getEditorInfo() {
    if (editorInfo) return editorInfo;
    try { editorInfo = await (await fetch('/api/editor/mode')).json(); } catch { editorInfo = { editorMode: false, projects: [] }; }
    return editorInfo;
  }

  // ── Autocomplete helper (issue keys) ────────────────────────────
  let issueKeysCache = null;
  async function getIssueKeys() {
    if (issueKeysCache) return issueKeysCache;
    try { issueKeysCache = await (await fetch('/api/issue/keys')).json(); } catch { issueKeysCache = []; }
    return issueKeysCache;
  }
  function buildAutocompleteInput(id, placeholder, keys) {
    const wrap = document.createElement('div'); wrap.style.position = 'relative'; wrap.style.flex = '1 1 auto';
    const inp = document.createElement('input');
    inp.id = id; inp.type = 'text'; inp.placeholder = placeholder;
    inp.style.cssText = 'width:100%;padding:7px 10px;border:1px solid var(--c-border);border-radius:7px;font-size:13px;color:var(--c-text);background:var(--c-bg);outline:none;';
    const list = document.createElement('ul');
    list.style.cssText = 'position:absolute;top:100%;left:0;right:0;background:var(--c-surface);border:1px solid var(--c-border);border-radius:7px;max-height:180px;overflow-y:auto;z-index:100;list-style:none;margin:2px 0 0;padding:4px;box-shadow:0 4px 16px rgba(90,60,160,.12);display:none;';
    inp.addEventListener('input', () => {
      const q = inp.value.trim().toUpperCase();
      list.innerHTML = '';
      if (!q) { list.style.display = 'none'; return; }
      const matches = keys.filter(k => k.key.includes(q) || k.summary.toUpperCase().includes(q)).slice(0, 12);
      if (!matches.length) { list.style.display = 'none'; return; }
      matches.forEach(m => {
        const li = document.createElement('li');
        li.style.cssText = 'padding:5px 10px;cursor:pointer;border-radius:5px;font-size:12px;color:var(--c-accent);';
        li.textContent = m.key + (m.summary ? '  —  ' + m.summary.substring(0, 60) : '');
        li.addEventListener('mousedown', (e) => { e.preventDefault(); inp.value = m.key; list.style.display = 'none'; });
        li.addEventListener('mouseover', () => li.style.background = 'var(--c-hover)');
        li.addEventListener('mouseout',  () => li.style.background = '');
        list.appendChild(li);
      });
      list.style.display = 'block';
    });
    inp.addEventListener('blur', () => setTimeout(() => list.style.display = 'none', 150));
    wrap.appendChild(inp); wrap.appendChild(list);
    return { wrap, inp };
  }

  // ── Add Child / Sibling overlay ──────────────────────────────────
  async function openAddIssueOverlay(mode) {
    const key = window.__ctxKey || (selectedAnchor && selectedAnchor.dataset.key);
    if (!key) { showToast('Please select a node first.', 'warn'); return; }
    const info = await getEditorInfo();
    const prefix = key.split('-')[0];
    const proj = (info.projects || []).find(p => p.projectKey === prefix || (p.projectKey && key.startsWith(p.projectKey)));
    const types = proj?.issueTypes || [];
    if (!types.length) { showToast('No issue types found for this project.', 'warn'); return; }
    const typeOptions = types.map(t => `<option value='${t}'>${t}</option>`).join('');

    const { ov, panel } = buildOverlay({
      id: 'mv-add-issue-ov',
      titleText: mode === 'Child' ? `🌱 Add Child to ${key}` : `🌳 Add Sibling of ${key}`,
      panelClass: 'mv-hier-panel',
      bodyHtml: `
        <div class='mv-field'><label>Issue Type</label><select id='add-issue-type'>${typeOptions}</select></div>
        <div class='mv-field'><label>Summary</label><input id='add-issue-summary' maxlength='250' placeholder='Enter issue summary…' /></div>`,
      footerHtml: `<button id='add-issue-cancel' class='mv-btn'>Cancel</button>
                   <button id='add-issue-create' class='mv-btn mv-btn-primary'>Create</button>`
    });

    panel.querySelector('#add-issue-cancel').addEventListener('click', () => ov.remove());
    const createBtn = panel.querySelector('#add-issue-create');
    createBtn.addEventListener('click', async () => {
      const issueType = panel.querySelector('#add-issue-type').value;
      const summary   = (panel.querySelector('#add-issue-summary').value || '').trim();
      if (!summary) { showToast('Summary is required.', 'warn'); return; }
      createBtn.disabled = true; createBtn.textContent = 'Creating…';
      try {
        const res = await fetch('/api/issue/create', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ baseKey: key, mode, issueType, summary })
        });
        const data = await res.json();
        if (!res.ok) throw new Error(data.error || 'Failed');
        showToast(`Created ${data.newKey || 'issue'} successfully. Update hierarchy to see it in the tree.`, 'success');
        issueKeysCache = null; // invalidate cache
        ov.remove();
        if (data.newKey) openEditIssue(data.newKey);
      } catch (err) {
        showToast('Create failed: ' + String(err?.message || err), 'error');
        createBtn.disabled = false; createBtn.textContent = 'Create';
      }
    });
    setTimeout(() => panel.querySelector('#add-issue-summary').focus(), 60);
  }

  // ── Link Related overlay ─────────────────────────────────────────
  async function openLinkRelatedOverlay() {
    const key = window.__ctxKey || (selectedAnchor && selectedAnchor.dataset.key);
    if (!key) { showToast('Please select a node first.', 'warn'); return; }
    const allKeys = await getIssueKeys();

    const { ov, panel } = buildOverlay({
      id: 'mv-link-ov',
      titleText: `🔗 Link Related to ${key}`,
      panelClass: 'mv-cfg-panel',
      bodyHtml: `
        <div style='display:flex;gap:6px;align-items:flex-end;margin-bottom:8px;'>
          <div id='link-inp-wrap' style='flex:1 1 auto;'></div>
          <button id='link-add-btn' class='mv-btn mv-btn-primary' style='flex-shrink:0;'>+ Add</button>
        </div>
        <ul id='link-list' style='list-style:none;padding:0;margin:0;max-height:220px;overflow:auto;'></ul>`,
      footerHtml: `<button id='link-cancel' class='mv-btn'>Cancel</button>
                   <button id='link-submit' class='mv-btn mv-btn-primary'>Link</button>`
    });

    const { wrap, inp } = buildAutocompleteInput('link-key-inp', 'Issue key e.g. ABC-123', allKeys);
    panel.querySelector('#link-inp-wrap').replaceWith(wrap);

    let linkedKeys = [];
    const listEl = panel.querySelector('#link-list');
    function refreshLinkList() {
      listEl.innerHTML = linkedKeys.map((k, i) => {
        const s = (allKeys.find(x => x.key === k)?.summary || '').substring(0, 70);
        return `<li style='display:flex;align-items:center;justify-content:space-between;padding:6px 8px;border:1px solid var(--c-border);border-radius:7px;margin-bottom:4px;font-size:12px;'>
          <span style='color:var(--c-accent);font-weight:500;'>${k}</span><span style='color:var(--c-text-soft);margin:0 8px;flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;'>${s}</span>
          <button data-rm='${i}' style='appearance:none;border:none;background:none;cursor:pointer;color:var(--c-danger);font-size:14px;'>✕</button></li>`;
      }).join('');
      listEl.querySelectorAll('[data-rm]').forEach(btn => {
        btn.addEventListener('click', () => { linkedKeys.splice(Number(btn.dataset.rm), 1); refreshLinkList(); });
      });
    }

    function addKey(raw) {
      (raw || '').split(/[\s,]+/).map(k => k.trim().toUpperCase()).filter(Boolean).forEach(k => {
        if (k === key.toUpperCase()) { showToast('Cannot link issue to itself.', 'warn'); return; }
        if (!allKeys.find(x => x.key === k)) { showToast(`Key ${k} not found — skipped.`, 'warn'); return; }
        if (!linkedKeys.includes(k)) linkedKeys.push(k);
      });
      refreshLinkList();
    }

    panel.querySelector('#link-add-btn').addEventListener('click', () => { addKey(inp.value); inp.value = ''; inp.focus(); });
    inp.addEventListener('keydown', e => { if (e.key === 'Enter') { e.preventDefault(); addKey(inp.value); inp.value = ''; } });

    panel.querySelector('#link-cancel').addEventListener('click', () => ov.remove());
    panel.querySelector('#link-submit').addEventListener('click', async () => {
      if (!linkedKeys.length) { showToast('No keys to link.', 'warn'); return; }
      const btn = panel.querySelector('#link-submit');
      btn.disabled = true; btn.textContent = 'Linking…';
      try {
        const res = await fetch('/api/issue/link-related', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ baseKey: key, keys: linkedKeys })
        });
        const data = await res.json();
        if (!res.ok) throw new Error(data.error || 'Failed');
        showToast('Related issues linked successfully.', 'success');
        ov.remove();
      } catch (err) {
        showToast('Link failed: ' + String(err?.message || err), 'error');
        btn.disabled = false; btn.textContent = 'Link';
      }
    });
    setTimeout(() => inp.focus(), 60);
  }

  // ── Change Parent overlay ────────────────────────────────────────
  async function openChangeParentOverlay() {
    const key = window.__ctxKey || (selectedAnchor && selectedAnchor.dataset.key);
    if (!key) { showToast('Please select a node first.', 'warn'); return; }
    const allKeys = await getIssueKeys();

    const { ov, panel } = buildOverlay({
      id: 'mv-chparent-ov',
      titleText: `🌳 Change Parent of ${key}`,
      panelClass: 'mv-hier-panel',
      bodyHtml: `
        <div class='mv-field'>
          <label>New Parent Key</label>
          <div id='chp-inp-wrap'></div>
        </div>`,
      footerHtml: `<button id='chp-cancel' class='mv-btn'>Cancel</button>
                   <button id='chp-ok' class='mv-btn mv-btn-primary'>Change</button>`
    });

    const { wrap, inp } = buildAutocompleteInput('chp-key-inp', 'e.g. ABC-10', allKeys);
    panel.querySelector('#chp-inp-wrap').replaceWith(wrap);

    panel.querySelector('#chp-cancel').addEventListener('click', () => ov.remove());
    panel.querySelector('#chp-ok').addEventListener('click', async () => {
      const newParent = (inp.value || '').trim().toUpperCase();
      if (!newParent) { showToast('Please enter a parent key.', 'warn'); return; }
      const btn = panel.querySelector('#chp-ok');
      btn.disabled = true; btn.textContent = 'Changing…';
      try {
        const res = await fetch('/api/issue/change-parent', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ childKey: key, newParentKey: newParent })
        });
        const data = await res.json();
        if (!res.ok) throw new Error(data.error || 'Failed');
        showToast(`Parent of ${key} changed to ${newParent}. Update hierarchy to reflect.`, 'success');
        ov.remove();
      } catch (err) {
        showToast('Change parent failed: ' + String(err?.message || err), 'error');
        btn.disabled = false; btn.textContent = 'Change';
      }
    });
    setTimeout(() => inp.focus(), 60);
  }

  // ── Move Up / Down ───────────────────────────────────────────────
  async function moveNode(direction) {
    const key = window.__ctxKey || (selectedAnchor && selectedAnchor.dataset.key);
    if (!key) { showToast('Please select a node first.', 'warn'); return; }
    const label = direction < 0 ? 'up' : 'down';
    const ok = await mvConfirm(`Move <strong>${key}</strong> ${label}?`, { confirmLabel: `Move ${label}` });
    if (!ok) return;
    try {
      const res = await fetch('/api/issue/move', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ key, direction })
      });
      const data = await res.json();
      if (!res.ok) throw new Error(data.error || 'Failed');
      showToast(`${key} moved ${label}.`, 'success');
      // Refresh tree to reflect new order
      await loadRoots(); await expandRootLevelOnce();
    } catch (err) {
      showToast('Move failed: ' + String(err?.message || err), 'error');
    }
  }

  // ── Create folder structure overlay ─────────────────────────────
  async function openCreateFolderOverlay() {
    const key = window.__ctxKey || (selectedAnchor && selectedAnchor.dataset.key);
    if (!key) { showToast('Please select a node first.', 'warn'); return; }

    // ── Show overlay with loading state while fetching preview ──────
    const { ov, panel } = buildOverlay({
      id: 'mv-folder-ov',
      titleText: `\uD83D\uDCC1 Create Folder Structure — ${key}`,
      panelClass: 'mv-hier-panel',
      bodyHtml: `
        <p style='font-size:12px;color:var(--c-text-soft);margin:0 0 10px;'>
          Paths to be created under <code style='background:var(--c-bg);padding:1px 5px;border-radius:4px;'>C:\\manual\\Release</code>
        </p>
        <div id='folder-preview-area' style='min-height:80px;display:flex;align-items:center;justify-content:center;'>
          <span style='color:var(--c-text-soft);font-size:12px;'>Loading paths\u2026</span>
        </div>`,
      footerHtml: `<button id='folder-cancel' class='mv-btn'>Cancel</button>
                   <button id='folder-ok' class='mv-btn mv-btn-primary' disabled>Create</button>`
    });

    panel.querySelector('#folder-cancel').addEventListener('click', () => ov.remove());
    const okBtn = panel.querySelector('#folder-ok');
    const previewArea = panel.querySelector('#folder-preview-area');

    // ── Fetch preview ────────────────────────────────────────────────
    let previewItems = [];
    try {
      const res = await fetch('/api/folder/preview?key=' + encodeURIComponent(key));
      if (!res.ok) throw new Error('HTTP ' + res.status);
      const data = await res.json();
      previewItems = data.items || [];
      if (!previewItems.length) {
        previewArea.innerHTML = `<span style='color:var(--c-text-soft);font-size:12px;'>No paths to create.</span>`;
      } else {
        const rows = previewItems.map(item =>
          `<tr>
             <td style='padding:3px 8px 3px 0;white-space:nowrap;'>
               <span style='display:inline-block;padding:1px 6px;border-radius:4px;font-size:10px;font-weight:600;
                 background:${item.type === 'Folder' ? 'rgba(26,107,191,.12)' : 'rgba(46,204,113,.12)'};
                 color:${item.type === 'Folder' ? 'var(--c-accent)' : '#2ecc71'};'>
                 ${item.type}
               </span>
             </td>
             <td style='font-size:11px;color:var(--c-text);word-break:break-all;'>${item.path}</td>
           </tr>`
        ).join('');
        previewArea.innerHTML =
          `<div style='max-height:280px;overflow-y:auto;width:100%;'>
             <table style='border-collapse:collapse;width:100%;'>${rows}</table>
           </div>
           <p style='font-size:11px;color:var(--c-text-soft);margin:8px 0 0;'>
             ${previewItems.length} path${previewItems.length !== 1 ? 's' : ''} will be created (existing paths are skipped).
           </p>`;
        okBtn.disabled = false;
      }
    } catch (err) {
      previewArea.innerHTML = `<span style='color:var(--c-danger);font-size:12px;'>Preview failed: ${err?.message || err}</span>`;
    }

    // ── Create on confirm ────────────────────────────────────────────
    okBtn.addEventListener('click', async () => {
      okBtn.disabled = true; okBtn.textContent = 'Creating\u2026';
      try {
        const res = await fetch('/api/folder/create', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ key })
        });
        const data = await res.json();
        if (!res.ok) throw new Error(data.error || 'HTTP ' + res.status);
        showToast(`Created ${data.created} path${data.created !== 1 ? 's' : ''} successfully.`, 'success');
        ov.remove();
      } catch (err) {
        showToast('Create failed: ' + String(err?.message || err), 'error');
        okBtn.disabled = false; okBtn.textContent = 'Create';
      }
    });
  }

  // ── Sidebar toolbar buttons ──────────────────────────────────────
  if (btnRecent) btnRecent.addEventListener('click', () => openRecentUpdatesTab());
  if (btnAI)     btnAI.addEventListener('click',     () => openAIChatTab());
  if (btnConfig) btnConfig.addEventListener('click', () => openConfigOverlay());
  if (btnUpdate) btnUpdate.addEventListener('click', () => openHierarchyUpdateOverlay());
  if (btnReport) btnReport.addEventListener('click', () => generateReport());

  // Search dialog
  let searchOptions = null;
  function union(arrays){ const set = new Set(); arrays.forEach(a => a?.forEach(v => set.add(v))); return Array.from(set).sort((a,b)=>a.localeCompare(b)); }
  async function ensureSearchOptions(){
    if (searchOptions) return searchOptions;
    const res = await fetch('/api/search/options');
    searchOptions = await res.json();
    return searchOptions;
  }
  function fillDropdown(sel, values, includeAll=true){
    sel.innerHTML = '';
    if (includeAll){ const opt = document.createElement('option'); opt.value = 'All'; opt.textContent = 'All'; sel.appendChild(opt); }
    (values || []).forEach(v => { const o=document.createElement('option'); o.value=v; o.textContent=v; sel.appendChild(o); });
    sel.value = 'All';
  }
  async function populateFilters(){
    const opts = await ensureSearchOptions();
    const projects = (opts?.projects || []).map(p => p.project).filter(Boolean).sort((a,b)=>a.localeCompare(b));
    fillDropdown(ddlProject, projects, true);
    const allTypes = union((opts?.projects || []).map(p => p.types));
    const allStatuses = union((opts?.projects || []).map(p => p.statuses));
    fillDropdown(ddlType, allTypes, true);
    fillDropdown(ddlStatus, allStatuses, true);

    ddlProject.onchange = () => {
      const sel = ddlProject.value;
      if (sel === 'All'){
        fillDropdown(ddlType, union((opts?.projects || []).map(p => p.types)), true);
        fillDropdown(ddlStatus, union((opts?.projects || []).map(p => p.statuses)), true);
      } else {
        const p = (opts?.projects || []).find(x => x.project === sel);
        fillDropdown(ddlType, p?.types || [], true);
        fillDropdown(ddlStatus, p?.statuses || [], true);
      }
    };
  }
  function toggleJqlUI(){
    const jqlMode = !!searchJql.checked;
    searchFilters.style.display = jqlMode ? 'none' : 'flex';
    searchText.placeholder = jqlMode ? 'Enter JQL...' : 'Enter issue key or search text...';
  }
  function showSearchLoading(){
    resultsFrame.srcdoc = `<html><body style='display:flex;align-items:center;justify-content:center;height:100%;font:14px Inter,sans-serif;color:#7c5cbf;background:#f7f4fb;'>Searching…</body></html>`;
  }

  // Build normal JQL (unchanged)
  function buildNormalJql(){
    const q = (searchText.value || '').trim();
    const proj = ddlProject.value || 'All';
    const type = ddlType.value || 'All';
    const status = ddlStatus.value || 'All';
    const filters = [];
    if (proj === 'All') {
      try {
        const projects = (searchOptions?.projects || []).map(p => p.project).filter(Boolean);
        if (projects.length) filters.push('(' + projects.map(p => `project = ""${p}""`).join(' OR ') + ')');
      } catch {}
    } else {
      filters.push(`project = ""${proj}""`);
    }
    if (q) filters.push(`text ~ ""${q}""`);
    if (type !== 'All') filters.push(`issuetype = ""${type}""`);
    if (status !== 'All') filters.push(`status = ""${status}""`);
    return `${filters.join(' AND ')} ORDER BY key ASC`;
  }

  function showSearchDialog(){
    searchOverlay.style.display = 'block';
    searchOverlay.setAttribute('aria-hidden', 'false');
    searchText.focus();
    if (!searchOptions) populateFilters();
  }
  function hideSearchDialog(){
    searchOverlay.style.display = 'none';
    searchOverlay.setAttribute('aria-hidden', 'true');
  }
  function openSearchDialog() { showSearchDialog(); }

  searchClose.addEventListener('click', hideSearchDialog);
  searchJql.addEventListener('change', () => { toggleJqlUI(); });
  toggleJqlUI();
  searchRun.addEventListener('click', async () => {
    const jql = searchJql.checked ? (searchText.value || '').trim() : buildNormalJql();
    if (!jql) return;
    showSearchLoading();
    try {
      const html = await (await fetch(`/api/search/html?jql=${encodeURIComponent(jql)}`)).text();
      resultsFrame.srcdoc = html;
    } catch {
      resultsFrame.srcdoc = `<html><body style='padding:20px;color:#c94040;font:13px Inter,sans-serif;'>Failed to run search.</body></html>`;
    }
  });
  searchText.addEventListener('keydown', (e) => {
    if (e.key === 'Enter') { e.preventDefault(); searchRun.click(); }
    if (e.key === 'Escape') { e.preventDefault(); hideSearchDialog(); }
  });
  searchOverlay.addEventListener('click', (e) => {
    const panel = e.target.closest('.mv-search-panel');
    if (!panel) hideSearchDialog();
  });

  // Global keyboard
  window.addEventListener('keydown', (e) => {
    const tag = (e.target?.tagName || '').toUpperCase();
    if (e.ctrlKey && (e.key === 'q' || e.key === 'Q')) {
      if (tag !== 'INPUT' && tag !== 'TEXTAREA' && !(e.target?.isContentEditable)) {
        e.preventDefault(); openSearchDialog();
      }
    }
    if (e.ctrlKey && (e.key === 'p' || e.key === 'P')) {
      if (tag !== 'INPUT' && tag !== 'TEXTAREA' && !(e.target?.isContentEditable)) {
        e.preventDefault(); generateReport();
      }
    }
    if (e.ctrlKey && (e.key === 'm' || e.key === 'M')) {
      if (tag !== 'INPUT' && tag !== 'TEXTAREA' && !(e.target?.isContentEditable)) {
        e.preventDefault(); openAIChatTab();
      }
    }
    if (e.key === 'Escape') {
      hideTreeMenu();
      hideTabMenu();
      if (searchOverlay.style.display !== 'none') hideSearchDialog();
      // Close any open overlay panels
      document.querySelectorAll('.mv-overlay').forEach(o => o.remove());
    }
  });

  await loadRoots();
  await expandRootLevelOnce();
  await openRecentUpdatesTab({ days: 14, activateTab: true });
  updateTabScrollButtons();
})();";

            Directory.CreateDirectory(WebAppRoot);
            await System.IO.File.WriteAllTextAsync(Path.Combine(WebAppRoot, "index.html"), indexHtml, Encoding.UTF8);
            await System.IO.File.WriteAllTextAsync(Path.Combine(WebAppRoot, "monovera.web.js"), webJs, Encoding.UTF8);
        }
    }
}