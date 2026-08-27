#!/usr/bin/env bash
# refresh-food-data.sh — regenerate the embedded static food databases.
#
# The embedded catalogs (WholeFoodsDatabase.cs, BrandedFoodsDatabase.cs,
# AustralianFoodsDatabase.cs) are compile-time snapshots. They are ONLY refreshed
# by re-running the generators in backend/tools/* — there is no automatic pipeline.
#
# Prerequisites (not installed by this script):
#   - python3 with openpyxl (for the AUSNUT Excel path)
#   - curl + enough disk for USDA FDC bulk exports (~1-2 GB compressed)
#   - FSANZ AUSNUT 2011-13 (or newer) survey file (.xlsx) if refreshing AU data
#
# Usage:
#   ./scripts/refresh-food-data.sh [--usda] [--branded] [--ausnut <file.xlsx>] [--all]
#
# After regeneration, review the diff of the generated .cs files and run `make ci`.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TOOLS="$ROOT/backend/tools"
WORK="${FOOD_DATA_WORKDIR:-$(mktemp -d)}"
FDC_BASE="https://bulkfood.nal.usda.gov/fdc-bulk"

do_usda() {
  echo "==> Downloading USDA FDC Foundation+SR Legacy foods..."
  curl -fL "$FDC_BASE/food-data-centr-survey-files/download" -o "$WORK/fdc.zip" # placeholder URL; check current FDC bulk endpoint
  mkdir -p "$WORK/usda" && unzip -o "$WORK/fdc.zip" -d "$WORK/usda"
  python3 "$TOOLS/UsdaFoodGenerator/generate.py" --input "$WORK/usda" --output "$ROOT/backend/src/GutAI.Infrastructure/Data/WholeFoodsDatabase.cs"
}

do_branded() {
  echo "==> Downloading USDA FDC Branded Foods..."
  curl -fL "$FDC_BASE/branded-foods/download" -o "$WORK/branded.zip" # placeholder URL; check current FDC bulk endpoint
  mkdir -p "$WORK/branded" && unzip -o "$WORK/branded.zip" -d "$WORK/branded"
  python3 "$TOOLS/UsdaBrandedFoodGenerator/generate.py" --input "$WORK/branded" --output "$ROOT/backend/src/GutAI.Infrastructure/Data/BrandedFoodsDatabase.cs"
}

do_ausnut() {
  local xlsx="$1"
  echo "==> Regenerating AUSNUT catalog from $xlsx ..."
  python3 "$TOOLS/AusnutFoodGenerator/generate.py" --input "$xlsx" \
    --output "$ROOT/backend/src/GutAI.Infrastructure/Data/AustralianFoodsDatabase.cs"
}

usda=false; branded=false; ausnut_file=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --usda) usda=true ;;
    --branded) branded=true ;;
    --ausnut) ausnut_file="$2"; shift ;;
    --all) usda=true; branded=true ;;
    *) echo "unknown flag $1"; exit 2 ;;
  esac
  shift
done

$usda && do_usda
$branded && do_branded
[[ -n "$ausnut_file" ]] && do_ausnut "$ausnut_file"

if ! $usda && ! $branded && [[ -z "$ausnut_file" ]]; then
  grep -v '^#' "$0" | head -0; sed -n '2,25p' "$0"; exit 2
fi

echo "==> Done. Review diffs, then run: make ci"
