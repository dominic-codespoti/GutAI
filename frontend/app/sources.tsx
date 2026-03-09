import {
  ScrollView,
  Text,
  View,
  TouchableOpacity,
  Linking,
} from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { SafeScreen } from "../components/SafeScreen";
import { radius, spacing } from "../src/utils/theme";
import { useThemeColors, useThemeFonts } from "../src/stores/theme";

const sources = [
  {
    heading: "FODMAP Data",
    items: [
      {
        label: "Monash University FODMAP Diet Program",
        url: "https://www.monashfodmap.com",
      },
      {
        label:
          'Barrett JS, Gibson PR. "Fermentable oligosaccharides, disaccharides, monosaccharides and polyols (FODMAPs) and nonallergic food intolerance." J Gastroenterol Hepatol. 2010;25(2):252-258',
      },
    ],
  },
  {
    heading: "Glycemic Index",
    items: [
      {
        label: "Sydney University Glycemic Index Research Service",
        url: "https://glycemicindex.com",
      },
      {
        label:
          'Atkinson FS, Brand-Miller JC, Foster-Powell K, et al. "International Tables of Glycemic Index and Glycemic Load Values 2021." Am J Clin Nutr. 2021;114(5):1625-1632',
      },
    ],
  },
  {
    heading: "Food Additive Safety",
    items: [
      {
        label: "CSPI Chemical Cuisine Rating Guide",
        url: "https://www.cspinet.org/eating-healthy/chemical-cuisine",
      },
      {
        label: "EFSA Food Additives Database",
        url: "https://www.efsa.europa.eu/en/data-report/food-additives",
      },
    ],
  },
  {
    heading: "Food Processing",
    items: [
      {
        label:
          'Monteiro CA, et al. "NOVA. The star shines bright." World Nutrition. 2016;7(1-3):28-38',
      },
    ],
  },
  {
    heading: "Dietary Guidelines",
    items: [
      {
        label:
          "NICE. Irritable bowel syndrome in adults: diagnosis and management. CG61. 2008 (updated 2017)",
        url: "https://www.nice.org.uk/guidance/cg61",
      },
      {
        label:
          'Tuck CJ, et al. "Food Intolerances." Nutrients. 2019;11(7):1684',
      },
    ],
  },
  {
    heading: "Nutritional Data",
    items: [
      {
        label: "Open Food Facts — open food products database",
        url: "https://world.openfoodfacts.org",
      },
      {
        label: "USDA FoodData Central",
        url: "https://fdc.nal.usda.gov",
      },
    ],
  },
];

export default function SourcesScreen() {
  const colors = useThemeColors();
  const fonts = useThemeFonts();

  return (
    <SafeScreen edges={["bottom"]}>
      <ScrollView
        style={{ flex: 1, backgroundColor: colors.bg }}
        contentContainerStyle={{ padding: spacing.xl, paddingBottom: 60 }}
      >
        <View
          style={{
            backgroundColor: colors.warningBg,
            borderRadius: radius.md,
            padding: spacing.lg,
            marginBottom: spacing.xl,
            borderWidth: 1,
            borderColor: colors.warningBorder,
          }}
        >
          <Text
            style={{
              fontSize: 15,
              fontWeight: "700",
              color: colors.warning,
              marginBottom: 6,
            }}
          >
            ⚕️ Medical Disclaimer
          </Text>
          <Text
            style={{
              fontSize: 13,
              color: colors.textSecondary,
              lineHeight: 20,
            }}
          >
            This app is for informational and educational purposes only. It is
            not a substitute for professional medical advice, diagnosis, or
            treatment. Always consult a qualified healthcare provider or
            registered dietitian before making dietary changes based on
            information in this app.
          </Text>
        </View>

        <Text style={{ ...fonts.h2, marginBottom: spacing.lg }}>
          📚 Data Sources & References
        </Text>

        {sources.map((section) => (
          <View key={section.heading} style={{ marginBottom: spacing.xl }}>
            <Text
              style={{
                fontSize: 15,
                fontWeight: "700",
                color: colors.text,
                marginBottom: spacing.sm,
              }}
            >
              {section.heading}
            </Text>
            {section.items.map((item, i) => (
              <View key={i} style={{ marginBottom: spacing.sm }}>
                {item.url ? (
                  <TouchableOpacity
                    onPress={() => Linking.openURL(item.url)}
                    style={{
                      flexDirection: "row",
                      alignItems: "flex-start",
                      gap: 6,
                    }}
                  >
                    <Ionicons
                      name="link-outline"
                      size={14}
                      color={colors.primary}
                      style={{ marginTop: 2 }}
                    />
                    <Text
                      style={{
                        fontSize: 13,
                        color: colors.primary,
                        flex: 1,
                        lineHeight: 19,
                        textDecorationLine: "underline",
                      }}
                    >
                      {item.label}
                    </Text>
                  </TouchableOpacity>
                ) : (
                  <View
                    style={{
                      flexDirection: "row",
                      alignItems: "flex-start",
                      gap: 6,
                    }}
                  >
                    <Ionicons
                      name="document-text-outline"
                      size={14}
                      color={colors.textMuted}
                      style={{ marginTop: 2 }}
                    />
                    <Text
                      style={{
                        fontSize: 13,
                        color: colors.textMuted,
                        flex: 1,
                        lineHeight: 19,
                        fontStyle: "italic",
                      }}
                    >
                      {item.label}
                    </Text>
                  </View>
                )}
              </View>
            ))}
          </View>
        ))}

        <View
          style={{
            borderTopWidth: 1,
            borderTopColor: colors.border,
            paddingTop: spacing.lg,
            marginTop: spacing.md,
          }}
        >
          <Text
            style={{
              fontSize: 12,
              color: colors.textMuted,
              lineHeight: 18,
              textAlign: "center",
            }}
          >
            The information and scores provided by this app are generated from
            the sources listed above and are intended to support — not replace —
            professional dietary guidance.
          </Text>
        </View>
      </ScrollView>
    </SafeScreen>
  );
}
