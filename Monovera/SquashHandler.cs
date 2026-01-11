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

namespace Monovera
{
    /// <summary>
    /// SquashHandler synchronizes Requirements and Scripted Test Cases from the local SQLite database to Squash TM.
    /// - Ensures project and root folders exist
    /// - Builds hierarchy from local DB
    /// - Upserts requirements and test-case folders/cases
    /// - Links test coverage to requirements
    /// - Provides progress callbacks and file logging
    /// </summary>
    public sealed class SquashHandler
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;
        private readonly string _projectName;
        private readonly string _description;
        private readonly string _jiraBaseUrl;

        // UI progress reporter: (phase, done, total, percent)
        private readonly Action<string, int, int, double>? _progress;

        // Optional hierarchy roots loaded from squash.conf (single root per project)
        private string? _reqHierarchyRootKey;
        private string? _tstHierarchyRootKey;

        // Caches for existing entities by cf_jiraref (Jira KEY)
        private readonly Dictionary<string, int> _tcIdByJiraRef = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _tcFolderIdByJiraRef = new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, int> _reqIdByJiraRef = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _reqFolderIdByJiraRef = new(StringComparer.OrdinalIgnoreCase);

        // Cached HAL trees (loaded once; updated locally on create/patch to avoid refetch)
        private JArray? _reqFolderTreeCache;
        private JArray? _tstFolderTreeCache;

        private const string DefaultProjectName = "Monovera";
        private const string RequirementsRootFolderName = "Requirements";
        private const string TestsRootFolderName = "Tests";

        public SquashHandler(string baseUrl, string apiToken, string jiraBaseURL, string projectName = DefaultProjectName, Action<string, int, int, double>? progress = null)
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

