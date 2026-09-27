using System.Globalization;
using System.Text.Json;
using Azure.Data.Tables;
using ScanMealRepair;
using GutAI.Application.Common.DTOs;

var parsed = ParseArgs(args);
if (parsed.Error is not null)
{
    Console.Error.WriteLine(parsed.Error);
    Console.Error.WriteLine("Usage: ScanMealRepair --connection <Azure Table connection string> [--apply] [--user <guid>]");
    return 2;
}

var connection = parsed.Connection ?? Environment.GetEnvironmentVariable("GUTAI_STORAGE_CONNECTION");
if (string.IsNullOrWhiteSpace(connection))
{
    Console.Error.WriteLine("Missing storage connection. Use --connection or GUTAI_STORAGE_CONNECTION.");
    return 2;
}

try
{
    var client = new TableClient(connection, "gutai");
    var scanFilter = parsed.User is { } user
        ? $"PartitionKey eq '{user}' and RowKey ge 'SCAN|' and RowKey lt 'SCAN|~' and Status eq 'Confirmed'"
        : "RowKey ge 'SCAN|' and RowKey lt 'SCAN|~' and Status eq 'Confirmed'";
    var mealsScanned = 0;
    var mealsNeedingRepair = 0;
    var itemsRepaired = 0;
    var ambiguitiesCount = 0;

    await foreach (var session in client.QueryAsync<TableEntity>(scanFilter))
    {
        if (!Guid.TryParse(session.PartitionKey, out var userId) ||
            !Guid.TryParse(session.RowKey["SCAN|".Length..], out var sessionId))
            continue;
        var drafts = JsonSerializer.Deserialize<List<MealDraftItemDto>>(session.GetString("DraftItemsJson") ?? "[]",
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true }) ?? [];
        var draftInputs = drafts.Select(ToDraftInput).ToArray();
        var mealFilter = $"PartitionKey eq '{userId}' and RowKey ge 'MEAL|' and RowKey lt 'MEAL|~'";
        await foreach (var meal in client.QueryAsync<TableEntity>(mealFilter))
        {
            if (!string.Equals(meal.GetString("OriginalText"), $"photo scan {sessionId}", StringComparison.Ordinal))
                continue;

            var mealId = Guid.Parse(meal.RowKey["MEAL|".Length..]);
            var itemPrefix = $"MEALITEM|{mealId}|";
            var itemFilter = $"PartitionKey eq '{userId}' and RowKey ge '{itemPrefix}' and RowKey lt '{itemPrefix}~'";
            var itemRows = new List<TableEntity>();
            await foreach (var row in client.QueryAsync<TableEntity>(itemFilter)) itemRows.Add(row);
            var committed = itemRows.Select(ToCommittedInput).ToArray();
            var plan = ScanMealRepairPlanner.Plan(draftInputs, committed);
            mealsScanned++;
            itemsRepaired += plan.Repairs.Count;
            ambiguitiesCount += plan.Ambiguities.Count;
            if (plan.Repairs.Count > 0) mealsNeedingRepair++;

            Console.WriteLine($"Meal {meal.RowKey} (session {sessionId}): {plan.Repairs.Count} repair(s), {plan.Ambiguities.Count} ambiguity/ambiguities");
            foreach (var repair in plan.Repairs)
            {
                Console.WriteLine($"  item {repair.CommittedItemId}: {repair.Reason}; {repair.Before.Calories} -> {repair.After.Calories} kcal; P {repair.Before.ProteinG} -> {repair.After.ProteinG}, C {repair.Before.CarbsG} -> {repair.After.CarbsG}, F {repair.Before.FatG} -> {repair.After.FatG}; provenance {repair.Before.NutritionProvenance ?? "(null)"} -> {repair.After.NutritionProvenance ?? "(null)"}");
            }
            foreach (var ambiguity in plan.Ambiguities)
                Console.WriteLine($"  item {ambiguity.CommittedItemId}: ambiguous ({ambiguity.Reason})");

            if (!parsed.Apply || plan.Repairs.Count == 0) continue;
            var repairedById = plan.Repairs.ToDictionary(r => r.CommittedItemId, StringComparer.Ordinal);
            foreach (var itemRow in itemRows)
            {
                if (!repairedById.TryGetValue(itemRow.RowKey, out var repair)) continue;
                itemRow["Calories"] = Format(repair.After.Calories);
                itemRow["ProteinG"] = Format(repair.After.ProteinG);
                itemRow["CarbsG"] = Format(repair.After.CarbsG);
                itemRow["FatG"] = Format(repair.After.FatG);
                itemRow["FiberG"] = Format(repair.After.FiberG);
                itemRow["SugarG"] = Format(repair.After.SugarG);
                itemRow["SodiumMg"] = Format(repair.After.SodiumMg);
                itemRow["NutritionProvenance"] = repair.After.NutritionProvenance;
                await client.UpdateEntityAsync(itemRow, itemRow.ETag, TableUpdateMode.Merge);
            }

            meal["TotalCalories"] = Format(plan.MealTotals.Calories);
            meal["TotalProteinG"] = Format(plan.MealTotals.ProteinG);
            meal["TotalCarbsG"] = Format(plan.MealTotals.CarbsG);
            meal["TotalFatG"] = Format(plan.MealTotals.FatG);
            await client.UpdateEntityAsync(meal, meal.ETag, TableUpdateMode.Merge);
        }
    }

    Console.WriteLine($"Summary: meals scanned {mealsScanned}; meals needing repair {mealsNeedingRepair}; items repaired {itemsRepaired}; ambiguities {ambiguitiesCount}; mode {(parsed.Apply ? "APPLY" : "DRY RUN")}");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Repair failed: {ex.Message}");
    return 1;
}

