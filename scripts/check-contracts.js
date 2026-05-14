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
    let current = null;
    let braceDepth = 0;

    for (const line of content.split("\n")) {
      // Match record declaration with optional positional parameters
      const recordMatch = line.match(/public\s+record\s+(\w+)\s*(?:\(([^)]*)\))?/);
      if (recordMatch) {
        current = recordMatch[1];
        dtos[current] = [];
        braceDepth = 0;

        // Extract positional parameters (e.g. string Role, DateTimeOffset CreatedAt)
        if (recordMatch[2]) {
          const params = recordMatch[2].split(",").map(p => p.trim()).filter(Boolean);
          for (const param of params) {
            const parts = param.split(/\s+/);
            if (parts.length >= 2) {
              const name = parts[parts.length - 1];
              const camel = toCamelCase(name);
              dtos[current].push(camel);
            }
          }
        }
        continue;
      }

      if (current) {
        braceDepth += (line.match(/{/g) || []).length;
        braceDepth -= (line.match(/}/g) || []).length;

        // Match { get; init; } properties
        const propMatch = line.match(
          /public\s+\S+\??\s+(\w+)\s*{\s*get;\s*init;\s*}/,
        );
        if (propMatch) {
          const camel = toCamelCase(propMatch[1]);
          dtos[current].push(camel);
        }

        // Match expression-bodied properties (e.g. public string Id => "...")
        const exprMatch = line.match(/public\s+\S+\??\s+(\w+)\s*=>/);
        if (exprMatch) {
          const camel = toCamelCase(exprMatch[1]);
          dtos[current].push(camel);
        }

        if (braceDepth <= 0 && line.includes("}")) {
          current = null;
        }
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
