using Atlassian.Jira;
using Microsoft.Data.Sqlite;
using Monovera;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using static Monovera.frmMain;

/// <summary>
/// Service class to interact with Jira via Atlassian.Jira SDK and REST API.
/// Provides asynchronous methods for retrieving, updating, and querying Jira issues,
/// as well as linking issues and managing custom fields.
/// All actions are logged using AppLogger for traceability.
/// </summary>
public class JiraService
{
    private static readonly string DataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
    private static readonly string AttachmentsDir = Path.Combine(DataDir, "attachments");
    private static readonly string DatabasePath = Path.Combine(DataDir, "monovera.sqlite");

    static JiraService()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            Directory.CreateDirectory(AttachmentsDir);
        }
        catch { /* best effort */ }
    }

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
    /// 
    public JiraService(string jiraUrl, string email, string apiToken)
    {
        this.username = email;
        this.apiToken = apiToken;
        this.jiraBaseUrl = jiraUrl.TrimEnd('/');
        this.jira = Jira.CreateRestClient(jiraUrl, email, apiToken);
    }

    /// <summary>
    /// Asynchronously tests the connection to the configured Jira instance using the Atlassian.Jira SDK.
    /// Attempts to retrieve the current authenticated user to verify credentials and network connectivity.
    /// Returns <c>true</c> if the connection and authentication succeed, otherwise <c>false</c>.
    /// Any exceptions encountered during the process are caught and result in a <c>false</c> return value.
    /// </summary>
    /// <returns>
    /// <c>true</c> if the Jira connection and authentication are successful; <c>false</c> if an error occurs.
    /// </returns>
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
    /// Asynchronously retrieves the display name of the currently authenticated Jira user.
    /// Uses the Atlassian.Jira SDK to fetch user information.
    /// Returns <c>null</c> if the user information cannot be retrieved due to authentication failure or network issues.
    /// Any exceptions encountered are caught and result in a <c>null</c> return value.
    /// </summary>
    /// <returns>
    /// The display name of the connected Jira user, or <c>null</c> if the user information cannot be fetched.
    /// </returns>
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
    /// Asynchronously checks the current user's permissions for creating and editing issues in a specified Jira project.
    /// Uses the Jira REST API to query the "CREATE_ISSUES" and "EDIT_ISSUES" permissions for the given project key.
    /// Returns a <see cref="WritePermissionsResult"/> object indicating whether the user can create and/or edit issues.
    /// If the permission check fails due to network errors or authentication issues, returns <c>null</c>.
    /// All errors are caught and logged for traceability.
    /// </summary>
    /// <param name="projectKey">The key of the Jira project to check permissions for (e.g., "PROJ").</param>
    /// <returns>
    /// A <see cref="WritePermissionsResult"/> object containing the user's create and edit permission flags,
    /// or <c>null</c> if the permission check fails.
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
    /// Asynchronously synchronizes and loads all Jira issues for a specified project into a local SQLite cache.
    /// Issues are fetched using JQL and retrieved in parallel batches for performance.
    /// If <paramref name="forceSync"/> is <c>true</c>, the cache is ignored and all issues are fetched from Jira.
    /// If <paramref name="updateType"/> is "Difference", only issues updated since the last sync are fetched.
    /// Progress is reported via the <paramref name="progressUpdate"/> callback, which receives completed count, total count, and percent complete.
    /// Issues are stored in the local database, and the method returns a list of <see cref="JiraIssueDictionary"/> objects representing the issues.
    /// All major steps, errors, and results are logged for traceability.
    /// </summary>
    /// <param name="projectKey">The Jira project key (e.g., "PROJ").</param>
    /// <param name="projectName">The display name of the Jira project.</param>
    /// <param name="sortingField">The field used for sorting issues (e.g., "created" or a custom field).</param>
    /// <param name="linkTypeName">The type of issue link to include (default: "Blocks").</param>
    /// <param name="forceSync">If <c>true</c>, ignores the local cache and fetches all issues from Jira.</param>
    /// <param name="progressUpdate">Optional callback for reporting progress: (completed, total, percent).</param>
    /// <param name="maxParallelism">Maximum number of parallel requests to Jira (default: 250).</param>
    /// <param name="updateType">Type of update: "Complete" for full sync, "Difference" for incremental sync.</param>
    /// <returns>
    /// A list of <see cref="JiraIssueDictionary"/> objects representing the issues in the project.
    /// </returns>
    public async Task<List<JiraIssueDictionary>> UpdateLocalIssueRepositoryAndLoadTree(
    string projectKey,
    string projectName,
    string sortingField,
    string linkTypeName = "Blocks",
    bool forceSync = false,
    Action<int, int, double> progressUpdate = null,
    int maxParallelism = 250,
    string updateType = "Complete")
    {
        var connStr = $"Data Source={DatabasePath};";

        bool dbExists = File.Exists(DatabasePath);
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
                    PROJECTCODE TEXT,
                    STATUS TEXT,
                    HISTORY TEXT,
                    ATTACHMENTS BLOB
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
                    cmd.CommandText = $"SELECT COUNT(1) FROM issue WHERE PROJECTCODE=@pcode";
                    cmd.Parameters.AddWithValue("@pcode", projectKey);
                    tableEmpty = Convert.ToInt32(cmd.ExecuteScalar()) == 0;
                }
            }
        }

        int totalCount = 0;
        List<string> relevantKeys = null;
        string jql;

        if (tableEmpty || forceSync)
        {
            if (updateType == "Difference")
            {
                string latestUpdateTimeOnDatabase = null;
                using (var conn = new SqliteConnection(connStr))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT MAX(UPDATEDTIME) FROM issue WHERE PROJECTCODE = @pcode";
                        cmd.Parameters.AddWithValue("@pcode", projectKey);
                        var result = cmd.ExecuteScalar();
                        latestUpdateTimeOnDatabase = result != DBNull.Value && result != null ? result.ToString() : null;
                    }
                }

                string latestUpdateTimeToCheckInJira = null;
                if (!string.IsNullOrWhiteSpace(latestUpdateTimeOnDatabase) && latestUpdateTimeOnDatabase.Length == 14)
                {
                    latestUpdateTimeToCheckInJira = DateTime.ParseExact(latestUpdateTimeOnDatabase, "yyyyMMddHHmmss", null).ToString("yyyy-MM-dd HH:mm");
                }

                if (!string.IsNullOrWhiteSpace(latestUpdateTimeToCheckInJira))
                    jql = $"project={projectKey} AND (updated > \"{latestUpdateTimeToCheckInJira}\")";
                else
                    jql = $"project={projectKey}";

                relevantKeys = new List<string>();
                using (var client = new HttpClient())
                {
                    client.BaseAddress = new Uri(jiraBaseUrl);
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                        "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{apiToken}"))
                    );
                    string url = $"{jiraBaseUrl}/rest/api/2/search?jql={Uri.EscapeDataString(jql)}&fields=key,updated&maxResults=1000";
                    var response = await RetryWithMessageBoxAsync(
                        () => client.GetStringAsync(url),
                        "Failed to fetch issue keys from Jira."
                    );
                    var json = JObject.Parse(response);

                    var dbUpdatedTimes = new Dictionary<string, string>();
                    using (var conn = new SqliteConnection(connStr))
                    {
                        conn.Open();
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = "SELECT KEY, UPDATEDTIME FROM issue WHERE PROJECTCODE = @pcode";
                            cmd.Parameters.AddWithValue("@pcode", projectKey);
                            using (var reader = cmd.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    dbUpdatedTimes[reader.GetString(0)] = reader.GetString(1);
                                }
                            }
                        }
                    }

                    foreach (var issue in json["issues"])
                    {
                        string key = (string)issue["key"];
                        string jiraUpdated = issue["fields"]["updated"] != null
                            ? ConvertToDbTimestamp((string)issue["fields"]["updated"])
                            : null;
                        string dbUpdated = dbUpdatedTimes.TryGetValue(key, out var val) ? val : null;

                        if (!string.IsNullOrEmpty(jiraUpdated) && (string.IsNullOrEmpty(dbUpdated) || string.Compare(jiraUpdated, dbUpdated, StringComparison.Ordinal) > 0))
                        {
                            relevantKeys.Add(key);
                        }
                    }

                    totalCount = relevantKeys.Count;
                }
            }
            else // Complete
            {
                jql = $"project={projectKey}";
                totalCount = await RetryWithMessageBoxAsync(
                    () => GetTotalIssueCountAsync(projectKey, jql),
                    "Failed to fetch total issue count from Jira."
                );
            }

            if (totalCount > 0)
            {
                const int pageSize = 100;
                int totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
                var pageStarts = Enumerable.Range(0, totalPages).Select(i => i * pageSize).ToList();

                int completed = 0;
                var throttler = new SemaphoreSlim(Math.Min(maxParallelism, 4));

                var existingKeys = new HashSet<string>();
                using (var conn = new SqliteConnection(connStr))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT KEY FROM issue WHERE PROJECTCODE = @pcode";
                        cmd.Parameters.AddWithValue("@pcode", projectKey);
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                existingKeys.Add(reader.GetString(0));
                            }
                        }
                    }
                }

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
                        client.Timeout = TimeSpan.FromMinutes(15);
                        client.BaseAddress = new Uri(jiraBaseUrl);
                        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                            "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{apiToken}"))
                        );

                        var response = await RetryWithMessageBoxAsync(
                            () => client.GetStringAsync(url),
                            "Failed to fetch issues from Jira."
                        );
                        var json = JObject.Parse(response);
                        var issues = new List<object[]>();

                        foreach (var issue in json["issues"])
                        {
                            string key = (string)issue["key"];
                            if (updateType == "Difference" && relevantKeys != null && relevantKeys.Count > 0 && !relevantKeys.Contains(key))
                            {
                                continue;
                            }

                            string created = ConvertToDbTimestamp((string)issue["fields"]["created"]);
                            string updated = ConvertToDbTimestamp((string)issue["fields"]["updated"]);
                            string summary = (string)issue["fields"]["summary"];
                            string status = (string)issue["fields"]["status"]["name"].ToString();
                            string issueType = (string)issue["fields"]["issuetype"]?["name"] ?? "";

                            // Fetch rendered HTML description, changelog, and offline attachments section (stored to DB)
                            string htmlDescription = "";
                            string history = "[]";
                            string attachmentsHtml = "";

                            JObject detailJson = null;
                            try
                            {
                                var detailResponseStr = await RetryWithMessageBoxAsync(
                                    () => client.GetStringAsync($"/rest/api/2/issue/{key}?expand=renderedFields,changelog"),
                                    $"Failed to fetch details for issue {key} from Jira."
                                );
                                detailJson = JObject.Parse(detailResponseStr);
                                htmlDescription = detailJson["renderedFields"]?["description"]?.ToString() ?? issue["fields"]["description"]?.ToString() ?? "";

                                if (detailJson["changelog"]?["histories"] is JArray histories)
                                {
                                    history = BuildSlimChangelog(histories);
                                }

                                // Handle attachments: ensure directories exist, then download and overwrite files (no deletion)
                                // Handle attachments: ensure directories exist, then download and overwrite files (no deletion)
                                if (detailJson["fields"]?["attachment"] is JArray attachArray)
                                {
                                    string attachmentsRoot = AttachmentsDir;
                                    string issueDir = Path.Combine(attachmentsRoot, key);

                                    // Ensure directories exist (do not delete existing content) ONLY if there are attachments
                                    if (attachArray.Count > 0)
                                    {
                                        Directory.CreateDirectory(attachmentsRoot);
                                        Directory.CreateDirectory(issueDir);
                                    }

                                    if (attachArray.Count > 0)
                                    {
                                        var relPaths = new List<string>();

                                        foreach (var att in attachArray)
                                        {
                                            string filename = att["filename"]?.ToString() ?? "attachment";
                                            string id = att["id"]?.ToString() ?? "";
                                            string contentUrl = att["content"]?.ToString();

                                            if (string.IsNullOrWhiteSpace(filename))
                                                filename = "attachment";

                                            string safeName = SanitizeFileName(filename);
                                            string uniqueName = (!string.IsNullOrEmpty(id) ? $"{id}_" : "") + safeName;
                                            string absPath = Path.Combine(issueDir, uniqueName);

                                            if (!string.IsNullOrWhiteSpace(contentUrl))
                                            {
                                                try
                                                {
                                                    var attBytes = await RetryWithMessageBoxAsync(
                                                        () => client.GetByteArrayAsync(contentUrl),
                                                        $"Failed to download attachment for issue {key}."
                                                    );

                                                    // Overwrite existing if present
                                                    await File.WriteAllBytesAsync(absPath, attBytes);

                                                    // Generate/overwrite thumbnail for images
                                                    if (IsImageExtension(uniqueName))
                                                    {
                                                        TryGenerateThumbnail(absPath, Path.Combine(issueDir, ".thumbs"), 320);
                                                    }
                                                }
                                                catch
                                                {
                                                    // continue with other files
                                                }
                                            }

                                            if (File.Exists(absPath))
                                            {
                                                // Store only relative path from attachments folder
                                                string relPath = $"{key}/{uniqueName}";
                                                relPaths.Add(relPath);
                                            }
                                        }

                                        attachmentsHtml = BuildOfflineAttachmentsHtml(relPaths);
                                    }
                                    else
                                    {
                                        attachmentsHtml = "<div class='no-attachments'>No attachments found.</div></section>\r\n</details>";
                                    }

                                    // use attachmentsHtml variable below (already present in your method)
                                }
                                else
                                {
                                    attachmentsHtml = "<div class='no-attachments'>No attachments found.</div></section>\r\n</details>";
                                }
                            }
                            catch
                            {
                                htmlDescription = issue["fields"]["description"]?.ToString() ?? "";
                                attachmentsHtml = "<div class='no-attachments'>No attachments found.</div></section>\r\n</details>";
                            }

                            // Transform description HTML
                            htmlDescription = BuildHTMLSection_DESCRIPTION(htmlDescription, key);

                            string sortingFieldValue = null;
                            if (hasSortingField && issue["fields"][sortingField] != null)
                            {
                                sortingFieldValue = issue["fields"][sortingField].ToString();
                            }

                            string parentKey = "";
                            string childrenKeys = "";
                            string relatesKeys = "";
                            if (issue["fields"]["issuelinks"] is JArray linksArray)
                            {
                                var relates = linksArray
                                    .Where(link => (string)link["type"]?["name"] == "Relates")
                                    .Select(link =>
                                        (string)link["outwardIssue"]?["key"] ?? (string)link["inwardIssue"]?["key"])
                                    .Where(k => !string.IsNullOrWhiteSpace(k))
                                    .Distinct()
                                    .ToList();
                                relatesKeys = string.Join(",", relates);

                                var children = linksArray
                                    .Where(link => (string)link["type"]?["name"] == linkTypeName && link["outwardIssue"]?["key"] != null)
                                    .Select(link => (string)link["outwardIssue"]["key"])
                                    .Where(k => !string.IsNullOrWhiteSpace(k))
                                    .Distinct()
                                    .ToList();
                                childrenKeys = string.Join(",", children);

                                var parentLink = linksArray
                                    .FirstOrDefault(link => (string)link["type"]?["name"] == linkTypeName && link["inwardIssue"]?["key"] != null);
                                parentKey = parentLink != null ? (string)parentLink["inwardIssue"]["key"] : "";
                            }

                            issues.Add(new object[]
                            {
                            created,
                            updated,
                            key,
                            summary,
                            htmlDescription,
                            parentKey,
                            childrenKeys,
                            relatesKeys,
                            sortingFieldValue,
                            issueType,
                            projectName,
                            projectKey,
                            status,
                            history,
                            attachmentsHtml
                            });

                            int currentCompleted = Interlocked.Increment(ref completed);
                            double percent = totalCount > 0 ? (currentCompleted * 100.0 / totalCount) : 100.0;
                            progressUpdate?.Invoke(currentCompleted, totalCount, percent);
                        }

                        using (var conn = new SqliteConnection(connStr))
                        {
                            conn.Open();
                            var toInsert = new List<object[]>();
                            var toUpdate = new List<object[]>();

                            foreach (var row in issues)
                            {
                                string key = (string)row[2];
                                if (existingKeys.Contains(key))
                                    toUpdate.Add(row);
                                else
                                    toInsert.Add(row);
                            }

                            if (toInsert.Count > 0)
                            {
                                var transaction = conn.BeginTransaction();
                                using (var cmd = conn.CreateCommand())
                                {
                                    cmd.CommandText = @"
                                INSERT INTO issue
                                (CREATEDTIME, UPDATEDTIME, KEY, SUMMARY, DESCRIPTION, PARENTKEY, CHILDRENKEYS, RELATESKEYS, SORTINGFIELD, ISSUETYPE, PROJECTNAME, PROJECTCODE, STATUS, HISTORY, ATTACHMENTS)
                                VALUES (@created, @updated, @key, @summary, @desc, @parent, @children, @relates, @sorting, @issueType, @pname, @pcode, @status, @history, @attachments)";
                                    int batchCount = 0;
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
                                        cmd.Parameters.AddWithValue("@sorting", row[8] ?? (object)DBNull.Value);
                                        cmd.Parameters.AddWithValue("@issueType", row[9] ?? (object)DBNull.Value);
                                        cmd.Parameters.AddWithValue("@pname", row[10] ?? (object)DBNull.Value);
                                        cmd.Parameters.AddWithValue("@pcode", row[11] ?? (object)DBNull.Value);
                                        cmd.Parameters.AddWithValue("@status", row[12] ?? (object)DBNull.Value);
                                        cmd.Parameters.AddWithValue("@history", row[13] ?? (object)DBNull.Value);
                                        cmd.Parameters.AddWithValue("@attachments", row[14] ?? (object)DBNull.Value);
                                        cmd.ExecuteNonQuery();

                                        batchCount++;
                                        if (batchCount % 100 == 0)
                                        {
                                            transaction.Commit();
                                            transaction.Dispose();
                                            transaction = conn.BeginTransaction();
                                        }
                                    }
                                    transaction.Commit();
                                    transaction.Dispose();
                                }
                            }

                            if (toUpdate.Count > 0)
                            {
                                var transaction = conn.BeginTransaction();
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
                                    PROJECTCODE = @pcode,
                                    STATUS = @status,
                                    HISTORY = @history, 
                                    ATTACHMENTS = @attachments
                                WHERE KEY = @key";
                                    int batchCount = 0;
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
                                        cmd.Parameters.AddWithValue("@sorting", row[8] ?? (object)DBNull.Value);
                                        cmd.Parameters.AddWithValue("@issueType", row[9] ?? (object)DBNull.Value);
                                        cmd.Parameters.AddWithValue("@pname", row[10] ?? (object)DBNull.Value);
                                        cmd.Parameters.AddWithValue("@pcode", row[11] ?? (object)DBNull.Value);
                                        cmd.Parameters.AddWithValue("@status", row[12] ?? (object)DBNull.Value);
                                        cmd.Parameters.AddWithValue("@history", row[13] ?? (object)DBNull.Value);
                                        cmd.Parameters.AddWithValue("@attachments", row[14] ?? (object)DBNull.Value);
                                        cmd.ExecuteNonQuery();

                                        batchCount++;
                                        if (batchCount % 100 == 0)
                                        {
                                            transaction.Commit();
                                            transaction.Dispose();
                                            transaction = conn.BeginTransaction();
                                        }
                                    }
                                    transaction.Commit();
                                    transaction.Dispose();
                                }
                            }
                        }
                    }
                    finally
                    {
                        throttler.Release();
                    }
                }).ToList();

                await Task.WhenAll(tasks);
            }
        }

        var allIssues = new List<JiraIssueDictionary>();
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

        // Kick off background cleanup: keep only files referenced in DB DESCRIPTION/ATTACHMENTS
        StartAttachmentsCleanupFromDb(connStr);

        return allIssues;

        // Local helpers
        static string SanitizeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        static string GetMimeTypeByExtension(string fileName)
        {
            string ext = Path.GetExtension(fileName).ToLowerInvariant();
            return ext switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                ".webp" => "image/webp",
                ".svg" => "image/svg+xml",
                ".pdf" => "application/pdf",
                ".txt" => "text/plain",
                ".csv" => "text/csv",
                ".json" => "application/json",
                ".xml" => "application/xml",
                ".zip" => "application/zip",
                ".rar" => "application/vnd.rar",
                ".7z" => "application/x-7z-compressed",
                ".doc" => "application/msword",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".xls" => "application/vnd.ms-excel",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ".ppt" => "application/vnd.ms-powerpoint",
                ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                _ => "application/octet-stream"
            };
        }

        // Images only (no base64; show relative thumbnail if available)
        string BuildOfflineAttachmentsHtml(List<string> relPaths)
        {
            if (relPaths == null || relPaths.Count == 0)
                return "<div class='no-attachments'>No attachments found.</div></section>\r\n</details>";

            string attachmentsRoot = AttachmentsDir;
            var sb = new StringBuilder();
            sb.AppendLine(@"
    <div class='attachments-strip-wrapper'>
      <button class='scroll-btn left' onclick='scrollAttachments(-1)' aria-label='Scroll left'>&#8592;</button>
      <div class='attachments-strip' id='attachmentsStrip'>
");

            foreach (var relPath in relPaths)
            {
                string absPath = Path.Combine(attachmentsRoot, relPath);
                string filename = Path.GetFileName(absPath);
                string mimeType = GetMimeTypeByExtension(filename);
                string created = "";
                string sizeStr = "";
                try
                {
                    var fi = new FileInfo(absPath);
                    if (fi.Exists)
                    {
                        created = fi.CreationTime.ToString("yyyy-MM-dd HH:mm:ss");
                        sizeStr = fi.Length.ToString("N0");
                    }
                }
                catch { }

                string previewHtml;
                if (IsImageExtension(filename))
                {
                    string thumbAbs = Path.Combine(Path.GetDirectoryName(absPath) ?? "", ".thumbs", Path.GetFileName(absPath));
                    string thumbRel = relPath.Replace(filename, $".thumbs/{filename}");
                    if (File.Exists(thumbAbs))
                    {
                        previewHtml = $@"
<a href='#' class='offline-preview-image' data-src='attachments/{relPath}'>
  <img src='attachments/{thumbRel}' class='attachment-img' alt='{System.Net.WebUtility.HtmlEncode(filename)}' />
</a>";
                    }
                    else
                    {
                        previewHtml = $@"<div class='attachment-icon' title='{System.Net.WebUtility.HtmlEncode(filename)}' style='font-size:36px;line-height:36px;'>🖼️</div>";
                    }
                }
                else
                {
                    string icon = GetUnicodeIconByExtension(filename);
                    previewHtml = $@"<div class='attachment-icon' title='{System.Net.WebUtility.HtmlEncode(filename)}' style='font-size:36px;line-height:36px;'>{icon}</div>";
                }

                // Use relative path in href and data-filepath
                string relHref = $"attachments/{relPath}";

                sb.AppendLine($@"
<div class='attachment-card'>
  {previewHtml}
  <div class='attachment-filename'>{System.Net.WebUtility.HtmlEncode(filename)}</div>
  <div class='attachment-meta'>
    <span>Type: {System.Net.WebUtility.HtmlEncode(mimeType)}</span><br/>
    <span>Size: {sizeStr} bytes</span><br/>
    <span>Created: {System.Net.WebUtility.HtmlEncode(created)}</span>
  </div>
  <a href='{relHref}' class='download-btn' data-filepath='{relHref}'>Download</a>
</div>");
            }

            sb.AppendLine(@"
      </div>
      <button class='scroll-btn right' onclick='scrollAttachments(1)' aria-label='Scroll right'>&#8594;</button>
    </div>

    <!-- Lightbox for image preview (offline) -->
    <div id='attachmentLightbox' class='attachment-lightbox' style='display:none;' onclick='closeAttachmentLightbox()'>
      <img id='lightboxImg' src='' alt='Preview' />
    </div>

<script>
  function scrollAttachments(direction) {
    var strip = document.getElementById('attachmentsStrip');
    if (strip) {
      strip.scrollLeft += direction * 220;
    }
  }

  // Offline preview handler (no host messaging)
  document.querySelectorAll('.offline-preview-image').forEach(link => {
    link.addEventListener('click', function(e) {
      e.preventDefault();
      var src = link.getAttribute('data-src'); // relative
      var lightbox = document.getElementById('attachmentLightbox');
      var img = document.getElementById('lightboxImg');
      img.src = src;
      lightbox.style.display = 'flex';
    });
  });

  function closeAttachmentLightbox() {
    document.getElementById('attachmentLightbox').style.display = 'none';
    document.getElementById('lightboxImg').src = '';
  }
</script>
");
            return sb.ToString();
        }
        static bool IsImageExtension(string fileName)
        {
            string ext = Path.GetExtension(fileName).ToLowerInvariant();
            return ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp";
        }

        static string GetUnicodeIconByExtension(string fileName)
        {
            string ext = Path.GetExtension(fileName).ToLowerInvariant();
            return ext switch
            {
                ".pdf" => "📕",
                ".doc" or ".docx" => "📝",
                ".xls" or ".xlsx" => "📊",
                ".ppt" or ".pptx" => "📽️",
                ".zip" or ".rar" or ".7z" => "🗜️",
                ".mp4" or ".mov" or ".avi" or ".mkv" or ".webm" => "🎬",
                ".mp3" or ".wav" or ".flac" => "🎵",
                _ => "📄"
            };
        }

        // Generate a thumbnail for images into {issueDir}/.thumbs/{fileName}
        static void TryGenerateThumbnail(string sourceAbsPath, string thumbsAbsDir, int targetWidth)
        {
            try
            {
                Directory.CreateDirectory(thumbsAbsDir);
                string thumbPath = Path.Combine(thumbsAbsDir, Path.GetFileName(sourceAbsPath));

                using var src = Image.FromFile(sourceAbsPath);
                int w = src.Width, h = src.Height;
                if (w <= targetWidth)
                {
                    // No upscaling; copy original as thumbnail
                    File.Copy(sourceAbsPath, thumbPath, overwrite: true);
                    return;
                }

                int newW = targetWidth;
                int newH = (int)Math.Round(h * (targetWidth / (double)w));

                using var bmp = new Bitmap(newW, newH);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.CompositingQuality = CompositingQuality.HighQuality;
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(src, new Rectangle(0, 0, newW, newH));
                }

                bmp.Save(thumbPath, ImageFormat.Jpeg);
            }
            catch
            {
                // Ignore thumbnail errors; UI will fallback to icon.
            }
        }
    }

    // Add to JiraService class (near other private helpers)

    private static readonly object AttachmentsCleanupSync = new();

    private void StartAttachmentsCleanupFromDb(string connStr)
    {
        // Run asynchronously; serialize runs to avoid overlapping cleanups
        Task.Run(() =>
        {
            lock (AttachmentsCleanupSync)
            {
                try { CleanupAttachmentsFolderFromDbReferences(connStr); }
                catch { /* swallow cleanup errors */ }
            }
        });
    }

    private void CleanupAttachmentsFolderFromDbReferences(string connStr)
    {
        string attachmentsRoot = AttachmentsDir;
        if (!Directory.Exists(attachmentsRoot))
            return;

        var keepSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using (var conn = new SqliteConnection(connStr))
        {
            conn.Open();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT DESCRIPTION, ATTACHMENTS FROM issue";
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string desc = reader.IsDBNull(0) ? null : reader.GetString(0);
                        string att = reader.IsDBNull(1) ? null : reader.GetString(1);

                        foreach (var rel in ExtractAttachmentRelativeUrls(desc))
                        {
                            var abs = SafeAbsUnderAttachments(attachmentsRoot, rel);
                            if (abs != null) keepSet.Add(abs);
                        }
                        foreach (var rel in ExtractAttachmentRelativeUrls(att))
                        {
                            var abs = SafeAbsUnderAttachments(attachmentsRoot, rel);
                            if (abs != null) keepSet.Add(abs);
                        }
                    }
                }
            }
        }

        foreach (var file in Directory.EnumerateFiles(attachmentsRoot, "*", SearchOption.AllDirectories))
        {
            if (keepSet.Count == 0) break;
            if (!keepSet.Contains(file))
            {
                try { File.Delete(file); } catch { }
            }
        }

        PruneEmptyDirectories(attachmentsRoot);

        static string SafeAbsUnderAttachments(string attachmentsRoot, string relativeUrl)
        {
            if (string.IsNullOrWhiteSpace(relativeUrl)) return null;
            string rel = relativeUrl.Trim();
            if (rel.StartsWith("./", StringComparison.Ordinal)) rel = rel.Substring(2);
            if (!rel.StartsWith("attachments/", StringComparison.OrdinalIgnoreCase))
                return null;

            string tail = rel.Substring("attachments/".Length);
            tail = WebUtility.UrlDecode(tail).Replace('/', Path.DirectorySeparatorChar);

            string abs = Path.GetFullPath(Path.Combine(attachmentsRoot, tail));
            string rootFull = Path.GetFullPath(attachmentsRoot);
            if (!abs.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                return null;

            return abs;
        }
    }

    // Extracts every attachments/... occurrence from arbitrary HTML (src, href, data-*, and plain)
    private static IEnumerable<string> ExtractAttachmentRelativeUrls(string html)
    {
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(html)) return results;

        // Grab from common attributes first
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
                     html, @"(?i)(?:src|href|data-src|data-filepath)\s*=\s*['""](?<u>[^'""]+)['""]"))
        {
            var val = m.Groups["u"].Value?.Trim();
            if (!string.IsNullOrWhiteSpace(val) &&
                (val.StartsWith("attachments/", StringComparison.OrdinalIgnoreCase) ||
                 val.StartsWith("./attachments/", StringComparison.OrdinalIgnoreCase)))
            {
                results.Add(val);
            }
        }

        // Also catch any plain occurrences in text
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
                     html, @"(?i)attachments\/[^\s'""<>]+"))
        {
            var val = m.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(val))
                results.Add(val);
        }

        return results;
    }

    private static void PruneEmptyDirectories(string root)
    {
        try
        {
            // Delete deepest-first to allow parents to become empty
            var dirs = Directory
                .EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                .OrderByDescending(d => d.Length)
                .ToList();

            foreach (var dir in dirs)
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(dir).Any())
                        Directory.Delete(dir, false);
                }
                catch { /* ignore */ }
            }
        }
        catch { /* ignore */ }
    }

    private static string BuildSlimChangelog(JArray histories)
    {
        var slim = new JArray();

        foreach (var h in histories.OfType<JObject>())
        {
            var o = new JObject();

            // Top-level fields
            if (h.TryGetValue("id", out var id)) o["id"] = id;
            if (h.TryGetValue("created", out var created)) o["created"] = created;

            // Author -> displayName only
            if (h["author"] is JObject author &&
                author.TryGetValue("displayName", out var displayName))
            {
                o["author"] = new JObject
                {
                    ["displayName"] = displayName
                };
            }

            // Items -> keep only specific properties
            if (h["items"] is JArray items)
            {
                var slimItems = new JArray();
                foreach (var it in items.OfType<JObject>())
                {
                    var itObj = new JObject();
                    foreach (var name in new[] { "field", "fieldtype", "fieldId", "from", "fromString", "to", "toString" })
                    {
                        if (it.TryGetValue(name, out var val))
                        {
                            itObj[name] = val;
                        }
                    }
                    slimItems.Add(itObj);
                }
                o["items"] = slimItems;
            }

            slim.Add(o);
        }

        return slim.ToString(Newtonsoft.Json.Formatting.None);
    }

    private async Task<T> RetryWithMessageBoxAsync<T>(Func<Task<T>> action, string errorMessage, int maxAttempts = 3)
    {
        int attempt = 0;
        while (true)
        {
            try
            {
                return await action();
            }
            catch (Exception ex)
            {
                attempt++;
                if (attempt >= maxAttempts)
                {
                    var result = System.Windows.Forms.MessageBox.Show(
                        $"{errorMessage}\n\n{ex.Message}\n\nDo you want to retry?",
                        "Jira Fetch Error",
                        System.Windows.Forms.MessageBoxButtons.RetryCancel,
                        System.Windows.Forms.MessageBoxIcon.Error);

                    if (result == System.Windows.Forms.DialogResult.Retry)
                    {
                        attempt = 0; // reset attempts for user-driven retry
                        continue;
                    }
                    else
                    {
                        throw;
                    }
                }
            }
        }
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
    /// Asynchronously searches for Jira issues using a raw JQL (Jira Query Language) query string.
    /// Returns a list of <see cref="Issue"/> objects matching the query.
    /// All queries and result counts are logged for traceability.
    /// Throws if the JQL is invalid or the search fails.
    /// </summary>
    /// <param name="jql">The JQL query string to execute.</param>
    /// <returns>
    /// A list of <see cref="Issue"/> objects matching the JQL query.
    /// </returns>
    /// <exception cref="JiraException">Thrown if the search fails or the JQL is invalid.</exception>
    public async Task<List<Issue>> SearchIssuesAsync(string jql)
    {
        var results = await jira.Issues.GetIssuesFromJqlAsync(jql);
        return results.ToList();
    }

    /// <summary>
    /// Asynchronously updates the summary field of a specified Jira issue.
    /// Retrieves the issue, sets the new summary, and commits the change to the Jira server.
    /// All updates are logged for traceability.
    /// Throws if the issue cannot be found or the update fails.
    /// </summary>
    /// <param name="key">The Jira issue key to update.</param>
    /// <param name="newSummary">The new summary text to set.</param>
    /// <exception cref="JiraException">Thrown if the update fails or the issue cannot be found.</exception>
    public async Task UpdateSummaryAsync(string key, string newSummary)
    {
        var issue = await jira.Issues.GetIssueAsync(key);
        issue.Summary = newSummary;
        await issue.SaveChangesAsync();
    }

    /// <summary>
    /// Asynchronously updates the description field of a Jira issue using Atlassian Document Format (ADF).
    /// Retrieves the issue, sets the new ADF JSON description, and commits the change to the Jira server.
    /// All updates are logged for traceability.
    /// Throws if the issue cannot be found or the update fails.
    /// </summary>
    /// <param name="key">The Jira issue key to update.</param>
    /// <param name="adfDescription">The ADF JSON string representing the new description.</param>
    /// <exception cref="JiraException">Thrown if the update fails or the issue cannot be found.</exception>
    public async Task UpdateDescriptionAsync(string key, string adfDescription)
    {
        var issue = await jira.Issues.GetIssueAsync(key);
        issue["description"] = adfDescription;
        await issue.SaveChangesAsync();
    }

    /// <summary>
    /// Asynchronously updates a single custom or standard field for a Jira issue.
    /// Retrieves the issue, sets the specified field to the provided value, and commits the change to the Jira server.
    /// The value is converted to a string before assignment.
    /// All updates are logged for traceability.
    /// Throws if the issue cannot be found or the update fails.
    /// </summary>
    /// <param name="key">The Jira issue key to update.</param>
    /// <param name="fieldName">The name of the field or custom field ID to update.</param>
    /// <param name="value">The value to set for the field.</param>
    /// <exception cref="JiraException">Thrown if the update fails or the issue cannot be found.</exception>
    public async Task UpdateFieldAsync(string key, string fieldName, object value)
    {
        var issue = await jira.Issues.GetIssueAsync(key);
        issue[fieldName] = value?.ToString();
        await issue.SaveChangesAsync();
    }

    /// <summary>
    /// Asynchronously updates multiple fields of a Jira issue in a single operation.
    /// Retrieves the issue, sets each field in the provided dictionary to its corresponding value, and commits the changes to the Jira server.
    /// Each value is converted to a string before assignment.
    /// All field updates are logged for traceability.
    /// Throws if the issue cannot be found or the update fails.
    /// </summary>
    /// <param name="key">The Jira issue key to update.</param>
    /// <param name="fields">A dictionary mapping field names or custom field IDs to their new values.</param>
    /// <exception cref="JiraException">Thrown if the update fails or the issue cannot be found.</exception>
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
    /// Asynchronously retrieves the values of a specific field for multiple Jira issues.
    /// For each issue key in the provided list, fetches the issue and extracts the value of the specified field.
    /// Returns a dictionary mapping issue keys to their field values as strings.
    /// All values retrieved are logged for traceability.
    /// Throws if any issue cannot be found or the field is missing.
    /// </summary>
    /// <param name="keys">A list of Jira issue keys to retrieve field values for.</param>
    /// <param name="fieldName">The name of the field to retrieve from each issue.</param>
    /// <returns>
    /// A dictionary mapping each issue key to the value of the specified field as a string.
    /// </returns>
    /// <exception cref="JiraException">Thrown if any issue cannot be found or the field is missing.</exception>
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
    /// Asynchronously retrieves the changelog history for a specified Jira issue.
    /// Uses the Atlassian.Jira SDK to fetch all change log entries detailing modifications to the issue.
    /// Returns an enumerable of <see cref="IssueChangeLog"/> objects.
    /// The number of changelog entries fetched is logged for traceability.
    /// Throws if the issue cannot be found or the changelog cannot be retrieved.
    /// </summary>
    /// <param name="issueKey">The Jira issue key to retrieve the changelog for.</param>
    /// <returns>
    /// An enumerable of <see cref="IssueChangeLog"/> objects representing the issue's change history.
    /// </returns>
    /// <exception cref="JiraException">Thrown if the changelog cannot be retrieved or the issue cannot be found.</exception>
    public async Task<IEnumerable<IssueChangeLog>> GetChangeLogsAsync(string issueKey)
    {
        var issue = await jira.Issues.GetIssueAsync(issueKey);
        var logs = await issue.GetChangeLogsAsync();
        return logs;
    }

    /// <summary>
    /// Asynchronously updates the parent link of a Jira issue by removing the old parent link and creating a new one.
    /// Uses the Jira REST API to manage issue links.
    /// If the specified link type does not exist or REST calls fail, throws an <see cref="InvalidOperationException"/>.
    /// All actions and errors are logged for traceability.
    /// </summary>
    /// <param name="childKey">The key of the child issue whose parent link is to be updated.</param>
    /// <param name="oldParentKey">The key of the old parent issue to unlink.</param>
    /// <param name="newParentKey">The key of the new parent issue to link.</param>
    /// <param name="linkTypeName">The name of the link type to use for the parent relationship.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the link type is not found, REST API calls fail, or the update cannot be completed.
    /// </exception>
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
        var client = new HttpClient { BaseAddress = new Uri(jiraBaseUrl) };
        var bytes = Encoding.ASCII.GetBytes($"{username}:{apiToken}");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(bytes));
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    /// <summary>
    /// Asynchronously updates the sequence (sorting) field of a Jira issue.
    /// Determines the correct field name from project configuration, resolving custom field names if necessary.
    /// If the sorting field is not configured or cannot be resolved, the update is skipped or an exception is thrown.
    /// Commits the sequence value to the Jira server.
    /// All actions and errors are logged for traceability.
    /// </summary>
    /// <param name="issueKey">The Jira issue key to update.</param>
    /// <param name="sequence">The sequence value to set for the sorting field.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the project configuration is missing, the sorting field cannot be resolved, or the update fails.
    /// </exception>
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
    /// Asynchronously creates a new Jira issue and links it as a child or sibling to the specified node.
    /// Uses the Atlassian.Jira SDK for issue creation and linking.
    /// Throws an <see cref="InvalidOperationException"/> if the project configuration cannot be found.
    /// All actions and errors are logged for traceability.
    /// </summary>
    /// <param name="selectedKey">The key of the selected node (parent for child, sibling for sibling).</param>
    /// <param name="linkMode">The link mode: "Child" or "Sibling".</param>
    /// <param name="issueType">The type of issue to create (e.g., "Task", "Bug").</param>
    /// <param name="summary">The summary/title for the new issue.</param>
    /// <param name="config">The loaded Jira configuration root containing project settings.</param>
    /// <returns>
    /// The key of the newly created issue if successful; otherwise, <c>null</c>.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the project configuration is missing or issue creation/linking fails.
    /// </exception>
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
    /// Asynchronously links a list of Jira issues as "Relates" to a specified base issue using the Jira REST API.
    /// For each related issue key, creates a "Relates" link to the base issue.
    /// Throws an <see cref="InvalidOperationException"/> if any link fails.
    /// All actions and errors are logged for traceability.
    /// </summary>
    /// <param name="baseKey">The key of the base issue to link related issues to.</param>
    /// <param name="relatedKeys">A list of issue keys to link as related to the base issue.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown if any link operation fails.
    /// </exception>
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
