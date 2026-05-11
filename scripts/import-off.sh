#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"

# ── Config ─────────────────────────────────────────────────────────────────────
# Priority: CLI arg > env var > prompt
STORAGE_ACCOUNT="${1:-${AzureStorage__AccountName:-}}"

if [ -z "$STORAGE_ACCOUNT" ]; then
  echo "Enter your Azure Storage account name (or set AzureStorage__AccountName):"
  read -r STORAGE_ACCOUNT
fi

if [ -z "$STORAGE_ACCOUNT" ]; then
  echo "❌ Storage account name is required."
  echo "   Usage: $0 [storage-account-name]"
  echo "   Or:    AzureStorage__AccountName=myaccount $0"
  exit 1
fi

# ── Run ─────────────────────────────────────────────────────────────────────────
echo "========================================"
echo "🍽  OFF Data Dump Import"
echo "   Storage account: $STORAGE_ACCOUNT"
echo "   This downloads ~12 GB and takes ~1 hour."
echo "========================================"
echo ""

AzureStorage__AccountName="$STORAGE_ACCOUNT" \
  dotnet run --project "$PROJECT_DIR/backend/src/GutAI.Api" -- --import-off

echo ""
echo "✅ Import complete."
