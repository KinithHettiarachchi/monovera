using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Monovera
{
    /// <summary>
    /// SquashHandler synchronizes Requirements and Scripted Test Cases from the local SQLite database to Squash TM.
    /// </summary>
    public sealed class SquashHandler
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;
        private readonly string _projectName;
        private readonly string _description;
        private readonly string _jiraBaseUrl;

        // Injected UI progress reporter: (done, total, percent)
        private readonly Action<string,int, int, double>? _progress;

        // Configurable main roots loaded from squash.conf (single root per project)
        private string? _reqHierarchyRootKey;
        private string? _tstHierarchyRootKey;

        // Caches for existing entities by cf_jiraref (Jira KEY)
        private readonly Dictionary<string, int> _tcIdByJiraRef = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _tcFolderIdByJiraRef = new(StringComparer.OrdinalIgnoreCase);

        // At fields section (near _tcIdByJiraRef/_tcFolderIdByJiraRef)
        private readonly Dictionary<string, int> _reqIdByJiraRef = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _reqFolderIdByJiraRef = new(StringComparer.OrdinalIgnoreCase);

        private const string DefaultProjectName = "Monovera";
        private const string RequirementsRootFolderName = "Requirements";
        private const string TestsRootFolderName = "Tests";

        public SquashHandler(string baseUrl, string apiToken, string jiraBaseURL, string projectName = DefaultProjectName, Action<string,int, int, double>? progress = null)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)) throw new ArgumentException("Squash baseUrl is required.");
            if (string.IsNullOrWhiteSpace(apiToken)) throw new ArgumentException("Squash API token is required.");

            _baseUrl = baseUrl.TrimEnd('/');
            _projectName = projectName;
            _progress = progress;
            _jiraBaseUrl = jiraBaseURL;

            _http = new HttpClient();
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            // Load main roots from squash.conf if present
            LoadSquashRootsFromConf();
        }

        /// <summary>
        /// Main synchronization entry point.
        /// </summary>
        public async Task UpdateSquashAsync(CancellationToken ct = default)
        {
            // 1) Ensure project
            int projectId = await EnsureProjectAsync(_projectName, ct).ConfigureAwait(false);

            // 2) Ensure root folders
            int reqRootFolderId = await EnsureRequirementRootFolderAsync(projectId, ct).ConfigureAwait(false);
            int tstRootFolderId = await EnsureTestCaseRootFolderAsync(projectId, ct).ConfigureAwait(false);

            // 2.5) Prefetch caches for efficient lookups
            await LoadAllTestCaseIdsByJiraRefAsync(ct).ConfigureAwait(false);
            await LoadAllTestCaseFolderIdsByJiraRefAsync(projectId, ct).ConfigureAwait(false);

            await LoadAllRequirementIdsByJiraRefAsync(ct).ConfigureAwait(false);
            await LoadAllRequirementFolderIdsByJiraRefAsync(projectId, ct).ConfigureAwait(false);

            // 3) Load DB issues
            var allIssues = await LoadAllIssuesAsync(ct).ConfigureAwait(false);

            // Split by project code
            var reqIssuesRaw = allIssues
                .Where(p => string.Equals(p.Value.ProjectCode, "REQ", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);

            var tstIssuesRaw = allIssues
                .Where(p => string.Equals(p.Value.ProjectCode, "TST", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);

            // Enforce ancestry to configured main roots
            var reqIssues = FilterIssuesToHierarchyRoot(reqIssuesRaw, _reqHierarchyRootKey);
            var tstIssues = FilterIssuesToHierarchyRoot(tstIssuesRaw, _tstHierarchyRootKey);

            var reqChildren = BuildChildrenMap(reqIssues);
            var tstChildren = BuildChildrenMap(tstIssues);

            // Progress: Test cases
            var totalTcLeaves = tstIssues.Values.Count(i => !IsFolderNode(i));
            int doneTcLeaves = 0;
            ReportPhaseProgress("Syncing test cases", doneTcLeaves, totalTcLeaves);

            await UpsertTestCaseHierarchyAsync(
                projectId, tstRootFolderId, tstIssues, tstChildren, ct,
                onLeafUpserted: () =>
                {
                    doneTcLeaves++;
                    ReportPhaseProgress("Syncing test cases", doneTcLeaves, totalTcLeaves);
                }).ConfigureAwait(false);

            // Progress: Requirements
            var totalReqLeaves = reqIssues.Values.Count(i => !IsFolderNode(i));
            int doneReqLeaves = 0;
            ReportPhaseProgress("Syncing requirements", doneReqLeaves, totalReqLeaves);

            await UpsertRequirementHierarchyAsync(
                projectId, reqRootFolderId, reqIssues, reqChildren, ct,
                onLeafUpserted: () =>
                {
                    doneReqLeaves++;
                    ReportPhaseProgress("Syncing requirements", doneReqLeaves, totalReqLeaves);
                }).ConfigureAwait(false);

            // Progress: Coverage (count per test case leaf)
            int totalCoverage = tstIssues.Values.Count(i => !IsFolderNode(i));
            int doneCoverage = 0;
            ReportPhaseProgress("Syncing coverage", doneCoverage, totalCoverage);

            await SyncAllCoverageByJiraRefAsync(
                reqIssues, tstIssues, ct,
                onTestCoverageSynced: () =>
                {
                    doneCoverage++;
                    ReportPhaseProgress("Syncing coverage", doneCoverage, totalCoverage);
                }).ConfigureAwait(false);

            // Final 100% marker
            ReportPhaseProgress("Sync complete", 1, 1);
        }

        #region Database loaders and helpers

        private sealed class DbIssueRow
        {
            public string Key { get; init; } = "";
            public string Summary { get; init; } = "";
            public string Description { get; init; } = "";
            public string IssueType { get; init; } = "";
            public string ParentKey { get; init; } = "";
            public string ProjectCode { get; init; } = "";
            public List<string> Children { get; init; } = new();
            public List<string> Related { get; init; } = new();
        }

        private async Task<Dictionary<string, DbIssueRow>> LoadAllIssuesAsync(CancellationToken ct)
        {
            var dict = new Dictionary<string, DbIssueRow>(StringComparer.OrdinalIgnoreCase);
            string connStr = $"Data Source={frmMain.DatabasePath};";

            await using var conn = new Microsoft.Data.Sqlite.SqliteConnection(connStr);
            await conn.OpenAsync(ct).ConfigureAwait(false);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT KEY, SUMMARY, DESCRIPTION, ISSUETYPE, PARENTKEY, CHILDRENKEYS, RELATESKEYS, PROJECTCODE
                FROM issue";
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                var key = r.IsDBNull(0) ? "" : r.GetString(0);
                if (string.IsNullOrWhiteSpace(key)) continue;

                var childrenCsv = r.IsDBNull(5) ? "" : r.GetString(5);
                var children = string.IsNullOrWhiteSpace(childrenCsv)
                    ? new List<string>()
                    : childrenCsv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                var relatesCsv = r.IsDBNull(6) ? "" : r.GetString(6);
                var related = string.IsNullOrWhiteSpace(relatesCsv)
                    ? new List<string>()
                    : relatesCsv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                var row = new DbIssueRow
                {
                    Key = key,
                    Summary = r.IsDBNull(1) ? "" : r.GetString(1),
                    Description = r.IsDBNull(2) ? "" : r.GetString(2),
                    IssueType = r.IsDBNull(3) ? "" : r.GetString(3),
                    ParentKey = r.IsDBNull(4) ? "" : r.GetString(4),
                    Children = children,
                    Related = related,
                    ProjectCode = r.IsDBNull(7) ? "" : r.GetString(7)
                };

                dict[key] = row;
            }

            return dict;
        }

        private static Dictionary<string, List<string>> BuildChildrenMap(Dictionary<string, DbIssueRow> issues)
        {
            var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in issues.Values)
            {
                if (row.Children.Count == 0) continue;
                if (!map.TryGetValue(row.Key, out var list))
                {
                    list = new List<string>();
                    map[row.Key] = list;
                }
                foreach (var c in row.Children)
                {
                    if (issues.ContainsKey(c) && !list.Contains(c, StringComparer.OrdinalIgnoreCase))
                        list.Add(c);
                }
            }

            foreach (var row in issues.Values)
            {
                if (string.IsNullOrWhiteSpace(row.ParentKey) || !issues.ContainsKey(row.ParentKey)) continue;
                if (!map.TryGetValue(row.ParentKey, out var list))
                {
                    list = new List<string>();
                    map[row.ParentKey] = list;
                }
                if (!list.Contains(row.Key, StringComparer.OrdinalIgnoreCase))
                    list.Add(row.Key);
            }

            foreach (var k in map.Keys.ToList())
                map[k] = map[k].OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();

            return map;
        }

        private static bool IsFolderNode(DbIssueRow node)
        {
            return node.IssueType.Equals("Project", StringComparison.OrdinalIgnoreCase)
                || node.IssueType.Equals("Folder", StringComparison.OrdinalIgnoreCase)
                || node.IssueType.Equals("Menu", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildRelatedPlainText(IEnumerable<string> relatedKeys)
        {
            var list = relatedKeys?.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (list == null || list.Count == 0) return "";
            return " | Related: " + string.Join(", ", list);
        }

        private static string BuildRequirementDescriptionHtml(DbIssueRow node)
        {
            string desc = System.Net.WebUtility.HtmlEncode(node.Description ?? string.Empty).Replace("\n", "<br/>");
            var sb = new StringBuilder();
            sb.Append($"<p>Reference: {node.Key}</p>");
            if (!string.IsNullOrWhiteSpace(desc))
                sb.Append($"<div>{desc}</div>");
            string related = BuildRelatedPlainText(node.Related);
            if (!string.IsNullOrWhiteSpace(related))
                sb.Append($"<div>{related}</div>");
            return sb.ToString();
        }

        // Clean plain-text BDD script from HTML description content
        private static string BuildBddScriptFromDescription(string description)
        {
            if (string.IsNullOrWhiteSpace(description))
                return "# To be added";

            string decoded = System.Net.WebUtility.HtmlDecode(description);
            decoded = Regex.Replace(decoded, @"<\s*br\s*/?\s*>", "\n", RegexOptions.IgnoreCase);
            decoded = Regex.Replace(decoded, @"</\s*p\s*>", "\n", RegexOptions.IgnoreCase);
            decoded = Regex.Replace(decoded, @"<[^>]+>", string.Empty);
            decoded = decoded.Replace("\r\n", "\n").Replace("\r", "\n");

            var lines = decoded.Split('\n');
            var sb = new StringBuilder();
            bool lastBlank = false;
            foreach (var line in lines)
            {
                var trimmed = line.TrimEnd();
                bool isBlank = string.IsNullOrWhiteSpace(trimmed);
                if (isBlank && lastBlank) continue;
                sb.AppendLine(trimmed);
                lastBlank = isBlank;
            }

            string result = sb.ToString().Trim();
            return string.IsNullOrWhiteSpace(result) ? "# To be added" : result;
        }

        // Load root keys from squash.conf (key=value lines)
        private void LoadSquashRootsFromConf()
        {
            try
            {
                var candidates = new[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "squash.conf"),
                    Path.Combine(Environment.CurrentDirectory, "squash.conf")
                };
                var confPath = candidates.FirstOrDefault(File.Exists);
                if (confPath is null) return;

                foreach (var raw in File.ReadAllLines(confPath))
                {
                    var line = raw?.Trim();
                    if (string.IsNullOrEmpty(line)) continue;
                    if (line.StartsWith("#") || line.StartsWith("//")) continue;

                    var idx = line.IndexOf('=');
                    if (idx <= 0) continue;

                    var key = line.Substring(0, idx).Trim();
                    var val = line.Substring(idx + 1).Trim().Trim('"').Trim('\'');

                    if (key.Equals("SQAUSH_REQ_ROOT", StringComparison.OrdinalIgnoreCase))
                        _reqHierarchyRootKey = val;
                    else if (key.Equals("SQAUSH_TST_ROOT", StringComparison.OrdinalIgnoreCase))
                        _tstHierarchyRootKey = val;
                }
            }
            catch
            {
                // Non-fatal; filtering will be skipped if roots aren't loaded.
            }
        }

        // Keep only issues whose ancestry reaches the given rootKey.
        private static Dictionary<string, DbIssueRow> FilterIssuesToHierarchyRoot(
            Dictionary<string, DbIssueRow> issues,
            string? rootKey)
        {
            if (string.IsNullOrWhiteSpace(rootKey))
            {
                return issues;
            }

            if (!issues.ContainsKey(rootKey))
            {
                return new Dictionary<string, DbIssueRow>(StringComparer.OrdinalIgnoreCase);
            }

            var parentByChild = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in issues)
            {
                var child = kvp.Key;
                var parent = kvp.Value.ParentKey ?? "";
                if (!string.IsNullOrWhiteSpace(parent))
                {
                    parentByChild[child] = parent;
                }
            }

            bool ReachesRoot(string key)
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var cur = key;
                while (!string.IsNullOrWhiteSpace(cur))
                {
                    if (!seen.Add(cur)) break;
                    if (string.Equals(cur, rootKey, StringComparison.OrdinalIgnoreCase)) return true;
                    if (!parentByChild.TryGetValue(cur, out var p)) break;
                    cur = p;
                }
                return false;
            }

            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { rootKey };
            foreach (var k in issues.Keys)
            {
                if (string.Equals(k, rootKey, StringComparison.OrdinalIgnoreCase)) continue;
                if (ReachesRoot(k)) allowed.Add(k);
            }

            var pruned = new Dictionary<string, DbIssueRow>(StringComparer.OrdinalIgnoreCase);
            foreach (var k in allowed)
            {
                if (issues.TryGetValue(k, out var row))
                    pruned[k] = row;
            }
            return pruned;
        }

        #endregion

        #region Project and root folders

        private async Task<int> EnsureProjectAsync(string name, CancellationToken ct)
        {
            ReportPhaseProgress("Ensuring project root folders...", 100, 100);

            int id = await FindProjectIdByNameAsync(name, ct).ConfigureAwait(false);
            if (id > 0) return id;

            var payload = new
            {
                _type = "project",
                name = name,
                label = "",
                description = ""
            };

            await PostAsync("/projects", payload, ct).ConfigureAwait(false);

            ReportPhaseProgress("Ensuring requirement root folders - Completed!", 100, 100);

            return await FindProjectIdByNameAsync(name, ct).ConfigureAwait(false);
        }

        private async Task<int> FindProjectIdByNameAsync(string name, CancellationToken ct)
        {
            var res = await GetAsync("/projects", ct).ConfigureAwait(false);
            var json = JObject.Parse(res);
            var projects = (JArray)json["_embedded"]?["projects"] ?? new JArray();

            foreach (JObject p in projects)
            {
                if (string.Equals((string)p["name"], name, StringComparison.OrdinalIgnoreCase))
                {
                    return (int)p["id"];
                }
            }
            return -1;
        }

        private async Task<JArray> GetRequirementFolderTreeAsync(int projectId, CancellationToken ct)
        {
            var res = await GetAsync($"/requirement-folders/tree/{projectId}", ct).ConfigureAwait(false);
            return JArray.Parse(res);
        }

        private async Task<JArray> GetTestCaseFolderTreeAsync(int projectId, CancellationToken ct)
        {
            var res = await GetAsync($"/test-case-folders/tree/{projectId}", ct).ConfigureAwait(false);
            return JArray.Parse(res);
        }

        private static JObject? FindFolderByName(JObject projectNode, string folderName)
        {
            var acc = new List<JObject>();
            void Walk(JArray children)
            {
                foreach (JObject f in children)
                {
                    acc.Add(f);
                    if (f["children"] is JArray sub && sub.Count > 0)
                    {
                        Walk(sub);
                    }
                }
            }

            var rootChildren = (JArray)projectNode["folders"] ?? new JArray();
            Walk(rootChildren);

            foreach (var f in acc)
            {
                if (string.Equals((string)f["name"], folderName, StringComparison.OrdinalIgnoreCase))
                    return f;
            }
            return null;
        }

        private async Task<int> EnsureRequirementRootFolderAsync(int projectId, CancellationToken ct)
        {
            ReportPhaseProgress("Ensuring requirement root folders...", 100, 100);

            var tree = await GetRequirementFolderTreeAsync(projectId, ct).ConfigureAwait(false);
            var projectNode = (JObject)tree[0];
            var existing = FindFolderByName(projectNode, RequirementsRootFolderName);
            if (existing != null) return (int)existing["id"];

            var payload = new
            {
                _type = "requirement-folder",
                name = RequirementsRootFolderName,
                description = $"Root folder for requirements | Project: {_projectName}",
                parent = new { _type = "project", id = projectId }
            };

            await PostAsync("/requirement-folders", payload, ct).ConfigureAwait(false);

            tree = await GetRequirementFolderTreeAsync(projectId, ct).ConfigureAwait(false);
            projectNode = (JObject)tree[0];
            var after = FindFolderByName(projectNode, RequirementsRootFolderName);

            ReportPhaseProgress("Ensuring requirement root folders - Completed!", 100, 100);

            return after != null ? (int)after["id"] : 0;
        }

        private async Task<int> EnsureTestCaseRootFolderAsync(int projectId, CancellationToken ct)
        {
            ReportPhaseProgress("Ensuring test case root folders...", 100, 100);

            var tree = await GetTestCaseFolderTreeAsync(projectId, ct).ConfigureAwait(false);
            var projectNode = (JObject)tree[0];
            var existing = FindFolderByName(projectNode, TestsRootFolderName);
            if (existing != null) return (int)existing["id"];

            var payload = new
            {
                _type = "test-case-folder",
                name = TestsRootFolderName,
                description = $"Root folder for scripted test cases | Project: {_projectName}",
                parent = new { _type = "project", id = projectId }
            };

            await PostAsync("/test-case-folders", payload, ct).ConfigureAwait(false);

            tree = await GetTestCaseFolderTreeAsync(projectId, ct).ConfigureAwait(false);
            projectNode = (JObject)tree[0];
            var after = FindFolderByName(projectNode, TestsRootFolderName);

            ReportPhaseProgress("Ensuring test case root folders - Completed!", 100, 100);

            return after != null ? (int)after["id"] : 0;
        }

        private async Task<int> EnsureTestCaseFolderAsync(
    int projectId,
    string name,
    int parentId,
    string parentType,
    string issueKey,
    string issueDescription,
    IEnumerable<string> relatedKeys,
    CancellationToken ct)
        {
            // Try resolve by cf_jiraref cache first
            var existingId = await FindTestCaseFolderIdByJiraRefAsync(issueKey, projectId, ct).ConfigureAwait(false);

            if (existingId > 0)
            {
                // Update without reloading the whole cache
                var patchPayload = new
                {
                    _type = "test-case-folder",
                    name = name,
                    description = issueDescription,
                    parent = new { _type = parentType, id = parentId },
                    custom_fields = new object[]
                    {
                        new { code = "cf_jiraref", value = issueKey },
                        new { code = "cf_jirarefurl", value = $"<a href='{_jiraBaseUrl}/browse/{issueKey}'>{_jiraBaseUrl}/browse/{issueKey}</a>" }
                    }
                };

                await PatchAsync($"/test-case-folders/{existingId}", patchPayload, ct).ConfigureAwait(false);

                // Keep cache consistent
                _tcFolderIdByJiraRef[issueKey] = existingId;
                return existingId;
            }

            // Create new and set cf_jiraref
            var createPayload = new
            {
                _type = "test-case-folder",
                name = name,
                description = issueDescription,
                parent = new { _type = parentType, id = parentId },
                custom_fields = new object[]
                {
            new { code = "cf_jiraref", value = issueKey },
            new { code = "cf_jirarefurl", value = $"<a href='{_jiraBaseUrl}/browse/{issueKey}'>{_jiraBaseUrl}/browse/{issueKey}</a>" }
                }
            };

            // Avoid full cache reload: parse return body to get new id
            var body = await PostAsync("/test-case-folders", createPayload, ct).ConfigureAwait(false);

            try
            {
                var obj = JObject.Parse(body);
                var newId = (int?)obj["id"] ?? 0;
                if (newId > 0)
                {
                    _tcFolderIdByJiraRef[issueKey] = newId;
                    return newId;
                }
            }
            catch
            {
                // If the API doesn’t return the entity body or id is missing, fall back once to a targeted GET of the created folder.
                // DO NOT reload the entire tree; instead, try a single GET using the parent folder to minimize cost.
            }

            // Fallback: single GET of the parent’s immediate children to find our newly created folder (lightweight compared to full tree)
            try
            {
                // Some Squash instances expose children listing; if unavailable, you may need to call the detail GET by Location header.
                // If Location header is needed, consider enhancing PostAsync to return headers too.
                var parentTree = await GetAsync($"/test-case-folders/{parentId}", ct).ConfigureAwait(false);
                var parentObj = JObject.Parse(parentTree);
                var children = (JArray?)parentObj["_embedded"]?["folders"] ?? new JArray();
                foreach (JObject f in children)
                {
                    var id = (int?)f["id"] ?? 0;
                    var cufs = (JArray?)f["custom_fields"];
                    if (id > 0 && cufs is not null)
                    {
                        foreach (JObject cf in cufs)
                        {
                            var code = (string?)cf["code"];
                            var val = cf["value"]?.ToString();
                            if (string.Equals(code, "cf_jiraref", StringComparison.OrdinalIgnoreCase) &&
                                string.Equals(val, issueKey, StringComparison.OrdinalIgnoreCase))
                            {
                                _tcFolderIdByJiraRef[issueKey] = id;
                                return id;
                            }
                        }
                    }
                }
            }
            catch
            {
                // swallow and fall through
            }

            // As a last resort, keep operating without id (will resolve on next global prefetch run)
            return 0;
        }

        #endregion

        #region Requirements upsert

        private async Task UpsertRequirementHierarchyAsync(
            int projectId,
            int reqRootFolderId,
            Dictionary<string, DbIssueRow> issues,
            Dictionary<string, List<string>> childrenMap,
            CancellationToken ct,
            Action? onLeafUpserted = null)
        {
            var roots = issues.Values
                .Where(i => string.IsNullOrWhiteSpace(i.ParentKey) || !issues.ContainsKey(i.ParentKey))
                .OrderBy(i => i.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var root in roots)
            {
                await UpsertRequirementNodeDfsAsync(
                    projectId,
                    root.Key,
                    "requirement-folder",
                    reqRootFolderId,
                    issues,
                    childrenMap,
                    ct,
                    onLeafUpserted).ConfigureAwait(false);
            }
        }

        private async Task UpsertRequirementNodeDfsAsync(
            int projectId,
            string nodeKey,
            string parentType,
            int parentId,
            Dictionary<string, DbIssueRow> issues,
            Dictionary<string, List<string>> childrenMap,
            CancellationToken ct,
            Action? onLeafUpserted = null)
        {
            if (!issues.TryGetValue(nodeKey, out var node)) return;

            string displayName = Chop($"{node.Key} {node.Summary}".Trim());

            if (IsFolderNode(node))
            {
                int folderId = await EnsureRequirementFolderAsync(projectId, displayName, parentId, parentType, node.Key, node.Related, ct).ConfigureAwait(false);
                if (childrenMap.TryGetValue(node.Key, out var childKeys))
                {
                    foreach (var ck in childKeys)
                    {
                        await UpsertRequirementNodeDfsAsync(projectId, ck, "requirement-folder", folderId, issues, childrenMap, ct, onLeafUpserted).ConfigureAwait(false);
                    }
                }
            }
            else
            {
                // Resolve existing requirement by cf_jiraref via cache
                int reqId = await FindRequirementIdByJiraRefAsync(node.Key, ct).ConfigureAwait(false);

                string descriptionHtml = node.Description;// BuildRequirementDescriptionHtml(node);

                var currentVersion = new
                {
                    _type = "requirement-version",
                    name = displayName,
                    description = descriptionHtml,
                    status = "WORK_IN_PROGRESS",
                    category = new { code = "CAT_FUNCTIONAL" },
                    custom_fields = new object[]
                    {
                        new { code = "cf_jiraref", value = node.Key },
                        new { code = "cf_jirarefurl", value = $"{_jiraBaseUrl}/browse/{node.Key}" }
                    }
                };

                if (reqId > 0)
                {
                    // PATCH minimal payload with current_version to enforce cf_jiraref
                    var patchPayload = new
                    {
                        _type = "requirement",
                        current_version = currentVersion,
                        parent = new { _type = "requirement-folder", id = parentId }
                    };
                    await PatchAsync($"/requirements/{reqId}", patchPayload, ct).ConfigureAwait(false);
                }
                else
                {
                    // POST new with parent and cf_jiraref
                    var createPayload = new
                    {
                        _type = "requirement",
                        current_version = currentVersion,
                        parent = new { _type = "requirement-folder", id = parentId }
                    };
                    await PostAsync("/requirements", createPayload, ct).ConfigureAwait(false);

                    // Update cache for fast future lookups
                    reqId = await FindRequirementIdByJiraRefAsync(node.Key, ct).ConfigureAwait(false);
                    if (reqId > 0) _reqIdByJiraRef[node.Key] = reqId;
                }

                onLeafUpserted?.Invoke();
            }
        }

        // Replace existing FindRequirementIdByJiraRefAsync with:
        private Task<int> FindRequirementIdByJiraRefAsync(string jiraRef, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(jiraRef))
                return Task.FromResult(-1);
            if (_reqIdByJiraRef.TryGetValue(jiraRef, out var id))
                return Task.FromResult(id);
            return Task.FromResult(-1);
        }

        #endregion

        #region Test cases upsert (scripted)

        private async Task UpsertTestCaseHierarchyAsync(
            int projectId,
            int tstRootFolderId,
            Dictionary<string, DbIssueRow> issues,
            Dictionary<string, List<string>> childrenMap,
            CancellationToken ct,
            Action? onLeafUpserted = null)
        {
            var roots = issues.Values
                .Where(i => string.IsNullOrWhiteSpace(i.ParentKey) || !issues.ContainsKey(i.ParentKey))
                .OrderBy(i => i.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var root in roots)
            {
                await UpsertTestCaseNodeDfsAsync(
                    projectId,
                    root.Key,
                    "test-case-folder",
                    tstRootFolderId,
                    issues,
                    childrenMap,
                    ct,
                    onLeafUpserted).ConfigureAwait(false);
            }
        }

        private async Task UpsertTestCaseNodeDfsAsync(
    int projectId,
    string nodeKey,
    string parentType,
    int parentId,
    Dictionary<string, DbIssueRow> issues,
    Dictionary<string, List<string>> childrenMap,
    CancellationToken ct,
    Action? onLeafUpserted = null)
        {
            if (!issues.TryGetValue(nodeKey, out var node)) return;

            string displayName = Chop($"[{node.Key}] {node.Summary}".Trim());

            if (IsFolderNode(node))
            {
                // Ensure/Update test case folder (already writes cf_jiraref)
                int folderId = await EnsureTestCaseFolderAsync(
                    projectId,
                    displayName,
                    parentId,
                    parentType,
                    node.Key,
                    node.Description,
                    node.Related,
                    ct).ConfigureAwait(false);

                if (childrenMap.TryGetValue(node.Key, out var childKeys))
                {
                    foreach (var ck in childKeys)
                        await UpsertTestCaseNodeDfsAsync(projectId, ck, "test-case-folder", folderId, issues, childrenMap, ct, onLeafUpserted).ConfigureAwait(false);
                }
                return;
            }

            // Resolve existing test case by cf_jiraref using prefetch cache
            if (!_tcIdByJiraRef.TryGetValue(node.Key, out var tcId))
            {
                tcId = await FindTestCaseIdByJiraRefAsync(node.Key, ct).ConfigureAwait(false);
                if (tcId > 0) _tcIdByJiraRef[node.Key] = tcId;
            }

            string script = BuildBddScriptFromDescription(node.Description);

            if (tcId > 0)
            {
                // PATCH existing test case (minimal body + ensure cf_jiraref under current_version)
                var patchPayload = new
                {
                    _type = "scripted-test-case",
                    name = displayName,
                    script = script,
                    parent = new { _type = "test-case-folder", id = parentId },
                    custom_fields = new object[]
                                                {
                                                    new
                                                    {
                                                        code = "cf_jiraref",
                                                        value = node.Key
                                                    },
                                                    new
                                                    {
                                                        code = "cf_jirarefurl",
                                                        value = $"<a href='{_jiraBaseUrl}/browse/{node.Key}'>{_jiraBaseUrl}/browse/{node.Key}</a>"
                                                    }
                                                }
                };
                await PatchAsync($"/test-cases/{tcId}", patchPayload, ct).ConfigureAwait(false);
            }
            else
            {
                // CREATE with parent and cf_jiraref under current_version
                var createPayload = new
                {
                    _type = "scripted-test-case",
                    name = displayName,
                    importance = "MEDIUM",
                    parent = new { _type = "test-case-folder", id = parentId },
                    script = script,
                    nature = new { code = "NAT_FUNCTIONAL_TESTING" },
                    type = new { code = "TYP_REGRESSION_TESTING" },
                    custom_fields = new object[]
                                                 {
                                                    new
                                                    {
                                                        code = "cf_jiraref",
                                                        value = node.Key
                                                    },
                                                    new
                                                    {
                                                        code = "cf_jirarefurl",
                                                        value = $"<a href='{_jiraBaseUrl}/browse/{node.Key}'>{_jiraBaseUrl}/browse/{node.Key}</a>"
                                                    }
                                                }
                };

                await PostAsync("/test-cases", createPayload, ct).ConfigureAwait(false);

                // Refresh cache entry for this key
                var newId = await FindTestCaseIdByJiraRefAsync(node.Key, ct).ConfigureAwait(false);
                if (newId > 0) _tcIdByJiraRef[node.Key] = newId;
            }

            onLeafUpserted?.Invoke();
        }

        #endregion

        #region Coverage synchronization (TST -> REQs) using cf_jiraref

        private async Task SyncAllCoverageByJiraRefAsync(
            Dictionary<string, DbIssueRow> reqIssues,
            Dictionary<string, DbIssueRow> tstIssues,
            CancellationToken ct,
            Action? onTestCoverageSynced = null)
        {
            var reqIdByKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var req in reqIssues.Values)
            {
                int id = await FindRequirementIdByJiraRefAsync(req.Key, ct).ConfigureAwait(false);
                if (id > 0) reqIdByKey[req.Key] = id;
            }

            foreach (var tst in tstIssues.Values)
            {
                int testId = await FindTestCaseIdByJiraRefAsync(tst.Key, ct).ConfigureAwait(false);
                if (testId <= 0)
                {
                    onTestCoverageSynced?.Invoke();
                    continue;
                }

                var desiredReqIds = tst.Related
                    .Where(k => reqIdByKey.ContainsKey(k))
                    .Select(k => reqIdByKey[k])
                    .Distinct()
                    .ToList();

                await SyncTestCaseCoverageAsync(testId, desiredReqIds, ct).ConfigureAwait(false);
                onTestCoverageSynced?.Invoke();
            }
        }

        private async Task SyncTestCaseCoverageAsync(int testCaseId, List<int> requirementIds, CancellationToken ct)
        {
            var payload = new { requirements = requirementIds };
            await PutAsync($"/test-cases/{testCaseId}/verified-requirements", payload, ct).ConfigureAwait(false);
        }

        #endregion

        #region HTTP helpers and caches

        private void ReportPhaseProgress(string phase, int done, int total)
        {
            double percent = total <= 0 ? 100.0 : (double)done / total * 100.0;
            _progress?.Invoke(phase, done, total, percent);
        }

        private async Task<string> GetAsync(string relative, CancellationToken ct)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, _baseUrl + relative);
            using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
            res.EnsureSuccessStatusCode();
            return await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }

        private async Task<string> PostAsync(string relative, object payload, CancellationToken ct)
        {
            var settings = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };
            string json = JsonConvert.SerializeObject(payload, settings);

            using var req = new HttpRequestMessage(HttpMethod.Post, _baseUrl + relative)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            req.Headers.Accept.Clear();
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/hal+json"));

            using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!res.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"POST {relative} failed {(int)res.StatusCode}: {body}\nPayload: {json}");
            }
            return body;
        }

        private async Task<string> PatchAsync(string relative, object payload, CancellationToken ct)
        {
            var settings = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };
            string json = JsonConvert.SerializeObject(payload, settings);

            var method = new HttpMethod("PATCH");
            using var req = new HttpRequestMessage(method, _baseUrl + relative)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            req.Headers.Accept.Clear();
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/hal+json"));

            using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"PATCH {relative} failed {(int)res.StatusCode}: {body}\nPayload: {json}");
            }
            return body;
        }

        private async Task<string> PutAsync(string relative, object payload, CancellationToken ct)
        {
            var settings = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };
            string json = JsonConvert.SerializeObject(payload, settings);

            using var req = new HttpRequestMessage(HttpMethod.Put, _baseUrl + relative)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            req.Headers.Accept.Clear();
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/hal+json"));

            using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"PUT {relative} failed {(int)res.StatusCode}: {body}\nPayload: {json}");
            }
            return body;
        }

        // Prefetch all scripted test cases and index by cf_jiraref
        private async Task LoadAllTestCaseIdsByJiraRefAsync(CancellationToken ct)
        {
            _tcIdByJiraRef.Clear();

            string nextUrl = "/test-cases?size=200&page=0";
            int totalPages = -1;
            int currentPage = 0;

            while (!string.IsNullOrWhiteSpace(nextUrl))
            {
                var res = await GetAsync(nextUrl, ct).ConfigureAwait(false);
                var json = JObject.Parse(res);

                var pageObj = (JObject?)json["page"];
                int size = pageObj?.Value<int?>("size") ?? 20;
                currentPage = pageObj?.Value<int?>("number") ?? currentPage;
                totalPages = pageObj?.Value<int?>("totalPages") ?? totalPages;

                var arr = (JArray?)json["_embedded"]?["test-cases"] ?? new JArray();
                int totalItemsThisPage = arr.Count;
                int doneItemsThisPage = 0;

                foreach (JObject t in arr)
                {
                    int id = t.Value<int>("id");

                    var name = t.Value<string>("name");
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        var m = Regex.Match(name, @"\[(?<key>[A-Z]+-\d+)\]", RegexOptions.IgnoreCase);
                        if (m.Success)
                        {
                            var k = m.Groups["key"].Value;
                            if (!string.IsNullOrWhiteSpace(k))
                                _tcIdByJiraRef[k] = id;
                        }
                    }

                    var currentVersion = (JObject?)t["current_version"];
                    var cufs = (JArray?)currentVersion?["custom_fields"];
                    if (cufs is not null)
                    {
                        foreach (JObject cf in cufs)
                        {
                            var code = (string?)cf["code"];
                            if (!string.Equals(code, "cf_jiraref", StringComparison.OrdinalIgnoreCase))
                                continue;

                            var valueToken = cf["value"];
                            var value = valueToken?.Type == JTokenType.Array
                                ? string.Join(",", ((JArray)valueToken!).Select(v => v?.ToString()))
                                : valueToken?.ToString();

                            if (!string.IsNullOrWhiteSpace(value))
                                _tcIdByJiraRef[value] = id;
                        }
                    }

                    doneItemsThisPage++;
                    ReportPhaseProgress("Indexing test cases (items)", doneItemsThisPage, totalItemsThisPage);
                }

                if (totalPages > 0)
                    ReportPhaseProgress("Indexing test cases (pages)", currentPage + 1, totalPages);

                var nextLink = (string?)json["_links"]?["next"]?["href"];
                if (!string.IsNullOrWhiteSpace(nextLink))
                {
                    nextUrl = nextLink.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                        ? nextLink.Replace(_baseUrl, "", StringComparison.OrdinalIgnoreCase)
                        : nextLink;
                }
                else if (totalPages >= 0 && currentPage + 1 < totalPages)
                {
                    nextUrl = $"/test-cases?size={size}&page={currentPage + 1}";
                }
                else
                {
                    nextUrl = null;
                }
            }

            if (totalPages > 0)
                ReportPhaseProgress("Indexing test cases (pages)", totalPages, totalPages);
        }

        // Replace FindTestCaseIdByJiraRefAsync with a cache-only version for O(1) lookups
        public Task<int> FindTestCaseIdByJiraRefAsync(string jiraRef, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(jiraRef))
                return Task.FromResult(-1);

            if (_tcIdByJiraRef.TryGetValue(jiraRef, out var id))
                return Task.FromResult(id);

            // Optional: try name-based pattern if not in cache (handles cases where cf_jiraref is missing but name has [KEY])
            // This is a best-effort fallback using the existing cache keys gathered from names during prefetch.
            // If absent, return -1 without scanning the server again.
            foreach (var kvp in _tcIdByJiraRef)
            {
                if (string.Equals(kvp.Key, jiraRef, StringComparison.OrdinalIgnoreCase))
                    return Task.FromResult(kvp.Value);
            }

            return Task.FromResult(-1);
        }

        // Prefetch all test-case folders and index by cf_jiraref
        private async Task LoadAllTestCaseFolderIdsByJiraRefAsync(int projectId, CancellationToken ct)
        {
            _tcFolderIdByJiraRef.Clear();

            var tree = await GetTestCaseFolderTreeAsync(projectId, ct).ConfigureAwait(false);

            var ids = new List<int>();
            void Walk(JArray folders)
            {
                foreach (JObject f in folders)
                {
                    if (f["id"] != null)
                        ids.Add((int)f["id"]);
                    if (f["children"] is JArray sub && sub.Count > 0)
                        Walk(sub);
                }
            }

            if (tree.Count > 0 && tree[0] is JObject root)
            {
                var folders = (JArray?)root["folders"] ?? new JArray();
                Walk(folders);
            }

            int total = ids.Count == 0 ? 1 : ids.Count;
            int done = 0;

            foreach (var id in ids)
            {
                try
                {
                    var res = await GetAsync($"/test-case-folders/{id}", ct).ConfigureAwait(false);
                    var obj = JObject.Parse(res);
                    var cufs = (JArray?)obj["custom_fields"];
                    if (cufs is not null)
                    {
                        foreach (JObject cf in cufs)
                        {
                            var code = (string?)cf["code"];
                            var val = cf["value"]?.ToString();
                            if (string.Equals(code, "cf_jiraref", StringComparison.OrdinalIgnoreCase) &&
                                !string.IsNullOrWhiteSpace(val))
                            {
                                _tcFolderIdByJiraRef[val] = id;
                            }
                        }
                    }
                }
                catch
                {
                    // ignore inaccessible folders
                }
                finally
                {
                    done++;
                    ReportPhaseProgress("Indexing test case folders", done, total);
                }
            }

            ReportPhaseProgress("Indexing test case folders", total, total);
        }

        private Task<int> FindTestCaseFolderIdByJiraRefAsync(string jiraRef, int projectId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(jiraRef))
                return Task.FromResult(-1);
            if (_tcFolderIdByJiraRef.TryGetValue(jiraRef, out var id))
                return Task.FromResult(id);
            return Task.FromResult(-1);
        }

        private async Task LoadAllRequirementIdsByJiraRefAsync(CancellationToken ct)
        {
            _reqIdByJiraRef.Clear();

            string nextUrl = "/requirements?size=200&page=0";
            int totalPages = -1;
            int currentPage = 0;

            while (!string.IsNullOrWhiteSpace(nextUrl))
            {
                var res = await GetAsync(nextUrl, ct).ConfigureAwait(false);
                var json = JObject.Parse(res);

                var pageObj = (JObject?)json["page"];
                int size = pageObj?.Value<int?>("size") ?? 20;
                currentPage = pageObj?.Value<int?>("number") ?? currentPage;
                totalPages = pageObj?.Value<int?>("totalPages") ?? totalPages;

                var arr = (JArray?)json["_embedded"]?["requirements"] ?? new JArray();
                int totalItemsThisPage = arr.Count;
                int doneItemsThisPage = 0;

                foreach (JObject r in arr)
                {
                    int id = r.Value<int>("id");
                    var currentVersion = (JObject?)r["current_version"];
                    var cufs = (JArray?)currentVersion?["custom_fields"];
                    if (cufs is not null)
                    {
                        foreach (JObject cf in cufs)
                        {
                            var code = (string?)cf["code"];
                            if (!string.Equals(code, "cf_jiraref", StringComparison.OrdinalIgnoreCase))
                                continue;

                            var valueToken = cf["value"];
                            var value = valueToken?.Type == JTokenType.Array
                                ? string.Join(",", ((JArray)valueToken!).Select(v => v?.ToString()))
                                : valueToken?.ToString();

                            if (!string.IsNullOrWhiteSpace(value))
                                _reqIdByJiraRef[value] = id;
                        }
                    }

                    doneItemsThisPage++;
                    ReportPhaseProgress("Indexing requirements (items)", doneItemsThisPage, totalItemsThisPage);
                }

                if (totalPages > 0)
                    ReportPhaseProgress("Indexing requirements (pages)", currentPage + 1, totalPages);

                var nextLink = (string?)json["_links"]?["next"]?["href"];
                if (!string.IsNullOrWhiteSpace(nextLink))
                {
                    nextUrl = nextLink.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                        ? nextLink.Replace(_baseUrl, "", StringComparison.OrdinalIgnoreCase)
                        : nextLink;
                }
                else if (totalPages >= 0 && currentPage + 1 < totalPages)
                {
                    nextUrl = $"/requirements?size={size}&page={currentPage + 1}";
                }
                else
                {
                    nextUrl = null;
                }
            }

            if (totalPages > 0)
                ReportPhaseProgress("Indexing requirements (pages)", totalPages, totalPages);
        }

        private async Task LoadAllRequirementFolderIdsByJiraRefAsync(int projectId, CancellationToken ct)
        {
            _reqFolderIdByJiraRef.Clear();

            var tree = await GetRequirementFolderTreeAsync(projectId, ct).ConfigureAwait(false);

            var ids = new List<int>();
            void Walk(JArray folders)
            {
                foreach (JObject f in folders)
                {
                    if (f["id"] != null)
                        ids.Add((int)f["id"]);
                    if (f["children"] is JArray sub && sub.Count > 0)
                        Walk(sub);
                }
            }

            if (tree.Count > 0 && tree[0] is JObject root)
            {
                var folders = (JArray?)root["folders"] ?? new JArray();
                Walk(folders);
            }

            int total = ids.Count == 0 ? 1 : ids.Count;
            int done = 0;

            foreach (var id in ids)
            {
                try
                {
                    var res = await GetAsync($"/requirement-folders/{id}", ct).ConfigureAwait(false);
                    var obj = JObject.Parse(res);
                    var cufs = (JArray?)obj["custom_fields"];
                    if (cufs is not null)
                    {
                        foreach (JObject cf in cufs)
                        {
                            var code = (string?)cf["code"];
                            var val = cf["value"]?.ToString();
                            if (string.Equals(code, "cf_jiraref", StringComparison.OrdinalIgnoreCase) &&
                                !string.IsNullOrWhiteSpace(val))
                            {
                                _reqFolderIdByJiraRef[val] = id;
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore inaccessible folders
                }
                finally
                {
                    done++;
                    ReportPhaseProgress("Indexing requirement folders", done, total);
                }
            }

            ReportPhaseProgress("Indexing requirement folders", total, total);
        }

        // Optimized to use the requirement-folder cache first; no full tree reloads after POST/PATCH
        private async Task<int> EnsureRequirementFolderAsync(
            int projectId,
            string name,
            int parentId,
            string parentType,
            string issueKey,
            IEnumerable<string> relatedKeys,
            CancellationToken ct)
        {
            if (_reqFolderIdByJiraRef.TryGetValue(issueKey, out var existingId) && existingId > 0)
            {
                var patchPayload = new
                {
                    _type = "requirement-folder",
                    name = Chop(name),
                    description = $"Reference: {issueKey}" + BuildRelatedPlainText(relatedKeys),
                    parent = new { _type = parentType, id = parentId },
                    custom_fields = new object[]
                    {
                        new { code = "cf_jiraref", value = issueKey },
                        new { code = "cf_jirarefurl", value = $"<a href='{_jiraBaseUrl}/browse/{issueKey}'>{_jiraBaseUrl}/browse/{issueKey}</a>" }
                    }
                };
                await PatchAsync($"/requirement-folders/{existingId}", patchPayload, ct).ConfigureAwait(false);
                return existingId;
            }

            // Fallback: create and update cache using returned id
            var createPayload = new
            {
                _type = "requirement-folder",
                name = Chop(name),
                description = $"Reference: {issueKey}" + BuildRelatedPlainText(relatedKeys),
                parent = new { _type = parentType, id = parentId },
                custom_fields = new object[]
                {
                    new { code = "cf_jiraref", value = issueKey },
                    new { code = "cf_jirarefurl", value = $"<a href='{_jiraBaseUrl}/browse/{issueKey}'>{_jiraBaseUrl}/browse/{issueKey}</a>" }
                }
            };

            var body = await PostAsync("/requirement-folders", createPayload, ct).ConfigureAwait(false);

            try
            {
                var obj = JObject.Parse(body);
                var newId = (int?)obj["id"] ?? 0;
                if (newId > 0)
                {
                    _reqFolderIdByJiraRef[issueKey] = newId;
                    return newId;
                }
            }
            catch
            {
                // If the API doesn't echo the body, you could add a minimal fallback here if needed.
            }

            return 0;
        }
        #endregion

        #region Common
        static string Chop(string value, int max=255)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return value.Length <= max
                ? value
                : value.Substring(0, max);
        }
        #endregion
    }
}