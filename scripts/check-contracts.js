#!/usr/bin/env node
/**
 * Contract Checker — verifies backend response shapes match frontend TypeScript
 * interfaces.
 *
 * How it works:
 * 1. Parses frontend/src/types/index.ts to extract all interface field names
 * 2. Parses ALL backend DTO files to extract record field names
 * 3. Cross-references them and reports mismatches
 *
 * Run: node scripts/check-contracts.js
 * Exit code 0 = all match, 1 = mismatches found
 */

const fs = require("fs");
const path = require("path");

const ROOT = path.join(__dirname, "..");

// ─── Parse frontend TypeScript interfaces ────────────────────────────────────
function parseTsInterfaces(filePath) {
  const content = fs.readFileSync(filePath, "utf-8");
  const interfaces = {};
  let current = null;
  let braceDepth = 0;

  for (const line of content.split("\n")) {
    const ifaceMatch = line.match(
      /^export\s+interface\s+(\w+)\s*(?:extends\s+\w+\s*)?{/,
    );
    if (ifaceMatch) {
      current = ifaceMatch[1];
      interfaces[current] = [];
      braceDepth = 1;
      continue;
    }

    if (current) {
      braceDepth += (line.match(/{/g) || []).length;
      braceDepth -= (line.match(/}/g) || []).length;

      if (braceDepth <= 0) {
        current = null;
        continue;
      }

      const fieldMatch = line.match(/^\s+(\w+)\??:\s/);
      if (fieldMatch) {
        interfaces[current].push(fieldMatch[1]);
      }
    }
  }
  return interfaces;
}

// ─── Proper camelCase conversion matching .NET's JsonNamingPolicy.CamelCase ──
function toCamelCase(name) {
  if (!name) return name;
  // Find the run of uppercase letters at the start
  let i = 0;
  while (i < name.length && name[i] === name[i].toUpperCase() && name[i] !== name[i].toLowerCase()) {
    i++;
  }
  if (i === 0) return name;
  if (i === 1) return name[0].toLowerCase() + name.slice(1);
  if (i === name.length) return name.toLowerCase();
  // Multiple uppercase: lowercase all but last (e.g. GICategory -> giCategory)
  return name.slice(0, i - 1).toLowerCase() + name.slice(i - 1);
}

// ─── Parse backend C# DTOs from multiple files ──────────────────────────────
function parseCSharpDtos(dirPath) {
  const dtos = {};
  const files = fs.readdirSync(dirPath).filter((f) => f.endsWith(".cs"));

  for (const file of files) {
    const content = fs.readFileSync(path.join(dirPath, file), "utf-8");
    const declarationPattern =
      /\bpublic\s+(?:(?:sealed|partial|abstract|readonly)\s+)*(?:record|class)\s+(\w+)/g;
    let declarationMatch;

    while ((declarationMatch = declarationPattern.exec(content)) !== null) {
      const dtoName = declarationMatch[1];
      dtos[dtoName] = [];

      let bodyStart = declarationPattern.lastIndex;
      let cursor = bodyStart;
      while (cursor < content.length && /\s/.test(content[cursor])) cursor++;

      // Positional record parameters may span lines and contain attributes whose
      // arguments also use parentheses, so find the matching close explicitly.
      if (content[cursor] === "(") {
        let depth = 0;
        let end = cursor;
        for (; end < content.length; end++) {
          if (content[end] === "(") depth++;
          else if (content[end] === ")" && --depth === 0) break;
        }

        if (end < content.length) {
          const parameters = content.slice(cursor + 1, end);
          const parts = [];
          let start = 0;
          let parenDepth = 0;
          let bracketDepth = 0;
          for (let i = 0; i < parameters.length; i++) {
            if (parameters[i] === "(") parenDepth++;
            else if (parameters[i] === ")") parenDepth--;
            else if (parameters[i] === "[") bracketDepth++;
            else if (parameters[i] === "]") bracketDepth--;
            else if (parameters[i] === "," && parenDepth === 0 && bracketDepth === 0) {
              parts.push(parameters.slice(start, i));
              start = i + 1;
            }
          }
          parts.push(parameters.slice(start));

          for (const parameter of parts) {
            const jsonName = parameter.match(
              /JsonPropertyName\s*\(\s*"([^"]+)"\s*\)/,
            )?.[1];
            const withoutAttributes = parameter.replace(/\[[^\]]*\]/g, "");
            const declaration = withoutAttributes.split("=")[0].trim();
            const name = declaration.match(/([A-Za-z_]\w*)\s*$/)?.[1];
            if (name) dtos[dtoName].push(jsonName || toCamelCase(name));
          }
          bodyStart = end + 1;
        }
      }
      cursor = bodyStart;
      while (cursor < content.length && /\s/.test(content[cursor])) cursor++;
      if (content[cursor] === ";") continue;

      const openingBrace = content.indexOf("{", bodyStart);
      if (openingBrace < 0) continue;
      let depth = 0;
      let closingBrace = openingBrace;
      for (; closingBrace < content.length; closingBrace++) {
        if (content[closingBrace] === "{") depth++;
        else if (content[closingBrace] === "}" && --depth === 0) break;
      }
      if (closingBrace >= content.length) continue;

      const body = content.slice(openingBrace + 1, closingBrace);
      const propertyPattern =
        /((?:\s*\[[^\]]+\]\s*)*)\s*public\s+(?:required\s+)?[\w?.<>,\[\]]+\s+(\w+)\s*(?=\{\s*get;|=>)/g;
      let propertyMatch;
      while ((propertyMatch = propertyPattern.exec(body)) !== null) {
        const jsonName = propertyMatch[1].match(
          /JsonPropertyName\s*\(\s*"([^"]+)"\s*\)/,
        )?.[1];
        dtos[dtoName].push(jsonName || toCamelCase(propertyMatch[2]));
      }
    }
  }
  return dtos;
}

