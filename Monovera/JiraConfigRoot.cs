using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Monovera
{
    /// <summary>
    /// Root configuration object for Monovera.
    /// Contains Jira authentication info and a list of configured projects.
    /// Serialized to and from configuration.json.
    /// </summary>
    public class JiraConfigRoot
    {
        /// <summary>
        /// Jira authentication and connection details.
        /// </summary>
        public JiraInfo Jira { get; set; } = new JiraInfo();

        /// <summary>
        /// List of project configurations for all Jira projects managed by Monovera.
        /// </summary>
        public List<ProjectConfig> Projects { get; set; } = new List<ProjectConfig>();
    }

    /// <summary>
    /// Jira authentication and connection details.
    /// Used for REST API access.
    /// </summary>
    public class JiraInfo
    {
        /// <summary>
        /// Base URL of the Jira instance (e.g. "https://yourdomain.atlassian.net").
        /// </summary>
        public string Url { get; set; }

        /// <summary>
        /// Jira user email for API authentication.
        /// </summary>
        public string Email { get; set; }

        /// <summary>
        /// Jira API token for authentication.
        /// </summary>
        public string Token { get; set; }
    }

    /// <summary>
    /// Configuration for a single Jira project.
    /// Includes project key, root issue, hierarchy link type, and icon mappings.
    /// </summary>
    public class ProjectConfig
    {
        /// <summary>
        /// Jira project key (e.g. "PROJECT1").
        /// </summary>
        public string Project { get; set; }

        /// <summary>
        /// Root issue key for the project (e.g. "PRJ1-100").
        /// </summary>
        public string Root { get; set; }

        /// <summary>
        /// Link type name used for hierarchy (e.g. "Blocks").
        /// Determines parent-child relationships.
        /// </summary>
        public string LinkTypeName { get; set; }

        /// <summary>
        /// Field name used for sorting issues (e.g. "Priority").
        /// </summary>
        public string SortingField { get; set; } // <-- Add this

        /// <summary>
        /// Maps issue type names to icon filenames (e.g. "User Story" => "type_userreq.png").
        /// Used for displaying icons in the UI.
        /// </summary>
        public Dictionary<string, string> Types { get; set; } = new();

        /// <summary>
        /// Maps status names to icon filenames (e.g. "Draft" => "status_draft.png").
        /// Used for displaying status icons in the UI.
        /// </summary>
        public Dictionary<string, string> Status { get; set; } = new();

        public bool HasCreatePermission { get; set; }
        public bool HasEditPermission { get; set; }
    }
}
