---
name: food-research
description: |
  Reusable workflow for researching food products, ingredients, and additives
  using GutAI's internal data sources, public APIs, and web search.
type: skill
---

# Food Research

A structured workflow for gathering external evidence and producing holistic analysis of food products, ingredients, and additives.

---

## When to Use

- You need a holistic analysis of an additive's safety profile
- You want to compare US vs EU regulatory status of an ingredient
- A user asks about a food safety concern that requires external research
- You're investigating a recall, contamination event, or emerging safety issue
- You need to validate whether GutAI's data on an ingredient is current
- You're considering adding a new data source and need to understand the landscape

## When NOT to Use

- The question is about a score computation (delegate to scoring investigation skill)
- The user wants a simple product lookup that GutAI already handles
- The question is about personal dietary advice
- The research is purely about GutAI's internal code structure

---

## Workflow

### Step 1: Clarify the Research Question

What aspect of the food/ingredient is being asked about?

| Question Type | Sources to Prioritize |
|--------------|----------------------|
| Safety / toxicity | FDA, EFSA, PubMed, CSPI |
| Regulatory status | FDA GRAS, EFSA, country-specific regulators |
| Recalls / contamination | FDA enforcement, FSIS, news |
| Nutrition / health claims | USDA, PubMed, authoritative health orgs |
| General background | Wikipedia, Open Food Facts, consumer guides |
| Market availability | Open Food Facts, USDA branded foods |
| Cross-jurisdiction comparison | EFSA vs FDA GRAS vs CODEX |

### Step 2: Search GutAI's Internal Data

Query the available endpoints first — these return GutAI's current understanding:

- `GET /api/food/search?q=<term>` — general food product search
- `GET /api/food/additives` — full additive catalog
- `GET /api/food/additives/{id}` — single additive detail
- `GET /api/food/{id}/safety-report` — GutAI's full safety analysis
- `GET /api/food/{id}/gut-risk` — GutAI's gut risk assessment

### Step 3: Search the Web

Use web search for external sources. Relevant sites by category:

**Regulatory:**
- fda.gov — FDA recalls, safety alerts, GRAS notices
- efsa.europa.eu — EFSA scientific opinions on additives
- fao.org — CODEX Alimentarius standards
- ec.europa.eu — EU food additives database

**Research:**
- pubmed.ncbi.nlm.nih.gov — scientific literature
- cochrane.org — systematic reviews

**Advocacy / Consumer:**
- cspinet.org — Center for Science in the Public Interest
- ewg.org — Environmental Working Group (food scores)
- consumerreports.org — Consumer Reports food safety

**Data:**
- world.openfoodfacts.org — Open Food Facts product data
- fdc.nal.usda.gov — USDA FoodData Central

**General:**
- en.wikipedia.org — background on ingredients

### Step 4: Synthesize Findings

When producing the analysis:

- Separate established consensus from emerging concerns
- Distinguish between hazard (potential) and risk (real-world exposure)
- Note when sources disagree and why
- Identify regulatory divergences (EU vs US is particularly relevant for GutAI)
- Flag when GutAI's internal data is outdated or contradicts external sources
- Note gaps in GutAI's coverage that could be filled

### Step 5: Produce the Report

Return findings in this structure:

```text
## Subject
<what was researched>

## Sources Searched
- <source> — <what was found or not found>

## Internal Data Summary
<what GutAI's own data shows>

## External Evidence Summary
<what web sources show, organized by source>

## Regulatory Landscape
- <jurisdiction>: <status>

## Safety Profile
<consensus concerns, disagreements, uncertainties>

## Research Gaps
<what data or evidence was missing or inconclusive>

## Key Takeaways
- <finding 1>
- <finding 2>
- <finding 3>

## Caveats
<limitations, dated evidence, conflicts>
```

---

## Reference Files

- `references/sources-map.md` — detailed source descriptions, endpoints, coverage
- `references/research-patterns.md` — common research topics and strategies by food category
