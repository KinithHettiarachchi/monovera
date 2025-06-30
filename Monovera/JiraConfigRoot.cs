using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Monovera
{
    public class JiraConfigRoot
    {
        public JiraInfo Jira { get; set; } = new JiraInfo();
        public List<ProjectConfig> Projects { get; set; } = new List<ProjectConfig>();
    }

    public class JiraInfo
    {
        public string Url { get; set; }
        public string Email { get; set; }
        public string Token { get; set; }
    }

    public class ProjectConfig
    {
        public string Project { get; set; }
        public string Root { get; set; }

        public string LinkTypeName { get; set; }
        public Dictionary<string, string> Types { get; set; } = new();
        public Dictionary<string, string> Status { get; set; } = new();
    }

}
