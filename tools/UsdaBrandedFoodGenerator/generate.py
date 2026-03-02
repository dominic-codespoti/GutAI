#!/usr/bin/env python3
"""
Generate BrandedFoodsDatabase.cs from USDA FoodData Central Branded Foods CSV bulk download.

Data source:
  - Branded Foods (2025-12-18): ~400k+ branded/packaged food products

Download:
  1. Go to https://fdc.nal.usda.gov/download-datasets
  2. Under "Latest Downloads", find "Branded" row, download the CSV (427MB zip)
  3. Extract to: tools/UsdaBrandedFoodGenerator/data/branded/FoodData_Central_branded_food_csv_2025-12-18/
  4. Run: python generate.py

The archive should contain food.csv, food_nutrient.csv, branded_food.csv, etc.

Filtering strategy (the raw dataset has ~400k items, we want ~2000-5000):
  - Require complete macros (energy, protein, fat, carbs) + non-empty ingredients
  - Filter to known popular brand owners (configurable list)
  - Deduplicate by normalized name
  - Cap per brand to avoid domination by a single brand

Nutrient IDs (same as whole foods generator):
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
DATA_DIR = os.path.join(SCRIPT_DIR, "data", "branded",
                        "FoodData_Central_branded_food_csv_2025-12-18")

OUTPUT_PATH = os.path.join(SCRIPT_DIR, "..", "..", "backend", "src",
                           "GutAI.Infrastructure", "Data", "BrandedFoodsDatabase.cs")

# Nutrient IDs
NID_ENERGY = "1008"
NID_PROTEIN = "1003"
NID_FAT = "1004"
NID_CARBS = "1005"
NID_FIBER = "1079"
NID_SUGAR = "2000"
NID_SODIUM = "1093"

REQUIRED_NUTRIENTS = {NID_ENERGY, NID_PROTEIN, NID_FAT, NID_CARBS}

# Max items per brand to prevent one brand dominating the database
MAX_PER_BRAND = 50

# Max total items in the output database
MAX_TOTAL = 5000

# Popular brand owners to include. Case-insensitive partial match against brand_owner field.
# This list covers major global + Australian brands.
POPULAR_BRANDS = [
    # Global mega-brands
    "Coca-Cola", "PepsiCo", "Nestle", "Unilever", "Kraft Heinz", "General Mills",
    "Kellogg", "Mars", "Mondelez", "Danone", "Campbell", "Conagra",
    "Post Holdings", "Hormel", "Tyson", "Smithfield", "Perdue",
    "Ocean Spray", "Del Monte", "Dole", "Chobani", "Fage",
    "Barilla", "Newman's Own", "Annie's", "Amy's Kitchen", "KIND",
    "Clif Bar", "RXBAR", "Nature Valley", "Quaker",
    "Stonyfield", "Yoplait", "Dannon", "Siggi's",
    "Sargento", "Tillamook", "Cabot", "Philadelphia",
    "Hershey", "Ghirardelli", "Lindt",
    "Ben & Jerry", "Haagen-Dazs",
    "Trader Joe", "Whole Foods", "365",
    "Great Value", "Kirkland", "Member's Mark",
    "Simple Truth", "O Organics", "Open Nature",
    "Bob's Red Mill", "King Arthur",
    "Mission", "Old El Paso", "Taco Bell",
    "Classico", "Rao's", "Prego", "Ragu",
    "Starbucks", "Dunkin", "Green Mountain",
    "Gatorade", "Powerade", "Body Armor",
    "Red Bull", "Monster Energy",
    "Oatly", "Silk", "Almond Breeze", "So Delicious",
    "Beyond Meat", "Impossible Foods", "Gardein", "MorningStar",
    "Birdseye", "Green Giant", "Stouffer",
    "Lean Cuisine", "Healthy Choice", "Smart Ones",
    "Oscar Mayer", "Boar's Head", "Hillshire",
    "Bumble Bee", "Starkist", "Chicken of the Sea",
    "Lays", "Doritos", "Cheetos", "Pringles", "Kettle",
    "Oreo", "Nabisco", "Pepperidge Farm",
    "Nature's Path", "Cascadian Farm", "Kashi", "Bear Naked",
    "Larabar", "GoMacro", "Nugo", "ThinkThin",
    "Justin's", "Jif", "Skippy", "Smucker",
    "Hellmann", "Best Foods", "French's", "Gulden",
    "Hidden Valley", "Bolthouse", "Primal Kitchen",
    "Siete", "Banza",
    # Australian brands
    "Sanitarium", "Weet-Bix", "Up & Go",
    "Woolworths", "Coles", "Aldi",
    "Arnott's", "Tim Tam",
    "Maggi", "Continental",
    "Bega", "Dairy Farmers", "Devondale",
    "Streets", "Peters",
    "SPC", "Golden Circle",
    "Vegemite", "MasterFoods",
    "Vittoria", "Moccona",
    "Bakers Delight", "Tip Top",
    "Don", "Hans",
    "Four'N Twenty", "Herbert Adams",
    "McCain", "Birds Eye",
    "CSR", "Bundaberg",
    "Sirena", "John West",
    "Uncle Tobys", "Carmans",
    "YoProSs", "Chobani",
    "Macro", "Simply Organic",
]

# Patterns to exclude from descriptions
EXCLUDE_PATTERNS = [
    "baby food", "infant formula", "baby cereal",
    "supplement", "protein isolate", "protein concentrate",
    "dog food", "cat food", "pet food",
    "medicinal", "pharmaceutical",
]


def load_branded_foods(data_dir):
    """Load branded_food.csv into {fdc_id: {brand_owner, brand_name, ingredients, ...}}."""
    branded = {}
    path = os.path.join(data_dir, "branded_food.csv")
    print(f"  Loading branded_food.csv...")
    with open(path, newline="", encoding="utf-8") as f:
        reader = csv.DictReader(f)
        for row in reader:
            fdc_id = row["fdc_id"]
            branded[fdc_id] = {
                "brand_owner": (row.get("brand_owner") or "").strip(),
                "brand_name": (row.get("brand_name") or "").strip(),
                "ingredients": (row.get("ingredients") or "").strip(),
                "serving_size": (row.get("serving_size") or "").strip(),
                "serving_size_unit": (row.get("serving_size_unit") or "").strip(),
                "branded_food_category": (row.get("branded_food_category") or "").strip(),
            }
    print(f"  Loaded {len(branded)} branded food entries")
    return branded


def load_food_nutrients(data_dir):
    """Load food_nutrient.csv into {fdc_id: {nutrient_id: amount}}."""
    nutrients = defaultdict(dict)
    path = os.path.join(data_dir, "food_nutrient.csv")
    print(f"  Loading food_nutrient.csv...")
    with open(path, newline="", encoding="utf-8") as f:
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
    print(f"  Loaded nutrients for {len(nutrients)} foods")
    return dict(nutrients)


def load_foods(data_dir):
    """Load food.csv for branded_food entries."""
    foods = []
    path = os.path.join(data_dir, "food.csv")
    print(f"  Loading food.csv...")
    with open(path, newline="", encoding="utf-8") as f:
        reader = csv.DictReader(f)
        for row in reader:
            if row["data_type"] == "branded_food":
                foods.append(row)
    print(f"  Found {len(foods)} branded food entries")
    return foods


def brand_matches(brand_owner):
    """Check if brand_owner matches any of the popular brands."""
    owner_lower = brand_owner.lower()
    return any(b.lower() in owner_lower for b in POPULAR_BRANDS)


def should_exclude(description):
    """Check if food should be excluded based on description."""
    desc_lower = description.lower()
    return any(p in desc_lower for p in EXCLUDE_PATTERNS)


def clean_name(description, brand_name, brand_owner):
    """Clean up the product name. USDA branded names are often already good."""
    name = description.strip()
    # Don't title-case branded foods — they often have proper casing already
    if name == name.upper():
        # All caps — apply basic title casing
        name = name.title()
    return name


def clean_ingredients(raw):
    """Clean ingredients text — trim, normalize whitespace."""
    if not raw:
        return ""
    # Remove excessive whitespace
    cleaned = " ".join(raw.split())
    # Cap length for the C# literal (some ingredients are absurdly long)
    if len(cleaned) > 500:
        # Truncate at last comma before 500 chars
        idx = cleaned.rfind(",", 0, 500)
        if idx > 200:
            cleaned = cleaned[:idx] + "..."
        else:
            cleaned = cleaned[:497] + "..."
    return cleaned


def escape_cs_string(s):
    """Escape a string for use in a C# verbatim string or regular string."""
    return s.replace("\\", "\\\\").replace('"', '\\"')


