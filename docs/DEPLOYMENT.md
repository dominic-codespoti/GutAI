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

Also update the OTA updates URL:

```jsonc
"updates": {
  "url": "https://u.expo.dev/xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
}
```

---

## Step 2 — Configure credentials

### iOS

EAS can manage all iOS credentials for you automatically. On first build it will prompt you to log in to your Apple Developer account and handle certificates + provisioning profiles.

If you prefer manual management:

```bash
eas credentials --platform ios
```

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

| Profile       | `EXPO_PUBLIC_API_URL`           | Purpose           |
| ------------- | ------------------------------- | ----------------- |
| `development` | `http://localhost:5000`         | Local dev         |
| `preview`     | `https://api-staging.gutai.app` | Internal testing  |
| `production`  | `https://api.gutai.app`         | App Store release |

**You must deploy the backend to a public URL before submitting.** The localhost URLs won't work on real devices in production builds.

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

For JS-only changes (no native module changes), push updates instantly without going through app review:

```bash
# Production channel
make eas-update
# or
cd frontend && npm run update

# Preview channel
cd frontend && npm run update:preview
```

OTA updates are picked up on next app launch. This is configured via the `runtimeVersion` and `updates.url` in `app.json`.

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
- [x] Privacy policy URL → `https://github.com/dominic-codespoti/pinchy/blob/main/PRIVACY_POLICY.md`
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
- [ ] Service account key for automated submission

---

## Versioning

Versions are managed in `app.json`:

```jsonc
"version": "1.0.0",          // User-visible version (both platforms)
"ios.buildNumber": "1",       // iOS build number (auto-incremented by EAS in production)
"android.versionCode": 1      // Android version code (auto-incremented by EAS in production)
```

The `production` profile in `eas.json` has `"autoIncrement": true`, so build numbers increase automatically on each production build.

---

## Quick Reference

```bash
# Full workflow: build → submit → done
make eas-prod          # Build for both platforms
make eas-submit        # Submit to both stores

# Hot-fix a JS bug without app review
make eas-update        # OTA update to production
```

---

## Troubleshooting

**"Expo project not found"**
→ Run `eas init` and update the `projectId` in `app.json`

**iOS build fails on credentials**
→ Run `eas credentials --platform ios` to reconfigure

**Android submit fails**
→ Check that `google-services-key.json` exists and the service account has "Release manager" role

**OTA update not appearing**
→ The `runtimeVersion` must match between the build and the update. If you changed native modules, you need a new build.

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
│  Azure Resource Group (gutai-{env})     │
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
- GitHub repo with Actions enabled

## Step 1 — Create Azure Service Principal

The GitHub Actions workflow uses OIDC (federated credentials) to authenticate with Azure. This is more secure than storing client secrets.

```bash
# Create a service principal
az ad app create --display-name "gutai-github-actions"

# Note the appId (client ID) from the output, then:
APP_ID="<appId from above>"
az ad sp create --id $APP_ID

# Get the object ID of the service principal
SP_OBJECT_ID=$(az ad sp show --id $APP_ID --query id -o tsv)

# Grant it Contributor on your subscription (or scope to a resource group)
SUBSCRIPTION_ID=$(az account show --query id -o tsv)
az role assignment create \
  --assignee-object-id $SP_OBJECT_ID \
  --assignee-principal-type ServicePrincipal \
  --role Contributor \
  --scope /subscriptions/$SUBSCRIPTION_ID

# Create federated credentials for GitHub Actions
az ad app federated-credential create --id $APP_ID --parameters '{
  "name": "gutai-main",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:dominic-codespoti/pinchy:ref:refs/heads/main",
  "audiences": ["api://AzureADTokenExchange"]
}'

# Also for environment-based deployments
az ad app federated-credential create --id $APP_ID --parameters '{
  "name": "gutai-staging",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:dominic-codespoti/pinchy:environment:staging",
  "audiences": ["api://AzureADTokenExchange"]
}'

az ad app federated-credential create --id $APP_ID --parameters '{
  "name": "gutai-prod",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:dominic-codespoti/pinchy:environment:prod",
  "audiences": ["api://AzureADTokenExchange"]
}'
```

## Step 2 — Configure GitHub Secrets

Go to **GitHub repo → Settings → Secrets and variables → Actions** and add:

### Secrets (Settings → Secrets)

| Secret                  | Value                                                   |
| ----------------------- | ------------------------------------------------------- |
| `AZURE_CLIENT_ID`       | App registration client ID                              |
| `AZURE_TENANT_ID`       | Azure AD tenant ID (`az account show --query tenantId`) |
| `AZURE_SUBSCRIPTION_ID` | Subscription ID (`az account show --query id`)          |
| `JWT_SECRET`            | Random 32+ char string for JWT signing                  |
| `USDA_API_KEY`          | USDA FoodData Central API key                           |
| `CALORIENINJAS_API_KEY` | CalorieNinjas API key                                   |
| `EDAMAM_APP_ID`         | Edamam application ID                                   |
| `EDAMAM_APP_KEY`        | Edamam application key                                  |

No GHCR credentials needed — the workflow uses `GITHUB_TOKEN` automatically.

### Variables (Settings → Variables)

| Variable       | Value                                |
| -------------- | ------------------------------------ |
| `CORS_ORIGINS` | `https://gutai.app` (or your domain) |

## Step 3 — Deploy

### Automatic (on push to main)

Any push to `main` that changes `backend/` or `infra/` triggers the pipeline:

1. **Test** — Runs `dotnet test`
2. **Build & Push** — Builds Docker image, pushes to `ghcr.io`
3. **Deploy Infra** — Runs Bicep deployment (creates/updates all Azure resources)
4. **Smoke Test** — Hits `/health` endpoint to verify

### Manual

```bash
# Via GitHub UI: Actions → Deploy Backend to Azure → Run workflow → Select environment

# Or via CLI:
gh workflow run deploy.yml -f environment=staging
gh workflow run deploy.yml -f environment=prod
```

### First-time local deploy (without GitHub Actions)

```bash
# Login
az login

# Create resource group
az group create --name gutai-staging --location australiaeast

# Build and push image to GHCR
echo $GITHUB_PAT | docker login ghcr.io -u YOUR_USERNAME --password-stdin
docker build -t ghcr.io/dominic-codespoti/pinchy/gutai-api:latest ./backend
docker push ghcr.io/dominic-codespoti/pinchy/gutai-api:latest

# Deploy infra (will prompt for secure params)
az deployment group create \
  --resource-group gutai-staging \
  --template-file infra/main.bicep \
  --parameters \
    environment=staging \
    containerImage=ghcr.io/dominic-codespoti/pinchy/gutai-api:latest \
    ghcrUsername=YOUR_USERNAME \
    ghcrPassword=$GITHUB_PAT
```

## Step 4 — Update EAS with the API URL

Once deployed, get the API URL:

```bash
az deployment group show \
  --resource-group gutai-prod \
  --name main \
  --query properties.outputs.apiUrl.value -o tsv
```

Then update `frontend/eas.json` → `build.production.env.EXPO_PUBLIC_API_URL` with that URL.

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
| `.github/workflows/deploy.yml` | CI/CD pipeline (test → build → push to GHCR → deploy → smoke test) |
| `.github/workflows/ci.yml`     | PR checks (build, lint, type check)                                |
