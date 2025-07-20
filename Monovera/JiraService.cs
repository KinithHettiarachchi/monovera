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

/// <summary>
/// Service class to interact with Jira via Atlassian.Jira SDK using REST API.
/// Provides asynchronous methods for retrieving, updating, and querying Jira issues.
/// </summary>
public class JiraService
{
    // The Jira REST client instance used for all operations.
    private readonly Jira jira;
    private readonly string username;
    private readonly string apiToken;
    private readonly string jiraBaseUrl;


    /// <summary>
    /// Initializes a new instance of JiraService with the specified Jira base URL and credentials.
    /// </summary>
    /// <param name="jiraUrl">Base URL of the Jira instance (e.g., https://yourdomain.atlassian.net)</param>
    /// <param name="email">Email associated with Jira account (used for authentication)</param>
    /// <param name="apiToken">API token generated for the Jira account</param>
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
    /// </summary>
    /// <returns>True if connected successfully; false if failed.</returns>
    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            // Attempt to get current user info (lightweight call to verify credentials)
            var user = await jira.Users.GetMyselfAsync();

            // If user is not null, connection is valid
            return user != null;
        }
        catch (Exception)
        {
            // Any error indicates failure to connect or authenticate
            return false;
        }
    }

    /// <summary>
    /// Gets the display name of the currently authenticated Jira user asynchronously.
    /// Returns null if unable to fetch user info.
    /// </summary>
    /// <returns>The display name of the connected user, or null if failed.</returns>
    public async Task<string?> GetConnectedUserNameAsync()
    {
        try
        {
            var user = await jira.Users.GetMyselfAsync();
            return user?.DisplayName; // or user?.Name depending on what you want
        }
        catch
        {
            // Could not retrieve user info, return null
            return null;
        }
    }

    /// <summary>
    /// Gets all Jira issues for a given project using raw JQL.
    /// </summary>
    /// <param name="projectKey">The Jira project key</param>
    /// <param name="fields">Fields to retrieve (SDK retrieves most by default)</param>
    /// <returns>List of Atlassian.Jira.Issue objects</returns>
    // Atlassian.Jira SDK does not provide a way to get the total count of issues for a JQL query directly.
    // The SDK's GetIssuesFromJqlAsync and LINQ queries only return the issues, not the total count.
    // There is no property or method in Atlassian.Jira.Issue or Jira.Issues that exposes the total count.
    // Therefore, if you need the total count for progress reporting, you must use the REST API (HttpClient).
    // If you only use the SDK, you can show indeterminate progress or use the count of issues retrieved so far.
    public async Task<List<JiraIssueDto>> GetAllIssuesForProject(
     string projectKey,
     List<string> fields,
     string sortingField = "created",
     string linkTypeName = "Blocks",
     bool forceSync = false,
     Action<int, int, double> progressUpdate = null,
     int maxParallelism = 5)
    {
        string cacheFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"{projectKey}.json");

        // Check if the cache file exists
        if (!forceSync && File.Exists(cacheFile))
        {
            string json = await File.ReadAllTextAsync(cacheFile);
            var cachedIssues = JsonSerializer.Deserialize<List<JiraIssueDto>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return cachedIssues ?? new List<JiraIssueDto>();
        }

        // If not cached, fetch from Jira
        var allIssues = new List<JiraIssueDto>();
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
                var issues = json["issues"].Select(issue => new JiraIssueDto
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
                                        //OutwardIssueSummary = (string)link["outwardIssue"]?["fields"]?["summary"],
                                       // OutwardIssueType = (string)link["outwardIssue"]?["fields"]?["issuetype"]?["name"]
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

        // Save the result to cache
        string serialized = JsonSerializer.Serialize(allIssues, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(cacheFile, serialized);

        return allIssues;
    }

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
        return doc.RootElement.GetProperty("total").GetInt32();
    }

    /// <summary>
    /// Retrieves a Jira issue asynchronously by its issue key.
    /// </summary>
    /// <param name="key">The issue key, e.g., "PROJ-123"</param>
    /// <returns>The Issue object representing the Jira issue</returns>
    public async Task<Issue> GetIssueAsync(string key)
    {
        // SDK returns Issue, wraps HTTP call internally
        return await jira.Issues.GetIssueAsync(key);
    }

    /// <summary>
    /// Searches for issues using a raw JQL query string asynchronously.
    /// </summary>
    /// <param name="jql">Jira Query Language (JQL) string, e.g., "project = PROJ AND status = Open"</param>
    /// <returns>A list of issues matching the query</returns>
    public async Task<List<Issue>> SearchIssuesAsync(string jql)
    {
        // Atlassian SDK's QueryAsync executes raw JQL and returns an IEnumerable<Issue>
        var results = await jira.Issues.GetIssuesFromJqlAsync(jql);
        return results.ToList();
    }

    /// <summary>
    /// Updates the summary field of a specified Jira issue.
    /// </summary>
    /// <param name="key">Issue key</param>
    /// <param name="newSummary">New summary text</param>
    public async Task UpdateSummaryAsync(string key, string newSummary)
    {
        var issue = await jira.Issues.GetIssueAsync(key);
        issue.Summary = newSummary;
        await issue.SaveChangesAsync(); // Commits changes to Jira server asynchronously
    }

    /// <summary>
    /// Updates the description field of a Jira issue using Atlassian Document Format (ADF).
    /// Note: The Atlassian SDK treats the "description" field as a raw string; ensure Jira supports ADF or plain text.
    /// </summary>
    /// <param name="key">Issue key</param>
    /// <param name="adfDescription">ADF JSON string representing the description</param>
    public async Task UpdateDescriptionAsync(string key, string adfDescription)
    {
        var issue = await jira.Issues.GetIssueAsync(key);

        // The SDK uses indexer to update custom or standard fields by field name
        issue["description"] = adfDescription;
        await issue.SaveChangesAsync();
    }

    /// <summary>
    /// Updates a single custom or standard field for a Jira issue.
    /// </summary>
    /// <param name="key">Issue key</param>
    /// <param name="fieldName">Field name or custom field ID, e.g., "customfield_10000"</param>
    /// <param name="value">Value to set; converted to string</param>
    public async Task UpdateFieldAsync(string key, string fieldName, object value)
    {
        var issue = await jira.Issues.GetIssueAsync(key);
        issue[fieldName] = value?.ToString();
        await issue.SaveChangesAsync();
    }

    /// <summary>
    /// Updates multiple fields of a Jira issue at once.
    /// </summary>
    /// <param name="key">Issue key</param>
    /// <param name="fields">Dictionary of field names and values</param>
    public async Task UpdateFieldsAsync(string key, Dictionary<string, object> fields)
    {
        var issue = await jira.Issues.GetIssueAsync(key);

        // Loop through fields dictionary and update each field accordingly
        foreach (var field in fields)
        {
            issue[field.Key] = field.Value?.ToString();
        }

        await issue.SaveChangesAsync();
    }

    /// <summary>
    /// Retrieves values of a specific field for multiple issues.
    /// </summary>
    /// <param name="keys">List of issue keys</param>
    /// <param name="fieldName">Field name to retrieve</param>
    /// <returns>Dictionary mapping issue keys to field values as strings</returns>
    public async Task<Dictionary<string, string>> GetFieldValuesAsync(List<string> keys, string fieldName)
    {
        var result = new Dictionary<string, string>();

        // Fetch each issue individually and get field value
        foreach (var key in keys)
        {
            var issue = await jira.Issues.GetIssueAsync(key);
            result[key] = issue[fieldName]?.ToString();
        }

        return result;
    }

    /// <summary>
    /// Retrieves the changelog history for a given Jira issue.
    /// </summary>
    /// <param name="issueKey">Issue key</param>
    /// <returns>An enumerable of IssueChangeLog objects detailing changes</returns>
    public async Task<IEnumerable<IssueChangeLog>> GetChangeLogsAsync(string issueKey)
    {
        var issue = await jira.Issues.GetIssueAsync(issueKey);

        // The SDK provides changelogs asynchronously
        return await issue.GetChangeLogsAsync();
    }

    /// <summary>
    /// Mock helper method to convert HTML content to Atlassian Document Format (ADF).
    /// In production, use a proper converter or library to generate valid ADF JSON.
    /// </summary>
    /// <param name="html">HTML string to convert</param>
    /// <returns>ADF JSON string</returns>
    private static string ConvertHtmlToAdf(string html)
    {
        // TODO: Implement real HTML-to-ADF conversion logic here.
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
}