static (string? Connection, bool Apply, Guid? User, string? Error) ParseArgs(string[] args)
{
    string? connection = null;
    Guid? user = null;
    var apply = false;
    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--connection" when i + 1 < args.Length:
                connection = args[++i];
                break;
            case "--apply":
                apply = true;
                break;
            case "--user" when i + 1 < args.Length:
                if (!Guid.TryParse(args[++i], out var parsedUser)) return (null, false, null, "--user must be a valid GUID.");
                user = parsedUser;
                break;
            case "--connection":
            case "--user":
                return (null, false, null, $"{args[i]} requires a value.");
            default:
                return (null, false, null, $"Unknown argument: {args[i]}");
        }
    }
    return (connection, apply, user, null);
}

static LegacyDraftItemInput ToDraftInput(MealDraftItemDto item) => new(
    item.ItemId,
    item.Name,
    item.CanonicalName,
    item.FoodProductId,
    item.Source,
    item.Grams,
    item.Calories,
    item.ProteinG,
    item.CarbsG,
    item.FatG,
    item.FiberG,
    item.SugarG,
    item.SodiumMg,
    item.Grounding?.Candidates.Select(c => new DraftCandidateInput(
        c.FoodProductId, c.Source, c.Calories100g, c.Protein100g, c.Carbs100g, c.Fat100g,
        c.Fiber100g, c.Sugar100g, c.SodiumMg100g)).ToArray());

static CommittedMealItemInput ToCommittedInput(TableEntity row) => new(
    row.RowKey,
    row.GetString("FoodName") ?? "",
    Guid.TryParse(row.GetString("FoodProductId"), out var foodProductId) ? foodProductId : null,
    ReadDecimal(row, "ServingWeightG") ?? 0m,
    ReadDecimal(row, "Calories") ?? 0m,
    ReadDecimal(row, "ProteinG") ?? 0m,
    ReadDecimal(row, "CarbsG") ?? 0m,
    ReadDecimal(row, "FatG") ?? 0m,
    ReadDecimal(row, "FiberG"),
    ReadDecimal(row, "SugarG"),
    ReadDecimal(row, "SodiumMg"),
    row.GetString("NutritionProvenance"));

static decimal? ReadDecimal(TableEntity entity, string key)
{
    var text = entity.GetString(key);
    return decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : null;
}

static string? Format(decimal? value) => value?.ToString(CultureInfo.InvariantCulture);
