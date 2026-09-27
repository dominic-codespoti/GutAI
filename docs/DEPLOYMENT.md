# GutAI — App Store Deployment Guide (EAS / Expo)

This guide covers deploying the GutAI mobile app to the **Apple App Store** and **Google Play Store** using Expo Application Services (EAS).

---

## Prerequisites

### Accounts you need

| Account                 | URL                                  | What for                  |
| ----------------------- | ------------------------------------ | ------------------------- |
| **Expo**                | https://expo.dev/signup              | EAS Build & Submit        |
| **Apple Developer**     | https://developer.apple.com/programs | iOS App Store ($99/yr)    |
| **Google Play Console** | https://play.google.com/console      | Play Store ($25 one-time) |

### Tools

```bash
# Install EAS CLI globally
npm install -g eas-cli

# Verify
eas --version   # needs >= 15.0.0
```

---

## Step 1 — Link to Expo project

```bash
cd frontend

# Log in to your Expo account
eas login

# Initialize the EAS project (creates the project on expo.dev)
eas init
```

This prints a **project ID** (UUID). Update `frontend/app.json`:

```jsonc
"extra": {
  "eas": {
    "projectId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"  // ← paste here
  }
},
"owner": "your-expo-username"  // ← your Expo account name
```

OTA updates are intentionally not configured for this release. Use a new store build for native or JavaScript changes.

---

## Step 2 — Configure credentials

### iOS

EAS can manage all iOS credentials for you automatically. On first build it will prompt you to log in to your Apple Developer account and handle certificates + provisioning profiles.

If you prefer manual management:

```bash
eas credentials --platform ios
```
For non-interactive production builds, configure an App Store Connect API key in EAS credentials before running the build; the expired provisioning profile cannot be refreshed non-interactively with only an Apple ID login.

### Android

For Google Play submissions, you need a **Google Service Account key**:

1. Go to **Google Play Console → Setup → API access**
2. Create or link a Google Cloud project
3. Create a **Service Account** with "Release manager" permissions
4. Download the JSON key
5. Save it as `frontend/google-services-key.json`
6. **Add it to `.gitignore`** (already handled — `*.key` and `*.json` service keys should never be committed)

Update `frontend/eas.json` → `submit.production.android.serviceAccountKeyPath` if the path differs.

For iOS submissions, update `eas.json`:

```jsonc
"ios": {
  "appleId": "you@email.com",          // Your Apple ID
  "ascAppId": "1234567890",            // App Store Connect app ID
  "appleTeamId": "ABCDEF1234"          // Apple Developer Team ID
}
```

---

## Step 3 — Set your production API URL

The API URL is set per build profile in `eas.json`:

| Profile       | `EXPO_PUBLIC_API_URL`                                                                  | Purpose                                      |
| ------------- | -------------------------------------------------------------------------------------- | -------------------------------------------- |
| `development` | `http://localhost:5000`                                                                | Local dev                                    |
| `preview`     | `https://gutai-prod-api.jollysand-125c64d0.australiaeast.azurecontainerapps.io`        | Internal testing against the deployed API   |
| `production`  | `https://gutai-prod-api.jollysand-125c64d0.australiaeast.azurecontainerapps.io`        | App Store release                            |

**You must deploy the backend to a public URL before submitting.** The production profile already points at the Azure Container Apps URL above.

---

## Step 4 — Build

### Development build (for testing on device/simulator)

```bash
# Simulator (iOS only)
make eas-dev
# or
cd frontend && npm run build:dev:ios

# Physical device
cd frontend && eas build --profile development-device --platform ios
```

### Preview build (internal distribution via QR code)

```bash
make eas-preview
```

Builds are shared via a link on expo.dev — testers install directly on device.

### Production build

```bash
make eas-prod
# or individually:
cd frontend && npm run build:prod:ios
cd frontend && npm run build:prod:android
```

---

## Step 5 — Submit to stores

### iOS → App Store Connect

```bash
make eas-submit-ios
# or
cd frontend && npm run submit:ios
```

This uploads the `.ipa` to App Store Connect. You then:

