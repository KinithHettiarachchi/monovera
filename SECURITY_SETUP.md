# 🔒 Security Configuration Guide

## ⚠️ IMPORTANT: Credentials Management

This project uses **local configuration files** for credentials. **NEVER commit these files to Git!**

---

## 📁 Configuration Files (Local Only)

### 1. `configuration.json` (Jira Credentials)
**Location:** Working directory (same folder as .exe)

**Setup:**
```powershell
# Copy the template
Copy-Item configuration.json.template configuration.json

# Edit with your credentials
notepad configuration.json
```

**Required Fields:**
- `Jira.Url` - Your Jira instance URL
- `Jira.Email` - Your Jira email
- `Jira.Token` - Your Jira API token (generate at: Jira → Account Settings → Security → API Tokens)

---

### 2. `squash.conf` (Squash TM Credentials)
**Location:** Working directory (same folder as .exe)

**Setup:**
```powershell
# Copy the template
Copy-Item squash.conf.template squash.conf

# Edit with your credentials
notepad squash.conf
```

**Required Fields:**
- `SQUASH_API_URL` - Your Squash TM instance URL
- `SQUASH_TOKEN` - Your Squash TM API token
- `SQUASH_PROJECT` - Your project name
- `SQUASH_JIRA_BASE_URL` - Your Jira base URL

---

## 🛡️ Security Best Practices

### ✅ What's Protected:
- ✅ `configuration.json` - Added to `.gitignore`
- ✅ `squash.conf` - Added to `.gitignore`
- ✅ `Data/` folder - Database and attachments (local only)
- ✅ `*.sqlite` files - Not tracked
- ✅ `monovera_*.db` - MonoveraBot knowledge base (local only)

### ❌ What's Committed (Safe):
- ✅ `configuration.json.template` - Template with placeholder values
- ✅ `squash.conf.template` - Template with placeholder values
- ✅ Source code (`.cs` files) - No hardcoded credentials

---

## 🔍 Verify Your Setup

Run this PowerShell script to check for accidentally committed credentials:

```powershell
# Check if credentials are ignored
git check-ignore configuration.json
git check-ignore squash.conf

# Should both return the filename (means they're ignored)
# If nothing is returned, they're NOT ignored (BAD!)

# Check Git history for leaked credentials
git log --all --full-history -- configuration.json
git log --all --full-history -- squash.conf

# Should be empty or show only removal commits
```

---

## 🚨 If Credentials Were Leaked to Git:

If you accidentally committed credentials, follow these steps:

### Option 1: Remove from Latest Commit (Not Pushed Yet)
```powershell
git rm --cached configuration.json
git commit --amend
```

### Option 2: Remove from History (Already Pushed)
```powershell
# Use BFG Repo-Cleaner or git filter-branch
# 1. Install BFG: https://rtyley.github.io/bfg-repo-cleaner/
# 2. Run:
bfg --delete-files configuration.json
bfg --delete-files squash.conf
git reflog expire --expire=now --all
git gc --prune=now --aggressive

# 3. Force push (⚠️ WARNING: This rewrites history!)
git push --force
```

### Option 3: Rotate Credentials (Safest)
1. Generate new Jira API token
2. Generate new Squash TM API token
3. Update `configuration.json` and `squash.conf`
4. Revoke old tokens in Jira/Squash

---

## 📋 Setup Checklist

- [ ] Copied `configuration.json.template` → `configuration.json`
- [ ] Filled in Jira credentials in `configuration.json`
- [ ] Copied `squash.conf.template` → `squash.conf`
- [ ] Filled in Squash TM credentials in `squash.conf`
- [ ] Verified `.gitignore` is in place
- [ ] Ran `git check-ignore configuration.json` (should output filename)
- [ ] Never committed actual credentials to Git

---

## 🎯 Summary

| File | Purpose | Git Status |
|------|---------|------------|
| `configuration.json` | **Jira credentials** | ❌ IGNORED (local only) |
| `squash.conf` | **Squash TM credentials** | ❌ IGNORED (local only) |
| `configuration.json.template` | Template with placeholders | ✅ COMMITTED (safe) |
| `squash.conf.template` | Template with placeholders | ✅ COMMITTED (safe) |
| `.gitignore` | Ignore rules | ✅ COMMITTED (protects you) |

**Your credentials are now secure!** 🔒
