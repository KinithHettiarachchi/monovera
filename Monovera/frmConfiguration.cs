using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Runtime;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using static Monovera.frmMain;

namespace Monovera
{
    /// <summary>
    /// Configuration form for Monovera.
    /// Allows users to view, add, edit, and delete Jira projects and update Jira authentication settings.
    /// Reads and writes configuration to configuration.json.
    /// </summary>
    public partial class frmConfiguration : Form
    {
        /// <summary>
        /// Holds the current configuration loaded from or to be saved to disk.
        /// </summary>
        private JiraConfigRoot _config = new JiraConfigRoot();

        /// <summary>
        /// Path to the configuration file.
        /// </summary>
        private const string ConfigFilePath = "configuration.json";

        /// <summary>
        /// Initializes the configuration form, sets up UI and event handlers.
        /// </summary>
        public frmConfiguration()
        {
            InitializeComponent();
            InitializeUI();
            this.BackColor= GetCSSColor_Tree_Background(frmMain.cssPath);
            this.Load += ConfigForm_Load;
        }

        /// <summary>
        /// Sets up UI controls and attaches event handlers for project management and saving.
        /// </summary>
        private void InitializeUI()
        {
            // Hide Jira token with password character
            txtToken.PasswordChar = '*';

            // Add Project button: opens ProjectForm, adds new project to config and list
            btnAddProject.Click += (s, e) =>
            {
                var dlg = new frmProject();
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    _config.Projects.Add(dlg.Project);
                    lstProjects.Items.Add(dlg.Project.Project);
                }
            };

            // Edit Project button: opens ProjectForm for selected project, updates config and list
            btnEditProject.Click += (s, e) =>
            {
                int idx = lstProjects.SelectedIndex;
                if (idx >= 0)
                {
                    var dlg = new frmProject(_config.Projects[idx]);
                    dlg.BackColor = GetCSSColor_Tree_Background(frmMain.cssPath);
                    if (dlg.ShowDialog() == DialogResult.OK)
                    {
                        _config.Projects[idx] = dlg.Project;
                        lstProjects.Items[idx] = dlg.Project.Project;
                    }
                }
            };

            // Delete Project button: confirms and removes selected project from config and list
            btnDeleteProject.Click += (s, e) =>
            {
                int idx = lstProjects.SelectedIndex;
                if (idx >= 0)
                {
                    var projName = lstProjects.Items[idx].ToString();
                    var confirm = MessageBox.Show($"Are you sure you want to delete project '{projName}'?",
                        "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                    if (confirm == DialogResult.Yes)
                    {
                        _config.Projects.RemoveAt(idx);
                        lstProjects.Items.RemoveAt(idx);
                    }
                }
                else
                {
                    MessageBox.Show("Please select a project to delete.", "No Selection", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            };

            // Save button: saves configuration to disk
            btnSave.Click += (s, e) => SaveConfig();
        }

        /// <summary>
        /// Saves the current configuration to configuration.json.
        /// Updates Jira authentication info from text fields.
        /// </summary>
        private void SaveConfig()
        {
            // Update Jira authentication info from UI fields
            _config.Jira = new JiraInfo
            {
                Url = txtUrl.Text,
                Email = txtEmail.Text,
                Token = txtToken.Text
            };

            try
            {
                // Serialize config to JSON and write to file
                var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigFilePath, json);

                MessageBox.Show("Configuration saved. Please restart the application for changes to take effect.", "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
                //Application.Exit(); // Optionally exit after saving
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to save configuration: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Loads configuration from configuration.json when the form is loaded.
        /// Populates UI fields and project list from loaded config.
        /// </summary>
        private void ConfigForm_Load(object sender, EventArgs e)
        {
            if (File.Exists(ConfigFilePath))
            {
                try
                {
                    // Read and deserialize configuration file
                    var json = File.ReadAllText(ConfigFilePath);
                    _config = JsonSerializer.Deserialize<JiraConfigRoot>(json) ?? new JiraConfigRoot();

                    // Populate Jira authentication fields
                    txtUrl.Text = _config.Jira?.Url ?? "";
                    txtEmail.Text = _config.Jira?.Email ?? "";
                    txtToken.Text = _config.Jira?.Token ?? "";

                    // Populate project list
                    lstProjects.Items.Clear();
                    foreach (var project in _config.Projects)
                    {
                        lstProjects.Items.Add(project.Project);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Failed to load configuration: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}
