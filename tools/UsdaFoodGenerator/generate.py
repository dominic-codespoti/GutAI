#!/usr/bin/env python3
"""
Generate WholeFoodsDatabase.cs from USDA FoodData Central CSV bulk downloads.

Data sources:
  - Foundation Foods (2025-12-18): ~436 whole foods with high-quality analytical data
  - SR Legacy (2018-04): ~7,793 foods from the legacy Standard Reference database

Nutrient IDs used (consistent across both datasets):
  1008 = Energy (KCAL)
  1003 = Protein (G)
  1004 = Total lipid / fat (G)
  1005 = Carbohydrate, by difference (G)
  1079 = Fiber, total dietary (G)
  2000 = Sugars, Total (G)
  1093 = Sodium, Na (MG) — converted to G in output to match OpenFoodFacts convention

Output: Sodium100g is in GRAMS (matching the rest of the codebase / OpenFoodFacts convention).
"""

import csv
import os
import sys
from collections import defaultdict
from datetime import datetime

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
DATA_DIR = os.path.join(SCRIPT_DIR, "data")

FOUNDATION_DIR = os.path.join(DATA_DIR, "foundation", "FoodData_Central_foundation_food_csv_2025-12-18")
SR_LEGACY_DIR = os.path.join(DATA_DIR, "sr_legacy", "FoodData_Central_sr_legacy_food_csv_2018-04")

OUTPUT_PATH = os.path.join(SCRIPT_DIR, "..", "..", "backend", "src", "GutAI.Infrastructure", "Data", "WholeFoodsDatabase.cs")

# Nutrient IDs
NID_ENERGY = "1008"
NID_PROTEIN = "1003"
NID_FAT = "1004"
NID_CARBS = "1005"
NID_FIBER = "1079"
NID_SUGAR = "2000"
NID_SODIUM = "1093"

REQUIRED_NUTRIENTS = {NID_ENERGY, NID_PROTEIN, NID_FAT, NID_CARBS}

# Categories to exclude (baby foods, supplements, etc.)
EXCLUDED_CATEGORY_IDS = {
    "3",   # Baby Foods
    "25",  # Restaurant Foods (SR Legacy)
}

# Foods to exclude by description pattern (heavily processed, obscure, or non-food)
EXCLUDE_PATTERNS = [
    "formulated bar",
    "infant formula",
    "baby food",
    "meal replacement",
    "supplement",
    "protein isolate",
    "protein concentrate",
    "whey protein",
    "leavening agents",
]


def title_case_food(name: str) -> str:
    """Convert USDA ALL CAPS names to readable title case, preserving some conventions."""
    if name != name.upper():
        return name  # already mixed case

    # Special handling for comma-separated USDA names like "CHICKEN, BREAST, RAW"
    parts = name.split(", ")
    result = []
    for part in parts:
        words = part.strip().split()
        titled = []
        for w in words:
            lower = w.lower()
            # Keep short prepositions/articles lowercase (unless first word)
            if lower in ("and", "or", "of", "with", "without", "in", "for", "the", "a", "an", "ns", "nfs") and titled:
                titled.append(lower)
            elif lower.startswith("(") and len(lower) > 1:
                titled.append("(" + lower[1:].capitalize())
            else:
                titled.append(lower.capitalize())
        result.append(" ".join(titled))

    return ", ".join(result)


def load_food_nutrients(data_dir: str) -> dict:
    """Load food_nutrient.csv into {fdc_id: {nutrient_id: amount}}."""
    nutrients = defaultdict(dict)
    path = os.path.join(data_dir, "food_nutrient.csv")
    with open(path, newline="") as f:
        reader = csv.DictReader(f)
        for row in reader:
            nid = row["nutrient_id"]
            if nid in (NID_ENERGY, NID_PROTEIN, NID_FAT, NID_CARBS, NID_FIBER, NID_SUGAR, NID_SODIUM):
                fdc_id = row["fdc_id"]
                try:
                    amount = float(row["amount"]) if row["amount"] else None
                except ValueError:
                    amount = None
                if amount is not None:
                    nutrients[fdc_id][nid] = amount
    return dict(nutrients)


def load_categories(data_dir: str) -> dict:
    """Load food_category.csv into {id: description}."""
    cats = {}
    path = os.path.join(data_dir, "food_category.csv")
    with open(path, newline="") as f:
        reader = csv.DictReader(f)
        for row in reader:
            cats[row["id"]] = row["description"]
    return cats


def load_foods(data_dir: str, data_type_filter: str) -> list:
    """Load food.csv filtered by data_type."""
    foods = []
    path = os.path.join(data_dir, "food.csv")
    with open(path, newline="") as f:
        reader = csv.DictReader(f)
        for row in reader:
            if row["data_type"] == data_type_filter:
                foods.append(row)
    return foods


def should_exclude(description: str) -> bool:
    """Check if food should be excluded based on description patterns."""
    desc_lower = description.lower()
    return any(p in desc_lower for p in EXCLUDE_PATTERNS)