1. Go to [App Store Connect](https://appstoreconnect.apple.com)
2. Select the build under **TestFlight** or **App Store** tab
3. Fill in metadata (description, screenshots, privacy policy URL)
4. Submit for review

### Android → Google Play Console

```bash
make eas-submit-android
# or
cd frontend && npm run submit:android
```

This uploads the `.aab` to the **internal** track by default. You then:

1. Go to [Google Play Console](https://play.google.com/console)
2. Promote from **Internal testing → Closed testing → Open testing → Production**
3. Fill in store listing (description, screenshots, privacy policy)
4. Submit for review

### Both at once

```bash
make eas-submit
```

---

## Step 6 — Over-the-Air (OTA) Updates

OTA updates are intentionally out of scope for this release. Do not run `eas update` or `make eas-update` for production; ship changes through a new EAS store build.

---

## Build Profiles Summary

| Profile              | Distribution | Build Type (Android) | Build Type (iOS) | Channel     |
| -------------------- | ------------ | -------------------- | ---------------- | ----------- |
| `development`        | internal     | debug APK            | simulator        | development |
| `development-device` | internal     | debug APK            | device           | development |
| `preview`            | internal     | APK                  | Release          | preview     |
| `production`         | store        | AAB (app-bundle)     | Release          | production  |

---

## App Store Checklist

Before your first submission, make sure you have:

### Both platforms

- [x] App icon (1024×1024 — `assets/icon.png`)
- [x] Splash screen image (`assets/splash-icon.png`)
- [x] Privacy policy URL → `https://github.com/dominic-codespoti/GutAI/blob/main/PRIVACY_POLICY.md`
- [ ] Production backend deployed to Azure and accessible
- [ ] `EXPO_PUBLIC_API_URL` set to production URL in `eas.json`

### iOS specific

- [ ] Apple Developer Program membership ($99/yr)
- [ ] App Store Connect listing created
- [ ] Screenshots for required device sizes (6.7", 6.5", 5.5" — use Simulator)
- [ ] Camera usage description (already set in `app.json`)
- [ ] App Review information (demo account credentials, notes)

### Android specific

- [ ] Google Play Console developer account ($25)
- [ ] Google Play listing created
- [ ] Screenshots (phone + 7" tablet if supporting)
- [ ] Content rating questionnaire completed
- [ ] Data safety form completed
- [x] Health apps declaration for Health Connect permissions completed
- [ ] Service account key for automated submission

---

## Versioning

The user-visible version is managed in `app.json`; native build counters are managed remotely by EAS:

```jsonc
"version": "1.0.0" // User-visible version (both platforms)
// Native buildNumber/versionCode fields are intentionally omitted;
// production counters are managed remotely by EAS.
```

The `production` profile in `eas.json` has `"autoIncrement": true`, and `cli.appVersionSource` is `"remote"`, so native counters increase monotonically across clean checkouts.

---

## Quick Reference

```bash
# Full workflow: build → submit → done
make eas-prod          # Build for both platforms
make eas-submit        # Submit to both stores

# Hot-fixes require a new store build for this release.
```

---

## Troubleshooting

**Local-day data looks different between screens**
→ Restart the frontend and API after deployment so both use the timezone-aware date contract. User-local ranges send an IANA timezone; stored meal and symptom timestamps remain UTC instants. Data exports report the selected local date range while preserving UTC timestamps in each record.

**"Expo project not found"**
→ Run `eas init` and update the `projectId` in `app.json`

**iOS build fails on credentials**
→ Run `eas credentials --platform ios` to reconfigure

**Android submit fails**
→ Check that `google-services-key.json` exists and the service account has "Release manager" role

**Native build fails after a dependency or plugin change**
→ Run `npx expo-doctor`, then regenerate the managed native project with `npx expo prebuild --clean --platform android` or `--platform ios` and inspect the generated native files before retrying EAS.

---

---

# Backend — Azure Deployment (Bicep + GitHub Actions)

The backend API deploys to **Azure Container Apps** with **Azure Table Storage** via Bicep infrastructure-as-code and a GitHub Actions CI/CD pipeline. Container images are stored on **GitHub Container Registry (ghcr.io)** — free with your GitHub account, no Azure Container Registry needed.

## Architecture

```
  GitHub Container Registry (ghcr.io)
  ┌──────────────────────────────┐
  │ ghcr.io/dominic-codespoti/   │
  │   pinchy/gutai-api:sha-xxx   │
  └──────────────┬───────────────┘
                 │ pull
┌────────────────▼────────────────────────┐
│  Azure Resource Group (rg-gutai-{env})  │
│                                         │
│  ┌─────────────────────────────────┐    │
│  │ Container Apps Environment      │    │
│  │  ┌───────────────────────────┐  │    │
│  │  │ gutai-{env}-api           │  │    │
│  │  │ .NET 10 (0-3 replicas)   │  │    │
│  │  └───────────┬───────────────┘  │    │
│  └──────────────│──────────────────┘    │
│                 │                       │
│  ┌──────────────▼──────┐  ┌──────────┐  │
│  │ Storage Account     │  │ Log      │  │
│  │ (Table Storage)     │  │ Analytics│  │
│  └─────────────────────┘  └──────────┘  │
└─────────────────────────────────────────┘
```

## Prerequisites

- Azure subscription
- Azure CLI installed (`az --version`)
- GitHub CLI installed (`gh --version`)
- GitHub repo with Actions enabled

## Quick Start (Fully Automated)

A single script handles Azure setup and production deployment:

```bash
# Setup only
make azure-setup

# Setup + first production deploy
make azure-deploy
```

Or run the script directly:

```bash
./scripts/azure-setup.sh          # Setup only
./scripts/azure-setup.sh --deploy # Setup + production deploy
```

The script will:

1. ✅ Verify you're logged into Azure CLI and GitHub CLI
2. ✅ Create (or reuse) an Azure AD app registration — `appId` captured programmatically
3. ✅ Create the service principal and grant Contributor role
4. ✅ Create OIDC federated credentials for GitHub Actions (main branch + environments)
5. ✅ Create the Azure resource group
6. ✅ Set all GitHub Actions secrets automatically (`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `JWT_SECRET`)
7. ✅ Prompt for app-specific API keys (USDA, CalorieNinjas, Edamam) — skips if already set
8. ✅ Create GitHub environments (`staging`, `prod`)
9. ✅ (With `--deploy`) Build Docker image, push to GHCR, deploy Bicep, and smoke test

## Manual Steps (Reference)

<details>
<summary>Click to expand manual setup steps (if you prefer not to use the script)</summary>

### Create Azure Service Principal

```bash
az ad app create --display-name "gutai-github-actions"
APP_ID="<appId from output>"
az ad sp create --id $APP_ID
SP_OBJECT_ID=$(az ad sp show --id $APP_ID --query id -o tsv)
SUBSCRIPTION_ID=$(az account show --query id -o tsv)
az role assignment create \
  --assignee-object-id $SP_OBJECT_ID \
  --assignee-principal-type ServicePrincipal \
  --role Contributor \
  --scope /subscriptions/$SUBSCRIPTION_ID
```

### Create Federated Credentials

```bash
az ad app federated-credential create --id $APP_ID --parameters '{
  "name": "gutai-main",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:dominic-codespoti/GutAI:ref:refs/heads/main",
  "audiences": ["api://AzureADTokenExchange"]
}'
```

### Configure GitHub Secrets

| Secret                  | Value                         |
| ----------------------- | ----------------------------- |
| `AZURE_CLIENT_ID`       | App registration client ID    |
| `AZURE_TENANT_ID`       | Azure AD tenant ID            |
| `AZURE_SUBSCRIPTION_ID` | Subscription ID               |
| `JWT_SECRET`            | Random 32+ char string        |
| `USDA_API_KEY`          | USDA FoodData Central API key |
| `CALORIENINJAS_API_KEY` | CalorieNinjas API key         |
| `EDAMAM_APP_ID`         | Edamam application ID         |
| `EDAMAM_APP_KEY`        | Edamam application key        |

</details>

## Deploying After Setup

### Automatic (on push to main)

Any push to `main` that changes `backend/` or `infra/` triggers the pipeline:

1. **Test** — Runs `dotnet test`
2. **Build & Push** — Builds Docker image, pushes to `ghcr.io`
3. **Deploy Infra** — Runs Bicep deployment (creates/updates all Azure resources)
4. **Smoke Test** — Hits `/health` endpoint to verify

### Manual

```bash
# Via GitHub CLI
gh workflow run deploy.yml -f environment=staging
gh workflow run deploy.yml -f environment=prod

# Via GitHub UI: Actions → Deploy Backend to Azure → Run workflow → Select environment
```

## Update EAS with the API URL

Once deployed, get the API URL:

```bash
az deployment group show \
  --resource-group rg-gutai-prod \
  --name main \
  --query properties.outputs.apiUrl.value -o tsv
```

Then update `frontend/eas.json` → `build.production.env.EXPO_PUBLIC_API_URL` with that URL.

## Production AI Configuration

Production's shared AI deployment is `gpt-5.4-mini`, and its deployment name and input/output prices are Bicep parameters passed explicitly by `.github/workflows/deploy.yml` (`0.20` and `1.20` USD per 1M tokens). The workflow deploys `infra/main.bicep` on every push to `main`; manual `az containerapp update --set-env-vars` changes to these values are overwritten by the next deploy.

Before changing the deployment, update both prices and run `make evals` plus the photo re-baseline required by AGENTS.md §18. Per-workload deployments would need new Bicep parameters; none are used today, so all workloads use the shared deployment. Per-workload reasoning effort and the Coach limits (`MaxToolIterations`, `MaxConsecutiveToolErrors`) come from `AzureOpenAI:Workloads` in `backend/src/GutAI.Api/appsettings.json`, the same values `make evals` evaluates.

Without pricing for a deployment, estimated cost is `null`; the p95 cost alert has no priced scan values to evaluate and cannot fire.

The following feature flags default to `false` in `Features`: `HiddenCalories`, `PortionCalibration`, `MealSuggestions`, and `WebGrounding`. `MealScan:RequireCompatibilityAgreement` and `MealScan:MultiQueryAutoSelect` also default to `false`.

Runtime defaults below come from the corresponding service configuration reads; set overrides in the production configuration source only when intentionally changing the behavior:

| Setting | Default | Purpose |
| --- | ---: | --- |
| `Mcp:MinCommitDelaySeconds` | `20` | Minimum time after an MCP-origin proposal before its draft can be committed |
| `MealDrafts:ClosedRetentionDays` | `90` | Retention for closed meal drafts |
| `MealDrafts:CleanupIntervalHours` | `24` | Draft-cleanup service interval |
| `MealScan:DeadlineSeconds` | `60` | Overall synchronous photo-scan deadline |
| `MealScan:MinSecondsForSelection` | `8` | Remaining time required to start batched candidate selection |
| `MealScan:MinSecondsForWeb` | `6` | Remaining time required to start the web lookup stage |
| `MealScan:MaxWebQueriesPerScan` | `2` | Maximum web lookup queries per scan |
| `MealSuggestions:MaxSuggestions` | `3` | Maximum suggestions returned per request |
| `MealSuggestions:MaxItemsPerSuggestion` | `6` | Maximum items in a suggestion |
| `MealSuggestions:MaxPoolSize` | `60` | Maximum server-built food candidates |
| `MealSuggestions:KcalTolerance` | `0.10` | Allowed relative calorie difference from the meal target (10%) |
| `MealSuggestions:MealShares:Breakfast` | `0.25` | Default share of the daily calorie goal |
| `MealSuggestions:MealShares:Lunch` | `0.35` | Default share of the daily calorie goal |
| `MealSuggestions:MealShares:Dinner` | `0.30` | Default share of the daily calorie goal |
| `MealSuggestions:MealShares:Snack` | `0.10` | Default share of the daily calorie goal |

`MealDrafts:TtlHours` defaults to `24` for pending drafts. `MealScan:MaxComponentsPerPhoto` defaults to `12`; hidden-calorie limits are `MealScan:MaxInferredComponents` (`3`) and `MealScan:MaxInferredGramsPerMeal` (`40`). The scan stages use the remaining overall deadline; they do not have independent fixed-duration timeouts.

### Feature-flag rollout

Keep every behavior flag off until both the golden scan gate and the AgentEvalHarness gate pass on the candidate build. Then enable and assess one at a time following the plan's sequence: `Features:HiddenCalories`, `Features:PortionCalibration`, `Features:MealSuggestions`, `Features:WebGrounding`, and `MealScan:MultiQueryAutoSelect`. Keep WebGrounding off until its separate source/terms review in decision D6 is complete, even if the gates pass. Enable `MealScan:RequireCompatibilityAgreement` only after the same gates pass. Re-run the relevant gates and review results before proceeding.

## Application Insights Alerts

The optional scan alerts are deployed only when `alertEmailAddress` is non-empty. The action group emails that address. Both scheduled query rules evaluate every 15 minutes over a one-hour window, auto-mitigate, and send severity-2 cost or severity-3 latency alerts:

| Parameter | Default | Meaning |
| --- | ---: | --- |
| `alertEmailAddress` | `''` | Recipient; leave empty to disable the action group and both rules |
| `scanCostP95ThresholdUsd` | `0.05` | Alert when p95 estimated AI cost per `meal_scan` exceeds this USD value |
| `scanLatencyP95BudgetMs` | `45000` | Alert when p95 `POST /api/meals/scan/image` duration exceeds this many milliseconds (75% of the 60-second scan deadline) |

Pass these parameters to `az deployment group create` (or set them in `infra/main.bicepparam`) to enable and tune notifications. Cost calculations require non-empty Bicep pricing parameters for the deployment.

The `Logging:OpenTelemetry:LogLevel` provider filter is the one used by the OpenTelemetry logger exported through Application Insights 3.x. Production sets the `GutAI.Infrastructure.Services.MealScanService` category to `Information` while the broader production default remains `Error`, so the structured per-scan usage trace can drive the cost query.


## AI Evaluation and Repair Operations

The evaluation harnesses are run manually on demand, never on a schedule or in GitHub; the regular CI workflow runs build, unit/integration tests, and contract checks only and makes no model calls.

- Run the live gates manually with `make evals` from the repository root; it runs the photo-scan gate, then the Coach, describe-food, and label suites. Use `make evals-photo` or `make evals-agents` to run only one part. The harness READMEs retain the direct `dotnet run` commands for advanced use.

  ```sh
  az login
  make evals
  ```

- The runner loads `AzureOpenAI` from `backend/src/GutAI.Api/appsettings.json` and overlays `backend/src/GutAI.Api/appsettings.Development.json`; existing environment variables override those values, and `AZURE_OPENAI_ENDPOINT` overrides the endpoint. Pricing defaults to $0.20 input / $1.20 output per 1M tokens for the `gpt-5.4-mini` deployment (gpt-5.6-luna list prices); override with `EVAL_INPUT_PER_1M` and `EVAL_OUTPUT_PER_1M`. `EVAL_SUITE` defaults to `all` and `EVAL_REPEAT` to `1`.
- The Azure CLI login is required; Docker is required for agent evals. If Azurite is not listening on `127.0.0.1:10002`, the runner starts a temporary container and removes it on exit. `GUTAI_EVAL_STORAGE` defaults to `UseDevelopmentStorage=true`.
- Reports are written to `eval-reports/<UTC timestamp>/`. `scripts/run-ai-evals.sh` exits `0` when all executed gates pass, `1` when at least one gate fails, and `2` when a prerequisite or configuration is missing. Through `make`, any non-zero result shows as make's own exit status `2`; the printed summary says which gate failed. `make evals` runs both parts even if the photo gate fails. The photo gate runs against a temporary copy of `golden-images`, leaving `golden-images/.cache` untouched; it takes about 2 minutes and costs about $0.02. Agent evals take about 4 minutes and cost about $0.05 per repeat.

- Run the gates before shipping changes to prompts or schemas, model deployments or effort, grounding/search ranking, calibration, or Coach tools. Review the reports and keep them with the change when relevant.
- [CorrectionAnalytics README](../backend/tools/CorrectionAnalytics/README.md) documents the read-only operator report: `dotnet run --project backend/tools/CorrectionAnalytics -- --connection "<storage connection string>" --out correction-report.json`. Treat its output as sensitive operational data; review and explicitly approve a calibration snippet before applying it.
- `ScanMealRepair` currently has no README. Its [program and usage](../backend/tools/ScanMealRepair/Program.cs) require a Table Storage connection (`--connection` or `GUTAI_STORAGE_CONNECTION`); run `dotnet run --project backend/tools/ScanMealRepair -- --connection "<storage connection string>"` for a dry run, optionally scoped with `--user <guid>`. Only add `--apply` after reviewing the dry-run report; that mode updates historical meal-item nutrition and meal totals.


## Cost Estimate

With scale-to-zero and minimal usage:

| Resource                         | Estimated Monthly Cost           |
| -------------------------------- | -------------------------------- |
| Container Apps (0-3 replicas)    | ~$0–15 (scale to zero when idle) |
| Storage Account (Table Storage)  | ~$1–5                            |
| Log Analytics (30-day)           | ~$2–5                            |
| GHCR (GitHub Container Registry) | Free (included with GitHub)      |
| **Total**                        | **~$3–20/month**                 |

## Files

| File                           | Purpose                                                            |
| ------------------------------ | ------------------------------------------------------------------ |
| `infra/main.bicep`             | Azure resources (Storage, Container Apps, Log Analytics)           |
| `infra/main.bicepparam`        | Default parameter values                                           |
| `scripts/azure-setup.sh`       | Fully automated Azure + GitHub setup script                        |
| `.github/workflows/deploy.yml` | CI/CD pipeline (test → build → push to GHCR → deploy → smoke test) |
| `.github/workflows/ci.yml`     | PR checks (build, lint, type check)                                |
