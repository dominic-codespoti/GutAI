#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
ASSETS="$ROOT/assets"
RAW="$ASSETS/store/captures/raw"
BG="$ASSETS/store/backgrounds/chatgpt_1787746372441.png"
OUT_APPLE="$ASSETS/store/final/apple"
OUT_GOOGLE="$ASSETS/store/final/google"
OUT_IPAD="$ASSETS/store/final/apple-ipad"
FONT_BOLD="$ROOT/node_modules/@expo-google-fonts/inter/700Bold/Inter_700Bold.ttf"
FONT_REGULAR="$ROOT/node_modules/@expo-google-fonts/inter/400Regular/Inter_400Regular.ttf"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
mkdir -p "$OUT_APPLE" "$OUT_GOOGLE" "$OUT_IPAD"

# Captions are benefit-led; the screenshot itself remains untouched real UI.
declare -a NAMES=(dashboard scan insights symptoms meals profile)
declare -a TITLES=(
  "Know what your gut is telling you"
  "Scan meals. See the whole picture."
  "Find patterns that are personal to you"
  "Make symptoms easier to understand"
  "Log the way that fits your day"
  "Make food choices feel personal"
)
declare -a SUBTITLES=(
  "Calories, patterns and progress at a glance"
  "AI-assisted portions with nutrition you can review"
  "Connect meals, symptoms and timing"
  "Track severity over time, not from memory"
  "Search, scan, describe or add manually"
  "Your goals, preferences and patterns in one place"
)
fit_pointsize() {
  local font="$1" text="$2" size="$3" max_width="$4"
  local width
  while :; do
    width="$(magick -font "$font" -pointsize "$size" -background none "label:$text" -format '%w' info:)"
    if (( width <= max_width || size <= 40 )); then
      printf '%s' "$size"
      return
    fi
    size=$((size - 2))
  done
}

compose_apple() {
  local name="$1" title="$2" subtitle="$3" out="$4"
  local screen="$TMP/${name}-apple.png"
  local title_size
  title_size="$(fit_pointsize "$FONT_BOLD" "$title" 74 1104)"

  # App Store Connect accepts 1284×2778 for this listing's iPhone set.
  magick "$RAW/$name.png" -resize 1104x2394! -bordercolor '#22C55E' -border 4 "$screen"
  magick "$BG" -resize 1284x2778^ -gravity center -extent 1284x2778 \
    -font "$FONT_BOLD" -fill '#F8FAFC' -pointsize "$title_size" \
    -gravity northwest -annotate +86+175 "$title" \
    -font "$FONT_REGULAR" -fill '#B7C5D8' -pointsize 34 \
    -annotate +88+243 "$subtitle" \
    \( "$screen" \) -gravity northwest -geometry +86+350 -composite \
    -alpha off -strip "$out"
}

compose_google() {
  local name="$1" title="$2" subtitle="$3" out="$4"
  local screen="$TMP/${name}-google.png"
  local title_size
  title_size="$(fit_pointsize "$FONT_BOLD" "$title" 58 960)"

  magick "$RAW/$name.png" -resize 770x1667! -bordercolor '#22C55E' -border 4 "$screen"
  magick "$BG" -resize 1080x1920^ -gravity center -extent 1080x1920 \
    -font "$FONT_BOLD" -fill '#F8FAFC' -pointsize "$title_size" \
    -gravity northwest -annotate +60+120 "$title" \
    -font "$FONT_REGULAR" -fill '#B7C5D8' -pointsize 27 \
    -annotate +62+179 "$subtitle" \
    \( "$screen" \) -gravity northwest -geometry +155+240 -composite \
    -alpha off -strip "$out"
}
compose_ipad() {
  local name="$1" title="$2" subtitle="$3" out="$4"
  local screen="$TMP/${name}-ipad.png"
  local title_size
  title_size="$(fit_pointsize "$FONT_BOLD" "$title" 100 1775)"

  magick "$RAW/ipad-$name.png" -resize 1780x2373! -bordercolor '#22C55E' -border 4 "$screen"
  magick "$BG" -flop -resize 2048x2732^ -gravity center -extent 2048x2732 \
    -font "$FONT_BOLD" -fill '#F8FAFC' -pointsize "$title_size" \
    -gravity northwest -annotate +134+210 "$title" \
    -font "$FONT_REGULAR" -fill '#B7C5D8' -pointsize 48 \
    -annotate +136+288 "$subtitle" \
    \( "$screen" \) -gravity northwest -geometry +130+340 -composite \
    -alpha off -strip "$out"
}

for i in "${!NAMES[@]}"; do
  name="${NAMES[$i]}"
  compose_apple "$name" "${TITLES[$i]}" "${SUBTITLES[$i]}" "$OUT_APPLE/$(printf '%02d' "$((i + 1))")-$name.png"
  compose_google "$name" "${TITLES[$i]}" "${SUBTITLES[$i]}" "$OUT_GOOGLE/$(printf '%02d' "$((i + 1))")-$name.png"
  compose_ipad "$name" "${TITLES[$i]}" "${SUBTITLES[$i]}" "$OUT_IPAD/$(printf '%02d' "$((i + 1))")-$name.png"
done


echo "Wrote Apple frames to $OUT_APPLE"
echo "Wrote Apple iPad frames to $OUT_IPAD"
echo "Wrote Google frames to $OUT_GOOGLE"