            WriteLog("Ctor", $"Initialized SquashHandler for baseUrl='{_baseUrl}' project='{_projectName}'");
            LoadSquashRootsFromConf();
        }

        /// <summary>
        /// Find a folder node by id inside a cached HAL project tree.
        /// </summary>
        private static JObject? FindFolderNodeById(JObject projectNode, int id)
        {
            var q = new Queue<JObject>();
            var roots = (JArray?)projectNode["folders"] ?? new JArray();
            foreach (JObject f in roots) q.Enqueue(f);

            while (q.Count > 0)
            {
                var cur = q.Dequeue();
                if ((int?)cur["id"] == id) return cur;

                if (cur["children"] is JArray ch)
                {
                    foreach (JObject c in ch) q.Enqueue(c);
                }
            }
            return null;
        }

        /// <summary>
        /// Create a minimal HAL folder node used to update the local tree cache.
        /// </summary>
        private static JObject CreateFolderNode(int id, string name)
        {
            return new JObject
            {
                ["id"] = id,
                ["name"] = name,
                ["children"] = new JArray()
            };
        }

        /// <summary>
        /// Main synchronization entry point:
        /// 1. Ensure project and root folders
        /// 2. Prefetch caches (IDs and folders)
        /// 3. Load DB issues, build hierarchy
        /// 4. Upsert requirements and test cases
        /// 5. Sync coverage (TC -> REQ)
        /// </summary>
        public async Task UpdateSquashAsync(CancellationToken ct = default)
        {
            WriteLog("UpdateSquashAsync", $"Starting Squash TM synchronization...");

            // 1) Ensure project
            WriteLog("UpdateSquashAsync", $"Ensuring project '{_projectName}' exists...");
            int projectId = await EnsureProjectAsync(_projectName, ct).ConfigureAwait(false);

            // 2) Ensure root folders
            WriteLog("UpdateSquashAsync", $"Ensuring root folders exist...");
            int reqRootFolderId = await EnsureRequirementRootFolderAsync(projectId, ct).ConfigureAwait(false);

            WriteLog("UpdateSquashAsync", $"Ensuring test case root folders exist...");
            int tstRootFolderId = await EnsureTestCaseRootFolderAsync(projectId, ct).ConfigureAwait(false);

            // 2.5) Prefetch caches for efficient lookups
            WriteLog("UpdateSquashAsync", $"Loading existing entities for efficient lookups...");
            await LoadAllTestCaseIdsByJiraRefAsync(ct).ConfigureAwait(false);

            WriteLog("UpdateSquashAsync", $"Loading existing test case folders...");
            await LoadAllTestCaseFolderIdsByJiraRefAsync(projectId, ct).ConfigureAwait(false);

            WriteLog("UpdateSquashAsync", $"Loading existing requirements...");
            await LoadAllRequirementIdsByJiraRefAsync(ct).ConfigureAwait(false);

            WriteLog("UpdateSquashAsync", $"Loading existing requirement folders...");
            await LoadAllRequirementFolderIdsByJiraRefAsync(projectId, ct).ConfigureAwait(false);

            // 3) Load DB issues
            WriteLog("UpdateSquashAsync", $"Loading issues from local database...");
            var allIssues = await LoadAllIssuesAsync(ct).ConfigureAwait(false);

            // Split by project code
            var reqIssuesRaw = allIssues
                .Where(p => string.Equals(p.Value.ProjectCode, "REQ", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);

            var tstIssuesRaw = allIssues
                .Where(p => string.Equals(p.Value.ProjectCode, "TST", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);

            // Enforce ancestry to configured main roots
            WriteLog("UpdateSquashAsync", $"Filtering issues to hierarchy roots...");
            var reqIssues = FilterIssuesToHierarchyRoot(reqIssuesRaw, _reqHierarchyRootKey);
            var tstIssues = FilterIssuesToHierarchyRoot(tstIssuesRaw, _tstHierarchyRootKey);

            WriteLog("UpdateSquashAsync", $"Building children maps...");
            var reqChildren = BuildChildrenMap(reqIssues);
            var tstChildren = BuildChildrenMap(tstIssues);

            // Progress: Requirements
            WriteLog("UpdateSquashAsync", $"Upserting requirement hierarchy...");
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

            // Progress: Test cases
            WriteLog("UpdateSquashAsync", $"Upserting test case hierarchy...");
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

            // Progress: Coverage (count per test case leaf)
            WriteLog("UpdateSquashAsync", $"Syncing test coverage...");
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
            WriteLog("UpdateSquashAsync", $"Squash TM synchronization complete.");
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

        /// <summary>
        /// Load all issue rows from SQLite and normalize children/related lists.
        /// </summary>
        private async Task<Dictionary<string, DbIssueRow>> LoadAllIssuesAsync(CancellationToken ct)
        {
            WriteLog("LoadAllIssuesAsync", $"Opening SQLite and loading issues...");
            var dict = new Dictionary<string, DbIssueRow>(StringComparer.OrdinalIgnoreCase);
            string connStr = $"Data Source={frmMain.DatabasePath};";

            await using var conn = new SqliteConnection(connStr);
            await conn.OpenAsync(ct).ConfigureAwait(false);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT KEY, SUMMARY, DESCRIPTION, ISSUETYPE, PARENTKEY, CHILDRENKEYS, RELATESKEYS, PROJECTCODE
                FROM issue";
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

            int count = 0;
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
                count++;
            }

            WriteLog("LoadAllIssuesAsync", $"Loaded {count} issues from SQLite.");
            return dict;
        }

        /// <summary>
        /// Build parent->children mapping from issues, honoring both explicit 'Children' and 'ParentKey'.
        /// </summary>
        private static Dictionary<string, List<string>> BuildChildrenMap(Dictionary<string, DbIssueRow> issues)
        {
            WriteLog("BuildChildrenMap", $"Building children map from {issues.Count} issues...");
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

            WriteLog("BuildChildrenMap", $"Built children map for {map.Count} parents.");
            return map;
        }

        /// <summary>
        /// Returns true if the node represents a folder-like entity in local DB hierarchy.
        /// </summary>
        private static bool IsFolderNode(DbIssueRow node)
        {
            return node.IssueType.Equals("Project", StringComparison.OrdinalIgnoreCase)
                || node.IssueType.Equals("Folder", StringComparison.OrdinalIgnoreCase)
                || node.IssueType.Equals("Menu", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Builds a single-line related keys suffix: " | Related: K1, K2".
        /// </summary>
        private static string BuildRelatedPlainText(IEnumerable<string> relatedKeys)
        {
            var list = relatedKeys?.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (list == null || list.Count == 0) return "";
            return " | Related: " + string.Join(", ", list);
        }

        /// <summary>
        /// Generates basic HTML content for requirement description including Jira key and related keys.
        /// </summary>
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

        /// <summary>
        /// Converts HTML description to clean plain-text BDD script used by scripted test cases.
        /// </summary>
        private static string BuildBddScriptFromDescription(string description)
        {
            if (string.IsNullOrWhiteSpace(description))
                return "";

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
            return result;
        }

        /// <summary>
        /// Loads optional hierarchy roots from squash.conf (key=value lines).
        /// Keys: SQAUSH_REQ_ROOT, SQAUSH_TST_ROOT
        /// </summary>
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
                if (confPath is null)
                {
                    WriteLog("LoadSquashRootsFromConf", $"No squash.conf found.");
                    return;
                }

                WriteLog("LoadSquashRootsFromConf", $"Loading squash.conf: {confPath}");
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
                    {
                        _reqHierarchyRootKey = val;
                        WriteLog("LoadSquashRootsFromConf", $"\tREQ root='{val}'");
                    }
                    else if (key.Equals("SQAUSH_TST_ROOT", StringComparison.OrdinalIgnoreCase))
                    {
                        _tstHierarchyRootKey = val;
                        WriteLog("LoadSquashRootsFromConf", $"\tTST root='{val}'");
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLog("LoadSquashRootsFromConf", $"Error loading squash.conf: {ex.Message}");
            }
        }

        /// <summary>
        /// Keep only issues whose ancestry reaches the given rootKey. If root is absent, returns filtered or empty.
        /// </summary>
        private static Dictionary<string, DbIssueRow> FilterIssuesToHierarchyRoot(
            Dictionary<string, DbIssueRow> issues,
            string? rootKey)
        {
            if (string.IsNullOrWhiteSpace(rootKey))
            {
                WriteLog("FilterIssuesToHierarchyRoot", $"No root configured; returning all {issues.Count} issues.");
                return issues;
            }

            if (!issues.ContainsKey(rootKey))
            {
                WriteLog("FilterIssuesToHierarchyRoot", $"Root '{rootKey}' not present in issues; returning empty.");
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

            WriteLog("FilterIssuesToHierarchyRoot", $"Filtered to {pruned.Count} issues under root='{rootKey}'.");
            return pruned;
        }

        #endregion

        #region Project and root folders

        /// <summary>
        /// Ensure project exists by name; creates if missing then returns the ID.
        /// </summary>
        private async Task<int> EnsureProjectAsync(string name, CancellationToken ct)
        {
            ReportPhaseProgress($"Ensuring project root folder : {name}", 5, 100);
            WriteLog("EnsureProjectAsync", $"Ensuring project '{name}' exists...");

            int id = await FindProjectIdByNameAsync(name, ct).ConfigureAwait(false);
            if (id > 0)
            {
                WriteLog("EnsureProjectAsync", $"\tProject exists: ID={id}");
                return id;
            }

            var payload = new
            {
                _type = "project",
                name = name,
                label = "",
                description = $"{name} Project"
            };

            WriteLog("EnsureProjectAsync", $"\tCreating project...");
            await PostAsync("/projects", payload, ct).ConfigureAwait(false);

            WriteLog("EnsureProjectAsync", $"\tProject created. Verifying...");
            ReportPhaseProgress($"Ensuring project root folder {name} - Completed!", 100, 100);

            return await FindProjectIdByNameAsync(name, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Find project ID by name via GET /projects listing.
        /// </summary>
        private async Task<int> FindProjectIdByNameAsync(string name, CancellationToken ct)
        {
            WriteLog("FindProjectIdByNameAsync", $"Searching for project '{name}'...");

            var res = await GetAsync("/projects", ct).ConfigureAwait(false);
            var json = JObject.Parse(res);
            var projects = (JArray)json["_embedded"]?["projects"] ?? new JArray();

            foreach (JObject p in projects)
            {
                string pname = (string?)p["name"] ?? "";
                WriteLog("FindProjectIdByNameAsync", $"\tChecking '{pname}'...");
                ReportPhaseProgress($"Ensuring project root folder : {name}", 50, 100);

                if (string.Equals(pname, name, StringComparison.OrdinalIgnoreCase))
                {
                    int id = (int)p["id"];
                    WriteLog("FindProjectIdByNameAsync", $"\tFound ID={id}");
                    return id;
                }
            }

            WriteLog("FindProjectIdByNameAsync", $"Not found.");
            return -1;
        }

        /// <summary>
        /// Get requirement folder tree (HAL). Uses cache if available; otherwise fetches once.
        /// </summary>
        private async Task<JArray> GetRequirementFolderTreeAsync(int projectId, CancellationToken ct)
        {
            WriteLog("GetRequirementFolderTreeAsync", $"Loading requirement folder tree for project ID {projectId}. (This may take a few minutes)...");
            if (_reqFolderTreeCache is not null)
            {
                WriteLog("GetRequirementFolderTreeAsync", $"\tUsing cached tree.");
                return _reqFolderTreeCache;
            }

            var res = await GetAsync($"/requirement-folders/tree/{projectId}", ct).ConfigureAwait(false);
            _reqFolderTreeCache = JArray.Parse(res);

            WriteLog("GetRequirementFolderTreeAsync", $"\tTree loaded.");
            return _reqFolderTreeCache;
        }

        /// <summary>
        /// Get test-case folder tree (HAL). Uses cache if available; otherwise fetches once.
        /// </summary>
        private async Task<JArray> GetTestCaseFolderTreeAsync(int projectId, CancellationToken ct)
        {
            WriteLog("GetTestCaseFolderTreeAsync", $"Loading test-case folder tree for project ID {projectId}. (This may take a few minutes)...");
            if (_tstFolderTreeCache is not null)
            {
                WriteLog("GetTestCaseFolderTreeAsync", $"\tUsing cached tree.");
                return _tstFolderTreeCache;
            }

            var res = await GetAsync($"/test-case-folders/tree/{projectId}", ct).ConfigureAwait(false);
            _tstFolderTreeCache = JArray.Parse(res);

            WriteLog("GetTestCaseFolderTreeAsync", $"\tTree loaded.");
            return _tstFolderTreeCache;
        }

        /// <summary>
        /// Find a folder node by name anywhere in the project tree.
        /// </summary>
        private static JObject? FindFolderByName(JObject projectNode, string folderName)
        {
            WriteLog("FindFolderByName", $"Searching for folder '{folderName}'...");

            var acc = new List<JObject>();
            void Walk(JArray children)
            {
                foreach (JObject f in children)
                {
                    acc.Add(f);
                    if (f["children"] is JArray sub && sub.Count > 0)
                        Walk(sub);
                }
            }

            var rootChildren = (JArray)projectNode["folders"] ?? new JArray();
            Walk(rootChildren);

            foreach (var f in acc)
            {
                if (string.Equals((string)f["name"], folderName, StringComparison.OrdinalIgnoreCase))
                {
                    WriteLog("FindFolderByName", $"\tFound '{folderName}' (ID {(int)f["id"]})");
                    return f;
                }
            }

            WriteLog("FindFolderByName", $"\tNot found: '{folderName}'");
            return null;
        }

        /// <summary>
        /// Ensure requirement root folder exists; creates if missing and updates local tree cache.
        /// </summary>
        private async Task<int> EnsureRequirementRootFolderAsync(int projectId, CancellationToken ct)
        {
            WriteLog("EnsureRequirementRootFolderAsync", $"Ensuring requirement root folder exists...");
            ReportPhaseProgress("Ensuring requirement root folders...", 5, 100);

            var tree = await GetRequirementFolderTreeAsync(projectId, ct).ConfigureAwait(false);
            var projectNode = (JObject)tree[0];
            var existing = FindFolderByName(projectNode, RequirementsRootFolderName);

            if (existing != null)
            {
                int existingId = (int)existing["id"];
                WriteLog("EnsureRequirementRootFolderAsync", $"\tAlready exists: ID={existingId}");
                return existingId;
            }

            var payload = new
            {
                _type = "requirement-folder",
                name = RequirementsRootFolderName,
                description = $"Root folder for requirements | Project: {_projectName}",
                parent = new { _type = "project", id = projectId }
            };

            WriteLog("EnsureRequirementRootFolderAsync", $"\tCreating root...");
            var body = await PostAsync("/requirement-folders", payload, ct).ConfigureAwait(false);

            int newId = 0;
            try
            {
                var obj = JObject.Parse(body);
                newId = (int?)obj["id"] ?? 0;
            }
            catch { /* ignore */ }

            if (newId > 0)
            {
                WriteLog("EnsureRequirementRootFolderAsync", $"\tCreated: ID={newId}");
                // Update local tree cache instead of refetching
                var roots = (JArray?)projectNode["folders"] ?? new JArray();
                if (projectNode["folders"] is null) projectNode["folders"] = roots;
                roots.Add(CreateFolderNode(newId, RequirementsRootFolderName));
            }

            ReportPhaseProgress("Ensuring requirement root folders - Completed!", 100, 100);
            return newId;
        }

        /// <summary>
        /// Ensure test-case root folder exists; creates if missing and updates local tree cache.
        /// </summary>
        private async Task<int> EnsureTestCaseRootFolderAsync(int projectId, CancellationToken ct)
        {
            WriteLog("EnsureTestCaseRootFolderAsync", $"Ensuring test-case root folder exists...");
            ReportPhaseProgress("Ensuring test case root folders...", 5, 100);

            var tree = await GetTestCaseFolderTreeAsync(projectId, ct).ConfigureAwait(false);
            var projectNode = (JObject)tree[0];
            var existing = FindFolderByName(projectNode, TestsRootFolderName);
            if (existing != null)
            {
                int existingId = (int)existing["id"];
                WriteLog("EnsureTestCaseRootFolderAsync", $"\tAlready exists: ID={existingId}");
                return existingId;
            }

            var payload = new
            {
                _type = "test-case-folder",
                name = TestsRootFolderName,
                description = $"Root folder for scripted test cases | Project: {_projectName}",
                parent = new { _type = "project", id = projectId }
            };

            WriteLog("EnsureTestCaseRootFolderAsync", $"\tCreating root...");
            var body = await PostAsync("/test-case-folders", payload, ct).ConfigureAwait(false);

            int newId = 0;
            try
            {
                var obj = JObject.Parse(body);
                newId = (int?)obj["id"] ?? 0;
            }
            catch { /* ignore */ }

            if (newId > 0)
            {
                WriteLog("EnsureTestCaseRootFolderAsync", $"\tCreated: ID={newId}");
                // Update local tree cache instead of refetching
                var roots = (JArray?)projectNode["folders"] ?? new JArray();
                if (projectNode["folders"] is null) projectNode["folders"] = roots;
                roots.Add(CreateFolderNode(newId, TestsRootFolderName));
            }

            ReportPhaseProgress("Ensuring test case root folders - Completed!", 100, 100);
            return newId;
        }

        /// <summary>
        /// Ensure a test-case folder (by jiraRef) exists/updated under parent. Updates local tree cache on success.
        /// </summary>
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
            WriteLog("EnsureTestCaseFolderAsync", $"Ensuring test case folder '{name}' for issue '{issueKey}'...");
            ReportPhaseProgress($"Ensuring test case folers...", 100, 100);

            var existingId = await FindTestCaseFolderIdByJiraRefAsync(issueKey, projectId, ct).ConfigureAwait(false);

            if (existingId > 0)
            {
                WriteLog("EnsureTestCaseFolderAsync", $"\tExists: ID={existingId} -> Patching...");
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

                _tcFolderIdByJiraRef[issueKey] = existingId;

                // Best-effort local name update
                if (_tstFolderTreeCache is not null)
                {
                    var projectNode = (JObject)_tstFolderTreeCache[0];
                    var node = FindFolderNodeById(projectNode, existingId);
                    if (node is not null) node["name"] = name;
                }

                WriteLog("EnsureTestCaseFolderAsync", $"\tEnsured ID={existingId}");
                return existingId;
            }

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

            WriteLog("EnsureTestCaseFolderAsync", $"\tCreating test case folder '{name}' for issue '{issueKey}'...");
            var body = await PostAsync("/test-case-folders", createPayload, ct).ConfigureAwait(false);

            int newId = 0;
            try
            {
                var obj = JObject.Parse(body);
                newId = (int?)obj["id"] ?? 0;
            }
            catch { /* ignore */ }

            if (newId > 0)
            {
                _tcFolderIdByJiraRef[issueKey] = newId;

                // Update local tree cache: attach under parentId
                if (_tstFolderTreeCache is not null)
                {
                    var projectNode = (JObject)_tstFolderTreeCache[0];
                    if (string.Equals(parentType, "project", StringComparison.OrdinalIgnoreCase))
                    {
                        var roots = (JArray?)projectNode["folders"] ?? new JArray();
                        if (projectNode["folders"] is null) projectNode["folders"] = roots;
                        roots.Add(CreateFolderNode(newId, name));
                    }
                    else
                    {
                        var parentNode = FindFolderNodeById(projectNode, parentId);
                        if (parentNode is not null)
                        {
                            var children = (JArray?)parentNode["children"] ?? new JArray();
                            if (parentNode["children"] is null) parentNode["children"] = children;
                            children.Add(CreateFolderNode(newId, name));
                        }
                    }
                }

                WriteLog("EnsureTestCaseFolderAsync", $"\tCreated ID={newId}");
                return newId;
            }

            // Last-resort: lightweight parent scan path retained
            try
            {
                WriteLog("EnsureTestCaseFolderAsync", $"\tFallback scan on parent ID={parentId}...");
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

                                // Update local tree cache as well
                                if (_tstFolderTreeCache is not null)
                                {
                                    var projectNode = (JObject)_tstFolderTreeCache[0];
                                    var parentNode = FindFolderNodeById(projectNode, parentId);
                                    if (parentNode is not null)
                                    {
                                        var ch = (JArray?)parentNode["children"] ?? new JArray();
                                        if (parentNode["children"] is null) parentNode["children"] = ch;
                                        ch.Add(CreateFolderNode(id, name));
                                    }
                                }

                                WriteLog("EnsureTestCaseFolderAsync", $"\tDiscovered ID={id} via parent scan");
                                return id;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLog("EnsureTestCaseFolderAsync", $"\tFallback scan failed: {ex.Message}");
            }

            WriteLog("EnsureTestCaseFolderAsync", $"\tUnable to ensure folder for '{issueKey}' (ID unresolved)");
            return 0;
        }

        #endregion

        #region Requirements upsert

        /// <summary>
        /// Upserts full requirement hierarchy under the given root folder.
        /// </summary>
        private async Task UpsertRequirementHierarchyAsync(
            int projectId,
            int reqRootFolderId,
            Dictionary<string, DbIssueRow> issues,
            Dictionary<string, List<string>> childrenMap,
            CancellationToken ct,
            Action? onLeafUpserted = null)
        {
            WriteLog("UpsertRequirementHierarchyAsync", $"Starting requirement DFS under folder={reqRootFolderId} with {issues.Count} issues...");
            var roots = issues.Values
                .Where(i => string.IsNullOrWhiteSpace(i.ParentKey) || !issues.ContainsKey(i.ParentKey))
                .OrderBy(i => i.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var root in roots)
            {
                WriteLog("UpsertRequirementHierarchyAsync", $"\tRoot '{root.Key}' -> DFS...");
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
            WriteLog("UpsertRequirementHierarchyAsync", $"Completed requirement DFS.");
        }

        /// <summary>
        /// DFS upsert for a single requirement node. Folder nodes create/patch folders; leaf nodes upsert requirements.
        /// When a requirement has children, a folder named with the requirement label is created and both the parent and children are placed under that folder.
        /// </summary>
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
            if (!issues.TryGetValue(nodeKey, out var node))
            {
                WriteLog("UpsertRequirementNodeDfsAsync", $"Node '{nodeKey}' not found in issues; skipping.");
                return;
            }

            string displayName = Chop($"{node.Key} {node.Summary}".Trim());
            WriteLog("UpsertRequirementNodeDfsAsync", $"Node '{nodeKey}' -> '{displayName}' parentId={parentId}");

            if (IsFolderNode(node))
            {
                // Ensure folder, then recurse with requirement-folder parent
                int folderId = await EnsureRequirementFolderAsync(
                    projectId,
                    displayName,
                    parentId,
                    parentType,
                    node.Key,
                    node.Related,
                    node.Description,
                    ct).ConfigureAwait(false);

                WriteLog("UpsertRequirementNodeDfsAsync", $"\tFolder ensured ID={folderId}; recursing children...");
                if (childrenMap.TryGetValue(node.Key, out var childKeys))
                {
                    foreach (var ck in childKeys)
                    {
                        await UpsertRequirementNodeDfsAsync(
                            projectId,
                            ck,
                            "requirement-folder",
                            folderId,
                            issues,
                            childrenMap,
                            ct,
                            onLeafUpserted).ConfigureAwait(false);
                    }
                }
                return;
            }

            // Does this requirement have children?
            bool hasChildren = childrenMap.TryGetValue(node.Key, out var childReqKeys) && childReqKeys.Count > 0;

            // If it has children, create/reuse a sub-folder under the current folder and reparent this requirement there.
            int effectiveParentId = parentId;
            if (hasChildren)
            {
                WriteLog("UpsertRequirementNodeDfsAsync", $"\tHas children -> creating dedicated folder under parent={parentId}");
                int subFolderId = await EnsureRequirementFolderAsync(
                    projectId,
                    displayName,              // folder label mirroring the requirement
                    parentId,                 // parent is the current requirement-folder
                    "requirement-folder",
                    node.Key,                 // keep cf_jiraref for traceability
                    node.Related,
                    node.Description,
                    ct).ConfigureAwait(false);

                effectiveParentId = subFolderId;
                WriteLog("UpsertRequirementNodeDfsAsync", $"\tSub-folder ensured ID={subFolderId}");
            }

            // Upsert requirement under effective parent (always a requirement-folder/project)
            int reqId = await FindRequirementIdByJiraRefAsync(node.Key, ct).ConfigureAwait(false);

            var currentVersion = new
            {
                _type = "requirement-version",
                name = displayName,
                description = node.Description,
                status = "WORK_IN_PROGRESS",
                category = new { code = "CAT_FUNCTIONAL" },
                custom_fields = new object[]
                {
                    new { code = "cf_jiraref", value = node.Key },
                    new { code = "cf_jirarefurl", value = $"<a href='{_jiraBaseUrl}/browse/{node.Key}'>{_jiraBaseUrl}/browse/{node.Key}</a>" }
                }
            };

            if (reqId > 0)
            {
                WriteLog("UpsertRequirementNodeDfsAsync", $"\tExisting requirement ID={reqId} -> PATCH under parent={effectiveParentId}");
                var patchPayload = new
                {
                    _type = "requirement",
                    current_version = currentVersion,
                    parent = new { _type = "requirement-folder", id = effectiveParentId }
                };
                await PatchAsync($"/requirements/{reqId}", patchPayload, ct).ConfigureAwait(false);
            }
            else
            {
                WriteLog("UpsertRequirementNodeDfsAsync", $"\tNew requirement -> CREATE under parent={effectiveParentId}");
                var createPayload = new
                {
                    _type = "requirement",
                    current_version = currentVersion,
                    parent = new { _type = "requirement-folder", id = effectiveParentId }
                };
                var body = await PostAsync("/requirements", createPayload, ct).ConfigureAwait(false);

                int newId = 0;
                try
                {
                    var obj = JObject.Parse(body);
                    newId = (int?)obj["id"] ?? 0;
                }
                catch { /* ignore */ }

                if (newId <= 0)
                    newId = await FindRequirementIdByJiraRefAsync(node.Key, ct).ConfigureAwait(false);

                if (newId > 0)
                {
                    _reqIdByJiraRef[node.Key] = newId;
                    reqId = newId;
                    WriteLog("UpsertRequirementNodeDfsAsync", $"\tCreated ID={newId}");
                }
                else
                {
                    WriteLog("UpsertRequirementNodeDfsAsync", $"\tCREATE returned no ID for '{node.Key}'");
                }
            }

            onLeafUpserted?.Invoke();

            // Recurse children into the same sub-folder (if any)
            if (hasChildren && reqId > 0)
            {
                WriteLog("UpsertRequirementNodeDfsAsync", $"\tRecursing children under folder={effectiveParentId}");
                foreach (var childKey in childReqKeys!)
                {
                    await UpsertRequirementNodeDfsAsync(
                        projectId,
                        childKey,
                        "requirement-folder",
                        effectiveParentId,
                        issues,
                        childrenMap,
                        ct,
                        onLeafUpserted).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Prefetch all requirements and index IDs by Jira ref and by name prefix.
        /// </summary>
        private async Task LoadAllRequirementIdsByJiraRefAsync(CancellationToken ct)
        {
            WriteLog("LoadAllRequirementIdsByJiraRefAsync", "Prefetching requirements...");
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

                    // 1) Index by name prefix "KEY " (requirements named like "KEY Summary")
                    var name = r.SelectToken("current_version.name")?.ToString()
                               ?? r.Value<string>("name");
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        var m = Regex.Match(name, @"^(?<key>[A-Za-z]+-\d+)\b", RegexOptions.IgnoreCase);
                        if (m.Success)
                        {
                            var k = m.Groups["key"].Value;
                            if (!string.IsNullOrWhiteSpace(k))
                                _reqIdByJiraRef[k] = id;
                        }
                    }

                    // 2) Index by current_version.custom_fields (cf_jiraref)
                    var verCufs = (JArray?)r.SelectToken("current_version.custom_fields");
                    if (verCufs is not null)
                    {
                        foreach (JObject cf in verCufs)
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

                    // 3) Some instances expose custom_fields at the root requirement level
                    var rootCufs = (JArray?)r["custom_fields"];
                    if (rootCufs is not null)
                    {
                        foreach (JObject cf in rootCufs)
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
                    if (doneItemsThisPage % 50 == 0)
                        WriteLog("LoadAllRequirementIdsByJiraRefAsync", $"\tIndexed {doneItemsThisPage}/{totalItemsThisPage} items in page {currentPage}");
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

            WriteLog("LoadAllRequirementIdsByJiraRefAsync", $"Prefetch complete. Cache size={_reqIdByJiraRef.Count}");
        }

        /// <summary>
        /// Prefetch all requirement folders and index IDs by cf_jiraref via detail GET on each folder id in cached tree.
        /// </summary>
        private async Task LoadAllRequirementFolderIdsByJiraRefAsync(int projectId, CancellationToken ct)
        {
            WriteLog("LoadAllRequirementFolderIdsByJiraRefAsync", $"Prefetching requirement folders...");
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
            WriteLog("LoadAllRequirementFolderIdsByJiraRefAsync", $"Prefetch complete. Cache size={_reqFolderIdByJiraRef.Count}");
        }

        /// <summary>
        /// Cached lookup of a test-case folder ID by Jira ref. No server calls.
        /// </summary>
        private Task<int> FindTestCaseFolderIdByJiraRefAsync(string jiraRef, int projectId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(jiraRef))
                return Task.FromResult(-1);
            if (_tcFolderIdByJiraRef.TryGetValue(jiraRef, out var id))
                return Task.FromResult(id);
            return Task.FromResult(-1);
        }

        /// <summary>
        /// Ensure a requirement folder (by jiraRef) exists/updated under parent. Updates caches and local tree on success.
        /// </summary>
        private async Task<int> EnsureRequirementFolderAsync(
            int projectId,
            string name,
            int parentId,
            string parentType,
            string issueKey,
            IEnumerable<string> relatedKeys,
            string description,
            CancellationToken ct)
        {
            if (_reqFolderIdByJiraRef.TryGetValue(issueKey, out var existingId) && existingId > 0)
            {
                WriteLog("EnsureRequirementFolderAsync", $"\tExists: ID={existingId} -> Patching...");
                var patchPayload = new
                {
                    _type = "requirement-folder",
                    name = Chop(name),
                    description = $"{description}",
                    parent = new { _type = parentType, id = parentId },
                    custom_fields = new object[]
                    {
                new { code = "cf_jiraref", value = issueKey },
                new { code = "cf_jirarefurl", value = $"<a href='{_jiraBaseUrl}/browse/{issueKey}'>{_jiraBaseUrl}/browse/{issueKey}</a>" }
                    }
                };
                await PatchAsync($"/requirement-folders/{existingId}", patchPayload, ct).ConfigureAwait(false);

                // Best-effort local name update
                if (_reqFolderTreeCache is not null)
                {
                    var projectNode = (JObject)_reqFolderTreeCache[0];
                    var node = FindFolderNodeById(projectNode, existingId);
                    if (node is not null) node["name"] = Chop(name);
                }

                WriteLog("EnsureRequirementFolderAsync", $"\tEnsured ID={existingId}");
                return existingId;
            }

            var createPayload = new
            {
                _type = "requirement-folder",
                name = Chop(name),
                description = $"{description}",
                parent = new { _type = parentType, id = parentId },
                custom_fields = new object[]
                {
            new { code = "cf_jiraref", value = issueKey },
            new { code = "cf_jirarefurl", value = $"<a href='{_jiraBaseUrl}/browse/{issueKey}'>{_jiraBaseUrl}/browse/{issueKey}</a>" }
                }
            };

            WriteLog("EnsureRequirementFolderAsync", $"\tCreating requirement folder '{name}' for '{issueKey}'...");
            var body = await PostAsync("/requirement-folders", createPayload, ct).ConfigureAwait(false);

            int newId = 0;
            try
            {
                var obj = JObject.Parse(body);
                newId = (int?)obj["id"] ?? 0;
            }
            catch { /* ignore */ }

            if (newId > 0)
            {
                _reqFolderIdByJiraRef[issueKey] = newId;

                // Update local tree cache: attach under parentId
                if (_reqFolderTreeCache is not null)
                {
                    var projectNode = (JObject)_reqFolderTreeCache[0];
                    if (string.Equals(parentType, "project", StringComparison.OrdinalIgnoreCase))
                    {
                        var roots = (JArray?)projectNode["folders"] ?? new JArray();
                        if (projectNode["folders"] is null) projectNode["folders"] = roots;
                        roots.Add(CreateFolderNode(newId, Chop(name)));
                    }
                    else
                    {
                        var parentNode = FindFolderNodeById(projectNode, parentId);
                        if (parentNode is not null)
                        {
                            var children = (JArray?)parentNode["children"] ?? new JArray();
                            if (parentNode["children"] is null) parentNode["children"] = children;
                            children.Add(CreateFolderNode(newId, Chop(name)));
                        }
                    }
                }

                WriteLog("EnsureRequirementFolderAsync", $"\tCreated ID={newId}");
            }
            else
            {
                WriteLog("EnsureRequirementFolderAsync", $"\tCREATE returned no ID for '{issueKey}'");
            }

            return newId;
        }

        /// <summary>
        /// O(1) lookup for requirement ID by Jira ref using preloaded cache.
        /// </summary>
        private Task<int> FindRequirementIdByJiraRefAsync(string jiraRef, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(jiraRef))
            {
                WriteLog("FindRequirementIdByJiraRefAsync", $"Empty jiraRef provided!");
                return Task.FromResult(-1);
            }

            if (_reqIdByJiraRef.TryGetValue(jiraRef, out var id))
            {
                WriteLog("FindRequirementIdByJiraRefAsync", $"Cache hit '{jiraRef}' => {id}");
                return Task.FromResult(id);
            }

            WriteLog("FindRequirementIdByJiraRefAsync", $"Cache miss '{jiraRef}'");
            foreach (var kvp in _reqIdByJiraRef)
            {
                if (string.Equals(kvp.Key, jiraRef, StringComparison.OrdinalIgnoreCase))
                {
                    WriteLog("FindRequirementIdByJiraRefAsync", $"Found by key-equality '{jiraRef}' => {kvp.Value}");
                    return Task.FromResult(kvp.Value);
                }
            }

            WriteLog("FindRequirementIdByJiraRefAsync", $"No entry for '{jiraRef}'");
            return Task.FromResult(-1);
        }

        #endregion

        #region Test cases upsert (scripted)

        /// <summary>
        /// Upserts full test-case hierarchy under root folder.
        /// </summary>
        private async Task UpsertTestCaseHierarchyAsync(
            int projectId,
            int tstRootFolderId,
            Dictionary<string, DbIssueRow> issues,
            Dictionary<string, List<string>> childrenMap,
            CancellationToken ct,
            Action? onLeafUpserted = null)
        {
            WriteLog("UpsertTestCaseHierarchyAsync", $"Starting TC DFS under folder={tstRootFolderId} with {issues.Count} issues...");
            var roots = issues.Values
                .Where(i => string.IsNullOrWhiteSpace(i.ParentKey) || !issues.ContainsKey(i.ParentKey))
                .OrderBy(i => i.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var root in roots)
            {
                WriteLog("UpsertTestCaseHierarchyAsync", $"\tRoot '{root.Key}' -> DFS...");
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
            WriteLog("UpsertTestCaseHierarchyAsync", $"Completed TC DFS.");
        }

        /// <summary>
        /// DFS upsert for a single TC node. Folder nodes ensure folders; leaf nodes upsert scripted test cases.
        /// </summary>
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
            if (!issues.TryGetValue(nodeKey, out var node))
            {
                WriteLog("UpsertTestCaseNodeDfsAsync", $"Node '{nodeKey}' not found; skipping.");
                return;
            }

            string displayName = Chop($"[{node.Key}] {node.Summary}".Trim());
            WriteLog("UpsertTestCaseNodeDfsAsync", $"Node '{nodeKey}' -> '{displayName}' parentId={parentId}");

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

                WriteLog("UpsertTestCaseNodeDfsAsync", $"\tFolder ensured ID={folderId}; recursing children...");
                if (childrenMap.TryGetValue(node.Key, out var childKeys))
                {
                    foreach (var ck in childKeys)
                    {
                        await UpsertTestCaseNodeDfsAsync(projectId, ck, "test-case-folder", folderId, issues, childrenMap, ct, onLeafUpserted).ConfigureAwait(false);
                    }
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
                WriteLog("UpsertTestCaseNodeDfsAsync", $"\tExisting TC ID={tcId} -> PATCH under parent={parentId}");
                var patchPayload = new
                {
                    _type = "scripted-test-case",
                    name = displayName,
                    script = script,
                    parent = new { _type = "test-case-folder", id = parentId },
                    custom_fields = new object[]
                    {
                        new { code = "cf_jiraref", value = node.Key },
                        new { code = "cf_jirarefurl", value = $"<a href='{_jiraBaseUrl}/browse/{node.Key}'>{_jiraBaseUrl}/browse/{node.Key}</a>" }
                    }
                };
                await PatchAsync($"/test-cases/{tcId}", patchPayload, ct).ConfigureAwait(false);
            }
            else
            {
                WriteLog("UpsertTestCaseNodeDfsAsync", $"\tNew TC -> CREATE under parent={parentId}");
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
                        new { code = "cf_jiraref", value = node.Key },
                        new { code = "cf_jirarefurl", value = $"<a href='{_jiraBaseUrl}/browse/{node.Key}'>{_jiraBaseUrl}/browse/{node.Key}</a>" }
                    }
                };

                await PostAsync("/test-cases", createPayload, ct).ConfigureAwait(false);

                // Refresh cache entry for this key
                var newId = await FindTestCaseIdByJiraRefAsync(node.Key, ct).ConfigureAwait(false);
                if (newId > 0)
                {
                    _tcIdByJiraRef[node.Key] = newId;
                    WriteLog("UpsertTestCaseNodeDfsAsync", $"\tCreated TC ID={newId}");
                }
                else
                {
                    WriteLog("UpsertTestCaseNodeDfsAsync", $"\tCREATE returned no ID for '{node.Key}'");
                }
            }

            onLeafUpserted?.Invoke();
        }

        #endregion

        #region Coverage synchronization (TST -> REQs) using cf_jiraref

        /// <summary>
        /// Iterates test issues: for each TC, find its ID and desired requirement IDs by related keys, then link coverage.
        /// </summary>
        private async Task SyncAllCoverageByJiraRefAsync(
            Dictionary<string, DbIssueRow> reqIssues,
            Dictionary<string, DbIssueRow> tstIssues,
            CancellationToken ct,
            Action? onTestCoverageSynced = null)
        {
            WriteLog("SyncAllCoverageByJiraRefAsync", $"Start coverage sync...");

            // Map requirement keys to IDs
            WriteLog("SyncAllCoverageByJiraRefAsync", $"\tResolving requirements...");
            var reqIdByKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var req in reqIssues.Values)
            {
                int id = await FindRequirementIdByJiraRefAsync(req.Key, ct).ConfigureAwait(false);
                if (id > 0)
                {
                    WriteLog("SyncAllCoverageByJiraRefAsync", $"\t\tREQ {req.Key} => {id}");
                    reqIdByKey[req.Key] = id;
                }
                else
                {
                    WriteLog("SyncAllCoverageByJiraRefAsync", $"\t\tREQ {req.Key} => not found");
                }
            }

            // Link coverage for each test case
            WriteLog("SyncAllCoverageByJiraRefAsync", $"\tLinking coverage...");
            foreach (var tst in tstIssues.Values)
            {
                int testId = await FindTestCaseIdByJiraRefAsync(tst.Key, ct).ConfigureAwait(false);
                if (testId <= 0)
                {
                    WriteLog("SyncAllCoverageByJiraRefAsync", $"\t\tTC {tst.Key} => not found; skip");
                    onTestCoverageSynced?.Invoke();
                    continue;
                }

                var desiredReqIds = tst.Related
                    .Where(k => reqIdByKey.ContainsKey(k))
                    .Select(k => reqIdByKey[k])
                    .Distinct()
                    .ToList();

                WriteLog("SyncAllCoverageByJiraRefAsync", $"\t\tTC {tst.Key} (ID {testId}) -> REQs [{string.Join(", ", desiredReqIds)}]");
                await SyncTestCaseCoverageAsync(testId, desiredReqIds, ct).ConfigureAwait(false);
                onTestCoverageSynced?.Invoke();
            }
            WriteLog("SyncAllCoverageByJiraRefAsync", $"Coverage sync completed.");
        }

        /// <summary>
        /// Link coverage via POST /test-cases/{id}/coverages/{csv}, sending x-www-form-urlencoded content.
        /// </summary>
        private async Task SyncTestCaseCoverageAsync(int testCaseId, List<int> requirementIds, CancellationToken ct)
        {
            if (requirementIds is null || requirementIds.Count == 0)
            {
                WriteLog("SyncTestCaseCoverageAsync", $"TC {testCaseId} -> no requirements to link; skip");
                return;
            }

            var csv = string.Join(",", requirementIds.Distinct());
            var relative = $"/test-cases/{testCaseId}/coverages/{csv}";
            WriteLog("SyncTestCaseCoverageAsync", $"TC {testCaseId} -> POST coverages [{csv}]");

            using var req = new HttpRequestMessage(HttpMethod.Post, _baseUrl + relative);
            req.Headers.Accept.Clear();
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            req.Content = new StringContent(string.Empty, Encoding.UTF8, "application/x-www-form-urlencoded");

            using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!res.IsSuccessStatusCode)
            {
                WriteLog("SyncTestCaseCoverageAsync", $"\tError: {(int)res.StatusCode} - {body}");
                throw new HttpRequestException($"POST {relative} failed {(int)res.StatusCode}: {body}");
            }
        }

        #endregion

        #region HTTP helpers and caches

        /// <summary>
        /// Report progress to the injected callback with calculated percent.
        /// </summary>
        private void ReportPhaseProgress(string phase, int done, int total)
        {
            double percent = total <= 0 ? 100.0 : (double)done / total * 100.0;
            _progress?.Invoke(phase, done, total, percent);
        }

        /// <summary>
        /// GET helper returning body string; ensures success or throws HttpRequestException.
        /// </summary>
        private async Task<string> GetAsync(string relative, CancellationToken ct)
        {
            WriteLog("GetAsync", $"GET {relative}");
            using var req = new HttpRequestMessage(HttpMethod.Get, _baseUrl + relative);
            req.Headers.Accept.Clear();
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/hal+json"));

            using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!res.IsSuccessStatusCode)
            {
                WriteLog("GetAsync", $"\tError {(int)res.StatusCode}: {body}");
                res.EnsureSuccessStatusCode();
            }

            return body;
        }

        /// <summary>
        /// POST helper with JSON payload; returns body or throws on non-success.
        /// </summary>
        private async Task<string> PostAsync(string relative, object payload, CancellationToken ct)
        {
            var settings = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };
            string json = JsonConvert.SerializeObject(payload, settings);
            WriteLog("PostAsync", $"POST {relative}\n\tPayload: {json}");

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
                WriteLog("PostAsync", $"\tError {(int)res.StatusCode}: {body}");
                throw new HttpRequestException($"POST {relative} failed {(int)res.StatusCode}: {body}\nPayload: {json}");
            }
            return body;
        }

        /// <summary>
        /// PATCH helper with JSON payload; returns body or throws on non-success.
        /// </summary>
        private async Task<string> PatchAsync(string relative, object payload, CancellationToken ct)
        {
            var settings = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };
            string json = JsonConvert.SerializeObject(payload, settings);
            WriteLog("PatchAsync", $"PATCH {relative}\n\tPayload: {json}");

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
                WriteLog("PatchAsync", $"\tError {(int)res.StatusCode}: {body}");
                throw new HttpRequestException($"PATCH {relative} failed {(int)res.StatusCode}: {body}\nPayload: {json}");
            }
            return body;
        }

        /// <summary>
        /// PUT helper with JSON payload; returns body or throws on non-success.
        /// </summary>
        private async Task<string> PutAsync(string relative, object payload, CancellationToken ct)
        {
            var settings = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };
            string json = JsonConvert.SerializeObject(payload, settings);
            WriteLog("PutAsync", $"PUT {relative}\n\tPayload: {json}");

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
                WriteLog("PutAsync", $"\tError {(int)res.StatusCode}: {body}");
                throw new HttpRequestException($"PUT {relative} failed {(int)res.StatusCode}: {body}\nPayload: {json}");
            }
            return body;
        }

        /// <summary>
        /// Prefetch all scripted test cases and index by cf_jiraref and name [KEY] fast-path.
        /// </summary>
        private async Task LoadAllTestCaseIdsByJiraRefAsync(CancellationToken ct)
        {
            WriteLog("LoadAllTestCaseIdsByJiraRefAsync", $"Prefetching test cases...");
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
                    if (doneItemsThisPage % 50 == 0)
                        WriteLog("LoadAllTestCaseIdsByJiraRefAsync", $"\tIndexed {doneItemsThisPage}/{totalItemsThisPage} items in page {currentPage}");
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

            WriteLog("LoadAllTestCaseIdsByJiraRefAsync", $"Prefetch complete. Cache size={_tcIdByJiraRef.Count}");
        }

        /// <summary>
        /// Cached lookup of a TC ID by Jira ref. No server calls here.
        /// </summary>
        public Task<int> FindTestCaseIdByJiraRefAsync(string jiraRef, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(jiraRef))
                return Task.FromResult(-1);

            if (_tcIdByJiraRef.TryGetValue(jiraRef, out var id))
                return Task.FromResult(id);

            // Optional: try name-based pattern keys stored during prefetch
            foreach (var kvp in _tcIdByJiraRef)
            {
                if (string.Equals(kvp.Key, jiraRef, StringComparison.OrdinalIgnoreCase))
                    return Task.FromResult(kvp.Value);
            }

            return Task.FromResult(-1);
        }

        /// <summary>
        /// Prefetch all test-case folders and index by cf_jiraref via detail GET on each folder id in cached tree.
        /// </summary>
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
                Walk(folders); // <-- fix the typo here
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

        #endregion

        #region Common

        /// <summary>
        /// Truncates strings to a maximum length.
        /// </summary>
        static string Chop(string value, int max = 255)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return value.Length <= max
                ? value
                : value.Substring(0, max);
        }

        /// <summary>
        /// Appends a single log line to a daily file in the 'log' folder; name squashsyncYYYYMMDD000000.log.
        /// Lines format: YYYY-MM-DD HH:MM:SS<TAB>Topic<TAB>Description
        /// </summary>
        private static void WriteLog(string topic, string description)
        {
            try
            {
                string appFolder = AppDomain.CurrentDomain.BaseDirectory;
                string logFolder = Path.Combine(appFolder, "log");
                Directory.CreateDirectory(logFolder);

                // One file per Run
                string datePart = DateTime.Now.ToString("yyyyMMdd");
                string logFileName = $"squashsync{datePart}.log";
                string logFilePath = Path.Combine(logFolder, logFileName);

                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string line = $"{timestamp}\t{topic}\t{description}{Environment.NewLine}";

                File.AppendAllText(logFilePath, line, Encoding.UTF8);
            }
            catch
            {
                // Swallow logging errors to avoid breaking main flow
            }
        }

        #endregion
    }
}