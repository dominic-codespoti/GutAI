# Global Branded Food Data (Cadbury + Australia Focus)

## Short answer

For free sources, the strongest current stack for global branded coverage (including Cadbury) and Australian market relevance is:

1. **Open Food Facts** (primary)
2. **FSANZ Branded Food Database** (as it becomes publicly available)
3. **USDA Global Branded / FoodData Central** (fallback, mostly US-oriented)

## Comparison

| Source | Cadbury/global brands | AU supermarket/private-label | Access model | Practical use |
|---|---|---|---|---|
| **Open Food Facts** | Strong | Good (varies by contribution density) | Free API + open dataset | Best current free primary for barcode/brand lookup |
| **FSANZ Branded Food Database** | Good for products sold in AU | Potentially strongest for AU | Program underway; publication subset and permissions apply | Best AU authority as publication matures |
| **USDA Global Branded (FDC)** | Good for multinationals | Limited for AU-specific shelf coverage | Free API + bulk data | Useful fallback/secondary source |

## Recommended strategy for GutAI

- Keep **Open Food Facts** as the main branded source.
- Add **USDA branded** as fallback for missing OFF barcodes.
- Integrate **FSANZ Branded Food Database** once public subset/API/files are stable.
- Run periodic barcode coverage checks using an AU barcode test set.

## Notes

- Global branded coverage is always dynamic and contributor/partner dependent.
- AU private-label depth (e.g., Coles/Woolworths house brands) is typically strongest in AU-native pipelines.