def format_decimal(val):
    """Format a float as a C# decimal literal."""
    if val == 0:
        return "0m"
    s = f"{val:.4f}".rstrip("0").rstrip(".")
    if "." not in s:
        s += ".0"
    return s + "m"


def process_branded(data_dir):
    """Process USDA branded foods dataset."""
    foods = load_foods(data_dir)
    branded = load_branded_foods(data_dir)
    nutrients = load_food_nutrients(data_dir)

    results = []
    stats = defaultdict(int)

    for food in foods:
        fdc_id = food["fdc_id"]
        desc = food["description"]

        # Get branded metadata
        bdata = branded.get(fdc_id)
        if not bdata:
            stats["no_branded_data"] += 1
            continue

        brand_owner = bdata["brand_owner"]
        brand_name = bdata["brand_name"]
        ingredients = bdata["ingredients"]

        # Filter to popular brands
        if not brand_matches(brand_owner):
            stats["unpopular_brand"] += 1
            continue

        # Require ingredients
        if not ingredients or len(ingredients) < 5:
            stats["no_ingredients"] += 1
            continue

        # Exclude unwanted categories
        if should_exclude(desc):
            stats["excluded_pattern"] += 1
            continue

        # Check nutrition completeness
        food_nuts = nutrients.get(fdc_id, {})
        if not REQUIRED_NUTRIENTS.issubset(food_nuts.keys()):
            stats["incomplete_nutrition"] += 1
            continue

        energy = food_nuts[NID_ENERGY]
        protein = food_nuts[NID_PROTEIN]
        fat = food_nuts[NID_FAT]
        carbs = food_nuts[NID_CARBS]
        fiber = food_nuts.get(NID_FIBER, 0.0)
        sugar = food_nuts.get(NID_SUGAR, 0.0)
        sodium_mg = food_nuts.get(NID_SODIUM, 0.0)
        sodium_g = sodium_mg / 1000.0

        # Sanity check: macro calories should be roughly close to reported energy
        if energy > 0:
            macro_kcal = (protein * 4) + (carbs * 4) + (fat * 9)
            ratio = macro_kcal / energy
            if ratio < 0.5 or ratio > 1.5:
                stats["bad_macro_ratio"] += 1
                continue

        name = clean_name(desc, brand_name, brand_owner)
        brand_display = brand_name or brand_owner

        results.append({
            "name": name,
            "fdc_id": int(fdc_id),
            "brand": brand_display,
            "brand_owner": brand_owner,
            "ingredients": clean_ingredients(ingredients),
            "calories": round(energy),
            "protein": round(protein, 2),
            "carbs": round(carbs, 2),
            "fat": round(fat, 2),
            "fiber": round(fiber, 2),
            "sugar": round(sugar, 2),
            "sodium_g": round(sodium_g, 4),
            "category": bdata.get("branded_food_category", ""),
        })

    print(f"\n  Filtering stats:")
    for k, v in sorted(stats.items()):
        print(f"    {k}: {v}")
    print(f"  Passed all filters: {len(results)}")

    return results


