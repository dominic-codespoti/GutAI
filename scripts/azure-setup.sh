#!/usr/bin/env bash
set -euo pipefail

# ─────────────────────────────────────────────────────────────────────────────
# GutAI — Automated Azure + GitHub Setup
#
# Single resource group, single region. Run once.
#
# This script:
#   1. Ensures you're logged into Azure CLI + GitHub CLI
#   2. Creates (or reuses) an Azure AD app registration + service principal
#   3. Grants Contributor on the subscription
#   4. Creates OIDC federated credentials for GitHub Actions
#   5. Creates the Azure resource group
#   6. Sets all required GitHub Actions secrets
#   7. Optionally does the first deploy (build → push → Bicep)
#
# Prerequisites:
#   - Azure CLI (az) installed
#   - GitHub CLI (gh) installed
#   - Docker installed (for --deploy only)
#
# Usage:
#   ./scripts/azure-setup.sh            # Setup Azure + GitHub secrets
#   ./scripts/azure-setup.sh --deploy   # Setup + first deploy
# ─────────────────────────────────────────────────────────────────────────────

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m'

log()  { echo -e "${CYAN}▸${NC} $*"; }
ok()   { echo -e "${GREEN}✅ $*${NC}"; }
warn() { echo -e "${YELLOW}⚠️  $*${NC}"; }
err()  { echo -e "${RED}❌ $*${NC}" >&2; exit 1; }

retry() {
  local max_attempts="${1:-3}"
  local delay=5
  shift
  for i in $(seq 1 "$max_attempts"); do
    if "$@"; then return 0; fi
    if [[ $i -lt $max_attempts ]]; then
      warn "Attempt ${i}/${max_attempts} failed, retrying in ${delay}s..."
      sleep "$delay"
      delay=$((delay * 2))
    fi
  done
  return 1
}

retry_or_die() {
  if ! retry "$@"; then
    err "Command failed after $1 attempts (network issue?). Re-run the script to resume."
  fi
}

# ── Config (hardcoded — one RG, one region) ──────────────────────────────────
RESOURCE_GROUP="rg-gutai-prod"
LOCATION="australiaeast"
GITHUB_REPO="dominic-codespoti/GutAI"
APP_DISPLAY_NAME="gutai-github-actions"
DO_DEPLOY=false

while [[ $# -gt 0 ]]; do
  case $1 in
    --deploy) DO_DEPLOY=true; shift ;;
    -h|--help) echo "Usage: $0 [--deploy]"; exit 0 ;;
    *) err "Unknown arg: $1" ;;
  esac
done

echo ""
echo -e "${CYAN}╔══════════════════════════════════════════════╗${NC}"
echo -e "${CYAN}║        GutAI — Azure Setup                   ║${NC}"
echo -e "${CYAN}╚══════════════════════════════════════════════╝${NC}"
echo ""
log "Resource Group: ${RESOURCE_GROUP}"
log "Location:       ${LOCATION}"
log "GitHub Repo:    ${GITHUB_REPO}"
log "First Deploy:   ${DO_DEPLOY}"
echo ""

# ── 0. Check prerequisites ───────────────────────────────────────────────────
command -v az >/dev/null 2>&1 || err "Azure CLI (az) not found. Install: https://aka.ms/install-azure-cli"
command -v gh >/dev/null 2>&1 || err "GitHub CLI (gh) not found. Install: https://cli.github.com"
if $DO_DEPLOY; then
  command -v docker >/dev/null 2>&1 || err "Docker not found (required for --deploy)"
fi

# ── 1. Ensure Azure login ────────────────────────────────────────────────────
log "Checking Azure login..."
if ! az account show &>/dev/null; then
  warn "Not logged into Azure. Opening browser login..."
  az login
fi

SUBSCRIPTION_ID=$(az account show --query id -o tsv)
TENANT_ID=$(az account show --query tenantId -o tsv)
SUBSCRIPTION_NAME=$(az account show --query name -o tsv)
ok "Azure: ${SUBSCRIPTION_NAME} (${SUBSCRIPTION_ID})"

# ── 2. Ensure GitHub login ───────────────────────────────────────────────────
log "Checking GitHub login..."
if ! gh auth status &>/dev/null; then
  warn "Not logged into GitHub CLI. Running gh auth login..."
  gh auth login
fi
GH_USER=$(gh api user --jq '.login')
ok "GitHub: ${GH_USER}"

# ── 3. Create or reuse Azure AD App Registration ─────────────────────────────
log "Looking for existing app registration '${APP_DISPLAY_NAME}'..."
APP_ID=$(retry 3 az ad app list --display-name "${APP_DISPLAY_NAME}" --query "[0].appId" -o tsv || true)

