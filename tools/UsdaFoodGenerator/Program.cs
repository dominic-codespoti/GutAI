using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UsdaFoodGenerator;

public static class Program
{
    private static readonly string[] PreferredDataTypes = ["Foundation", "SR Legacy"];
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public static async Task Main(string[] args)
    {
        var apiKey = args.Length > 0 ? args[0] : Environment.GetEnvironmentVariable("USDA_API_KEY") ?? "DEMO_KEY";
        var outputPath = args.Length > 1 ? args[1] : Path.Combine("..", "..", "backend", "src", "GutAI.Infrastructure", "Data", "WholeFoodsDatabase.generated.cs");

        Console.WriteLine($"USDA FoodData Central Generator");
        Console.WriteLine($"  API Key: {apiKey[..Math.Min(8, apiKey.Length)]}...");
        Console.WriteLine($"  Output:  {outputPath}");
        Console.WriteLine($"  Queries: {FoodQueries.All.Length}");
        Console.WriteLine();

        using var http = new HttpClient { BaseAddress = new Uri("https://api.nal.usda.gov/fdc/v1/") };

        var foods = new List<FoodEntry>();
        var missed = new List<string>();
        var skipped = new List<(string query, long fdcId, string reason)>();

        for (int i = 0; i < FoodQueries.All.Length; i++)
        {
            var q = FoodQueries.All[i];
            Console.Write($"  [{i + 1}/{FoodQueries.All.Length}] {q,-55} ");

            try
            {
                var match = await FindBestFoodAsync(http, apiKey, q);
                if (match is null)
                {
                    Console.WriteLine("[MISS]");
                    missed.Add(q);
                    continue;
                }

                await RateLimitDelay(apiKey);

                var details = await GetFoodDetailsAsync(http, apiKey, match.FdcId);
                if (details is null)
                {
                    Console.WriteLine($"[ERR] fdcId={match.FdcId}");
                    skipped.Add((q, match.FdcId, "details fetch failed"));
                    continue;
                }

                var entry = ToFoodEntry(details);
                if (entry is null)
                {
                    Console.WriteLine($"[SKIP] fdcId={match.FdcId} incomplete nutrients");
                    skipped.Add((q, match.FdcId, "missing required nutrients"));
                    continue;
                }

                // Macro sanity check
                var macroKcal = (entry.Protein * 4m) + (entry.Carbs * 4m) + (entry.Fat * 9m);
                var diff = Math.Abs(macroKcal - entry.Calories);
                var flag = diff > 50 ? " ⚠️ macro-cal diff=" + diff.ToString("F0") : "";

                Console.WriteLine($"[OK] {entry.Name,-45} {entry.Calories,4} cal  (FDC:{entry.FdcId}, {entry.DataType}){flag}");
                foods.Add(entry);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERR] {ex.Message}");
                missed.Add(q);
            }

            await RateLimitDelay(apiKey);
        }

        Console.WriteLine();
        Console.WriteLine($"Results: {foods.Count} OK, {missed.Count} missed, {skipped.Count} skipped");

        if (missed.Count > 0)
        {
            Console.WriteLine("Missed queries:");
            foreach (var m in missed) Console.WriteLine($"  - {m}");
        }

        if (skipped.Count > 0)
        {
            Console.WriteLine("Skipped (incomplete):");
            foreach (var (sq, fdc, reason) in skipped) Console.WriteLine($"  - {sq} (FDC:{fdc}) — {reason}");
        }

