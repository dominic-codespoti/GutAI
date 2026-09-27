#!/usr/bin/env bash
set -uo pipefail

usage() {
  echo "Usage: $0 {all|photo|agents}" >&2
}

mode=${1:-}
case "$mode" in
  all|photo|agents) ;;
  *) usage; exit 2 ;;
esac

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
cd "$repo_root"

if ! command -v node >/dev/null 2>&1; then
  echo "Error: node is required to load evaluation configuration." >&2
  exit 2
fi

config_lines=$(node - "$repo_root" <<'NODE'
const fs = require('fs');
const path = require('path');
const root = process.argv[2];
const basePath = path.join(root, 'backend/src/GutAI.Api/appsettings.json');
const devPath = path.join(root, 'backend/src/GutAI.Api/appsettings.Development.json');
try {
  const base = JSON.parse(fs.readFileSync(basePath, 'utf8')).AzureOpenAI || {};
  const dev = JSON.parse(fs.readFileSync(devPath, 'utf8')).AzureOpenAI || {};
  function merge(a, b) {
    const out = { ...a };
    for (const [key, value] of Object.entries(b)) {
      if (value && typeof value === 'object' && !Array.isArray(value) &&
          out[key] && typeof out[key] === 'object' && !Array.isArray(out[key])) {
        out[key] = merge(out[key], value);
      } else out[key] = value;
    }
    return out;
  }
  function flatten(value, keys = []) {
    if (value === null || value === undefined) return [];
    if (Array.isArray(value)) return [[`AzureOpenAI__${keys.join('__')}`, JSON.stringify(value)]];
    if (typeof value === 'object') {
      const entries = Object.entries(value);
      if (!entries.length) return [];
      return entries.flatMap(([key, child]) => flatten(child, [...keys, key]));
    }
    return [[`AzureOpenAI__${keys.join('__')}`, String(value)]];
  }
  for (const [key, value] of flatten(merge(base, dev))) console.log(`${key}=${value}`);
} catch (error) {
  console.error(`Error loading AzureOpenAI appsettings: ${error.message}`);
  process.exit(2);
}
NODE
) || { echo "Error: unable to load AzureOpenAI configuration." >&2; exit 2; }
extra_env=()
set_env_value() {
  local key=$1 value=$2 index
  for index in "${!extra_env[@]}"; do
    if [[ "${extra_env[index]%%=*}" == "$key" ]]; then
      extra_env[index]="$key=$value"
      return
    fi
  done
  extra_env+=("$key=$value")
}
while IFS='=' read -r key value; do
  [[ -z "$key" ]] && continue
  if [[ "$key" =~ ^[A-Za-z_][A-Za-z0-9_]*$ ]]; then
    if [[ ! -v "$key" ]]; then export "$key=$value"; fi
  elif ! printenv "$key" >/dev/null 2>&1; then
    set_env_value "$key" "$value"
  fi
done <<< "$config_lines"

if [[ -n "${AZURE_OPENAI_ENDPOINT:-}" ]]; then
  export AzureOpenAI__Endpoint="$AZURE_OPENAI_ENDPOINT"
fi
if [[ -z "${AzureOpenAI__Endpoint:-}" ]]; then
  echo "Error: AzureOpenAI endpoint is not configured (AzureOpenAI__Endpoint)." >&2
  exit 2
fi

deployment=${AzureOpenAI__Workloads__vision__Deployment:-${AzureOpenAI__DeploymentName:-}}
if [[ -z "$deployment" ]]; then
  echo "Error: neither AzureOpenAI__Workloads__vision__Deployment nor AzureOpenAI__DeploymentName is configured." >&2
  exit 2
fi
input_price=${EVAL_INPUT_PER_1M:-0.20}
output_price=${EVAL_OUTPUT_PER_1M:-1.20}
input_price_key="AzureOpenAI__Pricing__${deployment}__InputPer1M"
output_price_key="AzureOpenAI__Pricing__${deployment}__OutputPer1M"
if printenv "$input_price_key" >/dev/null 2>&1; then :; else set_env_value "$input_price_key" "$input_price"; fi
if printenv "$output_price_key" >/dev/null 2>&1; then :; else set_env_value "$output_price_key" "$output_price"; fi

