#!/usr/bin/env python3
"""Automated food scoring analysis - tests 50 foods through the GutAI API."""
import json, sys, time, requests

BASE = "http://localhost:5000"

# Register/login
r = requests.post(f"{BASE}/api/auth/register", json={
    "email": f"foodtest{int(time.time())}@test.com",
    "password": "Test123!",
    "displayName": "Food Tester"
})
if r.status_code not in (200, 201):
    r = requests.post(f"{BASE}/api/auth/login", json={
        "email": "foodtest@test.com", "password": "Test123!"
    })
token = r.json()["accessToken"]
headers = {"Authorization": f"Bearer {token}", "Content-Type": "application/json"}

# 50 foods to test - mix of whole foods and store-bought
FOODS = [
    # WHOLE FOODS - Fruits
    "banana",
    "apple",
    "avocado",
    "blueberries",
    "strawberries",
    "mango",
    "watermelon",
    "orange",
    "grapes",
    "cherries",
    # WHOLE FOODS - Vegetables
    "broccoli",
    "spinach",
    "sweet potato",
    "garlic",
    "onion",
    "mushroom",
    "cauliflower",
    "kale",
    "carrot",
    "celery",
    # WHOLE FOODS - Proteins
    "chicken breast",
    "salmon",
    "eggs",
    "ground beef",
    "tofu",
    # WHOLE FOODS - Grains/Legumes
    "white rice",
    "oatmeal",
    "quinoa",
    "lentils",
    "chickpeas",
    # STORE-BOUGHT - Dairy
    "greek yogurt",
    "cheddar cheese",
    "whole milk",
    "cottage cheese",
    # STORE-BOUGHT - Processed
    "bread",
    "pasta",
    "peanut butter",
    "honey",
    "olive oil",
    # STORE-BOUGHT - Packaged
    "protein bar",
    "granola",
    "almond milk",
    "kombucha",
    "hummus",
    # STORE-BOUGHT - Snacks/Other
    "dark chocolate",
    "rice cakes",
    "corn tortilla",
    "salsa",
    "kimchi",
    "sauerkraut",
]

results = []

for i, food_name in enumerate(FOODS):
    print(f"\n[{i+1}/50] Searching: {food_name}...", flush=True)

    # Search for the food
    try:
        r = requests.get(f"{BASE}/api/food/search", params={"q": food_name}, headers=headers, timeout=30)
        if r.status_code != 200:
            print(f"  ❌ Search failed: {r.status_code}")
            results.append({"food": food_name, "error": f"Search failed: {r.status_code}"})
            continue

        search_results = r.json()
        if not search_results:
            print(f"  ❌ No results found")
            results.append({"food": food_name, "error": "No results"})
            continue

        # Pick the best matching result
        product = search_results[0]
        food_id = product["id"]
        actual_name = product["name"]
        brand = product.get("brand", "")
        print(f"  Found: {actual_name} ({brand or 'no brand'})")
        print(f"  Ingredients: {(product.get('ingredientsText') or product.get('ingredients') or 'None')[:100]}...")

    except Exception as e:
        print(f"  ❌ Error: {e}")
        results.append({"food": food_name, "error": str(e)})
        continue

    entry = {
        "search_term": food_name,
        "actual_name": actual_name,
        "brand": brand,
        "id": food_id,
        "ingredients": product.get("ingredientsText") or product.get("ingredients") or "",
        "nutrition": {
            "calories": product.get("calories100g"),
            "protein": product.get("protein100g"),
            "carbs": product.get("carbs100g"),
            "fat": product.get("fat100g"),
            "fiber": product.get("fiber100g"),
            "sugar": product.get("sugar100g"),
            "sodium": product.get("sodium100g"),
        },
        "novaGroup": product.get("novaGroup"),
        "nutriScore": product.get("nutriScore"),
    }

    # Get safety report (includes gut risk, FODMAP, glycemic all at once)
    try:
        r = requests.get(f"{BASE}/api/food/{food_id}/safety-report", headers=headers, timeout=15)
        if r.status_code == 200:
            report = r.json()

            gut = report.get("gutRisk", {})
            entry["gutRisk"] = {
                "score": gut.get("gutScore"),
                "rating": gut.get("gutRating"),
                "flagCount": gut.get("flagCount"),
                "highRisk": gut.get("highRiskCount"),
                "medRisk": gut.get("mediumRiskCount"),
                "lowRisk": gut.get("lowRiskCount"),
                "confidence": gut.get("confidence"),
                "summary": gut.get("summary"),
                "flags": [{"name": f.get("name"), "category": f.get("category"),
                           "risk": f.get("riskLevel"), "fodmapClass": f.get("fodmapClass")}
                          for f in gut.get("flags", [])],
            }

            fodmap = report.get("fodmap", {})
            entry["fodmap"] = {
                "score": fodmap.get("fodmapScore"),
                "rating": fodmap.get("fodmapRating"),
                "triggerCount": fodmap.get("triggerCount"),
                "highCount": fodmap.get("highCount"),
                "modCount": fodmap.get("moderateCount"),
                "lowCount": fodmap.get("lowCount"),
                "categories": fodmap.get("categories"),
                "summary": fodmap.get("summary"),
                "triggers": [{"name": t.get("name"), "category": t.get("category"),
                              "severity": t.get("severity"), "sub": t.get("subCategory")}
                             for t in fodmap.get("triggers", [])],
            }

            glycemic = report.get("glycemic", {})
            entry["glycemic"] = {
                "gi": glycemic.get("estimatedGI"),
                "giCategory": glycemic.get("giCategory"),
                "gl": glycemic.get("estimatedGL"),
                "glCategory": glycemic.get("glCategory"),
                "matchCount": glycemic.get("matchCount"),
                "matches": [{"food": m.get("food"), "gi": m.get("gi"), "source": m.get("source")}
                            for m in glycemic.get("matches", [])],
                "summary": glycemic.get("gutImpactSummary"),
            }

            print(f"  Gut Score: {entry['gutRisk']['score']}/100 ({entry['gutRisk']['rating']})")
            print(f"  FODMAP Score: {entry['fodmap']['score']}/100 ({entry['fodmap']['rating']})")
            print(f"  GI: {entry['glycemic']['gi']} ({entry['glycemic']['giCategory']})")
            if entry['gutRisk']['flags']:
                print(f"  Gut Flags: {', '.join(f['name'] for f in entry['gutRisk']['flags'][:5])}")
            if entry['fodmap']['triggers']:
                print(f"  FODMAP Triggers: {', '.join(t['name'] for t in entry['fodmap']['triggers'][:5])}")
        else:
            print(f"  ⚠️ Safety report failed: {r.status_code}")
            entry["error"] = f"Safety report: {r.status_code}"
    except Exception as e:
        print(f"  ⚠️ Safety report error: {e}")
        entry["error"] = str(e)

    results.append(entry)
    time.sleep(0.3)

