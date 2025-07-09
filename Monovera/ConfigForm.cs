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
    public partial class ConfigForm : Form
    {
        private JiraConfigRoot _config = new JiraConfigRoot();
        private const string ConfigFilePath = "configuration.json";
        public ConfigForm()
        {
            InitializeComponent();
            InitializeUI();
            this.Load += ConfigForm_Load;
        }

        private void InitializeUI()
        {
            txtToken.PasswordChar = '*';

            btnAddProject.Click += (s, e) =>
            {
                var dlg = new ProjectForm();
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    _config.Projects.Add(dlg.Project);
                    lstProjects.Items.Add(dlg.Project.Project);
                }
            };

            btnEditProject.Click += (s, e) =>
            {
                int idx = lstProjects.SelectedIndex;
                if (idx >= 0)
                {
                    var dlg = new ProjectForm(_config.Projects[idx]);
                    if (dlg.ShowDialog() == DialogResult.OK)
                    {
                        _config.Projects[idx] = dlg.Project;
                        lstProjects.Items[idx] = dlg.Project.Project;
                    }
                }
            };

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


            btnSave.Click += (s, e) => SaveConfig();
        }

        private void SaveConfig()
        {
            _config.Jira = new JiraInfo
            {
                Url = txtUrl.Text,
                Email = txtEmail.Text,
                Token = txtToken.Text
            };

            try
            {
                var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigFilePath, json);

                MessageBox.Show("Configuration saved. Please restart the application for changes to take effect.", "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
                //Application.Exit();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to save configuration: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ConfigForm_Load(object sender, EventArgs e)
        {
            if (File.Exists(ConfigFilePath))
            {
                try
                {
                    var json = File.ReadAllText(ConfigFilePath);
                    _config = JsonSerializer.Deserialize<JiraConfigRoot>(json) ?? new JiraConfigRoot();

                    txtUrl.Text = _config.Jira?.Url ?? "";
                    txtEmail.Text = _config.Jira?.Email ?? "";
                    txtToken.Text = _config.Jira?.Token ?? "";

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
