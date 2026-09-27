# ScanMealRepair

`ScanMealRepair` is a one-off recovery utility for historical scan-confirmed meal logs in the Azure Table Storage `gutai` table. It reads confirmed scan sessions and their `DraftItemsJson`, finds the meal log with `OriginalText` set to `photo scan {sessionId}`, then compares the committed meal items with the saved draft.

It identifies two defects:

- **Stale gram edits:** the committed serving weight differs from the draft weight by more than 0.5 g, while committed calories still equal the draft calories rounded to a whole kcal. Nutrition is recomputed for the committed serving weight using the matching candidate's per-100 g values when available, otherwise the draft item's nutrition scaled to a per-100 g basis.
- **Zero-kcal web items:** the draft source is `web`, committed calories are zero, and draft calories are positive. Values are recomputed from the draft-derived basis for the committed weight, and provenance is set to `Web`.

Nutrition calculations use the application's `NutritionCalculator`. Meal totals are recalculated from the resulting repaired and unchanged item values.

## Matching drafts to committed items

For each committed item, the planner tries these matching rules in order:

1. Equal `FoodProductId` values when both the committed item and draft have an ID.
2. Case-insensitive equality between `FoodName` and the draft's `Name` or `CanonicalName` (the committed food name was title-cased at confirm time).
3. Position, only when draft and committed item counts are equal and exactly one unmatched pair remains at the same index.

A committed item matching multiple drafts is reported as ambiguous and is not changed. When multiple unmatched positional items remain, the planner reports them rather than guessing a pairing. A matched item without a usable nutrition basis is reported as `no basis` and left unchanged; items with no safe match are also left unchanged.

## Credentials and invocation
Use an Azure Table Storage connection string that can read the `gutai` table. Supply it with `--connection`, or set `GUTAI_STORAGE_CONNECTION` in the process environment. Applying repairs also requires permission to update the affected meal item and meal log entities.

From the repository root, run a dry run (the default):

```sh
dotnet run --project backend/tools/ScanMealRepair/ScanMealRepair.csproj -- --connection "$GUTAI_STORAGE_CONNECTION"
```

To limit the scan to one user's partition, add `--user` with that user's GUID:

```sh
dotnet run --project backend/tools/ScanMealRepair/ScanMealRepair.csproj -- --user <guid>
```

After reviewing the dry-run report, use `--apply` to write the planned repairs:

```sh
dotnet run --project backend/tools/ScanMealRepair/ScanMealRepair.csproj -- --connection "$GUTAI_STORAGE_CONNECTION" --apply
```

`--connection` takes precedence over the environment variable. `--apply` is opt-in; without it the utility performs no writes. When applying, it updates repaired item nutrition and provenance and the meal's calorie, protein, carbohydrate, and fat totals using Azure Table `UpdateEntityAsync` in Merge mode with each entity's ETag.

## Report and exit codes

The tool prints one line per matched meal with its repair and ambiguity counts. Repair lines include the committed item row key, repair reason (`stale-grams` or `web-zero`), before/after kcal and P/C/F values, and before/after provenance. Ambiguity lines include the committed item row key and the reason. The final summary reports meals scanned, meals needing repair, items repaired, ambiguity count, and whether the run was `DRY RUN` or `APPLY`.

- **0** — scan completed successfully.
- **1** — runtime/storage failure.
- **2** — usage error, invalid `--user` value, or missing storage connection.
