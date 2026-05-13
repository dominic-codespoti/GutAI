# Data Sources Map

## GutAI Internal Sources

| Endpoint / Source | What It Returns | Best For |
|------------------|----------------|----------|
| `GET /api/food/search?q=` | Composite search across USDA, OpenFoodFacts, branded foods | Finding product data, nutrition, ingredients |
| `GET /api/food/additives` | Full additive catalog (218 entries) | Regulatory status, CSPI rating, safety concerns |
| `GET /api/food/additives/{id}` | Single additive detail | Deep dive on one additive |
| `GET /api/food/{id}/safety-report` | Full safety report (GutRisk + FODMAP + GI + Personalized) | GutAI's synthesized view |
| `GET /api/food/{id}/gut-risk` | GutRisk assessment | Additive-focused risk flags |
| `GET /api/food/barcode/{code}` | Product by barcode | Direct product lookup |

## External API Sources

### OpenFoodFacts
- **Access**: GutAI's `OpenFoodFactsClient.cs` + website
- **Coverage**: 3M+ products, ingredients, additives, nutrition, NOVA, Nutri-Score, Eco-Score
- **Strengths**: Global coverage, open data, additive E-numbers, ingredient lists
- **Weaknesses**: User-contributed data quality varies, branded products dominate

### USDA FoodData Central
- **Access**: GutAI's `UsdaFoodDataClient.cs` + website
- **Coverage**: Whole foods, foundation foods, branded foods, SR Legacy
- **Strengths**: Authoritative nutrition data for whole foods, no processed products
- **Weaknesses**: Fewer additives listed, no FODMAP data, US-centric

### FDA OpenAPI
- **Access**: GutAI's `OpenFdaClient.cs` + website
- **Coverage**: Adverse events, enforcement reports, recall entries
- **Strengths**: Official US regulatory data, recall timeliness
- **Weaknesses**: Limited to US, adverse event reporting is voluntary

### CSPI (Center for Science in the Public Interest)
- **Access**: cspinet.org
- **Coverage**: Food additive safety ratings, chemical cuisine database
- **Strengths**: Independent assessment, long history of additive research
- **Weaknesses**: More conservative than regulatory bodies

## Web Research Targets

### Regulatory
| Site | Best For |
|------|----------|
| fda.gov | GRAS notices, food additive petitions, recalls, safety alerts |
| efsa.europa.eu | EU additive re-evaluations, safety opinions, ADI updates |
| ec.europa.eu/food/food-feed-portal | EU food additives database, permitted substances |
| fao.org/codex | CODEX general standard for food additives |
| accessdata.fda.gov/scripts/fdcc/ | FDA food additive status list |

### Research
| Site | Best For |
|------|----------|
| pubmed.ncbi.nlm.nih.gov | Peer-reviewed studies on specific additives |
| scholar.google.com | Broader literature search |
| cochrane.org | Systematic reviews on nutrition topics |

### Consumer / Advocacy
| Site | Best For |
|------|----------|
| cspinet.org | Additive safety ratings, chemical cuisine |
| ewg.org | Food scores, pesticide residue data |
| consumerreports.org | Food safety investigations, product testing |

### Data Portals
| Site | Best For |
|------|----------|
| world.openfoodfacts.org | Product-level ingredient/score data |
| fdc.nal.usda.gov | USDA search and download |
| foodb.ca | Comprehensive food compound database |

### General Reference
| Site | Best For |
|------|----------|
| en.wikipedia.org | Background, history, chemical properties of additives |

## Source Quality Notes

- **USDA/EFSA/FDA/FAO** — regulatory-grade, generally authoritative but often slower to update
- **CSPI/EWG** — advocacy-grade, more conservative, often diverges from regulatory consensus
- **OpenFoodFacts** — community-grade, broad coverage but variable accuracy
- **PubMed** — research-grade, must read full paper not just abstract
- **News/Web** — use for timeliness (recalls, emerging issues), not for established fact