if [[ -z "$APP_ID" || "$APP_ID" == "None" ]]; then
  log "Creating new app registration..."
  APP_ID=$(retry_or_die 3 az ad app create --display-name "${APP_DISPLAY_NAME}" --query appId -o tsv)
  ok "Created app registration: ${APP_ID}"
else
  ok "Reusing existing app registration: ${APP_ID}"
fi

# ── 4. Ensure Service Principal exists ────────────────────────────────────────
log "Ensuring service principal exists..."
SP_OBJECT_ID=$(retry 3 az ad sp show --id "$APP_ID" --query id -o tsv 2>/dev/null || true)

if [[ -z "$SP_OBJECT_ID" || "$SP_OBJECT_ID" == "None" ]]; then
  SP_OBJECT_ID=$(retry_or_die 3 az ad sp create --id "$APP_ID" --query id -o tsv)
  ok "Created service principal: ${SP_OBJECT_ID}"
else
  ok "Service principal exists: ${SP_OBJECT_ID}"
fi

# ── 5. Grant Contributor on subscription ──────────────────────────────────────
log "Ensuring Contributor role assignment..."
EXISTING_ROLE=$(retry 3 az role assignment list \
  --assignee "$SP_OBJECT_ID" \
  --role Contributor \
  --scope "/subscriptions/${SUBSCRIPTION_ID}" \
  --query "[0].id" -o tsv 2>/dev/null || true)

if [[ -z "$EXISTING_ROLE" || "$EXISTING_ROLE" == "None" ]]; then
  retry_or_die 3 az role assignment create \
    --assignee-object-id "$SP_OBJECT_ID" \
    --assignee-principal-type ServicePrincipal \
    --role Contributor \
    --scope "/subscriptions/${SUBSCRIPTION_ID}" \
    --output none
  ok "Granted Contributor role"
else
  ok "Contributor role already assigned"
fi

# ── 6. Create OIDC Federated Credentials ─────────────────────────────────────
create_federated_credential() {
  local NAME="$1"
  local SUBJECT="$2"

  EXISTING=$(retry 3 az ad app federated-credential list --id "$APP_ID" --query "[?name=='${NAME}'].name" -o tsv 2>/dev/null || true)
  if [[ -n "$EXISTING" && "$EXISTING" != "None" ]]; then
    ok "Federated credential '${NAME}' already exists"
    return
  fi

  log "Creating federated credential '${NAME}'..."
  retry_or_die 3 az ad app federated-credential create --id "$APP_ID" --parameters "{
    \"name\": \"${NAME}\",
    \"issuer\": \"https://token.actions.githubusercontent.com\",
    \"subject\": \"${SUBJECT}\",
    \"audiences\": [\"api://AzureADTokenExchange\"]
  }" --output none
  ok "Created federated credential '${NAME}'"
}

create_federated_credential "gutai-main" "repo:${GITHUB_REPO}:ref:refs/heads/main"
create_federated_credential "gutai-prod" "repo:${GITHUB_REPO}:environment:prod"

# ── 7. Create Resource Group ─────────────────────────────────────────────────
log "Ensuring resource group '${RESOURCE_GROUP}'..."
retry_or_die 3 az group create \
  --name "$RESOURCE_GROUP" \
  --location "$LOCATION" \
  --tags project=gutai \
  --output none
ok "Resource group '${RESOURCE_GROUP}' ready"

# ── 8. Set GitHub Secrets ─────────────────────────────────────────────────────
log "Setting GitHub Actions secrets..."

set_secret() {
  local NAME="$1"
  local VALUE="$2"
  echo "$VALUE" | gh secret set "$NAME" --repo "$GITHUB_REPO" 2>/dev/null
  ok "Secret: ${NAME}"
}

prompt_secret() {
  local NAME="$1"
  local DESC="$2"
  local OPTIONAL="${3:-false}"

  if gh secret list --repo "$GITHUB_REPO" 2>/dev/null | grep -q "^${NAME}"; then
    ok "Secret '${NAME}' already set (skipping)"
    return
  fi

  if [[ "$OPTIONAL" == "true" ]]; then
    read -rp "$(echo -e "${YELLOW}${DESC} (Enter to skip):${NC} ")" VALUE
    if [[ -z "$VALUE" ]]; then
      warn "Skipped optional secret: ${NAME}"
      return
    fi
  else
    while true; do
      read -rp "$(echo -e "${YELLOW}${DESC}:${NC} ")" VALUE
      [[ -n "$VALUE" ]] && break
      echo -e "${RED}Required — cannot be empty.${NC}"
    done
  fi
  set_secret "$NAME" "$VALUE"
}

