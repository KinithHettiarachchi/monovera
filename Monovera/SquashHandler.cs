using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Monovera
{
    /// <summary>
    /// SquashHandler synchronizes Requirements and Scripted Test Cases from the local SQLite database to Squash TM.
    ///
    /// End-to-end flow:
    /// 1) Ensure the default Squash project exists (creates if missing).
    /// 2) Ensure top-level folders under the project:
    ///    - Requirements root folder (for REQ hierarchy)
    ///    - Tests root folder (for TST hierarchy)
    /// 3) Load all issues from SQLite and build parent-child maps.
    /// 4) Upsert folders and entities following the DB hierarchy:
    ///    - Folder nodes (IssueType = Folder/Menu/Project) create Squash folders.
    ///    - Leaf nodes create/update requirements or scripted test cases.
    ///    - Each entity stores DB KEY in custom field "cf_jiraref".
    /// 5) Synchronize coverage:
    ///    - Each test case verifies requirements listed in DB "RELATESKEYS" for that TST item.
    ///    - Links are replaced to exactly match the DB (strict sync).
    ///
    /// Progress reporting:
    /// - Shows phase and counters during requirements, test cases, and coverage sync via frmMain.UpdateProgressFromService.
    /// </summary>
    public sealed class SquashHandler
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;
        private readonly string _projectName;
        private readonly Action<int, int, double>? _progress;

        private const string RequirementsRootFolderName = "Requirements";
        private const string TestsRootFolderName = "Tests";

        public SquashHandler(string baseUrl, string apiToken, string projectName = "Project Monovera", Action<int, int, double>? progress = null)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)) throw new ArgumentException("Squash baseUrl is required.");
            if (string.IsNullOrWhiteSpace(apiToken)) throw new ArgumentException("Squash API token is required.");

            _baseUrl = baseUrl.TrimEnd('/');
            _projectName = projectName;
            _progress = progress;

            _http = new HttpClient();
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public async Task UpdateSquashAsync(CancellationToken ct = default)
        {
            int projectId = await EnsureProjectAsync(_projectName, ct).ConfigureAwait(false);

            int reqRootFolderId = await EnsureRequirementRootFolderAsync(projectId, ct).ConfigureAwait(false);
            int tstRootFolderId = await EnsureTestCaseRootFolderAsync(projectId, ct).ConfigureAwait(false);

            var allIssues = await LoadAllIssuesAsync(ct).ConfigureAwait(false);

            var reqIssues = allIssues
                .Where(p => string.Equals(p.Value.ProjectCode, "REQ", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);

            var tstIssues = allIssues
                .Where(p => string.Equals(p.Value.ProjectCode, "TST", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);

            var reqChildren = BuildChildrenMap(reqIssues);
            var tstChildren = BuildChildrenMap(tstIssues);

            // Progress: Test cases
            var totalTcLeaves = tstIssues.Values.Count(i => !IsFolderNode(i));
            int doneTcLeaves = 0;
            ReportPhaseProgress("Syncing test cases", doneTcLeaves, totalTcLeaves);

            await UpsertTestCaseHierarchyAsync(projectId, tstRootFolderId, tstIssues, tstChildren, ct,
                onLeafUpserted: () =>
                {
                    doneTcLeaves++;
                    ReportPhaseProgress("Syncing test cases", doneTcLeaves, totalTcLeaves);
                }).ConfigureAwait(false);

            // Progress: Requirements
            var totalReqLeaves = reqIssues.Values.Count(i => !IsFolderNode(i));
            int doneReqLeaves = 0;
            ReportPhaseProgress("Syncing requirements", doneReqLeaves, totalReqLeaves);

            await UpsertRequirementHierarchyAsync(projectId, reqRootFolderId, reqIssues, reqChildren, ct,
                onLeafUpserted: () =>
                {
                    doneReqLeaves++;
                    ReportPhaseProgress("Syncing requirements", doneReqLeaves, totalReqLeaves);
                }).ConfigureAwait(false);

          

            // Progress: Coverage (count per test case)
            int totalCoverage = tstIssues.Values.Count(i => !IsFolderNode(i));
            int doneCoverage = 0;
            ReportPhaseProgress("Syncing coverage", doneCoverage, totalCoverage);

            await SyncAllCoverageByJiraRefAsync(reqIssues, tstIssues, ct,
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

            await using var conn = new SqliteConnection(connStr);
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

        private static string BuildBddScriptFromDescription(string description)
        {
            if (string.IsNullOrWhiteSpace(description))
                return "# To be added";

            // Remove common Confluence-export wrapper around gherkin code
            string s = description
                .Replace("<p><span class=\"error\"><pre><code class=\"language-gherkin\">", string.Empty)
                .Replace("</code></pre></span></p>", string.Empty)
                .Replace("<pre><code class=\"language-gherkin\">", string.Empty)
                .Replace("</code></pre>", string.Empty);

            // Decode HTML entities to plain text (e.g., &quot; → ", &amp; → &, etc.)
            s = System.Net.WebUtility.HtmlDecode(s);

            // Optional: normalize CRLFs
            s = s.Replace("\r\n", "\n");

            // Trim surrounding whitespace
            return s.Trim();
        }

        #endregion

        #region Project and top-level folders

        private async Task<int> EnsureProjectAsync(string name, CancellationToken ct)
        {
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
            return after != null ? (int)after["id"] : 0;
        }

        private async Task<int> EnsureTestCaseRootFolderAsync(int projectId, CancellationToken ct)
        {
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
            return after != null ? (int)after["id"] : 0;
        }

        private async Task<int> EnsureRequirementFolderAsync(
    int projectId,
    string name,
    int parentId,
    string parentType,
    string issueKey,
    string? nodeDescription,
    IEnumerable<string> relatedKeys,
    CancellationToken ct)
        {
            // 1) Prefer match by cf_jiraref == KEY (DB)
            int byJiraRefId = await FindRequirementFolderIdByJiraRefAsync(projectId, issueKey, ct).ConfigureAwait(false);
            string descriptionToSend = !string.IsNullOrWhiteSpace(nodeDescription)
                                        ? nodeDescription
                                        : "";

            if (byJiraRefId > 0)
            {
                // Update and ensure cf_jiraref remains set
                var updatePayload = new
                {
                    _type = "requirement-folder",
                    name = name,
                    description = descriptionToSend,
                    custom_fields = new object[]
                    {
                new { code = "cf_jiraref", value = issueKey }
                    }
                };

                await PatchAsync($"/requirement-folders/{byJiraRefId}", updatePayload, ct).ConfigureAwait(false);
                return byJiraRefId;
            }

            // 2) Fallback: find by name in tree; if found, treat as update and set cf_jiraref
            var tree = await GetRequirementFolderTreeAsync(projectId, ct).ConfigureAwait(false);
            var projectNode = (JObject)tree[0];
            var existing = FindFolderByName(projectNode, name);
            if (existing != null)
            {
                int id = (int)existing["id"];
                var updatePayload = new
                {
                    _type = "requirement-folder",
                    name = name,
                    description = descriptionToSend,
                    custom_fields = new object[]
                    {
                new { code = "cf_jiraref", value = issueKey }
                    }
                };
                await PatchAsync($"/requirement-folders/{id}", updatePayload, ct).ConfigureAwait(false);
                return id;
            }

            // 3) Create new folder with cf_jiraref
            var createPayload = new
            {
                _type = "requirement-folder",
                name = name,
                description = descriptionToSend,
                parent = new { _type = parentType, id = parentId },
                custom_fields = new object[]
                {
            new { code = "cf_jiraref", value = issueKey }
                }
            };

            await PostAsync("/requirement-folders", createPayload, ct).ConfigureAwait(false);

            // Re-read tree and return the created folder id
            tree = await GetRequirementFolderTreeAsync(projectId, ct).ConfigureAwait(false);
            projectNode = (JObject)tree[0];
            var after = FindFolderByName(projectNode, name);
            return after != null ? (int)after["id"] : 0;
        }

        // Walks the requirement-folder tree and returns the folder id whose custom_fields contains cf_jiraref == jiraRef
        private async Task<int> FindRequirementFolderIdByJiraRefAsync(int projectId, string jiraRef, CancellationToken ct)
        {
            //var tree = await GetRequirementFolderTreeAsync(projectId, ct).ConfigureAwait(false);
            //if (tree == null || tree.Count == 0) return -1;

            //var projectNode = (JObject)tree[0];
            //var ids = new List<int>();

            //void CollectIds(JArray folders)
            //{
            //    foreach (JObject f in folders)
            //    {
            //        if (f.TryGetValue("id", out var idToken) && idToken.Type == JTokenType.Integer)
            //            ids.Add((int)idToken);
            //        if (f["children"] is JArray sub && sub.Count > 0)
            //            CollectIds(sub);
            //    }
            //}

            //var rootFolders = (JArray)projectNode["folders"] ?? new JArray();
            //CollectIds(rootFolders);

            //foreach (var id in ids)
            //{
            //    // Fetch full folder details to read custom_fields
            //    string res = await GetAsync($"/requirement-folders/{id}", ct).ConfigureAwait(false);
            //    var json = JObject.Parse(res);

            //    var cufs = json["custom_fields"] as JArray;
            //    if (cufs == null) continue;

            //    foreach (JObject cf in cufs)
            //    {
            //        var code = (string?)cf["code"];
            //        var valueToken = cf["value"];
            //        if (!string.Equals(code, "cf_jiraref", StringComparison.OrdinalIgnoreCase))
            //            continue;

            //        var value = valueToken?.Type == JTokenType.Array
            //            ? string.Join(",", ((JArray)valueToken!).Select(v => v?.ToString()))
            //            : valueToken?.ToString();

            //        if (string.Equals(value, jiraRef, StringComparison.OrdinalIgnoreCase))
            //            return id;
            //    }
            //}

            return -1;
        }

        private async Task<int> EnsureTestCaseFolderAsync(
    int projectId,
    string name,
    int parentId,
    string parentType,
    string issueKey,
    string? nodeDescription,
    IEnumerable<string> relatedKeys,
    CancellationToken ct)
        {
            // 1) Prefer match by cf_jiraref == KEY (DB)
            int byJiraRefId = await FindTestCaseFolderIdByJiraRefAsync(projectId, issueKey, ct).ConfigureAwait(false);
            string descriptionToSend = !string.IsNullOrWhiteSpace(nodeDescription)
                                        ? nodeDescription
                                        : "";

            if (byJiraRefId > 0)
            {
                // Update and ensure cf_jiraref remains set
                var updatePayload = new
                {
                    _type = "test-case-folder",
                    name = name,
                    description = descriptionToSend,
                    custom_fields = new object[]
                    {
                        new { code = "cf_jiraref", value = issueKey }
                    }
                };

                await PatchAsync($"/test-case-folders/{byJiraRefId}", updatePayload, ct).ConfigureAwait(false);
                return byJiraRefId;
            }

            // 2) Fallback: find by name in tree; if found, treat as update and set cf_jiraref
            var tree = await GetTestCaseFolderTreeAsync(projectId, ct).ConfigureAwait(false);
            var projectNode = (JObject)tree[0];
            var existing = FindFolderByName(projectNode, name);
            if (existing != null)
            {
                int id = (int)existing["id"];
                var updatePayload = new
                {
                    _type = "test-case-folder",
                    name = name,
                    description = descriptionToSend,
                    custom_fields = new object[]
                    {
                        new { code = "cf_jiraref", value = issueKey }
                    }
                };
                await PatchAsync($"/test-case-folders/{id}", updatePayload, ct).ConfigureAwait(false);
                return id;
            }

            // 3) Create new folder with cf_jiraref
            var createPayload = new
            {
                _type = "test-case-folder",
                name = name,
                description = descriptionToSend,
                parent = new { _type = parentType, id = parentId },
                custom_fields = new object[]
                {
                    new { code = "cf_jiraref", value = issueKey }
                }
            };
            await PostAsync("/test-case-folders", createPayload, ct).ConfigureAwait(false);

            // Re-read tree and return the created folder id
            tree = await GetTestCaseFolderTreeAsync(projectId, ct).ConfigureAwait(false);
            projectNode = (JObject)tree[0];
            var after = FindFolderByName(projectNode, name);
            return after != null ? (int)after["id"] : 0;
        }

        // Walks the test-case folder tree and returns the folder id whose custom_fields contains cf_jiraref == jiraRef
        private async Task<int> FindTestCaseFolderIdByJiraRefAsync(int projectId, string jiraRef, CancellationToken ct)
        {
            //var tree = await GetTestCaseFolderTreeAsync(projectId, ct).ConfigureAwait(false);
            //if (tree == null || tree.Count == 0) return -1;

            //var projectNode = (JObject)tree[0];
            //var ids = new List<int>();

            //void CollectIds(JArray folders)
            //{
            //    foreach (JObject f in folders)
            //    {
            //        if (f.TryGetValue("id", out var idToken) && idToken.Type == JTokenType.Integer)
            //            ids.Add((int)idToken);
            //        if (f["children"] is JArray sub && sub.Count > 0)
            //            CollectIds(sub);
            //    }
            //}

            //var rootFolders = (JArray)projectNode["folders"] ?? new JArray();
            //CollectIds(rootFolders);

            //foreach (var id in ids)
            //{
            //    // Fetch full folder details to read custom_fields
            //    string res = await GetAsync($"/test-case-folders/{id}", ct).ConfigureAwait(false);
            //    var json = JObject.Parse(res);

            //    var cufs = json["custom_fields"] as JArray;
            //    if (cufs == null) continue;

            //    foreach (JObject cf in cufs)
            //    {
            //        var code = (string?)cf["code"];
            //        var valueToken = cf["value"];
            //        if (!string.Equals(code, "cf_jiraref", StringComparison.OrdinalIgnoreCase))
            //            continue;

            //        var value = valueToken?.Type == JTokenType.Array
            //            ? string.Join(",", ((JArray)valueToken!).Select(v => v?.ToString()))
            //            : valueToken?.ToString();

            //        if (string.Equals(value, jiraRef, StringComparison.OrdinalIgnoreCase))
            //            return id;
            //    }
            //}

            return -1;
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

            string displayName = $"{node.Key} {node.Summary}".Trim();

            if (IsFolderNode(node))
            {
                int folderId = await EnsureRequirementFolderAsync(
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
            }
            else
            {
                int reqId = await FindRequirementIdByJiraRefAsync(node.Key, ct).ConfigureAwait(false);
                string descriptionToSend = BuildRequirementDescriptionHtml(node);

                var currentVersion = new
                {
                    _type = "requirement-version",
                    name = string.IsNullOrWhiteSpace(node.Summary) ? node.Key : node.Summary,
                    description = descriptionToSend,
                    status = "WORK_IN_PROGRESS",
                    category = new { code = "CAT_USER_STORY" },
                    custom_fields = new object[]
                    {
                        new { code = "cf_jiraref", value = node.Key }
                    }
                };

                if (reqId > 0)
                {
                    await PatchAsync($"/requirements/{reqId}", new
                    {
                        _type = "requirement",
                        current_version = currentVersion
                    }, ct).ConfigureAwait(false);
                }
                else
                {
                    await PostAsync("/requirements", new
                    {
                        _type = "requirement",
                        current_version = currentVersion,
                        parent = new { _type = parentType, id = parentId }
                    }, ct).ConfigureAwait(false);

                    reqId = await FindRequirementIdByJiraRefAsync(node.Key, ct).ConfigureAwait(false);
                }

                onLeafUpserted?.Invoke();

                if (reqId > 0 && childrenMap.TryGetValue(node.Key, out var childKeys) && childKeys.Count > 0)
                {
                    foreach (var ck in childKeys)
                    {
                        await UpsertRequirementNodeDfsAsync(
                            projectId,
                            ck,
                            "requirement",
                            reqId,
                            issues,
                            childrenMap,
                            ct,
                            onLeafUpserted).ConfigureAwait(false);
                    }
                }
            }
        }

        private async Task<int> FindRequirementIdByJiraRefAsync(string jiraRef, CancellationToken ct)
        {
            var res = await GetAsync("/requirements", ct).ConfigureAwait(false);
            var json = JObject.Parse(res);
            var arr = (JArray)json["_embedded"]?["requirements"] ?? new JArray();

            foreach (JObject r in arr)
            {
                var currentVersion = (JObject?)r["current_version"];
                var cufs = (JArray?)currentVersion?["custom_fields"];
                if (cufs is null) continue;

                foreach (JObject cf in cufs)
                {
                    var code = (string?)cf["code"];
                    var valueToken = cf["value"];
                    if (!string.Equals(code, "cf_jiraref", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var value = valueToken?.Type == JTokenType.Array
                        ? string.Join(",", ((JArray)valueToken!).Select(v => v?.ToString()))
                        : valueToken?.ToString();

                    if (string.Equals(value, jiraRef, StringComparison.OrdinalIgnoreCase))
                        return (int)r["id"];
                }
            }

            return -1;
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

            string displayName = $"{node.Key} {node.Summary}".Trim();

            if (IsFolderNode(node))
            {
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
                    {
                        await UpsertTestCaseNodeDfsAsync(
                            projectId,
                            ck,
                            "test-case-folder",
                            folderId,
                            issues,
                            childrenMap,
                            ct,
                            onLeafUpserted).ConfigureAwait(false);
                    }
                }
            }
            else
            {
                // Build script content from DB description
                string descriptionToSend = BuildBddScriptFromDescription(node.Description);

                // Decide create vs update by cf_jiraref matching KEY from DB
                int tcId = await FindTestCaseIdByJiraRefAsync(node.Key, ct).ConfigureAwait(false);

                if (tcId > 0)
                {
                    // PATCH scripted test case – keep minimal fields and ensure cf_jiraref remains set under current_version
                    var updatePayload = new
                    {
                        _type = "scripted-test-case",
                        name = string.IsNullOrWhiteSpace(node.Summary) ? node.Key : $"{node.Key} {node.Summary}",
                        script = descriptionToSend,
                        custom_fields = new[]
                                            {
                                                new
                                                {
                                                    code = "cf_jiraref",
                                                    value = node.Key
                                                }
                                            }
                    };

                    await PatchAsync($"/test-cases/{tcId}", updatePayload, ct).ConfigureAwait(false);
                }
                else
                {
                    // POST scripted test case – minimal documented shape + current_version custom field for cf_jiraref
                    var createPayload = new
                    {
                        _type = "scripted-test-case",
                        name = string.IsNullOrWhiteSpace(node.Summary) ? node.Key : $"{node.Key} {node.Summary}",
                        parent = new { _type = parentType, id = parentId },
                        script = descriptionToSend,
                        custom_fields = new[]
                                            {
                                                new
                                                {
                                                    code = "cf_jiraref",
                                                    value = node.Key
                                                }
                                            }
                    };

                    await PostAsync("/test-cases", createPayload, ct).ConfigureAwait(false);
                }

                onLeafUpserted?.Invoke();

                // If there are children beneath a leaf (rare), keep walking under the same parent folder
                if (childrenMap.TryGetValue(node.Key, out var childKeys) && childKeys.Count > 0)
                {
                    foreach (var ck in childKeys)
                    {
                        await UpsertTestCaseNodeDfsAsync(
                            projectId,
                            ck,
                            "test-case-folder",
                            parentId,
                            issues,
                            childrenMap,
                            ct,
                            onLeafUpserted).ConfigureAwait(false);
                    }
                }
            }
        }

        private async Task<int> FindTestCaseIdByJiraRefAsync(string jiraRef, CancellationToken ct)
        {
            var res = await GetAsync("/test-cases", ct).ConfigureAwait(false);
            var json = JObject.Parse(res);
            var arr = (JArray)json["_embedded"]?["test-cases"] ?? new JArray();

            foreach (JObject t in arr)
            {
                var currentVersion = (JObject?)t["current_version"];
                var cufs = (JArray?)currentVersion?["custom_fields"];
                if (cufs is null) continue;

                foreach (JObject cf in cufs)
                {
                    var code = (string?)cf["code"];
                    var valueToken = cf["value"];
                    if (!string.Equals(code, "cf_jiraref", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var value = valueToken?.Type == JTokenType.Array
                        ? string.Join(",", ((JArray)valueToken!).Select(v => v?.ToString()))
                        : valueToken?.ToString();

                    if (string.Equals(value, jiraRef, StringComparison.OrdinalIgnoreCase))
                        return (int)t["id"];
                }
            }

            return -1;
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

        #region HTTP helpers

        private void ReportPhaseProgress(string phase, int done, int total)
        {
            double percent = total <= 0 ? 100.0 : (double)done / total * 100.0;
            // Use injected progress reporter to avoid static/instance form issues
            _progress?.Invoke(done, total, percent);
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
            var settings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                StringEscapeHandling = StringEscapeHandling.EscapeHtml
            };
            string json = JsonConvert.SerializeObject(payload, settings);
            using var req = new HttpRequestMessage(HttpMethod.Post, _baseUrl + relative)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            req.Headers.Accept.Clear();
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
                throw new HttpRequestException($"POST {relative} failed {(int)res.StatusCode}: {body}\nPayload: {json}");
            return body;
        }

        private async Task<string> PatchAsync(string relative, object payload, CancellationToken ct)
        {
            var settings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                StringEscapeHandling = StringEscapeHandling.EscapeHtml
            };
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
                throw new HttpRequestException($"PATCH {relative} failed {(int)res.StatusCode}: {body}\nPayload: {json}");
            return body;
        }

        private async Task<string> PutAsync(string relative, object payload, CancellationToken ct)
        {
            var settings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                StringEscapeHandling = StringEscapeHandling.EscapeHtml
            };
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
                throw new HttpRequestException($"PUT {relative} failed {(int)res.StatusCode}: {body}\nPayload: {json}");
            return body;
        }

        #endregion
    }
}