        var cs = EmitCSharpFile(foods);
        var fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, cs, Encoding.UTF8);
        Console.WriteLine($"\nWrote {fullPath} ({foods.Count} foods)");
    }

    private static async Task RateLimitDelay(string apiKey)
    {
        // DEMO_KEY: 30 req/hour, 50 req/day — need ~1.2s between requests to stay safe
        // Real key: 1000 req/hour — 100ms is plenty
        if (apiKey == "DEMO_KEY")
            await Task.Delay(2500);
        else
            await Task.Delay(150);
    }

    private static async Task<SearchFood?> FindBestFoodAsync(HttpClient http, string apiKey, string query)
    {
        var url = $"foods/search?api_key={Uri.EscapeDataString(apiKey)}&query={Uri.EscapeDataString(query)}&dataType={Uri.EscapeDataString("Foundation,SR Legacy")}&pageSize=25";

        var resp = await http.GetAsync(url);
        if (resp.StatusCode == HttpStatusCode.TooManyRequests)
        {
            Console.Write("[RATE LIMITED, waiting 60s] ");
            await Task.Delay(60_000);
            resp = await http.GetAsync(url);
        }
        if (!resp.IsSuccessStatusCode) return null;

        var data = await resp.Content.ReadFromJsonAsync<SearchResponse>(JsonOpts);
        if (data?.Foods is null || data.Foods.Count == 0) return null;

        var ql = query.Trim().ToLowerInvariant();

        return data.Foods
            .OrderBy(f => DataTypeRank(f.DataType))
            .ThenByDescending(f => ScoreMatch(f.Description ?? "", ql))
            .FirstOrDefault();
    }

    private static int DataTypeRank(string? dt) => dt switch
    {
        "Foundation" => 0,
        "SR Legacy" => 1,
        _ => 50,
    };

    private static int ScoreMatch(string desc, string ql)
    {
        var dl = desc.ToLowerInvariant();
        if (dl == ql) return 300;
        if (dl.StartsWith(ql)) return 200;
        if (dl.Contains(ql)) return 100;
        // Count matching words
        var words = ql.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Count(w => dl.Contains(w)) * 10;
    }

    private static async Task<FoodDetails?> GetFoodDetailsAsync(HttpClient http, string apiKey, long fdcId)
    {
        var resp = await http.GetAsync($"food/{fdcId}?api_key={Uri.EscapeDataString(apiKey)}");
        if (resp.StatusCode == HttpStatusCode.TooManyRequests)
        {
            Console.Write("[RATE LIMITED, waiting 60s] ");
            await Task.Delay(60_000);
            resp = await http.GetAsync($"food/{fdcId}?api_key={Uri.EscapeDataString(apiKey)}");
        }
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<FoodDetails>(JsonOpts);
    }

    private static FoodEntry? ToFoodEntry(FoodDetails food)
    {
        decimal? kcal = GetNutrient(food, "Energy", "kcal")
                     ?? GetNutrient(food, "Energy", "KCAL");
        decimal? protein = GetNutrient(food, "Protein", "g")
                        ?? GetNutrient(food, "Protein", "G");
        decimal? carbs = GetNutrient(food, "Carbohydrate, by difference", "g")
                      ?? GetNutrient(food, "Carbohydrate, by difference", "G");
        decimal? fat = GetNutrient(food, "Total lipid (fat)", "g")
                    ?? GetNutrient(food, "Total lipid (fat)", "G");
        decimal? fiber = GetNutrient(food, "Fiber, total dietary", "g")
                      ?? GetNutrient(food, "Fiber, total dietary", "G");
        decimal? sugar = GetNutrient(food, "Sugars, total including NLEA", "g")
                      ?? GetNutrient(food, "Sugars, Total", "g")
                      ?? GetNutrient(food, "Total Sugars", "g");
        decimal? sodiumMg = GetNutrient(food, "Sodium, Na", "mg")
                         ?? GetNutrient(food, "Sodium, Na", "MG");

        // USDA warns: missing values ≠ zero. Skip incomplete entries.
        if (kcal is null || protein is null || carbs is null || fat is null)
            return null;

        // Clean up USDA ALL CAPS descriptions
        var name = food.Description ?? $"FDC {food.FdcId}";
        if (name == name.ToUpperInvariant() && name.Length > 3)
            name = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.ToLowerInvariant());

        return new FoodEntry
        {
            Name = name,
            Calories = Math.Round(kcal.Value, 2),
            Protein = Math.Round(protein.Value, 2),
            Carbs = Math.Round(carbs.Value, 2),
            Fat = Math.Round(fat.Value, 2),
            Fiber = Math.Round(fiber ?? 0m, 2),
            Sugar = Math.Round(sugar ?? 0m, 2),
            SodiumG = sodiumMg.HasValue ? Math.Round(sodiumMg.Value / 1000m, 4) : 0m,
            FdcId = food.FdcId,
            DataType = food.DataType ?? "Unknown",
        };
    }

    private static decimal? GetNutrient(FoodDetails food, string name, string unit)
    {
        if (food.FoodNutrients is null) return null;

        foreach (var fn in food.FoodNutrients)
        {
            if (fn.Nutrient is null) continue;
            if (string.Equals(fn.Nutrient.Name, name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(fn.Nutrient.UnitName, unit, StringComparison.OrdinalIgnoreCase))
                return fn.Amount;
        }
        return null;
    }

    private static string EmitCSharpFile(List<FoodEntry> foods)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated>");
        sb.AppendLine($"// Generated from USDA FoodData Central on {DateTime.UtcNow:yyyy-MM-dd}.");
        sb.AppendLine($"// {foods.Count} whole foods. Do not edit manually — re-run UsdaFoodGenerator.");
        sb.AppendLine("// Sodium values are in GRAMS per 100g (consistent with OpenFoodFacts).");
        sb.AppendLine("// </auto-generated>");
        sb.AppendLine();
        sb.AppendLine("using GutAI.Application.Common.DTOs;");
        sb.AppendLine();
        sb.AppendLine("namespace GutAI.Infrastructure.Data;");
        sb.AppendLine();
        sb.AppendLine("public static class WholeFoodsDatabase");
        sb.AppendLine("{");
        sb.AppendLine("    private static readonly List<FoodProductDto> Foods =");
        sb.AppendLine("    [");

        string? lastCategory = null;
        foreach (var f in foods)
        {
            var cat = CategorizeFood(f.Name);
            if (cat != lastCategory)
            {
                if (lastCategory is not null) sb.AppendLine();
                sb.AppendLine($"        // ── {cat} ──");
                lastCategory = cat;
            }

            sb.AppendLine($"        F(\"{Escape(f.Name)}\", {Dec(f.Calories)}, {Dec(f.Protein)}, {Dec(f.Carbs)}, {Dec(f.Fat)}, {Dec(f.Fiber)}, {Dec(f.Sugar)}, {Dec(f.SodiumG)}), // FDC:{f.FdcId} ({f.DataType})");
        }

        sb.AppendLine("    ];");
        sb.AppendLine();
        sb.AppendLine("""
    public static List<FoodProductDto> Search(string query, int maxResults = 10)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return Foods
            .Select(f => (food: f, score: MatchScore(f.Name, terms, query)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .Take(maxResults)
            .Select(x => x.food)
            .ToList();
    }

    private static int MatchScore(string name, string[] terms, string fullQuery)
    {
        int score = 0;
        var lower = name.ToLowerInvariant();
        var queryLower = fullQuery.ToLowerInvariant();

        if (lower.StartsWith(queryLower)) score += 100;
        else if (lower.Contains(queryLower)) score += 50;

        foreach (var term in terms)
        {
            if (lower.Contains(term, StringComparison.OrdinalIgnoreCase))
                score += 20;
        }

        return score;
    }

    private static FoodProductDto F(string name, decimal cal, decimal protein, decimal carbs, decimal fat, decimal fiber, decimal sugar, decimal sodium) =>
        new()
        {
            Name = name,
            Calories100g = cal,
            Protein100g = protein,
            Carbs100g = carbs,
            Fat100g = fat,
            Fiber100g = fiber,
            Sugar100g = sugar,
            Sodium100g = sodium,
            DataSource = "USDA",
        };
""");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string Dec(decimal d)
    {
        var s = d.ToString("0.####", CultureInfo.InvariantCulture);
        return s + "m";
    }

    private static string CategorizeFood(string name)
    {
        var n = name.ToLowerInvariant();

        if (ContainsAny(n, "strawberr", "blueberr", "raspberr", "blackberr", "banana", "apple", "orange", "grape",
            "mango", "pineapple", "watermelon", "avocado", "peach", "pear", "cherr", "kiwi", "lemon", "lime",
            "grapefruit", "cantaloupe", "honeydew", "melon", "plum", "apricot", "papaya", "pomegranate",
            "cranberr", "passion fruit", "fig,", "dates", "raisin", "coconut meat"))
            return "Fruits";

        if (ContainsAny(n, "broccoli", "spinach", "carrot", "tomato", "sweet potato", "potato", "onion", "garlic",
            "cucumber", "pepper", "lettuce", "kale", "cauliflower", "zucchini", "mushroom", "celery", "asparagus",
            "beet", "corn", "peas", "cabbage", "brussels", "eggplant", "artichoke", "radish", "turnip",
            "green bean", "leek", "fennel", "okra", "bok choy", "chard", "collard", "arugula", "watercress",
            "endive", "parsnip", "rutabaga", "jicama", "squash", "pumpkin"))
            return "Vegetables";

        if (ContainsAny(n, "chicken", "turkey", "duck", "beef", "pork", "bacon", "ham", "lamb", "venison",
            "bison", "liver"))
            return "Proteins — Meat";

        if (ContainsAny(n, "salmon", "tuna", "shrimp", "cod", "tilapia", "halibut", "sardine", "mackerel",
            "trout", "crab", "lobster", "scallop", "mussel", "oyster", "clam", "catfish", "swordfish",
            "anchov", "squid", "octopus", "fish"))
            return "Proteins — Seafood";

        if (ContainsAny(n, "egg", "tofu", "tempeh", "edamame", "seitan"))
            return "Proteins — Eggs & Plant";

        if (ContainsAny(n, "rice", "oat", "quinoa", "pasta", "couscous", "bulgur", "barley", "farro",
            "millet", "buckwheat", "cornmeal", "flour", "bread", "tortilla", "bagel", "muffin", "pita",
            "croissant", "pancake", "waffle", "granola", "cornflake", "cereal"))
            return "Grains & Cereals";

        if (ContainsAny(n, "lentil", "chickpea", "bean", "pinto", "navy", "lima", "split pea", "soybean",
            "mung", "black-eyed"))
            return "Legumes";

        if (ContainsAny(n, "milk", "yogurt", "cheese", "butter", "cream", "half and half", "whipped", "kefir",
            "ricotta", "cottage"))
            return "Dairy";

        if (ContainsAny(n, "almond", "walnut", "peanut", "cashew", "pecan", "pistachio", "macadamia",
            "brazil nut", "hazelnut", "pine nut", "chestnut", "chia", "flax", "sunflower seed", "pumpkin seed",
            "sesame seed", "hemp seed", "poppy seed"))
            return "Nuts & Seeds";

        if (ContainsAny(n, "oil", "ghee", "lard"))
            return "Oils & Fats";

        if (ContainsAny(n, "soy sauce", "vinegar", "mustard", "ketchup", "mayonnaise", "hot sauce",
            "worcestershire", "fish sauce", "tahini", "hummus", "salsa", "pesto", "sriracha", "bbq",
            "teriyaki", "ranch", "dressing"))
            return "Condiments & Sauces";

        if (ContainsAny(n, "honey", "maple", "sugar", "molasses", "agave", "cocoa", "chocolate", "baking",
            "vanilla", "yeast", "gelatin", "cornstarch"))
            return "Sweeteners & Baking";

        if (ContainsAny(n, "peanut butter", "almond butter", "cashew butter", "sunflower butter", "nutella", "jam"))
            return "Nut Butters & Spreads";

        if (ContainsAny(n, "coffee", "tea", "juice", "coconut water", "coconut milk", "almond milk",
            "soy milk", "oat milk", "lemonade"))
            return "Beverages";

        if (ContainsAny(n, "basil", "cilantro", "parsley", "mint", "rosemary", "thyme", "dill", "ginger",
            "turmeric", "cinnamon", "cumin", "paprika", "pepper", "chili", "oregano", "cayenne", "nutmeg",
            "clove", "coriander", "garlic powder", "onion powder", "salt"))
            return "Herbs & Spices";

        if (ContainsAny(n, "dried", "prune", "trail mix", "popcorn", "rice cake", "chip", "pretzel"))
            return "Snacks & Dried Fruits";

        if (ContainsAny(n, "sauerkraut", "kimchi", "pickle", "olive", "caper", "sun-dried", "roasted red",
            "coconut, shredded"))
            return "Prepared & Preserved";

        return "Other";
    }

    private static bool ContainsAny(string text, params string[] terms) =>
        terms.Any(t => text.Contains(t));
}

