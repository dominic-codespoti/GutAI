#!/usr/bin/env bash
set -euo pipefail

# ──────────────────────────────────────────────────────────────────────────────
# GutAI Seed Script — Seeds 30 days of realistic meal and symptom data
# for a test account to exercise the correlation engine.
#
# Usage:  ./scripts/seed-data.sh [email] [password]
#   Defaults: seed-demo@test.com / Test123!
#
# The script will:
#   1. Register or login the account
#   2. Complete onboarding
#   3. Seed 30 days of meals (3-4 per day) with known trigger foods
#   4. Seed realistic symptom patterns correlated with trigger foods
#   5. Verify the insights/correlations endpoint returns data
# ──────────────────────────────────────────────────────────────────────────────

API="http://localhost:5000"
EMAIL="${1:-seed-demo@test.com}"
PASSWORD="${2:-Test123!}"
DISPLAY_NAME="Demo User"

echo "🌱 GutAI Seed Script"
echo "   API: $API"
echo "   Account: $EMAIL"
echo ""

# ── Auth ─────────────────────────────────────────────────────────────────────

get_token() {
  # Try login first, then register
  local resp
  resp=$(curl -sf "$API/api/auth/login" \
    -H "Content-Type: application/json" \
    -d "{\"email\":\"$EMAIL\",\"password\":\"$PASSWORD\"}" 2>/dev/null) || true

  if [ -z "$resp" ]; then
    echo "   Registering new account..."
    resp=$(curl -sf "$API/api/auth/register" \
      -H "Content-Type: application/json" \
      -d "{\"email\":\"$EMAIL\",\"password\":\"$PASSWORD\",\"displayName\":\"$DISPLAY_NAME\"}")
  else
    echo "   Logged into existing account."
  fi

  TOKEN=$(echo "$resp" | python3 -c "import sys,json; print(json.load(sys.stdin)['accessToken'])")
  USER_ID=$(echo "$resp" | python3 -c "import sys,json; print(json.load(sys.stdin)['user']['id'])")
  echo "   User ID: $USER_ID"
}

echo "🔐 Authenticating..."
get_token
echo "   ✅ Token acquired"
echo ""

AUTH="Authorization: Bearer $TOKEN"

# ── Complete onboarding ──────────────────────────────────────────────────────

echo "📋 Completing onboarding..."
curl -sf "$API/api/user/profile" -X PUT \
  -H "Content-Type: application/json" -H "$AUTH" \
  -d '{"onboardingCompleted":true,"allergies":["gluten"],"dietaryPreferences":["low-fodmap"]}' > /dev/null
echo "   ✅ Onboarding done"
echo ""

# ── Helper: post a meal ──────────────────────────────────────────────────────

post_meal() {
  local meal_type="$1"
  local logged_at="$2"
  shift 2
  local items="$*"

  local body="{\"mealType\":\"$meal_type\",\"loggedAt\":\"$logged_at\",\"items\":[$items]}"

  local resp
  resp=$(curl -sf "$API/api/meals" -X POST \
    -H "Content-Type: application/json" -H "$AUTH" \
    -d "$body" 2>/dev/null) || { echo "   ⚠️  Failed to create meal at $logged_at"; return 1; }

  local meal_id
  meal_id=$(echo "$resp" | python3 -c "import sys,json; print(json.load(sys.stdin)['id'])" 2>/dev/null) || true
  echo "$meal_id"
}

# ── Helper: post a symptom ───────────────────────────────────────────────────

post_symptom() {
  local type_id="$1"
  local severity="$2"
  local occurred_at="$3"
  local notes="${4:-}"
  local meal_id="${5:-}"

  local body="{\"symptomTypeId\":$type_id,\"severity\":$severity,\"occurredAt\":\"$occurred_at\""
  [ -n "$notes" ] && body="$body,\"notes\":\"$notes\""
  [ -n "$meal_id" ] && body="$body,\"relatedMealLogId\":\"$meal_id\""
  body="$body}"

  curl -sf "$API/api/symptoms" -X POST \
    -H "Content-Type: application/json" -H "$AUTH" \
    -d "$body" > /dev/null 2>&1 || echo "   ⚠️  Failed symptom at $occurred_at"
}

