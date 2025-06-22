
# Monovera

**Monovera** is an open-source Windows Forms application that allows users to efficiently **visualize**, **search**, and **navigate** a Jira requirement repository structured using **parent-child relationships**.

It is ideal for product development, QA, and business analysis teams working with structured Jira data. Monovera provides a user-friendly interface to explore hierarchical issue relationships and access detailed issue information — including attachments, links, history, and even embedded `.feature` files from SVN.

---

## ✨ Features

- 🧭 **Visual Tree View of Requirements**  
  Displays the full parent-child issue hierarchy using collapsible tree nodes.

- 🔍 **Powerful Search Dialog (Ctrl+Shift+S)**  
  Search by text, issue type, and status across selected projects — shows results grouped by **Title match** and **Description match**.

- 📋 **Issue Details Viewer**  
  Clicking an issue displays full detail:
  - Summary and rendered description
  - Linked issues (Parent, Children, Related)
  - Inline change history with diff
  - Attachments with previews and downloads
  - Raw JSON response

- 💡 **Icon Support**  
  Rich icons for issue types and statuses for improved usability.

- 🔗 **Jira Link Detection**  
  Auto-converts inline Jira links and SVN-style links into clickable in-app links.

- 🧩 **SVN Integration**  
  Automatically fetches and embeds `.feature` file content referenced via SVN links in descriptions.

---

## 🛠 Configuration

Before launching, set up your Jira connection by creating a `configuration.properties` file in the application directory with the following content:

```properties
JIRA_HOME=https://YOUR_DOMAIN.atlassian.net
JIRA_EMAIL=YOUR_EMAIL@YOUR_DOMAIN.com
JIRA_TOKEN=YOUR_JIRA_TOKEN
JIRA_PROJECTS=YOUR_PROJECT1,YOUR_PROJECT2
JIRA_PROJECT_ROOTS=PRJ1-100,PRJ2-1
```

- `JIRA_PROJECTS`: Comma-separated list of Jira project keys to be loaded.
- `JIRA_PROJECT_ROOTS`: Root issue keys to initialize the tree from (e.g., top-level User Reqs).
- `JIRA_TOKEN`: Personal Access Token (PAT) from your Atlassian account.

🔒 **Note**: Your credentials are only used locally — no cloud storage or telemetry.

---

## 🔎 Search Example

Press `Ctrl+Shift+S` to open the search dialog.

You can:
- Enter an issue key to jump directly.
- Or search by text across selected projects.
- Filter by Issue Type and Status

Results appear in a collapsible viewer with clickable links styled as:

```text
🧑 Add login validation [PRJ1-123]
🧑 Setup error messages [PRJ2-124]
```

Clicking a result auto-navigates the tree to that issue.

---

## 📁 Project Structure

- **Form1.cs**: Main form. Loads tree view, handles issue detail rendering, icons, caching, attachments, history.
- **SearchDialog.cs**: Search interface with filters and result presentation.
- **configuration.properties**: Connection and project configuration.

---

## 📸 Screenshots

| Tree View | Issue Details | Search Dialog |
|-----------|----------------|----------------|
| ![](screenshots/tree.png) | ![](screenshots/details.png) | ![](screenshots/search.png) |

---

## 🧑‍💻 Build Instructions

1. Clone the repo:
   ```bash
   git clone https://github.com/KinithHettiarachchi/monovera.git
   ```

2. Open the solution in **Visual Studio**.

3. Make sure you have:
   - .NET 6+ or .NET Framework (depending on your version)
   - WebView2 runtime installed

4. Create the `images/` folder in your app directory and place the required icons (`type_userreq.png`, etc.).

5. Run the application.

---

## 🧱 Dependencies

- **WebView2** – For rendering HTML-based views
- **SharpSVN** – To pull `.feature` file content from SVN repositories
- **System.Text.Json**, **HttpClient** – For Jira API communication

---

## 🔓 License

Monovera is open-source and released under the [MIT License](LICENSE).

You are free to use, modify, and distribute this software. Contributions welcome!

---

## 🙌 Contributing

We welcome contributions to improve Monovera!

- Create feature branches
- Write clear commit messages
- Submit a pull request

For major changes, please open an issue first to discuss the proposal.

---

## 📬 Contact

If you use Monovera or would like to contribute, feel free to get in touch via GitHub issues.
