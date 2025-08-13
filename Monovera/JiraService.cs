using Atlassian.Jira;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using static Monovera.frmMain;
using Monovera;
using System.Data.SQLite;
using Microsoft.Data.Sqlite;

/// <summary>
/// Service class to interact with Jira via Atlassian.Jira SDK and REST API.
/// Provides asynchronous methods for retrieving, updating, and querying Jira issues,
/// as well as linking issues and managing custom fields.
/// All actions are logged using AppLogger for traceability.
/// </summary>
public class JiraService
{
    /// <summary>
    /// Atlassian.Jira SDK client instance used for all SDK-based operations.
    /// </summary>
    private readonly Jira jira;

    /// <summary>
    /// Jira account username (email) used for authentication.
    /// </summary>
    private readonly string username;

    /// <summary>
    /// Jira API token used for authentication.
    /// </summary>
    private readonly string apiToken;

    /// <summary>
    /// Base URL of the Jira instance (e.g., https://yourdomain.atlassian.net).
    /// </summary>
    private readonly string jiraBaseUrl;

    /// <summary>
    /// Initializes a new instance of JiraService with the specified Jira base URL and credentials.
    /// </summary>
    /// <param name="jiraUrl">Base URL of the Jira instance.</param>
    /// <param name="email">Email associated with Jira account.</param>
    /// <param name="apiToken">API token generated for the Jira account.</param>
    public JiraService(string jiraUrl, string email, string apiToken)
    {
        this.username = email;
        this.apiToken = apiToken;
        this.jiraBaseUrl = jiraUrl.TrimEnd('/');
        this.jira = Jira.CreateRestClient(jiraUrl, email, apiToken);
    }