# ── Meal item builders ───────────────────────────────────────────────────────
# Each function outputs a JSON meal item string

item() {
  local name="$1" cal="$2" prot="$3" carb="$4" fat="$5" fiber="${6:-0}" sugar="${7:-0}" sodium="${8:-0}" servings="${9:-1}" unit="${10:-serving}" weight="${11:-}"
  local json="{\"foodName\":\"$name\",\"servings\":$servings,\"servingUnit\":\"$unit\",\"calories\":$cal,\"proteinG\":$prot,\"carbsG\":$carb,\"fatG\":$fat,\"fiberG\":$fiber,\"sugarG\":$sugar,\"sodiumMg\":$sodium"
  [ -n "$weight" ] && json="$json,\"servingWeightG\":$weight"
  json="$json}"
  echo "$json"
}

# ── Food definitions ─────────────────────────────────────────────────────────
# TRIGGER FOODS (will be correlated with symptoms)
SPICY_CURRY=$(item "Spicy Thai Curry" 650 28 45 38 4 6 1200 1 serving 350)
GREASY_PIZZA=$(item "Pepperoni Pizza (3 slices)" 900 36 84 42 3 9 2100 3 slice 300)
FRIED_CHICKEN=$(item "Fried Chicken Wings" 720 48 24 48 1 0 1800 6 piece 360)
BEER=$(item "Beer (IPA)" 250 2 20 0 0 0 14 2 pint 568)
ICE_CREAM=$(item "Ice Cream (Chocolate)" 380 6 44 20 2 36 120 2 scoop 200)
ENERGY_DRINK=$(item "Energy Drink" 220 0 56 0 0 54 200 1 can 500)
INSTANT_RAMEN=$(item "Instant Ramen" 550 12 72 22 2 4 2200 1 serving 400)
HOT_SAUCE_WINGS=$(item "Buffalo Hot Wings" 680 52 8 48 1 1 2400 8 piece 400)
SODA=$(item "Cola" 140 0 39 0 0 39 45 1 can 355)
PROCESSED_MEAT=$(item "Salami Sandwich" 480 22 38 26 2 4 1600 1 sandwich 250)

# SAFE FOODS (rarely trigger symptoms)
OATMEAL=$(item "Oatmeal with Banana" 320 10 58 6 6 12 5 1 bowl 350)
GRILLED_CHICKEN=$(item "Grilled Chicken Breast" 280 52 0 6 0 0 140 1 serving 200)
BROWN_RICE=$(item "Brown Rice" 220 5 46 2 4 0 10 1 cup 195)
STEAMED_VEGGIES=$(item "Steamed Broccoli & Carrots" 80 4 16 1 6 4 60 1 serving 200)
SALMON=$(item "Grilled Salmon" 360 40 0 22 0 0 80 1 fillet 200)
SWEET_POTATO=$(item "Baked Sweet Potato" 180 4 42 0 6 12 70 1 medium 200)
GREEK_YOGURT=$(item "Greek Yogurt with Berries" 180 18 20 4 2 14 60 1 cup 250)
QUINOA_BOWL=$(item "Quinoa & Vegetable Bowl" 380 14 52 12 8 4 200 1 bowl 400)
GREEN_SMOOTHIE=$(item "Green Smoothie" 220 8 38 4 6 18 40 1 glass 400)
EGGS_TOAST=$(item "Scrambled Eggs on Toast" 350 22 28 16 2 2 480 1 serving 250)
CHICKEN_SOUP=$(item "Chicken & Vegetable Soup" 240 18 22 8 4 4 680 1 bowl 400)
TUNA_SALAD=$(item "Tuna Salad" 280 32 8 14 3 2 520 1 serving 250)
BANANA=$(item "Banana" 105 1 27 0 3 14 1 1 medium 120)
APPLE=$(item "Apple" 95 0 25 0 4 19 2 1 medium 180)
RICE_BOWL=$(item "Chicken Rice Bowl" 480 35 52 12 3 2 600 1 bowl 400)

