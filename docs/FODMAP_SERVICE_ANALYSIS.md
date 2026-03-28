# FODMAP Service Analysis

## Overview

The FODMAP (Fermentable Oligosaccharides, Disaccharides, Monosaccharides, And Polyols) Service provides comprehensive analysis of food products to identify potential digestive triggers for individuals with IBS and other gut sensitivities.

## Architecture

### Service Location
- **File**: `backend/src/GutAI.Infrastructure/Services/FodmapService.cs`
- **Interface**: `IFodmapService` (`backend/src/GutAI.Application/Common/Interfaces/IFodmapService.cs`)

### Detection Pipeline (6 Layers)

The service uses a multi-layer detection approach to identify FODMAP triggers:

1. **Ingredient Text Scanning**
   - Scans product ingredients against 900+ FODMAP patterns
   - Uses regex matching for word boundaries
   - Respects "lactose-free" and "gluten-free" claims

2. **Additive Tags Checking**
   - Checks OpenFoodFacts additive tags (E-numbers)
   - Identifies sugar alcohols (polyols): E420-E968

3. **Linked Additive Name Matching**
   - Scans additive names for FODMAP keywords
   - Detects sorbitol, mannitol, maltitol, etc.

4. **High Sugar Detection**
   - Flags products with >30g sugar per 100g
   - Checks for fructose sources (fruit juice, HFCS)

5. **Product Name Matching (Whole Foods)**
   - Matches product names against 500+ whole food patterns
   - Skips generic patterns when detailed ingredients exist

6. **Lactase Enzyme Mitigation**
   - Downgrades lactose triggers to "Low" if lactase present
   - Handles lactose-free claims appropriately

## Scoring Algorithm

### Score Calculation

```
Base Score: 100
Multiplier = 1.0 × (severity multipliers)

Severity Multipliers:
- High: ×0.40
- Moderate: ×0.85
- Low: ×0.95

Category Stacking Penalty:
If ≥3 distinct FODMAP categories:
  Apply ×0.92^(distinctCategories - 2)

Final Score = Clamp(100 × multiplier, 0, 100)
```

### Rating Mapping

| Score Range | Rating |
|-------------|--------|
| 75-100 | Low FODMAP |
| 60-74 | Moderate FODMAP |
| 30-59 | High FODMAP |
| 0-29 | Very High FODMAP |

### Confidence Levels

| Condition | Confidence |
|-----------|------------|
| Detailed ingredients (>50 chars with commas) | High |
| Trusted whole food (USDA/AUSNUT) OR simple ingredients | Medium |
| No ingredients + unknown branded product | Low |

## FODMAP Categories

### Oligosaccharides

**Fructans (High FODMAP)**
- Wheat, barley, rye
- Onion, garlic, shallots, leeks
- Inulin, chicory root
- Cashews, pistachios

**GOS (Galacto-oligosaccharides)**
- Chickpeas, lentils, beans
- Soy products

### Disaccharides

**Lactose (High/Medium FODMAP)**
- Milk, soft cheeses (ricotta, cottage)
- Ice cream, yogurt
- Cream, sour cream

### Monosaccharides

**Excess Fructose (High FODMAP)**
- Apples, pears
- Honey, agave, HFCS
- Fruit juices

### Polyols

**High FODMAP Sugar Alcohols**
- Sorbitol (E420)
- Mannitol (E421)
- Maltitol (E965)

**Low FODMAP Sugar Alcohols**
- Erythritol (E968) - best tolerated
- Xylitol (E967) - moderate

## Data Source

### FodmapData.cs

All FODMAP data is centralized in `backend/src/GutAI.Infrastructure/Services/FodmapData.cs`:

- **IngredientTriggers**: ~900 patterns for ingredient matching
- **WholeFoodTriggers**: ~500 patterns for whole food names
- **Additives**: E-number to FODMAP mapping
- **AdditiveNameTriggers**: Additive name patterns
- **GenericWholeFoodPatterns**: Generic names to skip

### Data Structure

