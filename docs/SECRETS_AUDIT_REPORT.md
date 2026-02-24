# 🔐 Secrets & Credentials Audit Report

**Repository:** `/home/dom/projects/gut-ai`  
**Audit Date:** February 25, 2026  
**Auditor:** Automated secrets scanner  

---

## Executive Summary

| Severity | Count | Description |
|----------|-------|-------------|
| 🔴 CRITICAL | 2 | Hardcoded secrets committed to source control |
| 🟠 HIGH | 3 | Test credentials in tracked files, leaked identity info |
| 🟡 MEDIUM | 4 | Well-known dev keys, placeholder patterns |
| 🟢 LOW / OK | 7 | Properly handled secrets (env vars, GitHub Secrets, @secure params) |

---

## 🔴 CRITICAL FINDINGS

### 1. Hardcoded JWT Signing Secret in Committed Config Files

**Files:**
- `backend/src/GutAI.Api/appsettings.Development.json` (line 12)
- `docker-compose.yml` (line 11)

**Values:**
```
"Secret": "local-dev-secret-key-that-is-at-least-32-characters-long!!"
Jwt__Secret=local-dev-secret-key-that-is-at-least-32-characters-long!!
```

**Risk:** The JWT signing secret is hardcoded in two committed files. While labeled "local-dev", anyone with access to this repo can forge valid JWT tokens for any development environment using this key. If this key is accidentally used in staging/production, it would allow complete authentication bypass.

**Recommendation:**
- Remove `Jwt:Secret` from `appsettings.Development.json` entirely
- Use environment variables or user-secrets for development: `dotnet user-secrets set "Jwt:Secret" "<value>"`
- Ensure `docker-compose.yml` reads from `.env` file: `Jwt__Secret=${JWT_SECRET}` (with `.env` gitignored)

---

### 2. Test Account Credentials in Tracked File

**File:** `test-accounts.md` (committed to git, NOT gitignored)

**Values:**
```
Email:    guttester@demo.com
Password: TestPass123!

Email:    e2e2@test.com
Password: Test1234!
```

**Risk:** These are real credentials for accounts that exist (or will exist) in the application. If the app is deployed with these accounts present, anyone with repo access can log in. The file is tracked by git and not excluded by `.gitignore`.

**Recommendation:**
- Add `test-accounts.md` to `.gitignore`
- Or replace with a script that creates test accounts from environment variables
- If these accounts exist in production, change the passwords immediately

---

## 🟠 HIGH FINDINGS

### 3. Azurite Well-Known Development Storage Key in Multiple Files

**Files:**
- `docker-compose.yml` (line 10)
- `backend/tests/GutAI.Api.Tests/GutAiWebFactory.cs` (line 33)
- `backend/tests/GutAI.IntegrationTests/AzuriteFixture.cs` (line 32)

**Value:**
```
AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==
```

**Risk:** LOW — This is the **well-known Azurite development storage emulator key** that is publicly documented by Microsoft. It only works with the local Azurite emulator and cannot access real Azure Storage. **This is acceptable** for dev/test contexts.

**Status:** ✅ ACCEPTABLE (well-known dev emulator key)

---

### 4. Seed Script Contains Default Credentials

**File:** `scripts/seed-data.sh` (lines 20-21)

**Values:**
```bash
EMAIL="${1:-seed-demo@test.com}"
PASSWORD="${2:-Test123!}"
```

**Risk:** Default test credentials are hardcoded as fallback values. While parameterized (user can override), the defaults are committed. The script also echoes credentials at the end (line 370): `echo "   Login: $EMAIL / $PASSWORD"`.

**Recommendation:**
- Require credentials as mandatory arguments (no defaults)
- Or read from environment variables without defaults

---

### 5. EAS Config Contains Placeholder Apple Credentials

**File:** `frontend/eas.json` (lines 57-59)

**Values:**
```json
"appleId": "your-apple-id@email.com",
"ascAppId": "your-app-store-connect-app-id",
"appleTeamId": "YOUR_TEAM_ID"
```