def cap_per_brand(foods, max_per_brand, max_total):
    """Cap items per brand and total count, preferring items with shorter names
    (usually the 'main' product, not variants)."""
    # Sort by name length (shorter = more likely the base product)
    foods.sort(key=lambda f: len(f["name"]))

    brand_counts = defaultdict(int)
    capped = []
    for f in foods:
        brand_key = f["brand_owner"].lower()
        if brand_counts[brand_key] >= max_per_brand:
            continue
        brand_counts[brand_key] += 1
        capped.append(f)
        if len(capped) >= max_total:
            break

    return capped


def deduplicate(foods):
    """Deduplicate by normalized name."""
    seen = set()
    deduped = []
    for f in foods:
        key = f["name"].lower().strip()
        if key not in seen:
            seen.add(key)
            deduped.append(f)
    return deduped


def generate_cs(foods):
    """Generate the BrandedFoodsDatabase.cs file content."""
    foods_sorted = sorted(foods, key=lambda f: f["name"].lower())

    lines = []
    lines.append("// <auto-generated>")
    lines.append(f"// Generated from USDA FoodData Central Branded Foods CSV on {datetime.now().strftime('%Y-%m-%d')}.")
    lines.append(f"// Source: Branded Foods (2025-12-18)")
    lines.append(f"// Total foods: {len(foods_sorted)}")
    lines.append("//")
    lines.append("// Nutrient values are per 100g. Sodium is in GRAMS (not mg) to match OpenFoodFacts convention.")
    lines.append("// Do not edit manually — re-run tools/UsdaBrandedFoodGenerator/generate.py to regenerate.")
    lines.append("// </auto-generated>")
    lines.append("")
    lines.append("using GutAI.Application.Common.DTOs;")
    lines.append("using GutAI.Domain.Enums;")
    lines.append("")
    lines.append("namespace GutAI.Infrastructure.Data;")
    lines.append("")
    lines.append("public static class BrandedFoodsDatabase")
    lines.append("{")
    lines.append("    private static readonly Lazy<FoodSearchIndex> _index = new(() =>")
    lines.append("    {")
    lines.append("        var index = new FoodSearchIndex();")
    lines.append("        index.AddRange(Foods);")
    lines.append("        return index;")
    lines.append("    });")
    lines.append("")
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
            name_esc = escape_cs_string(f["name"])
            brand_esc = escape_cs_string(f["brand"])
            ingr_esc = escape_cs_string(f["ingredients"])
            cal = f["calories"]
            pro = format_decimal(f["protein"])
            carb = format_decimal(f["carbs"])
            fat = format_decimal(f["fat"])
            fib = format_decimal(f["fiber"])
            sug = format_decimal(f["sugar"])
            sod = format_decimal(f["sodium_g"])
            lines.append(f'        F("{name_esc}", "{brand_esc}", "{ingr_esc}", {cal}, {pro}, {carb}, {fat}, {fib}, {sug}, {sod}), // FDC:{f["fdc_id"]}')
        lines.append("")

    lines.append("    ];")
    lines.append("")
    lines.append("    public static int Count => Foods.Count;")
    lines.append("")
    lines.append('    public static List<FoodProductDto> Search(string query, int maxResults = 10)')
    lines.append("    {")
    lines.append("        if (string.IsNullOrWhiteSpace(query)) return [];")
    lines.append("        return _index.Value.Search(query, maxResults);")
    lines.append("    }")
    lines.append("")
    lines.append('    private static FoodProductDto F(string name, string brand, string ingredients, int cal, decimal protein, decimal carbs, decimal fat, decimal fiber, decimal sugar, decimal sodium) =>')
    lines.append("        new()")
    lines.append("        {")
    lines.append("            Name = name,")
    lines.append("            Brand = brand,")
    lines.append("            Ingredients = ingredients,")
    lines.append("            Calories100g = cal,")
    lines.append("            Protein100g = protein,")
    lines.append("            Carbs100g = carbs,")
    lines.append("            Fat100g = fat,")
    lines.append("            Fiber100g = fiber,")
    lines.append("            Sugar100g = sugar,")
    lines.append("            Sodium100g = sodium,")
    lines.append('            DataSource = "USDA",')
    lines.append("            FoodKind = FoodKind.Branded,")
    lines.append("        };")
    lines.append("}")
    lines.append("")

    return "\n".join(lines)