echo "🍽️  Seeding 30 days of meals and symptoms..."
echo ""

MEAL_COUNT=0
SYMPTOM_COUNT=0

# ── Main loop: 30 days ───────────────────────────────────────────────────────

for DAY_OFFSET in $(seq 30 -1 0); do
  DATE=$(date -d "-${DAY_OFFSET} days" +%Y-%m-%d 2>/dev/null || date -v-${DAY_OFFSET}d +%Y-%m-%d)
  DOW=$(date -d "$DATE" +%u 2>/dev/null || date -j -f "%Y-%m-%d" "$DATE" +%u)

  echo -n "  📅 $DATE: "

  # ── BREAKFAST (always safe) ──────────────────────────────────────────────
  BREAKFAST_TIME="${DATE}T07:30:00Z"
  case $((DAY_OFFSET % 4)) in
    0) post_meal "Breakfast" "$BREAKFAST_TIME" "$OATMEAL" > /dev/null ;;
    1) post_meal "Breakfast" "$BREAKFAST_TIME" "$EGGS_TOAST" > /dev/null ;;
    2) post_meal "Breakfast" "$BREAKFAST_TIME" "$GREEK_YOGURT,$BANANA" > /dev/null ;;
    3) post_meal "Breakfast" "$BREAKFAST_TIME" "$GREEN_SMOOTHIE" > /dev/null ;;
  esac
  MEAL_COUNT=$((MEAL_COUNT + 1))

  # ── LUNCH ────────────────────────────────────────────────────────────────
  LUNCH_TIME="${DATE}T12:30:00Z"

  # Every 3rd day: trigger food at lunch
  if [ $((DAY_OFFSET % 3)) -eq 0 ]; then
    case $((DAY_OFFSET % 12)) in
      0) LUNCH_MEAL_ID=$(post_meal "Lunch" "$LUNCH_TIME" "$SPICY_CURRY,$BROWN_RICE") ;;
      3) LUNCH_MEAL_ID=$(post_meal "Lunch" "$LUNCH_TIME" "$INSTANT_RAMEN,$SODA") ;;
      6) LUNCH_MEAL_ID=$(post_meal "Lunch" "$LUNCH_TIME" "$PROCESSED_MEAT,$ENERGY_DRINK") ;;
      9) LUNCH_MEAL_ID=$(post_meal "Lunch" "$LUNCH_TIME" "$GREASY_PIZZA") ;;
    esac

    # Symptoms 2-6 hours after trigger lunch
    SYMPTOM_HOUR=$((14 + RANDOM % 4))
    SYMPTOM_TIME="${DATE}T${SYMPTOM_HOUR}:$(printf '%02d' $((RANDOM % 60))):00Z"

    case $((DAY_OFFSET % 12)) in
      0)
        post_symptom 1 $((5 + RANDOM % 4)) "$SYMPTOM_TIME" "After spicy curry" "$LUNCH_MEAL_ID"
        post_symptom 6 $((4 + RANDOM % 5)) "$SYMPTOM_TIME" "Heartburn from curry" "$LUNCH_MEAL_ID"
        SYMPTOM_COUNT=$((SYMPTOM_COUNT + 2))
        ;;
      3)
        post_symptom 1 $((4 + RANDOM % 4)) "$SYMPTOM_TIME" "After instant ramen" "$LUNCH_MEAL_ID"
        post_symptom 8 $((3 + RANDOM % 5)) "$SYMPTOM_TIME" "Stomach pain from ramen" "$LUNCH_MEAL_ID"
        SYMPTOM_COUNT=$((SYMPTOM_COUNT + 2))
        ;;
      6)
        post_symptom 7 $((3 + RANDOM % 4)) "$SYMPTOM_TIME" "Nausea after processed food" "$LUNCH_MEAL_ID"
        post_symptom 19 $((5 + RANDOM % 4)) "$SYMPTOM_TIME" "Energy crash" "$LUNCH_MEAL_ID"
        SYMPTOM_COUNT=$((SYMPTOM_COUNT + 2))
        ;;
      9)
        post_symptom 1 $((6 + RANDOM % 4)) "$SYMPTOM_TIME" "Pizza bloat" "$LUNCH_MEAL_ID"
        post_symptom 6 $((5 + RANDOM % 5)) "$SYMPTOM_TIME" "Acid reflux from pizza" "$LUNCH_MEAL_ID"
        post_symptom 18 $((4 + RANDOM % 4)) "$SYMPTOM_TIME" "Feeling sluggish" "$LUNCH_MEAL_ID"
        SYMPTOM_COUNT=$((SYMPTOM_COUNT + 3))
        ;;
    esac
  else
    # Safe lunch
    case $((DAY_OFFSET % 5)) in
      0) post_meal "Lunch" "$LUNCH_TIME" "$GRILLED_CHICKEN,$BROWN_RICE,$STEAMED_VEGGIES" > /dev/null ;;
      1) post_meal "Lunch" "$LUNCH_TIME" "$CHICKEN_SOUP" > /dev/null ;;
      2) post_meal "Lunch" "$LUNCH_TIME" "$TUNA_SALAD,$APPLE" > /dev/null ;;
      3) post_meal "Lunch" "$LUNCH_TIME" "$QUINOA_BOWL" > /dev/null ;;
      4) post_meal "Lunch" "$LUNCH_TIME" "$RICE_BOWL,$STEAMED_VEGGIES" > /dev/null ;;
    esac
  fi
  MEAL_COUNT=$((MEAL_COUNT + 1))

  # ── DINNER ───────────────────────────────────────────────────────────────
  DINNER_TIME="${DATE}T19:00:00Z"

  # Weekends (Fri/Sat = 5,6) and every 4th day: trigger dinner
  if [ "$DOW" -ge 5 ] || [ $((DAY_OFFSET % 4)) -eq 1 ]; then
    case $((DAY_OFFSET % 8)) in
      0|4) DINNER_MEAL_ID=$(post_meal "Dinner" "$DINNER_TIME" "$FRIED_CHICKEN,$BEER") ;;
      1|5) DINNER_MEAL_ID=$(post_meal "Dinner" "$DINNER_TIME" "$GREASY_PIZZA,$BEER,$SODA") ;;
      2|6) DINNER_MEAL_ID=$(post_meal "Dinner" "$DINNER_TIME" "$HOT_SAUCE_WINGS,$BEER") ;;
      3|7) DINNER_MEAL_ID=$(post_meal "Dinner" "$DINNER_TIME" "$SPICY_CURRY,$BEER") ;;
    esac

    # Symptoms 1-4 hours after trigger dinner
    SYMPTOM_HOUR=$((20 + RANDOM % 3))
    SYMPTOM_TIME="${DATE}T${SYMPTOM_HOUR}:$(printf '%02d' $((RANDOM % 60))):00Z"

    case $((DAY_OFFSET % 8)) in
      0|4)
        post_symptom 1 $((5 + RANDOM % 5)) "$SYMPTOM_TIME" "Bloated after fried food" "$DINNER_MEAL_ID"
        post_symptom 2 $((4 + RANDOM % 5)) "$SYMPTOM_TIME" "Gas from fried chicken" "$DINNER_MEAL_ID"
        post_symptom 11 $((3 + RANDOM % 4)) "$SYMPTOM_TIME" "Headache from beer" "$DINNER_MEAL_ID"
        SYMPTOM_COUNT=$((SYMPTOM_COUNT + 3))
        ;;
      1|5)
        post_symptom 1 $((6 + RANDOM % 4)) "$SYMPTOM_TIME" "Major bloating after pizza+beer" "$DINNER_MEAL_ID"
        post_symptom 6 $((5 + RANDOM % 5)) "$SYMPTOM_TIME" "Acid reflux" "$DINNER_MEAL_ID"
        post_symptom 18 $((5 + RANDOM % 4)) "$SYMPTOM_TIME" "Very fatigued" "$DINNER_MEAL_ID"
        post_symptom 20 $((4 + RANDOM % 5)) "${DATE}T23:30:00Z" "Can't sleep after heavy meal" "$DINNER_MEAL_ID"
        SYMPTOM_COUNT=$((SYMPTOM_COUNT + 4))
        ;;
      2|6)
        post_symptom 6 $((6 + RANDOM % 4)) "$SYMPTOM_TIME" "Severe heartburn from hot wings" "$DINNER_MEAL_ID"
        post_symptom 8 $((5 + RANDOM % 5)) "$SYMPTOM_TIME" "Stomach pain" "$DINNER_MEAL_ID"
        post_symptom 4 $((4 + RANDOM % 4)) "${DATE}T23:00:00Z" "GI distress from wings" "$DINNER_MEAL_ID"
        SYMPTOM_COUNT=$((SYMPTOM_COUNT + 3))
        ;;
      3|7)
        post_symptom 1 $((5 + RANDOM % 5)) "$SYMPTOM_TIME" "Bloating from curry" "$DINNER_MEAL_ID"
        post_symptom 3 $((4 + RANDOM % 5)) "$SYMPTOM_TIME" "Cramping" "$DINNER_MEAL_ID"
        post_symptom 11 $((3 + RANDOM % 5)) "$SYMPTOM_TIME" "Headache" "$DINNER_MEAL_ID"
        SYMPTOM_COUNT=$((SYMPTOM_COUNT + 3))
        ;;
    esac
  else
    # Safe dinner
    case $((DAY_OFFSET % 4)) in
      0) post_meal "Dinner" "$DINNER_TIME" "$SALMON,$SWEET_POTATO,$STEAMED_VEGGIES" > /dev/null ;;
      1) post_meal "Dinner" "$DINNER_TIME" "$GRILLED_CHICKEN,$QUINOA_BOWL" > /dev/null ;;
      2) post_meal "Dinner" "$DINNER_TIME" "$CHICKEN_SOUP,$BROWN_RICE" > /dev/null ;;
      3) post_meal "Dinner" "$DINNER_TIME" "$RICE_BOWL,$STEAMED_VEGGIES" > /dev/null ;;
    esac
  fi
  MEAL_COUNT=$((MEAL_COUNT + 1))

  # ── SNACK (some days) ────────────────────────────────────────────────────
  if [ $((DAY_OFFSET % 3)) -ne 2 ]; then
    SNACK_TIME="${DATE}T15:30:00Z"
    case $((DAY_OFFSET % 6)) in
      0) post_meal "Snack" "$SNACK_TIME" "$BANANA" > /dev/null ;;
      1) post_meal "Snack" "$SNACK_TIME" "$APPLE" > /dev/null ;;
      2) post_meal "Snack" "$SNACK_TIME" "$ICE_CREAM" > /dev/null
         # Ice cream sometimes triggers symptoms
         if [ $((DAY_OFFSET % 2)) -eq 0 ]; then
           ICE_TIME="${DATE}T17:00:00Z"
           post_symptom 1 $((3 + RANDOM % 4)) "$ICE_TIME" "Bloated after ice cream"
           post_symptom 2 $((3 + RANDOM % 3)) "$ICE_TIME" "Gas from dairy"
           SYMPTOM_COUNT=$((SYMPTOM_COUNT + 2))
         fi
         ;;
      3) post_meal "Snack" "$SNACK_TIME" "$GREEK_YOGURT" > /dev/null ;;
      4) post_meal "Snack" "$SNACK_TIME" "$ENERGY_DRINK" > /dev/null
         # Energy drink triggers
         CRASH_TIME="${DATE}T17:30:00Z"
         post_symptom 19 $((5 + RANDOM % 4)) "$CRASH_TIME" "Energy crash after energy drink"
         post_symptom 10 $((3 + RANDOM % 4)) "$CRASH_TIME" "Brain fog"
         SYMPTOM_COUNT=$((SYMPTOM_COUNT + 2))
         ;;
      5) post_meal "Snack" "$SNACK_TIME" "$BANANA,$GREEK_YOGURT" > /dev/null ;;
    esac
    MEAL_COUNT=$((MEAL_COUNT + 1))
  fi

  # ── Random baseline symptoms (not food-related, ~20% of days) ──────────
  if [ $((RANDOM % 5)) -eq 0 ]; then
    RANDOM_TIME="${DATE}T$(printf '%02d' $((8 + RANDOM % 12))):$(printf '%02d' $((RANDOM % 60))):00Z"
    case $((RANDOM % 4)) in
      0) post_symptom 18 $((2 + RANDOM % 3)) "$RANDOM_TIME" "General fatigue" ;;
      1) post_symptom 22 $((2 + RANDOM % 3)) "$RANDOM_TIME" "Feeling off" ;;
      2) post_symptom 11 $((2 + RANDOM % 3)) "$RANDOM_TIME" "Mild headache" ;;
      3) post_symptom 23 $((2 + RANDOM % 2)) "$RANDOM_TIME" "Mild anxiety" ;;
    esac
    SYMPTOM_COUNT=$((SYMPTOM_COUNT + 1))
  fi

  echo "meals ✓, symptoms ✓"