**Risk:** LOW — These are placeholder values, not real credentials. However, if someone fills these in without gitignoring the file, real Apple Developer account identifiers would be committed.

**Recommendation:**
- Add a note to replace with environment variables before production use
- Consider moving these to a gitignored `eas.local.json`

---

## 🟡 MEDIUM FINDINGS

### 6. `DEMO_KEY` Hardcoded as USDA API Key Fallback

**Files:**
- `backend/src/GutAI.Api/appsettings.Development.json` (line 19): `"UsdaApiKey": "DEMO_KEY"`
- `docker-compose.yml` (line 15): `ExternalApis__UsdaApiKey=${USDA_API_KEY:-DEMO_KEY}`
- `tools/UsdaFoodGenerator/Program.cs` (line 21): `?? "DEMO_KEY"`
- `.env.example` (line 7): `USDA_API_KEY=demo_key`

**Risk:** LOW — `DEMO_KEY` is a publicly documented USDA rate-limited key. Not a real secret, but it signals the pattern of hardcoding fallback keys.

**Status:** ✅ ACCEPTABLE (publicly documented demo key)

---

### 7. Production Config Contains Unexpanded Environment Variable Placeholder

**File:** `backend/src/GutAI.Api/appsettings.Production.json` (line 12)

**Value:**
```json
"Secret": "${JWT_SECRET}"
```

**Risk:** This is a string literal `"${JWT_SECRET}"` — ASP.NET Core does NOT expand shell-style `${}` placeholders in JSON config files. If this file is loaded without the `Jwt__Secret` environment variable overriding it, the literal string `${JWT_SECRET}` would be used as the JWT signing key, which would be guessable and insecure.

**Recommendation:**
- Remove the `Jwt.Secret` value from `appsettings.Production.json` entirely (leave it blank or omit)
- Rely solely on the environment variable `Jwt__Secret` set in the container runtime
- The `Program.cs` already throws if `Jwt:Secret` is missing, so omitting it is safe

---

### 8. Production Connection String Placeholder

**File:** `backend/src/GutAI.Api/appsettings.Production.json` (line 9)

**Value:**
```json
"AzureStorage": "<your-production-azure-storage-connection-string>"
```

**Risk:** LOW — This is a placeholder, not a real connection string. But it could lead to confusion if someone fills it in and commits.

---

### 9. Expo Project ID and Owner Exposed

**Files:**
- `frontend/app.json` (line 62): `"projectId": "18ddbacc-5aee-4310-afba-96d053017225"`
- `frontend/app.json` (line 63): `"owner": "thed24"`

