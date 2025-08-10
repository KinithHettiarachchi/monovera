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
    public async Task<List<JiraIssueDictionary>> GetAllIssuesForProject(
        string projectKey,
        List<string> fields,
        string sortingField = "created",
        string linkTypeName = "Blocks",
        bool forceSync = false,
        Action<int, int, double> progressUpdate = null,
        int maxParallelism = 10)
    {
        string cacheFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"{projectKey}.json");

        if (!forceSync && File.Exists(cacheFile))
        {
            string json = await File.ReadAllTextAsync(cacheFile);
            var cachedIssues = JsonSerializer.Deserialize<List<JiraIssueDictionary>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return cachedIssues ?? new List<JiraIssueDictionary>();
        }

        var allIssues = new List<JiraIssueDictionary>();
        string jql = $"project={projectKey} ORDER BY key ASC";
        int totalCount = await GetTotalIssueCountAsync(projectKey);

        if (totalCount == 0) return allIssues;

        const int pageSize = 100;
        int totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        var pageStarts = Enumerable.Range(0, totalPages).Select(i => i * pageSize).ToList();
        var batches = pageStarts
            .Select((startAt, idx) => new { startAt, batch = idx / maxParallelism })
            .GroupBy(x => x.batch)
            .Select(g => g.Select(x => x.startAt).ToList())
            .ToList();

        int completed = 0;

        foreach (var batch in batches)
        {
            var tasks = batch.Select(async startAt =>
            {
                string fieldsStr = fields != null && fields.Any() ? string.Join(",", fields) : "*all";
                string url = $"{jiraBaseUrl}/rest/api/2/search?jql={Uri.EscapeDataString(jql)}&startAt={startAt}&maxResults={pageSize}&fields={Uri.EscapeDataString(fieldsStr)}";

                using var client = new HttpClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{jiraEmail}:{jiraToken}")));

                var response = await client.GetStringAsync(url);
                var json = JObject.Parse(response);
                var issues = json["issues"].Select(issue => new JiraIssueDictionary
                {
                    Key = (string)issue["key"],
                    Summary = (string)issue["fields"]["summary"],
                    Type = (string)issue["fields"]["issuetype"]?["name"],
                    IssueLinks = issue["fields"]["issuelinks"] is JArray linksArray
                                ? linksArray
                                    .Where(link => (string)link["type"]?["name"] == linkTypeName)
                                    .Select(link => new JiraIssueLink
                                    {
                                        LinkTypeName = (string)link["type"]?["name"],
                                        OutwardIssueKey = (string)link["outwardIssue"]?["key"],
                                    })
                                    .ToList()
                                : new List<JiraIssueLink>(),
                    SortingField = (string)issue["fields"]?[sortingField]
                }).ToList();

                return issues;
            });

            var results = await Task.WhenAll(tasks);
            foreach (var list in results)
            {
                allIssues.AddRange(list);
                completed += list.Count;
                double percent = totalCount > 0 ? (completed * 100.0 / totalCount) : 100.0;
                progressUpdate?.Invoke(completed, totalCount, percent);
            }
        }

        string serialized = JsonSerializer.Serialize(allIssues, new JsonSerializerOptions { WriteIndented = true });

        // Post-process the JSON to remove unwanted properties/values
        using var doc = JsonDocument.Parse(serialized);
        var filtered = new List<JsonElement>();

        foreach (var issue in doc.RootElement.EnumerateArray())
        {
            using var objDoc = JsonDocument.Parse(issue.GetRawText());
            var obj = objDoc.RootElement;

            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                foreach (var prop in obj.EnumerateObject())
                {
                    // Exclude unwanted properties/values
                    if (prop.Name == "Updated" && prop.Value.ValueKind == JsonValueKind.Null)
                        continue;
                    if (prop.Name == "Created" && prop.Value.ValueKind == JsonValueKind.Null)
                        continue;
                    if (prop.Name == "CustomFields")
                        continue;
                    if (prop.Name == "IssueLinks" && prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        // Filter IssueLinks array
                        writer.WritePropertyName("IssueLinks");
                        writer.WriteStartArray();
                        foreach (var link in prop.Value.EnumerateArray())
                        {
                            using var linkDoc = JsonDocument.Parse(link.GetRawText());
                            var linkObj = linkDoc.RootElement;
                            writer.WriteStartObject();
                            foreach (var linkProp in linkObj.EnumerateObject())
                            {
                                if (linkProp.Name == "OutwardIssueSummary" && linkProp.Value.GetString() == "")
                                    continue;
                                if (linkProp.Name == "OutwardIssueType" && linkProp.Value.GetString() == "")
                                    continue;
                                writer.WritePropertyName(linkProp.Name);
                                linkProp.Value.WriteTo(writer);
                            }
                            writer.WriteEndObject();
                        }
                        writer.WriteEndArray();
                    }
                    else
                    {
                        writer.WritePropertyName(prop.Name);
                        prop.Value.WriteTo(writer);
                    }
                }
                writer.WriteEndObject();
            }
            filtered.Add(JsonDocument.Parse(ms.ToArray()).RootElement.Clone());
        }

        // Write filtered JSON to file
        using (var ms = new MemoryStream())
        {
            using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartArray();
                foreach (var item in filtered)
                    item.WriteTo(writer);
                writer.WriteEndArray();
            }
            await File.WriteAllTextAsync(cacheFile, Encoding.UTF8.GetString(ms.ToArray()));
        }

        return allIssues;
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