// ── API Response Models ──

public sealed class SearchResponse
{
    [JsonPropertyName("foods")] public List<SearchFood>? Foods { get; set; }
    [JsonPropertyName("totalHits")] public int TotalHits { get; set; }
}

public sealed class SearchFood
{
    [JsonPropertyName("fdcId")] public long FdcId { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("dataType")] public string? DataType { get; set; }
    [JsonPropertyName("foodCategory")] public string? FoodCategory { get; set; }
}

public sealed class FoodDetails
{
    [JsonPropertyName("fdcId")] public long FdcId { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("dataType")] public string? DataType { get; set; }
    [JsonPropertyName("foodNutrients")] public List<FoodNutrientEntry>? FoodNutrients { get; set; }
}

public sealed class FoodNutrientEntry
{
    [JsonPropertyName("amount")] public decimal? Amount { get; set; }
    [JsonPropertyName("nutrient")] public NutrientInfo? Nutrient { get; set; }
}

public sealed class NutrientInfo
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("unitName")] public string? UnitName { get; set; }
}

public sealed class FoodEntry
{
    public required string Name { get; set; }
    public decimal Calories { get; set; }
    public decimal Protein { get; set; }
    public decimal Carbs { get; set; }
    public decimal Fat { get; set; }
    public decimal Fiber { get; set; }
    public decimal Sugar { get; set; }
    public decimal SodiumG { get; set; }
    public long FdcId { get; set; }
    public required string DataType { get; set; }
}