**Risk:** LOW — Expo project IDs are not sensitive (they're needed for OTA updates and are public in published apps). The owner name is a public Expo username.

**Status:** ✅ ACCEPTABLE

---

## 🟢 PROPERLY HANDLED (No Action Required)

### 10. GitHub Actions Secrets — ✅ CORRECT
**File:** `.github/workflows/deploy.yml`

All production secrets are properly referenced via `${{ secrets.* }}`:
- `secrets.GITHUB_TOKEN` — GHCR auth
- `secrets.AZURE_CLIENT_ID` — Azure OIDC login
- `secrets.AZURE_TENANT_ID` — Azure OIDC login
- `secrets.AZURE_SUBSCRIPTION_ID` — Azure OIDC login
- `secrets.JWT_SECRET` — JWT signing key
- `secrets.USDA_API_KEY` — USDA API
- `secrets.CALORIENINJAS_API_KEY` — CalorieNinjas API
- `secrets.EDAMAM_APP_ID` — Edamam API
- `secrets.EDAMAM_APP_KEY` — Edamam API

Azure auth uses OIDC federated credentials (no client secret stored).

### 11. Bicep Infrastructure — ✅ CORRECT
**File:** `infra/main.bicep`

All sensitive parameters use `@secure()` decorator:
- `jwtSecret`
- `usdaApiKey`
- `calorieNinjasApiKey`
- `edamamAppId`
- `edamamAppKey`
- `ghcrUsername`
- `ghcrPassword`

All are stored as Container App secrets with `secretRef` mappings.

### 12. Bicep Parameters — ✅ CORRECT
**File:** `infra/main.bicepparam`

No secrets in the parameter file — only `environment`, `location`, and `containerImage`.

### 13. `.gitignore` — ✅ MOSTLY CORRECT
**File:** `.gitignore`

Properly excludes:
- `.env` (but not `.env.example` ✅)
- `*.pfx`, `*.pem`
- `bin/`, `obj/`, `node_modules/`

**File:** `frontend/.gitignore`

Properly excludes:
- `*.p12`, `*.p8`, `*.key`, `*.mobileprovision`, `*.pem`, `*.jks`
- `.env*.local`
- `google-services-key.json`

### 14. No `.env` Files Committed — ✅ CORRECT
Git only tracks `.env.example`, which contains no real secrets.

### 15. Empty API Keys in Production Config — ✅ CORRECT
**File:** `backend/src/GutAI.Api/appsettings.Production.json`
```json
"UsdaApiKey": "",
"CalorieNinjasApiKey": "",
"EdamamAppId": "",
"EdamamAppKey": ""
```
All API keys are empty in the production config and are injected via environment variables at runtime.

### 16. Test Files Use Fixture Passwords — ✅ ACCEPTABLE
Test files use synthetic passwords like `"TestPass123"`, `"hashed-password"`, `"hashed-pw-123"` in test fixtures. These are not real credentials and exist only in test contexts (e.g., `AuthContractTests.cs`, `GutAiWebFactory.cs`, `EndToEndFlowTests.cs`, `TableStorageCrudTests.cs`).

---

## Items NOT Found (Negative Results — ✅ GOOD)

| Category | Status |
|----------|--------|
| Stripe/payment keys | ✅ Not found |
| Firebase/Supabase keys | ✅ Not found |
| AWS credentials | ✅ Not found |
| GCP credentials | ✅ Not found |
| SMTP/email credentials | ✅ Not found |
| Webhook secrets | ✅ Not found |
| Private keys (.pem, .pfx, .p12) | ✅ Not committed (gitignored) |
| OAuth client secrets | ✅ Not found |
| Hardcoded bearer tokens | ✅ Not found |
| Embedded URL credentials | ✅ Not found |

---

## Recommended Actions (Priority Order)

| # | Action | Severity | Effort |
|---|--------|----------|--------|
| 1 | **Remove JWT secret from `appsettings.Development.json`** — use `dotnet user-secrets` instead | 🔴 Critical | Low |
| 2 | **Add `test-accounts.md` to `.gitignore`** and remove from git tracking (`git rm --cached test-accounts.md`) | 🔴 Critical | Low |
| 3 | **Remove JWT secret from `docker-compose.yml`** — read from `.env` file instead (`Jwt__Secret=${JWT_SECRET}`) | 🔴 Critical | Low |
| 4 | **Fix `appsettings.Production.json`** — remove the `"${JWT_SECRET}"` placeholder (ASP.NET doesn't expand it) | 🟡 Medium | Low |
| 5 | **Rotate the JWT secret** if the repo has ever been public, since the dev key is committed | 🟠 High | Low |
| 6 | **Remove default credentials from `seed-data.sh`** — require as mandatory arguments | 🟡 Medium | Low |
| 7 | **Consider `.env.template` pattern for `docker-compose.yml`** — document required env vars without providing values | 🟡 Medium | Low |

---

## Git History Warning

⚠️ Even if you remove these values from the files now, **they remain in git history**. If this repository has ever been shared or pushed to a remote:
1. The JWT dev secret should be considered compromised
2. The test account passwords should be changed
3. Consider using `git filter-branch` or BFG Repo-Cleaner to purge history if the repo was ever public