# Save results
with open("/home/dom/projects/gut-ai/scripts/food-analysis-results.json", "w") as f:
    json.dump(results, f, indent=2)

print(f"\n\n{'='*80}")
print(f"ANALYSIS COMPLETE - {len(results)} foods tested")
print(f"{'='*80}")

# Summary tables
print(f"\n{'Food':<30} {'Gut':>5} {'FODMAP':>7} {'GI':>4} {'GI Cat':>8} {'FODMAP Rating':<20} {'Gut Rating':<10}")
print("-" * 95)
for r in results:
    if "error" in r and "gutRisk" not in r:
        print(f"{r.get('search_term','?'):<30} {'ERR':>5} {'ERR':>7} {'ERR':>4} {'ERR':>8} {'ERR':<20} {'ERR':<10}")
        continue
    gut = r.get("gutRisk", {})
    fod = r.get("fodmap", {})
    gly = r.get("glycemic", {})
    print(f"{r.get('search_term','?'):<30} {gut.get('score','?'):>5} {fod.get('score','?'):>7} {str(gly.get('gi','?')):>4} {gly.get('giCategory','?'):>8} {fod.get('rating','?'):<20} {gut.get('rating','?'):<10}")

print(f"\n{'='*80}")
print("DETAILED ISSUES & ANALYSIS")
print(f"{'='*80}")

for r in results:
    if "error" in r and "gutRisk" not in r:
        continue
    issues = []
    gut = r.get("gutRisk", {})
    fod = r.get("fodmap", {})
    gly = r.get("glycemic", {})
    name = r.get("search_term", "?")
    actual = r.get("actual_name", "?")

    # Check for scoring issues
    if gut.get("score") == 100 and fod.get("score", 100) < 80:
        issues.append(f"MISMATCH: Gut score perfect (100) but FODMAP score is {fod['score']} ({fod['rating']})")

    if fod.get("score") == 100 and gut.get("score", 100) < 80:
        issues.append(f"MISMATCH: FODMAP score perfect (100) but Gut score is {gut['score']} ({gut['rating']})")

    if gly.get("gi") is None:
        issues.append("MISSING: No glycemic index data")

    if not r.get("ingredients"):
        issues.append("MISSING: No ingredients data — scoring may be incomplete")

    # Check for known accuracy issues against Monash data
    # Whole foods that should be low FODMAP
    low_fodmap_foods = {"banana", "blueberries", "strawberries", "orange", "grapes",
                        "broccoli", "spinach", "kale", "carrot", "chicken breast",
                        "salmon", "eggs", "ground beef", "tofu", "white rice",
                        "quinoa", "oatmeal", "cheddar cheese", "olive oil",
                        "dark chocolate", "rice cakes", "corn tortilla", "sauerkraut",
                        "almond milk", "peanut butter"}

    if name in low_fodmap_foods and fod.get("score", 100) < 80:
        issues.append(f"INACCURATE: {name} should be Low FODMAP per Monash, but scored {fod['score']} ({fod['rating']})")

    # Foods that should be high FODMAP
    high_fodmap_foods = {"garlic", "onion", "mushroom", "cauliflower", "watermelon",
                         "mango", "cherries", "apple", "lentils", "chickpeas",
                         "honey", "whole milk", "cottage cheese"}

    if name in high_fodmap_foods and fod.get("score", 100) >= 80:
        issues.append(f"INACCURATE: {name} should be High/Mod FODMAP per Monash, but scored {fod['score']} (Low FODMAP)")

    # Foods that should have low GI
    low_gi_foods = {"apple", "banana", "orange", "grapes", "cherries", "strawberries",
                    "blueberries", "lentils", "chickpeas", "quinoa", "oatmeal",
                    "sweet potato"}

    if name in low_gi_foods and gly.get("gi") is not None and gly["gi"] >= 70:
        issues.append(f"INACCURATE: {name} should be Low/Medium GI, but got GI={gly['gi']} (High)")

    # Foods with no gut risk that should have some
    if name in {"garlic", "onion"} and gut.get("score", 0) == 100:
        issues.append(f"MISSING: {name} should have gut risk flags for fructans")

    if issues:
        print(f"\n📍 {name} → {actual}")
        for issue in issues:
            print(f"   ⚠️  {issue}")

print(f"\n{'='*80}")
print("Done. Full results saved to scripts/food-analysis-results.json")