def process_dataset(data_dir: str, data_type_filter: str, source_label: str) -> list:
    """Process a USDA dataset and return food entries."""
    print(f"  Loading {source_label} foods...")
    foods = load_foods(data_dir, data_type_filter)
    print(f"  Found {len(foods)} {data_type_filter} entries")

    print(f"  Loading {source_label} nutrients...")
    nutrients = load_food_nutrients(data_dir)
    print(f"  Loaded nutrients for {len(nutrients)} foods")

    categories = load_categories(data_dir)

    results = []
    skipped_incomplete = 0
    skipped_excluded = 0
    skipped_category = 0

    for food in foods:
        fdc_id = food["fdc_id"]
        desc = food["description"]
        cat_id = food.get("food_category_id", "")

        if cat_id in EXCLUDED_CATEGORY_IDS:
            skipped_category += 1
            continue

        if should_exclude(desc):
            skipped_excluded += 1
            continue

        food_nuts = nutrients.get(fdc_id, {})

        # Check required nutrients
        if not REQUIRED_NUTRIENTS.issubset(food_nuts.keys()):
            skipped_incomplete += 1
            continue

        energy = food_nuts[NID_ENERGY]
        protein = food_nuts[NID_PROTEIN]
        fat = food_nuts[NID_FAT]
        carbs = food_nuts[NID_CARBS]
        fiber = food_nuts.get(NID_FIBER, 0.0)
        sugar = food_nuts.get(NID_SUGAR, 0.0)
        sodium_mg = food_nuts.get(NID_SODIUM, 0.0)

        # Convert sodium from mg to g (matching OpenFoodFacts / codebase convention)
        sodium_g = sodium_mg / 1000.0

        # Sanity check: macro calories should be roughly close to reported energy
        macro_kcal = (protein * 4) + (carbs * 4) + (fat * 9)
        if energy > 0:
            ratio = macro_kcal / energy if energy != 0 else 0
            # Skip if wildly off (>50% difference) - usually indicates data issues
            if ratio < 0.5 or ratio > 1.5:
                skipped_excluded += 1
                continue

        name = title_case_food(desc)
        category = categories.get(cat_id, "")

        results.append({
            "name": name,
            "fdc_id": int(fdc_id),
            "calories": round(energy),
            "protein": round(protein, 2),
            "carbs": round(carbs, 2),
            "fat": round(fat, 2),
            "fiber": round(fiber, 2),
            "sugar": round(sugar, 2),
            "sodium_g": round(sodium_g, 4),
            "category": category,
            "source": source_label,
        })

    print(f"  Results: {len(results)} foods (skipped: {skipped_incomplete} incomplete, {skipped_excluded} excluded, {skipped_category} wrong category)")
    return results


def deduplicate(foods: list) -> list:
    """Deduplicate by normalized name, preferring Foundation over SR Legacy."""
    seen = {}
    for f in foods:
        key = f["name"].lower().strip()
        if key not in seen:
            seen[key] = f
        elif f["source"] == "Foundation" and seen[key]["source"] != "Foundation":
            seen[key] = f  # prefer Foundation
    return list(seen.values())


def format_decimal(val: float) -> str:
    """Format a float as a C# decimal literal."""
    if val == 0:
        return "0m"
    # Remove trailing zeros but keep at least one decimal
    s = f"{val:.4f}".rstrip("0").rstrip(".")
    if "." not in s:
        s += ".0"
    return s + "m"