// ─── Mapping from frontend interface → backend DTO ───────────────────────────
const INTERFACE_TO_DTO = {
  UserProfile: "UserProfileDto",
  AuthResponse: "AuthResponse",
  MealLog: "MealLogDto",
  MealItem: "MealItemDto",
  FoodProduct: "FoodProductDto",
  FoodAdditive: "FoodAdditiveDto",
  ParsedFoodItem: "ParsedFoodItemDto",
  Correlation: "CorrelationDto",
  GutRiskAssessment: "GutRiskAssessmentDto",
  GutRiskFlag: "GutRiskFlagDto",
  FodmapAssessment: "FodmapAssessmentDto",
  FodmapTrigger: "FodmapTriggerDto",
  SubstitutionResult: "SubstitutionResultDto",
  Substitution: "SubstitutionDto",
  GlycemicAssessment: "GlycemicAssessmentDto",
  GlycemicMatch: "GlycemicMatchDto",
  PersonalizedScore: "PersonalizedScoreDto",
  ScoreExplanation: "ScoreExplanationDto",
  FoodDiaryAnalysis: "FoodDiaryAnalysisDto",
  FoodSymptomPattern: "FoodSymptomPatternDto",
  TimingInsight: "TimingInsightDto",
  EliminationDietStatus: "EliminationDietStatusDto",
  ReintroductionResult: "ReintroductionResultDto",
  DailyNutritionSummary: "DailyNutritionSummaryDto",
  SymptomLog: "SymptomLogDto",
  SymptomType: "SymptomTypeDto",
  ChatMessage: "ChatHistoryMessage",
  GroundingCandidate: "GroundingCandidateDto",
  GroundingAttempt: "GroundingAttemptDto",
  MealDraftItem: "MealDraftItemDto",
  MealDraft: "MealDraftDto",
  MealDraftTotals: "MealDraftTotalsDto",
  MealDraftCommitItem: "MealDraftCommitItem",
  MealDraftCommitRequest: "MealDraftCommitRequest",
  MealDraftUpdateRequest: "MealDraftUpdateRequest",
  MealDraftCommitResult: "MealDraftCommitResult",
  NutritionPer100g: "NutritionPer100gDto",
  NutritionAmounts: "NutritionAmountsDto",
  DescribedFoodComponent: "DescribedFoodComponentDto",
  NutritionTargets: "NutritionTargetsDto",
  NutritionBudget: "NutritionBudgetDto",
  MealSuggestionRequest: "MealSuggestionRequest",
  MealSuggestion: "MealSuggestionDto",
  MealSuggestionResult: "MealSuggestionResultDto",
  MealSuggestionStatus: "MealSuggestionStatusDto",
};