set_secret "AZURE_CLIENT_ID" "$APP_ID"
set_secret "AZURE_TENANT_ID" "$TENANT_ID"
set_secret "AZURE_SUBSCRIPTION_ID" "$SUBSCRIPTION_ID"

echo ""
log "Checking application secrets..."

if gh secret list --repo "$GITHUB_REPO" 2>/dev/null | grep -q "^JWT_SECRET"; then
  ok "Secret 'JWT_SECRET' already set (skipping)"
else
  JWT_SECRET=$(openssl rand -base64 48)
  set_secret "JWT_SECRET" "$JWT_SECRET"
  ok "Auto-generated JWT_SECRET"
fi

prompt_secret "USDA_API_KEY" "USDA FoodData Central API key (get one at https://fdc.nal.usda.gov/api-key-signup)" false

# ── 9. Create GitHub Environment ─────────────────────────────────────────────
log "Ensuring GitHub environment..."
gh api -X PUT "repos/${GITHUB_REPO}/environments/prod" --silent 2>/dev/null || true
ok "Environment: prod"

# ── 10. (Optional) First Deploy ──────────────────────────────────────────────
if $DO_DEPLOY; then
  echo ""
  echo -e "${CYAN}── First Deploy ──${NC}"

  IMAGE="ghcr.io/${GITHUB_REPO}/gutai-api:latest"

  log "Logging into GHCR..."
  GH_TOKEN=$(gh auth token)
  echo "$GH_TOKEN" | docker login ghcr.io -u "$GH_USER" --password-stdin

  log "Building Docker image..."
  docker build -t "$IMAGE" ./backend

  log "Pushing to GHCR..."
  docker push "$IMAGE"
  ok "Image pushed: ${IMAGE}"

  log "Deploying Bicep to '${RESOURCE_GROUP}'..."

  if [[ -z "${JWT_SECRET:-}" ]]; then
    JWT_SECRET=$(openssl rand -base64 48)
    set_secret "JWT_SECRET" "$JWT_SECRET"
  fi

  read -rp "$(echo -e "${YELLOW}USDA API key for deploy (or Enter to use DEMO_KEY):${NC} ")" USDA_KEY
  USDA_KEY="${USDA_KEY:-DEMO_KEY}"

  az deployment group create \
    --resource-group "$RESOURCE_GROUP" \
    --template-file infra/main.bicep \
    --parameters \
      containerImage="$IMAGE" \
      jwtSecret="$JWT_SECRET" \
      usdaApiKey="$USDA_KEY" \
      ghcrUsername="$GH_USER" \
      ghcrPassword="$GH_TOKEN"

  API_URL=$(az deployment group show \
    --resource-group "$RESOURCE_GROUP" \
    --name main \
    --query properties.outputs.apiUrl.value -o tsv)

  ok "API deployed to: ${API_URL}"

  log "Running smoke test..."
  for i in $(seq 1 12); do
    STATUS=$(curl -s -o /dev/null -w "%{http_code}" "${API_URL}/health" 2>/dev/null || echo "000")
    if [[ "$STATUS" == "200" ]]; then
      ok "Health check passed!"
      break
    fi
    echo "  Attempt ${i}/12: status=${STATUS}, retrying in 10s..."
    sleep 10
  done
fi

# ── Summary ───────────────────────────────────────────────────────────────────
echo ""
echo -e "${GREEN}╔══════════════════════════════════════════════╗${NC}"
echo -e "${GREEN}║          Setup Complete!                      ║${NC}"
echo -e "${GREEN}╚══════════════════════════════════════════════╝${NC}"
echo ""
echo -e "  Azure AD App:      ${CYAN}${APP_ID}${NC}"
echo -e "  Tenant:            ${CYAN}${TENANT_ID}${NC}"
echo -e "  Subscription:      ${CYAN}${SUBSCRIPTION_ID}${NC}"
echo -e "  Resource Group:    ${CYAN}${RESOURCE_GROUP}${NC}"
echo -e "  GitHub Repo:       ${CYAN}${GITHUB_REPO}${NC}"
echo ""
if $DO_DEPLOY; then
  echo -e "  API URL:           ${CYAN}${API_URL}${NC}"
  echo ""
  echo -e "  Next: Update frontend/eas.json with the API URL above."
else
  echo -e "  Next steps:"
  echo -e "    ${CYAN}1.${NC} Push to main (changes to backend/ or infra/) to auto-deploy"
  echo -e "    ${CYAN}2.${NC} Or run: ${YELLOW}gh workflow run deploy.yml${NC}"
  echo -e "    ${CYAN}3.${NC} Or re-run with --deploy: ${YELLOW}./scripts/azure-setup.sh --deploy${NC}"
fi
echo ""