def generate_cs(foods: list) -> str:
    """Generate the WholeFoodsDatabase.cs file content."""
    foods_sorted = sorted(foods, key=lambda f: f["name"].lower())

    lines = []
    lines.append("// <auto-generated>")
    lines.append(f"// Generated from USDA FoodData Central bulk CSV data on {datetime.now().strftime('%Y-%m-%d')}.")
    lines.append(f"// Sources: Foundation Foods (2025-12-18), SR Legacy (2018-04)")
    lines.append(f"// Total foods: {len(foods_sorted)}")
    lines.append("//")
    lines.append("// Nutrient values are per 100g. Sodium is in GRAMS (not mg) to match OpenFoodFacts convention.")
    lines.append("// Do not edit manually — re-run tools/UsdaFoodGenerator/generate.py to regenerate.")
    lines.append("// </auto-generated>")
    lines.append("")
    lines.append("using GutAI.Application.Common.DTOs;")
    lines.append("")
    lines.append("namespace GutAI.Infrastructure.Data;")
    lines.append("")
    lines.append("public static class WholeFoodsDatabase")
    lines.append("{")
    lines.append("    private static readonly List<FoodProductDto> Foods =")
    lines.append("    [")

    # Group by category for readability
    by_category = defaultdict(list)
    for f in foods_sorted:
        by_category[f["category"] or "Uncategorized"].append(f)

    for category in sorted(by_category.keys()):
        cat_foods = by_category[category]
        lines.append(f"        // {category} ({len(cat_foods)})")
        for f in sorted(cat_foods, key=lambda x: x["name"].lower()):
            name_escaped = f["name"].replace('"', '\\"')
            cal = f["calories"]
            pro = format_decimal(f["protein"])
            carb = format_decimal(f["carbs"])
            fat = format_decimal(f["fat"])
            fib = format_decimal(f["fiber"])
            sug = format_decimal(f["sugar"])
            sod = format_decimal(f["sodium_g"])
            lines.append(f'        F("{name_escaped}", {cal}, {pro}, {carb}, {fat}, {fib}, {sug}, {sod}), // FDC:{f["fdc_id"]}')
        lines.append("")

    lines.append("    ];")
    lines.append("")
    lines.append('    public static List<FoodProductDto> Search(string query, int maxResults = 10)')
    lines.append("    {")
    lines.append("        if (string.IsNullOrWhiteSpace(query)) return [];")
    lines.append("")
    lines.append("        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);")
    lines.append("")
    lines.append("        return Foods")
    lines.append("            .Select(f => (food: f, score: MatchScore(f.Name, terms, query)))")
    lines.append("            .Where(x => x.score > 0)")
    lines.append("            .OrderByDescending(x => x.score)")
    lines.append("            .Take(maxResults)")
    lines.append("            .Select(x => x.food)")
    lines.append("            .ToList();")
    lines.append("    }")
    lines.append("")
    lines.append("    private static int MatchScore(string name, string[] terms, string fullQuery)")
    lines.append("    {")
    lines.append("        int score = 0;")
    lines.append("        var lower = name.ToLowerInvariant();")
    lines.append("        var queryLower = fullQuery.ToLowerInvariant();")
    lines.append("")
    lines.append("        if (lower.StartsWith(queryLower)) score += 100;")
    lines.append("        else if (lower.Contains(queryLower)) score += 50;")
    lines.append("")
    lines.append("        foreach (var term in terms)")
    lines.append("        {")
    lines.append("            if (lower.Contains(term, StringComparison.OrdinalIgnoreCase))")
    lines.append("                score += 20;")
    lines.append("        }")
    lines.append("")
    lines.append("        return score;")
    lines.append("    }")
    lines.append("")
    lines.append('    private static FoodProductDto F(string name, int cal, decimal protein, decimal carbs, decimal fat, decimal fiber, decimal sugar, decimal sodium) =>')
    lines.append("        new()")
    lines.append("        {")
    lines.append("            Name = name,")
    lines.append("            Calories100g = cal,")
    lines.append("            Protein100g = protein,")
    lines.append("            Carbs100g = carbs,")
    lines.append("            Fat100g = fat,")
    lines.append("            Fiber100g = fiber,")
    lines.append("            Sugar100g = sugar,")
    lines.append("            Sodium100g = sodium,")
    lines.append('            DataSource = "USDA",')
    lines.append("        };")
    lines.append("}")
    lines.append("")

    return "\n".join(lines)


def main():
    print("USDA WholeFoodsDatabase Generator")
    print("=" * 50)

    # Check data dirs exist
    for d, label in [(FOUNDATION_DIR, "Foundation"), (SR_LEGACY_DIR, "SR Legacy")]:
        if not os.path.isdir(d):
            print(f"ERROR: {label} data directory not found: {d}")
            sys.exit(1)

    # Process Foundation foods (highest quality, prefer these)
    print("\nProcessing Foundation Foods...")
    foundation = process_dataset(FOUNDATION_DIR, "foundation_food", "Foundation")

    # Process SR Legacy foods (broader coverage)
    print("\nProcessing SR Legacy Foods...")
    sr_legacy = process_dataset(SR_LEGACY_DIR, "sr_legacy_food", "SR Legacy")

    # Merge and deduplicate (Foundation takes priority)
    print("\nMerging and deduplicating...")
    all_foods = foundation + sr_legacy
    deduped = deduplicate(all_foods)
    print(f"  {len(all_foods)} total -> {len(deduped)} after dedup")

    # Category summary
    by_cat = defaultdict(int)
    for f in deduped:
        by_cat[f["category"] or "Uncategorized"] += 1
    print("\nCategory breakdown:")
    for cat in sorted(by_cat.keys()):
        print(f"  {cat}: {by_cat[cat]}")

    # Generate C#
    print(f"\nGenerating C# file...")
    cs_content = generate_cs(deduped)

    os.makedirs(os.path.dirname(OUTPUT_PATH), exist_ok=True)
    with open(OUTPUT_PATH, "w", encoding="utf-8") as f:
        f.write(cs_content)

    file_size = os.path.getsize(OUTPUT_PATH)
    print(f"  Written to: {OUTPUT_PATH}")
    print(f"  File size: {file_size:,} bytes")
    print(f"  Total foods: {len(deduped)}")
    print("\nDone!")


if __name__ == "__main__":
    main()
