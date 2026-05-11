# GutRisk Service Analysis

## Additive Database Coverage

### GutHarmfulAdditives (GutRiskData.cs)
- **Before expansion**: ~119 E-numbers
- **After expansion**: **312 E-numbers** (+193 new)
- Covers all major EU additive categories:
  - Colorants (47): natural (curcumin, carmine, chlorophyll, carotenoids, anthocyanins, beetroot) and synthetic
  - Preservatives (60+): sorbates, benzoates, sulfites, nitrites, parabens, propionates, acetates, lactates
  - Antioxidants (31): vitamin C derivatives, tocopherols, gallates, erythorbates, EDTA, rosemary extract
  - Acidity Regulators (30): citrates, tartrates, malates, lactates, phosphates, adipates, succinates
  - Emulsifiers (17): agar, konjac, modified gum arabic, fatty acid salts, propylene glycol esters, plant sterols
  - Thickeners (15): various gums, modified celluloses, beta-cyclodextrin, pullulan
  - Sweeteners (19): sugar alcohols, artificial sweeteners, natural sweeteners (stevia, thaumatin, tagatose)
  - Anti-caking agents (9): silicates, phosphates, talc, bentonite, ferrocyanides
  - Glazing agents (7): beeswax, candelilla wax, carnauba wax, shellac
  - Packaging gases (6): argon, nitrogen, nitrous oxide, oxygen, hydrogen
  - Flour treatment agents (2): L-cysteine, carbamide
  - Flavor enhancers (2): glutamic acid, zinc acetate
  - Plus: sequestrants, humectants, propellants, anti-foaming agents, foaming agents

### Seeded FoodAdditives (DbSeeder.cs)
- **Before expansion**: 25 additives with full regulatory data
- **After expansion**: **218 additives** (+193 new)
- Each record includes CSPI rating, US/EU regulatory status, health concerns, description, alternate names, banned countries

### GumHarmfulAdditives Risk Level Distribution
- **High**: Red 2G, Brown FK, Biphenyl, Orthophenyl Phenol, Boric Acid, Formaldehyde, Ethoxyquin, Potassium Bromate, Hexamethylenetetramine, E239, parabens (propyl), etc.
- **Medium**: Many synthetic azo colors, parabens (methyl/ethyl), propyl gallate, octyl/dodecyl gallate, aluminium silicates, EDTA, phosphates, some preservatives
- **Low**: Natural colors, vitamins, most gums/thickeners, most acidity regulators, packaging gases, mineral anti-caking agents, waxes, propionates, lactates

## Category Mapping
Categories in `GutHarmfulAdditives` map to trigger types via `CategoryMap`:
- `TriggerType.Additive`: Most additive categories (emulsifiers, preservatives, colorants, etc.)
- `TriggerType.Fodmap`: Sugar alcohols, polyol sources, high-FODMAP ingredients, dairy/lactose, fructose sources, GOS sources
- `TriggerType.Processing`: Processing level (NOVA)
- `TriggerType.Nutrient`: Sodium, sugar concerns
- `TriggerType.Combination`: Stacking penalties

## Key Insights
- The scoring engine now comprehensively covers the vast majority of EU/globally authorized food additives
- Natural additives (colors from plants, vitamins, minerals, waxes) are rated Low risk
- Synthetic azo dyes are generally Medium risk
- Emulsifiers that damage mucus layer (polysorbates, CMC) are High/Medium risk
- Growing evidence supports the inclusion of more emulsifiers and gums as potential gut irritants
- The `CategoryMap` allows flexible classification of additives into different trigger types