if ! az account show >/dev/null 2>&1; then
  echo "Error: Azure CLI is not logged in; run az login." >&2
  exit 2
fi
if [[ "$mode" == all || "$mode" == agents ]]; then
  if ! command -v docker >/dev/null 2>&1; then
    echo "Error: Docker is required for agent evaluations." >&2
    exit 2
  fi
fi

stamp=$(date -u +%Y%m%dT%H%M%SZ)
report_dir="$repo_root/eval-reports/$stamp"
mkdir -p "$report_dir" || { echo "Error: cannot create $report_dir" >&2; exit 2; }
tmp_dir=
azurite_started=0
cleanup() {
  if (( azurite_started )); then docker stop gutai-evals-azurite >/dev/null 2>&1 || true; fi
  if [[ -n "$tmp_dir" ]]; then rm -rf "$tmp_dir"; fi
}
trap cleanup EXIT

photo_status=0
agent_status=0
ran_photo=0
ran_agents=0
if [[ "$mode" == all || "$mode" == photo ]]; then
  ran_photo=1
  if [[ ! -d "$repo_root/golden-images" ]]; then
    echo "Error: golden-images directory was not found." >&2
    photo_status=2
  else
    tmp_dir=$(mktemp -d) || { echo "Error: could not create temporary image directory." >&2; photo_status=2; }
    if (( photo_status == 0 )); then
      cp -a "$repo_root/golden-images/." "$tmp_dir/" || { echo "Error: could not copy golden-images." >&2; photo_status=2; }
    fi
    if (( photo_status == 0 )); then
      photo_project="backend/tools/GoldenScanHarness/GutAI.GoldenScanHarness.csproj"
      photo_assembly="$repo_root/backend/tools/GoldenScanHarness/bin/Release/net10.0/GutAI.GoldenScanHarness.dll"
      echo "Building photo-scan harness..."
      dotnet build "$photo_project" -c Release --verbosity quiet 2>&1 | tee "$report_dir/golden.log"
      photo_status=${PIPESTATUS[0]}
      if (( photo_status != 0 )); then
        echo "Error: photo-scan harness build failed." | tee -a "$report_dir/golden.log" >&2
        photo_status=2
      elif [[ ! -f "$photo_assembly" ]]; then
        echo "Error: built photo-scan assembly not found at $photo_assembly." | tee -a "$report_dir/golden.log" >&2
        photo_status=2
      else
        echo "Running photo-scan gate (report: $report_dir/golden-report.json)"
        env "${extra_env[@]}" dotnet "$photo_assembly" \
          --images "$tmp_dir" --mode in-process --refresh --gate \
          --report "$report_dir/golden-report.json" 2>&1 | tee -a "$report_dir/golden.log"
        photo_status=${PIPESTATUS[0]}
      fi
    fi
  fi
fi

if [[ "$mode" == all || "$mode" == agents ]]; then
  ran_agents=1
  if ! (echo >/dev/tcp/127.0.0.1/10002) >/dev/null 2>&1; then
    echo "Starting temporary Azurite container..."
    if docker run -d --rm --name gutai-evals-azurite \
      -p 127.0.0.1:10000:10000 -p 127.0.0.1:10001:10001 -p 127.0.0.1:10002:10002 \
      mcr.microsoft.com/azure-storage/azurite >/dev/null; then
      azurite_started=1
      listening=0
      for _ in {1..30}; do
        if (echo >/dev/tcp/127.0.0.1/10002) >/dev/null 2>&1; then listening=1; break; fi
        sleep 1
      done
      if (( ! listening )); then
        echo "Error: Azurite did not start listening on 127.0.0.1:10002 within 30 seconds." >&2
        agent_status=2
      fi
    else
      echo "Error: failed to start Azurite." >&2
      agent_status=2
    fi
  fi
  if (( agent_status == 0 )); then
    agent_project="tools/AgentEvalHarness/GutAI.AgentEvalHarness.csproj"
    agent_assembly="$repo_root/backend/tools/AgentEvalHarness/bin/Release/net10.0/GutAI.AgentEvalHarness.dll"
    echo "Building agent evaluation harness..."
    (cd "$repo_root/backend" && dotnet build "$agent_project" -c Release --verbosity quiet) \
      2>&1 | tee "$report_dir/agents.log"
    agent_status=${PIPESTATUS[0]}
    if (( agent_status != 0 )); then
      echo "Error: agent evaluation harness build failed." | tee -a "$report_dir/agents.log" >&2
      agent_status=2
    elif [[ ! -f "$agent_assembly" ]]; then
      echo "Error: built agent evaluation assembly not found at $agent_assembly." | tee -a "$report_dir/agents.log" >&2
      agent_status=2
    else
      echo "Running agent evaluations (report: $report_dir/agent-eval-report.json)"
      (cd "$repo_root/backend" && \
        GUTAI_EVAL_STORAGE="${GUTAI_EVAL_STORAGE:-UseDevelopmentStorage=true}" \
        env "${extra_env[@]}" dotnet "$agent_assembly" \
          --suite "${EVAL_SUITE:-all}" --repeat "${EVAL_REPEAT:-1}" --gate \
          --root tools/AgentEvalHarness --report "$report_dir/agent-eval-report.json") \
        2>&1 | tee -a "$report_dir/agents.log"
      agent_status=${PIPESTATUS[0]}
    fi
  fi