done

echo ""
echo "📊 Seed Summary"
echo "   Meals created:    ~$MEAL_COUNT"
echo "   Symptoms created: ~$SYMPTOM_COUNT"
echo ""

# ── Verify correlations ──────────────────────────────────────────────────────

echo "🔍 Verifying correlations..."
FROM=$(date -d "-30 days" +%Y-%m-%d 2>/dev/null || date -v-30d +%Y-%m-%d)
TO=$(date +%Y-%m-%d)

CORR=$(curl -sf "$API/api/insights/correlations?from=$FROM&to=$TO" \
  -H "$AUTH" 2>/dev/null) || { echo "   ⚠️  Could not fetch correlations"; CORR="[]"; }

CORR_COUNT=$(echo "$CORR" | python3 -c "import sys,json; data=json.load(sys.stdin); print(len(data))" 2>/dev/null || echo "0")
echo "   Found $CORR_COUNT correlations"

if [ "$CORR_COUNT" -gt 0 ]; then
  echo ""
  echo "   Top correlations:"
  echo "$CORR" | python3 -c "
import sys, json
data = json.load(sys.stdin)
for c in data[:10]:
    conf_icon = '🔴' if c['confidence'] == 'High' else '🟡' if c['confidence'] == 'Medium' else '🟢'
    print(f\"   {conf_icon} {c['foodOrAdditive']:30s} → {c['symptomName']:20s} | {c['occurrences']}× | {c['frequencyPercent']:.0f}% | avg sev: {c['averageSeverity']}\")
"
fi

echo ""
echo "🎯 Checking trigger foods..."
TRIGGERS=$(curl -sf "$API/api/insights/trigger-foods?from=$FROM&to=$TO" \
  -H "$AUTH" 2>/dev/null) || { echo "   ⚠️  Could not fetch trigger foods"; TRIGGERS="[]"; }

TRIGGER_COUNT=$(echo "$TRIGGERS" | python3 -c "import sys,json; data=json.load(sys.stdin); print(len(data))" 2>/dev/null || echo "0")
echo "   Found $TRIGGER_COUNT trigger foods"

if [ "$TRIGGER_COUNT" -gt 0 ]; then
  echo ""
  echo "$TRIGGERS" | python3 -c "
import sys, json
data = json.load(sys.stdin)
for i, tf in enumerate(data[:10]):
    print(f\"   {i+1}. {tf['food']:30s} | {tf['totalOccurrences']}× | avg sev: {tf['avgSeverity']} | triggers: {', '.join(tf['symptoms'])}\")
"
fi

echo ""
echo "✅ Seed complete! Open the app → Insights tab to see correlations."
echo "   Login: $EMAIL / $PASSWORD"