// Additional directories to scan for backend DTO records (e.g. interfaces file)
const EXTRA_DTO_DIRS = [
  path.join(ROOT, "backend/src/GutAI.Application/Common/Interfaces"),
];

// Known intentional mismatches:
// - Endpoint transforms DTO field names (e.g. UsRegulatoryStatus → usStatus)
// - Backend-only fields not exposed to frontend
// - Frontend uses a subset of backend DTO fields
const KNOWN_EXCEPTIONS = {
  MealLog: ["userId", "photoUrl", "originalText"],
  MealItem: ["cholesterolMg", "saturatedFatG", "potassiumMg"],
  FoodProduct: ["additivesTags", "nutritionInfo", "isDeleted", "imageFrontUrl", "imageIngredientsUrl", "imageNutritionUrl"],
  FoodAdditive: [
    "usStatus", "euStatus",                     // frontend names (endpoint maps from usRegulatoryStatus)
    "usRegulatoryStatus", "euRegulatoryStatus",  // backend DTO names
    "efsaLastReviewDate", "epaCancerClass", "fdaAdverseEventCount", "fdaRecallCount", "lastUpdated",
  ],
  ParsedFoodItem: ["servingSize", "servingQuantity"],
  SymptomLog: ["duration"],
};

// ─── Main ────────────────────────────────────────────────────────────────────
function main() {
  const tsPath = path.join(ROOT, "frontend/src/types/index.ts");
  const dtoDirPath = path.join(
    ROOT,
    "backend/src/GutAI.Application/Common/DTOs",
  );

  if (!fs.existsSync(tsPath)) {
    console.error("❌ Frontend types file not found:", tsPath);
    process.exit(1);
  }
  if (!fs.existsSync(dtoDirPath)) {
    console.error("❌ Backend DTOs directory not found:", dtoDirPath);
    process.exit(1);
  }

  const tsInterfaces = parseTsInterfaces(tsPath);
  const csDtos = parseCSharpDtos(dtoDirPath);

  // Merge DTOs from extra directories (e.g. Interfaces file with records)
  for (const extraDir of EXTRA_DTO_DIRS) {
    if (fs.existsSync(extraDir)) {
      const extraDtos = parseCSharpDtos(extraDir);
      Object.assign(csDtos, extraDtos);
    }
  }

  let errors = 0;
  let checked = 0;

  for (const [tsName, dtoName] of Object.entries(INTERFACE_TO_DTO)) {
    const tsFields = tsInterfaces[tsName];
    const dtoFields = csDtos[dtoName];

    if (!tsFields) {
      console.warn(`⚠️  Frontend interface '${tsName}' not found`);
      continue;
    }
    if (!dtoFields) {
      console.warn(`⚠️  Backend DTO '${dtoName}' not found`);
      continue;
    }

    checked++;
    const exceptions = KNOWN_EXCEPTIONS[tsName] || [];

    // Check frontend fields exist in backend
    for (const field of tsFields) {
      if (exceptions.includes(field)) continue;
      if (!dtoFields.includes(field)) {
        console.error(
          `❌ ${tsName}.${field} exists in frontend but not in ${dtoName}`,
        );
        errors++;
      }
    }

    // Check backend fields exist in frontend
    for (const field of dtoFields) {
      if (exceptions.includes(field)) continue;
      if (!tsFields.includes(field)) {
        console.error(
          `❌ ${dtoName}.${field} exists in backend but not in ${tsName}`,
        );
        errors++;
      }
    }
  }

  console.log(`\n✅ Checked ${checked} interface↔DTO pairs`);

  if (errors > 0) {
    console.error(`\n❌ ${errors} contract mismatch(es) found!`);
    process.exit(1);
  } else {
    console.log("✅ All frontend↔backend contracts match!");
    process.exit(0);
  }
}

main();
