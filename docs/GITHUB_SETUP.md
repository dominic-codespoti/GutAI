# GitHub Repository Setup for Deployment

This guide covers everything you need to configure in the GitHub repo (`dominic-codespoti/GutAI`) to enable automated Azure deployments.

---

## 1. GitHub Actions Secrets

Go to **Settings → Secrets and variables → Actions → Secrets** and create these:

| Secret                  | Value                            | How to get it                                                                                                      |
| ----------------------- | -------------------------------- | ------------------------------------------------------------------------------------------------------------------ |
| `AZURE_CLIENT_ID`       | Azure app registration client ID | From `az ad app create` output                                                                                     |
| `AZURE_TENANT_ID`       | Azure AD tenant ID               | `az account show --query tenantId -o tsv`                                                                          |
| `AZURE_SUBSCRIPTION_ID` | Azure subscription ID            | `az account show --query id -o tsv`                                                                                |
| `GH_PAT`                | GitHub Personal Access Token     | Create at [Developer Settings](https://github.com/settings/tokens/new) (scopes: `read:packages`, `write:packages`) |
| `JWT_SECRET`            | Random 32+ char string           | `openssl rand -base64 32`                                                                                          |
| `USDA_API_KEY`          | Your USDA FoodData Central key   | https://fdc.nal.usda.gov/api-key-signup                                                                            |
| `CALORIENINJAS_API_KEY` | Your CalorieNinjas key           | https://calorieninjas.com/api                                                                                      |
| `EDAMAM_APP_ID`         | Edamam application ID            | https://developer.edamam.com                                                                                       |
| `EDAMAM_APP_KEY`        | Edamam application key           | Same as above                                                                                                      |

> `GITHUB_TOKEN` is provided automatically — no setup needed for GHCR auth.

---

## 2. GitHub Environments

Go to **Settings → Environments** and create two environments:

- **`staging`** — no protection rules needed
- **`prod`** — optionally add "Required reviewers" so prod deploys need manual approval

---

## 3. Azure OIDC Setup (one-time)

Run these commands locally to allow GitHub Actions to authenticate with Azure without storing a client secret:

```bash
# Create the app registration
az ad app create --display-name "gutai-github-actions"
# Note the appId → that's your AZURE_CLIENT_ID

APP_ID="<appId>"
az ad sp create --id $APP_ID

# Grant Contributor on your subscription
SUBSCRIPTION_ID=$(az account show --query id -o tsv)
SP_OBJECT_ID=$(az ad sp show --id $APP_ID --query id -o tsv)
az role assignment create \
  --assignee-object-id $SP_OBJECT_ID \
  --assignee-principal-type ServicePrincipal \
  --role Contributor \
  --scope /subscriptions/$SUBSCRIPTION_ID

# Federated credentials (one per trigger)
az ad app federated-credential create --id $APP_ID --parameters '{
  "name": "gutai-main",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:dominic-codespoti/GutAI:ref:refs/heads/main",
  "audiences": ["api://AzureADTokenExchange"]
}'

az ad app federated-credential create --id $APP_ID --parameters '{
  "name": "gutai-staging",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:dominic-codespoti/GutAI:environment:staging",
  "audiences": ["api://AzureADTokenExchange"]
}'

az ad app federated-credential create --id $APP_ID --parameters '{
  "name": "gutai-prod",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:dominic-codespoti/GutAI:environment:prod",
  "audiences": ["api://AzureADTokenExchange"]
}'
```

---

## 4. Repository Settings

- **Actions → General → Workflow permissions** → set to **"Read and write permissions"** (needed so `GITHUB_TOKEN` can push to GHCR)
- **Packages** → will auto-create once the first image is pushed.
- **Package Settings** (once created) → ensure **"Linked repositories"** shows your repo and **"Inherit permissions"** (or explicit write) is enabled if using an organization.

> **⚠️ WARNING on GHCR + Azure Container Apps**
> Using `GITHUB_TOKEN` for the Registry password in Azure is fine for the _first_ deployment. However, the token will expire. For long-term production use, you should create a personal access token (`GH_PAT`) and store it as a secret. The workflow will automatically pick up `GH_PAT` if available, otherwise it falls back to `GITHUB_TOKEN`.

---

## Summary

Once these 4 things are done, push to `main` with any change to `backend/` or `infra/` and the pipeline runs automatically:

**test → build → push to GHCR → deploy to Azure → smoke test**