fi

node - "$report_dir" "$ran_photo" "$ran_agents" <<'NODE'
const fs = require('fs');
const path = require('path');
const dir = process.argv[2];
const photo = process.argv[3] === '1';
const agents = process.argv[4] === '1';
function report(name) {
  const file = path.join(dir, name);
  if (!fs.existsSync(file)) return null;
  try { return JSON.parse(fs.readFileSync(file, 'utf8')); }
  catch (error) { console.log(`  Unable to read ${file}: ${error.message}`); return null; }
}
if (photo) {
  const data = report('golden-report.json');
  console.log('\nPhoto-scan gate:');
  if (!data) console.log(`  Report missing; see ${path.join(dir, 'golden.log')}`);
  else {
    const gate = data.gate || {};
    const failures = gate.failed || gate.failures || [];
    const notEvaluated = gate.notEvaluated || [];
    const status = gate.passed === true ? 'PASS' : gate.passed === false ? 'FAIL' : 'UNKNOWN';
    console.log(`  ${status}; passed: ${gate.passed ?? 'n/a'}; failed: ${Array.isArray(failures) ? failures.length : failures}; notEvaluated: ${Array.isArray(notEvaluated) ? notEvaluated.length : notEvaluated}`);
    if (Array.isArray(failures) && failures.length) console.log(`  failures: ${failures.map(f => typeof f === 'string' ? f : `${f.name}=${f.actual} (threshold ${f.threshold})`).join(', ')}`);
    const usage = data.usage || {};
    console.log(`  model calls: ${usage.modelCalls ?? 'n/a'}; estimated cost USD: ${usage.estimatedCostUsd ?? 'n/a'}`);
  }
}
if (agents) {
  const data = report('agent-eval-report.json');
  console.log('\nAgent evaluation suites:');
  if (!data) console.log(`  Report missing; see ${path.join(dir, 'agents.log')}`);
  else {
    for (const [name, suite] of Object.entries(data.suites || {})) {
      let input = 0, output = 0;
      for (const item of suite.cases || []) {
        input += Number(item.inputTokens || 0); output += Number(item.outputTokens || 0);
        for (const turn of item.turns || []) {
          input += Number(turn.inputTokens || 0); output += Number(turn.outputTokens || 0);
        }
      }
      const failures = suite.gateFailures || [];
      console.log(`  ${name}: ${suite.gatePassed === true ? 'PASS' : 'FAIL'}; gatePassed=${suite.gatePassed}; gateFailures=${failures.length ? failures.join(', ') : 'none'}; inputTokens=${input}; outputTokens=${output}`);
    }
  }
}
console.log(`\nReports: ${dir}`);
NODE
summary_status=$?
if (( summary_status != 0 )); then echo "Warning: report summary could not be generated." >&2; fi
if (( photo_status == 2 || agent_status == 2 )); then exit 2; fi
if (( photo_status == 1 || agent_status == 1 )); then exit 1; fi
exit 0