```csharp
public static readonly (string Pattern, Regex? Regex, FodmapTriggerDto Trigger)[] IngredientTriggers
public static readonly (string Pattern, FodmapTriggerDto Trigger)[] WholeFoodTriggers
public static readonly Dictionary<string, FodmapTriggerDto> Additives
public static readonly (string Pattern, FodmapTriggerDto Trigger)[] AdditiveNameTriggers
```

## API Usage

### Endpoint

```
GET /api/food/{id}/fodmap
```

### Response DTO

```csharp
public record FodmapAssessmentDto
{
    public int FodmapScore { get; init; }                    // 0-100
    public string FodmapRating { get; init; } = "Low FODMAP"; // Low/Moderate/High/Very High
    public int TriggerCount { get; init; }
    public int HighCount { get; init; }
    public int ModerateCount { get; init; }
    public int LowCount { get; init; }
    public List<string> Categories { get; init; } = [];
    public List<FodmapTriggerDto> Triggers { get; init; } = [];
    public string Summary { get; init; } = "";
    public string? Confidence { get; init; }                   // Low/Medium/High
}

public record FodmapTriggerDto
{
    public string Name { get; init; } = "";
    public string Category { get; init; } = "";        // Oligosaccharide/Disaccharide/Monosaccharide/Polyol/Other
    public string SubCategory { get; init; } = "";     // Fructan/GOS/Lactose/Excess Fructose/Sorbitol/Mannitol
    public string Severity { get; init; } = "Low";    // High/Moderate/Low
    public string Explanation { get; init; } = "";
}
```

## Integration Points

### GutRisk Service
- FODMAP flags integrated into GutRisk assessment
- FODMAP class stacking penalties applied
- Dose sensitivity classification

### Food Diary Analysis
- FODMAP scores used for food-symptom correlation
- Triggers tracked in elimination diet tracking

### Substitution Service
- Low-FODMAP alternatives suggested for high-FODMAP foods
- Category-aware substitution logic

## Testing

### Test File
- **Location**: `backend/tests/GutAI.Infrastructure.Tests/FodmapServiceTests.cs`
- **Lines**: 798 lines
- **Coverage**: All FODMAP categories, scoring logic, real-world products

### Key Test Cases
- Score calculation for each severity level
- All 5 FODMAP category detection
- Deduplication logic
- Lactase enzyme mitigation
- Real-world products (Nutella, protein bars, sugar-free gum)

## Known Issues & Limitations

### Current Limitations

1. **No Portion Size Awareness**: Cannot distinguish between small amounts (e.g., pinch of garlic powder) vs large amounts (e.g., whole garlic cloves)

2. **Static Data Structure**: All FODMAP entries hardcoded; updates require code redeployment

3. **Limited Fermentation Context**: Some fermented foods have reduced FODMAP content but this isn't consistently applied

4. **Simplified Scoring**: Multiplicative penalty may not reflect real-world tolerance patterns

### Documented Issues

See `docs/DATA_FILES_AUDIT_REPORT.md` for detailed analysis of:
- Data duplication between IngredientTriggers and WholeFoodTriggers
- Severity inconsistencies
- Scoring accuracy rates

## Future Improvements

1. **Portion Size Modeling**: Add quantity-aware scoring
2. **User-Specific Profiles**: Learn from user corrections
3. **External Data Source**: Move from inline arrays to configurable JSON/YAML
4. **Machine Learning**: Use pattern recognition for improved classification
5. **Integration with Clinical Data**: Link to symptom severity patterns

## References

- **Monash University FODMAP Diet**: Primary research source
- **FODMAP Friendly**: Certification program data
- **Clinical Studies**: Integration with IBS research literature

## Related Documentation

- `docs/DATA_FILES_AUDIT_REPORT.md` - Comprehensive data audit
- `docs/SCORING_ANALYSIS_REPORT.md` - Scoring system analysis
- `docs/MEAL_LOGGING_BUG_ANALYSIS.md` - Meal logging timezone bug

---

*Last Updated: March 29, 2026*
