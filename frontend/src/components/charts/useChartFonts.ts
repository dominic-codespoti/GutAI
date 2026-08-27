import { useFont } from "@shopify/react-native-skia";

const INTER_REGULAR = require("@expo-google-fonts/inter/400Regular/Inter_400Regular.ttf");
const INTER_SEMIBOLD = require("@expo-google-fonts/inter/600SemiBold/Inter_600SemiBold.ttf");

/** Skia fonts for chart axis/tick labels — victory-native requires real font assets. */
export function useChartFonts() {
  const regular = useFont(INTER_REGULAR, 10);
  const medium = useFont(INTER_SEMIBOLD, 11);
  return { regular, medium };
}