def main():
    print("USDA BrandedFoodsDatabase Generator")
    print("=" * 50)

    if not os.path.isdir(DATA_DIR):
        print(f"ERROR: Branded data directory not found: {DATA_DIR}")
        print()
        print("To download the data:")
        print("  1. Go to https://fdc.nal.usda.gov/download-datasets")
        print("  2. Download 'Branded' CSV (December 2025)")
        print(f"  3. Extract to: {DATA_DIR}")
        print("  4. Re-run this script")
        sys.exit(1)

    # Check required files
    required_files = ["food.csv", "food_nutrient.csv", "branded_food.csv"]
    for fname in required_files:
        fpath = os.path.join(DATA_DIR, fname)
        if not os.path.isfile(fpath):
            print(f"ERROR: Required file not found: {fpath}")
            sys.exit(1)

    # Process
    print("\nProcessing Branded Foods...")
    results = process_branded(DATA_DIR)

    # Deduplicate
    print(f"\nDeduplicating...")
    deduped = deduplicate(results)
    print(f"  {len(results)} -> {len(deduped)} after dedup")

    # Cap per brand and total
    print(f"\nCapping to {MAX_PER_BRAND}/brand, {MAX_TOTAL} total...")
    capped = cap_per_brand(deduped, MAX_PER_BRAND, MAX_TOTAL)
    print(f"  {len(deduped)} -> {len(capped)} after capping")

    # Brand summary
    brand_counts = defaultdict(int)
    for f in capped:
        brand_counts[f["brand_owner"]] += 1
    print(f"\nTop brands:")
    for brand, count in sorted(brand_counts.items(), key=lambda x: -x[1])[:20]:
        print(f"  {brand}: {count}")

    # Category summary
    by_cat = defaultdict(int)
    for f in capped:
        by_cat[f["category"] or "Uncategorized"] += 1
    print(f"\nCategory breakdown ({len(by_cat)} categories):")
    for cat in sorted(by_cat.keys()):
        print(f"  {cat}: {by_cat[cat]}")

    # Generate C#
    print(f"\nGenerating C# file...")
    cs_content = generate_cs(capped)

    os.makedirs(os.path.dirname(OUTPUT_PATH), exist_ok=True)
    with open(OUTPUT_PATH, "w", encoding="utf-8") as f:
        f.write(cs_content)

    file_size = os.path.getsize(OUTPUT_PATH)
    lines_count = cs_content.count("\n")
    print(f"  Written to: {OUTPUT_PATH}")
    print(f"  File size: {file_size:,} bytes")
    print(f"  Lines: {lines_count:,}")
    print(f"  Total foods: {len(capped)}")
    print("\nDone!")


if __name__ == "__main__":
    main()