    /// <summary>
    /// Tests the Jira connection by attempting to fetch the current user asynchronously.
    /// Returns true if connection and authentication are successful; false otherwise.
    /// Logs the result.
    /// </summary>
    /// <returns>True if connected successfully; false if failed.</returns>
    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            var user = await jira.Users.GetMyselfAsync();
            return user != null;
        }
        catch (Exception ex)
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the display name of the currently authenticated Jira user asynchronously.
    /// Returns null if unable to fetch user info.
    /// Logs the result.
    /// </summary>
    /// <returns>The display name of the connected user, or null if failed.</returns>
    public async Task<string?> GetConnectedUserNameAsync()
    {
        try
        {
            var user = await jira.Users.GetMyselfAsync();
            return user?.DisplayName;
        }
        catch (Exception ex)
        {
            return null;
        }
    }

    /// <summary>
    /// Represents the result of a Jira project permission check for the current user.
    /// Indicates whether the user can create issues and/or edit issues in the specified project.
    /// </summary>
    public class WritePermissionsResult
    {
        public bool CanCreateIssues { get; set; }
        public bool CanEditIssues { get; set; }
    }

    /// <summary>
    /// Checks the current user's permissions for creating and editing issues in a specified Jira project.
    /// Uses the Jira REST API to query "CREATE_ISSUES" and "EDIT_ISSUES" permissions.
    /// Returns a <see cref="WritePermissionsResult"/> indicating the user's capabilities.
    /// Logs any errors encountered during the permission check.
    /// </summary>
    /// <param name="projectKey">The key of the Jira project to check permissions for (e.g., "PROJECT1").</param>
    /// <returns>
    /// A <see cref="WritePermissionsResult"/> object containing the create and edit permission flags,
    /// or null if the permission check fails.
    /// </returns>
    public async Task<WritePermissionsResult?> GetWritePermissionsAsync(string projectKey)
    {
        try
        {
            using var client = new HttpClient();
            client.BaseAddress = new Uri(jiraBaseUrl);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{apiToken}"))
            );

            string permissionsToCheck = "CREATE_ISSUES,EDIT_ISSUES";
            var response = await client.GetAsync($"/rest/api/3/mypermissions?projectKey={projectKey}&permissions={permissionsToCheck}");

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var permissions = doc.RootElement.GetProperty("permissions");

            bool canCreate = permissions.TryGetProperty("CREATE_ISSUES", out var create)
                             && create.GetProperty("havePermission").GetBoolean();

            bool canEdit = permissions.TryGetProperty("EDIT_ISSUES", out var edit)
                           && edit.GetProperty("havePermission").GetBoolean();

            return new WritePermissionsResult
            {
                CanCreateIssues = canCreate,
                CanEditIssues = canEdit
            };
        }
        catch (Exception ex)
        {
            return null;
        }
    }

    /// <summary>
    /// Gets all Jira issues for a given project using raw JQL.
    /// Optionally loads from cache unless forceSync is true.
    /// Progress is reported via the progressUpdate callback.
    /// Issues are fetched in parallel batches for performance.
    /// Logs all major steps and results.
    /// </summary>
    /// <param name="projectKey">The Jira project key.</param>
    /// <param name="fields">Fields to retrieve (SDK retrieves most by default).</param>
    /// <param name="sortingField">Field used for sorting (default: "created").</param>
    /// <param name="linkTypeName">Type of issue link to include (default: "Blocks").</param>
    /// <param name="forceSync">If true, ignores cache and fetches from Jira.</param>
    /// <param name="progressUpdate">Callback for progress reporting (completed, total, percent).</param>
    /// <param name="maxParallelism">Maximum parallel requests to Jira.</param>
    /// <returns>List of JiraIssueDictionary objects for the project.</returns>
    public async Task<List<JiraIssueDictionary>> UpdateLocalIssueRepositoryAndLoadTree(
    string projectKey,
    string projectName,
    string sortingField,
    string linkTypeName = "Blocks",
    bool forceSync = false,
    Action<int, int, double> progressUpdate = null,
    int maxParallelism = 100,
    string updateType = "Complete")
    {
        string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "monovera.sqlite");
        string connStr = $"Data Source={dbPath};";

        bool dbExists = File.Exists(dbPath);
        bool tableEmpty = true;

        // Ensure DB and tables exist, and set WAL mode for concurrency
        if (!dbExists)
        {
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                PRAGMA journal_mode=WAL;
                CREATE TABLE IF NOT EXISTS issue (
                    ID INTEGER PRIMARY KEY AUTOINCREMENT,
                    CREATEDTIME TEXT,
                    UPDATEDTIME TEXT,
                    KEY TEXT UNIQUE,
                    SUMMARY TEXT,
                    DESCRIPTION TEXT,
                    PARENTKEY TEXT,
                    CHILDRENKEYS TEXT,
                    RELATESKEYS TEXT,
                    SORTINGFIELD TEXT,
                    ISSUETYPE TEXT,
                    PROJECTNAME TEXT,
                    PROJECTCODE TEXT
                );";
                    cmd.ExecuteNonQuery();
                }
            }
            tableEmpty = true;
        }
        else
        {
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = $"SELECT COUNT(1) FROM issue WHERE PROJECTCODE='{projectKey}'";
                    tableEmpty = Convert.ToInt32(cmd.ExecuteScalar()) == 0;
                }
            }
        }

        // If DB is empty or forced sync, update DB from Jira
        if (tableEmpty || forceSync)
        {
            // Build JQL based on updateType
            string jql;
            if (updateType == "Difference")
            {
                // Get max UPDATEDTIME from DB for this project
                string maxUpdatedTime = null;
                using (var conn = new SqliteConnection(connStr))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT MAX(UPDATEDTIME) FROM issue WHERE PROJECTCODE = @pcode";
                        cmd.Parameters.AddWithValue("@pcode", projectKey);
                        var result = cmd.ExecuteScalar();
                        maxUpdatedTime = result != DBNull.Value && result != null ? result.ToString() : null;
                    }
                }

                // Convert maxUpdatedTime to Jira format
                string jiraTime = null;
                if (!string.IsNullOrWhiteSpace(maxUpdatedTime) && maxUpdatedTime.Length == 14)
                {
                    // yyyyMMddHHmmss -> yyyy-MM-dd HH:mm
                    jiraTime = DateTime.ParseExact(maxUpdatedTime, "yyyyMMddHHmmss", null).ToString("yyyy-MM-dd HH:mm");
                }

                if (!string.IsNullOrWhiteSpace(jiraTime))
                    jql = $"project={projectKey} AND (updated >= \"{jiraTime}\" OR created >= \"{jiraTime}\")";
                else
                    jql = $"project={projectKey}";
            }
            else // Complete
            {
                jql = $"project={projectKey}";
            }

            int totalCount = await GetTotalIssueCountAsync(projectKey, jql);

            if (totalCount > 0)
            {
                const int pageSize = 100;
                int totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
                var pageStarts = Enumerable.Range(0, totalPages).Select(i => i * pageSize).ToList();

                int completed = 0;
                var allIssueRows = new List<object[]>();

                var throttler = new SemaphoreSlim(Math.Min(maxParallelism, 4)); // SQLite: keep parallelism low
                var tasks = pageStarts.Select(async startAt =>
                {
                    await throttler.WaitAsync();
                    try
                    {
                        string fieldsStr = "summary,description,issuetype,status,created,updated,issuelinks";
                        bool hasSortingField = !string.IsNullOrWhiteSpace(sortingField);
                        if (hasSortingField)
                        {
                            fieldsStr += $",{sortingField}";
                        }
                        string url = $"{jiraBaseUrl}/rest/api/2/search" +
                                     $"?jql={Uri.EscapeDataString(jql)}" +
                                     $"&startAt={startAt}" +
                                     $"&maxResults={pageSize}" +
                                     $"&fields={Uri.EscapeDataString(fieldsStr)}";

                        using var client = new HttpClient();
                        client.Timeout = TimeSpan.FromMinutes(5); // Increase timeout to 5 minutes
                        client.BaseAddress = new Uri(jiraBaseUrl);
                        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                            "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{apiToken}"))
                        );

                        var response = await client.GetStringAsync(url);
                        var json = JObject.Parse(response);
                        var issues = json["issues"].Select(issue =>
                        {
                            string created = ConvertToDbTimestamp((string)issue["fields"]["created"]);
                            string updated = ConvertToDbTimestamp((string)issue["fields"]["updated"]);
                            string key = (string)issue["key"];
                            string summary = (string)issue["fields"]["summary"];
                            string description = issue["fields"]["description"]?.ToString() ?? "";
                            string parentKey = "";
                            string childrenKeys = "";
                            string relatesKeys = "";
                            string sortingFieldValue = null;

                            if (hasSortingField && issue["fields"][sortingField] != null)
                            {
                                sortingFieldValue = issue["fields"][sortingField].ToString();
                            }

                            string issueType = (string)issue["fields"]["issuetype"]?["name"] ?? "";

                            if (issue["fields"]["issuelinks"] is JArray linksArray)
                            {
                                var relates = linksArray
                                    .Where(link => (string)link["type"]?["name"] == "Relates")
                                    .Select(link =>
                                        (string)link["outwardIssue"]?["key"] ?? "")
                                    .Where(k => !string.IsNullOrWhiteSpace(k))
                                    .Distinct()
                                    .ToList();
                                relatesKeys = string.Join(",", relates);

                                var children = linksArray
                                    .Where(link => (string)link["type"]?["name"] == linkTypeName)
                                    .Select(link =>
                                        (string)link["outwardIssue"]?["key"])
                                    .Where(k => !string.IsNullOrWhiteSpace(k))
                                    .Distinct()
                                    .ToList();
                                childrenKeys = string.Join(",", children);

                                var parentLink = linksArray
                                    .FirstOrDefault(link =>
                                        (string)link["type"]?["name"] == linkTypeName &&
                                        link["inwardIssue"]?["key"] != null);

                                parentKey = parentLink != null ? (string)parentLink["inwardIssue"]["key"] : "";
                            }

                            return new object[]
                            {
                            created, updated, key, summary, description, parentKey, childrenKeys, relatesKeys, sortingFieldValue, issueType, projectName, projectKey
                            };
                        }).ToList();

                        lock (allIssueRows)
                        {
                            allIssueRows.AddRange(issues);
                        }

                        Interlocked.Add(ref completed, issues.Count);
                        double percent = totalCount > 0 ? (completed * 100.0 / totalCount) : 100.0;
                        progressUpdate?.Invoke(completed, totalCount, percent);
                    }
                    finally
                    {
                        throttler.Release();
                    }
                }).ToList();

                await Task.WhenAll(tasks);

                // Now batch check existence and update times, then batch write
                using (var conn = new SqliteConnection(connStr))
                {
                    conn.Open();

                    // Build a lookup for all keys
                    var keys = allIssueRows.Select(row => (string)row[2]).Distinct().ToList();
                    var dbTimes = new Dictionary<string, string>();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = $"SELECT KEY, UPDATEDTIME FROM issue WHERE KEY IN ({string.Join(",", keys.Select((k, i) => $"@k{i}"))})";
                        for (int i = 0; i < keys.Count; i++)
                            cmd.Parameters.AddWithValue($"@k{i}", keys[i]);
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                dbTimes[reader.GetString(0)] = reader.GetString(1);
                            }
                        }
                    }

                    // Prepare batch
                    var toInsert = new List<object[]>();
                    var toUpdate = new List<object[]>();

                    foreach (var row in allIssueRows)
                    {
                        string key = (string)row[2];
                        string updated = (string)row[1];
                        bool exists = dbTimes.ContainsKey(key);
                        bool shouldUpdate = false;
                        if (exists)
                        {
                            string dbUpdated = dbTimes[key];
                            if (string.Compare(updated, dbUpdated, StringComparison.Ordinal) > 0)
                                shouldUpdate = true;
                        }
                        if (!exists)
                            toInsert.Add(row);
                        else if (shouldUpdate)
                            toUpdate.Add(row);
                    }

                    // Batch insert
                    if (toInsert.Count > 0)
                    {
                        using (var transaction = conn.BeginTransaction())
                        {
                            using (var cmd = conn.CreateCommand())
                            {
                                cmd.CommandText = @"
                                INSERT INTO issue
                                (CREATEDTIME, UPDATEDTIME, KEY, SUMMARY, DESCRIPTION, PARENTKEY, CHILDRENKEYS, RELATESKEYS, SORTINGFIELD, ISSUETYPE, PROJECTNAME, PROJECTCODE)
                                VALUES (@created, @updated, @key, @summary, @desc, @parent, @children, @relates, @sorting, @issueType, @pname, @pcode)";
                                foreach (var row in toInsert)
                                {
                                    cmd.Parameters.Clear();
                                    cmd.Parameters.AddWithValue("@created", row[0] ?? (object)DBNull.Value);
                                    cmd.Parameters.AddWithValue("@updated", row[1] ?? (object)DBNull.Value);
                                    cmd.Parameters.AddWithValue("@key", row[2] ?? (object)DBNull.Value);
                                    cmd.Parameters.AddWithValue("@summary", row[3] ?? (object)DBNull.Value);
                                    cmd.Parameters.AddWithValue("@desc", row[4] ?? (object)DBNull.Value);
                                    cmd.Parameters.AddWithValue("@parent", row[5] ?? (object)DBNull.Value);
                                    cmd.Parameters.AddWithValue("@children", row[6] ?? (object)DBNull.Value);
                                    cmd.Parameters.AddWithValue("@relates", row[7] ?? (object)DBNull.Value);
                                    if (!string.IsNullOrWhiteSpace(sortingField))
                                        cmd.Parameters.AddWithValue("@sorting", row[8] ?? (object)DBNull.Value);
                                    else
                                        cmd.Parameters.AddWithValue("@sorting", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@issueType", row[9] ?? (object)DBNull.Value);
                                    cmd.Parameters.AddWithValue("@pname", row[10] ?? (object)DBNull.Value);
                                    cmd.Parameters.AddWithValue("@pcode", row[11] ?? (object)DBNull.Value);
                                    cmd.ExecuteNonQuery();
                                }
                            }
                            transaction.Commit();
                        }
                    }

                    // Batch update
                    if (toUpdate.Count > 0)
                    {
                        using (var transaction = conn.BeginTransaction())
                        {
                            using (var cmd = conn.CreateCommand())
                            {
                                cmd.CommandText = @"
                                UPDATE issue SET
                                    CREATEDTIME = @created,
                                    UPDATEDTIME = @updated,
                                    SUMMARY = @summary,
                                    DESCRIPTION = @desc,
                                    PARENTKEY = @parent,
                                    CHILDRENKEYS = @children,
                                    RELATESKEYS = @relates,
                                    SORTINGFIELD = @sorting,
                                    ISSUETYPE = @issueType,
                                    PROJECTNAME = @pname,
                                    PROJECTCODE = @pcode
                                WHERE KEY = @key";
                                foreach (var row in toUpdate)
                                {
                                    cmd.Parameters.Clear();
                                    cmd.Parameters.AddWithValue("@created", row[0] ?? (object)DBNull.Value);
                                    cmd.Parameters.AddWithValue("@updated", row[1] ?? (object)DBNull.Value);
                                    cmd.Parameters.AddWithValue("@key", row[2] ?? (object)DBNull.Value);
                                    cmd.Parameters.AddWithValue("@summary", row[3] ?? (object)DBNull.Value);
                                    cmd.Parameters.AddWithValue("@desc", row[4] ?? (object)DBNull.Value);
                                    cmd.Parameters.AddWithValue("@parent", row[5] ?? (object)DBNull.Value);
                                    cmd.Parameters.AddWithValue("@children", row[6] ?? (object)DBNull.Value);
                                    cmd.Parameters.AddWithValue("@relates", row[7] ?? (object)DBNull.Value);
                                    if (!string.IsNullOrWhiteSpace(sortingField))
                                        cmd.Parameters.AddWithValue("@sorting", row[8] ?? (object)DBNull.Value);
                                    else
                                        cmd.Parameters.AddWithValue("@sorting", DBNull.Value);
                                    cmd.Parameters.AddWithValue("@issueType", row[9] ?? (object)DBNull.Value);
                                    cmd.Parameters.AddWithValue("@pname", row[10] ?? (object)DBNull.Value);
                                    cmd.Parameters.AddWithValue("@pcode", row[11] ?? (object)DBNull.Value);
                                    cmd.ExecuteNonQuery();
                                }
                            }
                            transaction.Commit();
                        }
                    }
                }
            }
        }

        // Always load all issues for this project from the database for tree display
        var allIssues = new List<JiraIssueDictionary>();

        // Get all project keys from configuration
        var allProjectKeys = config?.Projects?.Select(p => p.Root.Split('-')[0]).Distinct().ToList() ?? new List<string>();

        using (var conn = new SqliteConnection(connStr))
        {
            conn.Open();
            foreach (var projectKeyIter in allProjectKeys)
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT KEY, SUMMARY, PARENTKEY, CHILDRENKEYS, RELATESKEYS, SORTINGFIELD, ISSUETYPE FROM issue WHERE PROJECTCODE = @pcode";
                    cmd.Parameters.AddWithValue("@pcode", projectKeyIter);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            allIssues.Add(new JiraIssueDictionary
                            {
                                Key = reader.GetString(0),
                                Summary = reader.GetString(1),
                                ParentKey = reader.IsDBNull(2) ? null : reader.GetString(2),
                                IssueLinks = new List<JiraIssueLink>(),
                                CustomFields = null,
                                SortingField = reader.IsDBNull(5) ? null : reader.GetString(5),
                                Type = reader.IsDBNull(6) ? null : reader.GetString(6),
                            });
                        }
                    }
                    cmd.Parameters.Clear();
                }
            }
        }
        return allIssues;
    }

    // Update GetTotalIssueCountAsync to accept JQL
    private async Task<int> GetTotalIssueCountAsync(string projectKey, string jql = null)
    {
        if (string.IsNullOrWhiteSpace(jql))
            jql = $"project={projectKey} ORDER BY key ASC";
        using var httpClient = new HttpClient();
        httpClient.BaseAddress = new Uri(jiraBaseUrl);

        var authToken = Convert.ToBase64String(
            System.Text.Encoding.ASCII.GetBytes($"{username}:{apiToken}"));
        httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authToken);

        string url = $"/rest/api/2/search?jql={Uri.EscapeDataString(jql)}&maxResults=0";

        var response = await httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        int total = doc.RootElement.GetProperty("total").GetInt32();
        return total;
    }

    private static string ConvertToDbTimestamp(string jiraDate)
    {
        if (DateTime.TryParse(jiraDate, out var dt))
            return dt.ToString("yyyyMMddHHmmss");
        return "";
    }

    /// <summary>
    /// Gets the total number of issues for a project using Jira REST API and JQL.
    /// Used for progress reporting and batching.
    /// Logs the result.
    /// </summary>
    /// <param name="projectKey">The Jira project key.</param>
    /// <returns>Total issue count as integer.</returns>
    private async Task<int> GetTotalIssueCountAsync(string projectKey)
    {
        string jql = $"project={projectKey} ORDER BY key ASC";
        using var httpClient = new HttpClient();
        httpClient.BaseAddress = new Uri(jiraBaseUrl);

        var authToken = Convert.ToBase64String(
            System.Text.Encoding.ASCII.GetBytes($"{username}:{apiToken}"));
        httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authToken);

        string url = $"/rest/api/2/search?jql={Uri.EscapeDataString(jql)}&maxResults=0";

        var response = await httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        int total = doc.RootElement.GetProperty("total").GetInt32();
        return total;
    }

    /// <summary>
    /// Retrieves a Jira issue asynchronously by its issue key using the SDK.
    /// Logs the action and result.
    /// </summary>
    /// <param name="key">The issue key, e.g., "PROJ-123".</param>
    /// <returns>The Issue object representing the Jira issue.</returns>
    public async Task<Issue> GetIssueAsync(string key)
    {
        var issue = await jira.Issues.GetIssueAsync(key);
        return issue;
    }

    /// <summary>
    /// Searches for issues using a raw JQL query string asynchronously.
    /// Returns a list of matching issues.
    /// Logs the query and result count.
    /// </summary>
    /// <param name="jql">Jira Query Language (JQL) string.</param>
    /// <returns>A list of issues matching the query.</returns>
    public async Task<List<Issue>> SearchIssuesAsync(string jql)
    {
        var results = await jira.Issues.GetIssuesFromJqlAsync(jql);
        return results.ToList();
    }

    /// <summary>
    /// Updates the summary field of a specified Jira issue.
    /// Commits changes to Jira server asynchronously.
    /// Logs the update.
    /// </summary>
    /// <param name="key">Issue key.</param>
    /// <param name="newSummary">New summary text.</param>
    public async Task UpdateSummaryAsync(string key, string newSummary)
    {
        var issue = await jira.Issues.GetIssueAsync(key);
        issue.Summary = newSummary;
        await issue.SaveChangesAsync();
    }

    /// <summary>
    /// Updates the description field of a Jira issue using Atlassian Document Format (ADF).
    /// Commits changes to Jira server asynchronously.
    /// Logs the update.
    /// </summary>
    /// <param name="key">Issue key.</param>
    /// <param name="adfDescription">ADF JSON string representing the description.</param>
    public async Task UpdateDescriptionAsync(string key, string adfDescription)
    {
        var issue = await jira.Issues.GetIssueAsync(key);
        issue["description"] = adfDescription;
        await issue.SaveChangesAsync();
    }

    /// <summary>
    /// Updates a single custom or standard field for a Jira issue.
    /// Commits changes to Jira server asynchronously.
    /// Logs the update.
    /// </summary>
    /// <param name="key">Issue key.</param>
    /// <param name="fieldName">Field name or custom field ID.</param>
    /// <param name="value">Value to set; converted to string.</param>
    public async Task UpdateFieldAsync(string key, string fieldName, object value)
    {
        var issue = await jira.Issues.GetIssueAsync(key);
        issue[fieldName] = value?.ToString();
        await issue.SaveChangesAsync();
    }

    /// <summary>
    /// Updates multiple fields of a Jira issue at once.
    /// Commits changes to Jira server asynchronously.
    /// Logs each field update.
    /// </summary>
    /// <param name="key">Issue key.</param>
    /// <param name="fields">Dictionary of field names and values.</param>
    public async Task UpdateFieldsAsync(string key, Dictionary<string, object> fields)
    {
        var issue = await jira.Issues.GetIssueAsync(key);
        foreach (var field in fields)
        {
            issue[field.Key] = field.Value?.ToString();
        }
        await issue.SaveChangesAsync();
    }

    /// <summary>
    /// Retrieves values of a specific field for multiple issues.
    /// Logs each value retrieved.
    /// </summary>
    /// <param name="keys">List of issue keys.</param>
    /// <param name="fieldName">Field name to retrieve.</param>
    /// <returns>Dictionary mapping issue keys to field values as strings.</returns>
    public async Task<Dictionary<string, string>> GetFieldValuesAsync(List<string> keys, string fieldName)
    {
         var result = new Dictionary<string, string>();
        foreach (var key in keys)
        {
            var issue = await jira.Issues.GetIssueAsync(key);
            result[key] = issue[fieldName]?.ToString();
        }
        return result;
    }

    /// <summary>
    /// Retrieves the changelog history for a given Jira issue.
    /// Logs the number of changelog entries fetched.
    /// </summary>
    /// <param name="issueKey">Issue key.</param>
    /// <returns>An enumerable of IssueChangeLog objects detailing changes.</returns>
    public async Task<IEnumerable<IssueChangeLog>> GetChangeLogsAsync(string issueKey)
    {
        var issue = await jira.Issues.GetIssueAsync(issueKey);
        var logs = await issue.GetChangeLogsAsync();
        return logs;
    }

    /// <summary>
    /// Converts HTML content to Atlassian Document Format (ADF).
    /// This is a mock implementation; replace with a real converter for production.
    /// Logs the conversion.
    /// </summary>
    /// <param name="html">HTML string to convert.</param>
    /// <returns>ADF JSON string.</returns>
    private static string ConvertHtmlToAdf(string html)
    {
        return @"{
            ""version"": 1,
            ""type"": ""doc"",
            ""content"": [
                {
                    ""type"": ""paragraph"",
                    ""content"": [
                        {
                            ""type"": ""text"",
                            ""text"": ""Converted HTML to ADF""
                        }
                    ]
                }
            ]
        }";
    }

    /// <summary>
    /// Updates the parent link of a Jira issue by removing the old parent link and creating a new one.
    /// Uses Jira REST API for link management.
    /// Throws if the link type is not found or REST calls fail.
    /// Logs all actions and errors.
    /// </summary>
    /// <param name="childKey">Key of the child issue.</param>
    /// <param name="oldParentKey">Key of the old parent issue.</param>
    /// <param name="newParentKey">Key of the new parent issue.</param>
    /// <param name="linkTypeName">Name of the link type.</param>
    public async Task UpdateParentLinkAsync(string childKey, string oldParentKey, string newParentKey, string linkTypeName)
    {
        var client = GetJiraClient();

        try
        {
            var response = await client.GetAsync($"{jiraBaseUrl}/rest/api/2/issue/{childKey}");
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Failed to fetch issue '{childKey}' from Jira. Status: {response.StatusCode}");
            }

            var json = JObject.Parse(await response.Content.ReadAsStringAsync());

            var links = json["fields"]?["issuelinks"] as JArray;
            bool linkTypeFound = false;
            if (links != null)
            {
                foreach (var link in links)
                {
                    var typeName = link["type"]?["name"]?.ToString();
                    var inwardIssue = link["inwardIssue"]?["key"]?.ToString();

                    if (typeName?.Equals(linkTypeName, StringComparison.OrdinalIgnoreCase) == true)
                        linkTypeFound = true;

                    bool match = typeName?.Equals(linkTypeName, StringComparison.OrdinalIgnoreCase) == true &&
                                 (inwardIssue == oldParentKey);

                    if (match && link["id"] != null)
                    {
                        string linkId = link["id"]!.ToString();
                        var delResponse = await client.DeleteAsync($"{jiraBaseUrl}/rest/api/2/issueLink/{linkId}");
                        if (!delResponse.IsSuccessStatusCode)
                        {
                            throw new InvalidOperationException($"Failed to delete old parent link for issue '{childKey}'. Status: {delResponse.StatusCode}");
                        }
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(linkTypeName) && !linkTypeFound)
            {
                throw new InvalidOperationException(
                    $"The link type '{linkTypeName}' specified in configuration does not exist in Jira for issue '{childKey}'.\n" +
                    $"Please check your configuration and Jira link types.");
            }

            if (!string.IsNullOrWhiteSpace(newParentKey))
            {
                var payload = new
                {
                    type = new { name = linkTypeName },
                    inwardIssue = new { key = newParentKey },
                    outwardIssue = new { key = childKey }
                };

                var content = new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(payload), System.Text.Encoding.UTF8, "application/json");
                var postResponse = await client.PostAsync($"{jiraBaseUrl}/rest/api/2/issueLink", content);
                if (!postResponse.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(
                        $"Failed to create new parent link for issue '{childKey}' with link type '{linkTypeName}'. Status: {postResponse.StatusCode}");
                }
             }
        }
        catch (Exception ex)
        {
             throw new InvalidOperationException(
                $"Error updating parent link for issue '{childKey}'.\nDetails: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Creates and configures a new HttpClient for Jira REST API calls.
    /// Sets authentication and content headers.
    /// Logs the creation.
    /// </summary>
    /// <returns>Configured HttpClient instance.</returns>
    private HttpClient GetJiraClient()
    {
        var client = new HttpClient();
        var byteArray = System.Text.Encoding.ASCII.GetBytes($"{jiraEmail}:{jiraToken}");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    /// <summary>
    /// Updates the sequence field of a Jira issue.
    /// If the sorting field is empty, skips the update.
    /// Resolves custom field names if needed.
    /// Logs all actions and errors.
    /// </summary>
    /// <param name="issueKey">Issue key.</param>
    /// <param name="sequence">Sequence value to set.</param>
    public async Task UpdateSequenceFieldAsync(string issueKey, int sequence)
    {
        var dashIndex = issueKey.IndexOf('-');
        if (dashIndex < 1)
        {
            throw new ArgumentException("Invalid issue key format.", nameof(issueKey));
        }
        var keyPrefix = issueKey.Substring(0, dashIndex);

        var projectConfig = config?.Projects?
            .FirstOrDefault(p => p.Root.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase));
        if (projectConfig == null)
        {
            throw new InvalidOperationException($"Project config not found for issue key prefix: {keyPrefix}");
        }

        var sortingField = projectConfig.SortingField;
        if (string.IsNullOrWhiteSpace(sortingField))
        {
            return;
        }

        string fieldName = sortingField;

        try
        {
            if (sortingField.StartsWith("customfield_", StringComparison.OrdinalIgnoreCase))
            {
                fieldName = await GetCustomFieldNameAsync(sortingField);
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"The field '{fieldName}' specified in configuration could not be read for '{issueKey}'.\n" +
                $"Please check your configuration and Jira custom fields.\n\nDetails: {ex.Message}", ex);
        }

        try
        {
            var issue = await jira.Issues.GetIssueAsync(issueKey);
            issue[fieldName] = sequence.ToString();
            await issue.SaveChangesAsync();
         }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"The field '{fieldName}' specified in configuration does not exist in Jira for issue '{issueKey}'.\n" +
                $"Please check your configuration and Jira custom fields.\n\nDetails: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Cache for mapping custom field IDs to their names.
    /// Used to avoid repeated REST API calls for field name resolution.
    /// </summary>
    private static Dictionary<string, string> customFieldIdToNameCache = new();

    /// <summary>
    /// Resolves a Jira custom field ID to its display name using Jira REST API.
    /// Uses cache for performance.
    /// Throws if the field is not found.
    /// Logs all actions and errors.
    /// </summary>
    /// <param name="fieldId">Custom field ID (e.g., "customfield_10000").</param>
    /// <returns>Custom field display name.</returns>
    private async Task<string> GetCustomFieldNameAsync(string fieldId)
    {
        if (customFieldIdToNameCache.TryGetValue(fieldId, out var name))
        {
            return name;
        }

        using var client = new HttpClient();
        client.BaseAddress = new Uri(jiraBaseUrl);
        var authToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{apiToken}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);

        var response = await client.GetAsync("/rest/api/3/field");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(json);
        foreach (var field in doc.RootElement.EnumerateArray())
        {
            var id = field.GetProperty("id").GetString();
            var fname = field.GetProperty("name").GetString();
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(fname))
            {
                customFieldIdToNameCache[id] = fname;
                if (id.Equals(fieldId, StringComparison.OrdinalIgnoreCase))
                {
                    return fname;
                }
            }
        }

        throw new InvalidOperationException($"Custom field with ID '{fieldId}' not found on the JIRA server.");
    }

    /// <summary>
    /// Creates a Jira issue and links it as a child or sibling to the specified node.
    /// Uses Atlassian.Jira SDK for issue creation and linking.
    /// Throws if project config is not found.
    /// Logs all actions and errors.
    /// </summary>
    /// <param name="selectedKey">The key of the selected node (parent for child, sibling for sibling).</param>
    /// <param name="linkMode">"Child" or "Sibling".</param>
    /// <param name="issueType">The issue type to create.</param>
    /// <param name="summary">The summary for the new issue.</param>
    /// <param name="config">The loaded Jira configuration root.</param>
    /// <returns>The new issue key if successful, null otherwise.</returns>
    public async Task<string?> CreateAndLinkJiraIssueAsync(
        string selectedKey,
        string linkMode,
        string issueType,
        string summary,
        frmMain.JiraConfigRoot config)
    {
        try
        {
            var dashIndex = selectedKey.IndexOf('-');
            var keyPrefix = dashIndex > 0 ? selectedKey.Substring(0, dashIndex) : selectedKey;
            var projectConfig = config?.Projects?.FirstOrDefault(p => p.Root.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase));
            if (projectConfig == null)
            {
                throw new InvalidOperationException("Project config not found for selected key.");
            }

            string projectKey = projectConfig.Root.Split("-")[0];
            string linkTypeName = projectConfig.LinkTypeName;

            var issue = jira.CreateIssue(projectKey);
            issue.Type = issueType;
            issue.Summary = summary;

            await issue.SaveChangesAsync();

            await issue.LinkToIssueAsync(selectedKey, linkTypeName);

            return issue.Key.Value;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Links a list of issues as "Relates" to a base issue using Jira REST API.
    /// Throws if any link fails.
    /// Logs all actions and errors.
    /// </summary>
    /// <param name="baseKey">The key of the base issue.</param>
    /// <param name="relatedKeys">List of issue keys to link as related.</param>
    public async Task LinkRelatedIssuesAsync(string baseKey, List<string> relatedKeys)
    {
        var client = GetJiraClient();
        string linkTypeName = "Relates";

        foreach (var relatedKey in relatedKeys)
        {
            var payload = new
            {
                type = new { name = linkTypeName },
                inwardIssue = new { key = baseKey },
                outwardIssue = new { key = relatedKey }
            };

            var content = new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var response = await client.PostAsync($"{jiraBaseUrl}/rest/api/2/issueLink", content);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Failed to link issue '{relatedKey}' as related to '{baseKey}'. Status: {response.StatusCode}");
            }
        }
    }
}
