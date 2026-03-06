import {
  ScrollView,
  Text,
  View,
  Linking,
  TouchableOpacity,
} from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { SafeScreen } from "../components/SafeScreen";
import { radius, spacing } from "../src/utils/theme";
import { useThemeColors, useThemeFonts } from "../src/stores/theme";

const CONTACT_EMAIL = "support@workoutquestapp.com";

export default function PrivacyPolicyScreen() {
  const colors = useThemeColors();
  const fonts = useThemeFonts();

  return (
    <SafeScreen edges={["bottom"]}>
      <ScrollView
        style={{ flex: 1, backgroundColor: colors.bg }}
        contentContainerStyle={{ padding: spacing.xl, paddingBottom: 60 }}
      >
        <Text
          style={{
            fontSize: 22,
            fontWeight: "800",
            color: colors.text,
            marginBottom: 4,
          }}
        >
          Privacy Policy
        </Text>
        <Text style={{ ...fonts.caption, marginBottom: spacing.xl }}>
          Effective Date: February 24, 2026 · Last Updated: February 24, 2026
        </Text>

        <Text style={{ ...fonts.body, marginBottom: spacing.lg }}>
          GutLens ("we", "us", "our") is a gut-health food diary app that helps
          you track meals, monitor symptoms, and discover food-related patterns.
          This privacy policy explains what data we collect, how we use it, and
          your rights.
        </Text>

        <SectionHeading>1. Data We Collect</SectionHeading>

        <SubHeading>Account Information</SubHeading>
        <BulletList
          items={[
            "Email address",
            "Display name",
            "Password (stored as a salted hash — we never store or see your plaintext password)",
            "Timezone",
          ]}
        />

        <SubHeading>Health & Dietary Preferences</SubHeading>
        <BulletList
          items={[
            "Self-reported allergies (e.g., peanuts, dairy, gluten)",
            "Dietary preferences (e.g., vegan, keto, low-FODMAP)",
            "Daily nutrition goals (calories, protein, carbs, fat, fiber)",
          ]}
        />

        <SubHeading>Food Diary</SubHeading>
        <BulletList
          items={[
            "Meal entries including food names, portion sizes, and nutritional values",
            "Timestamps of when meals were logged",
            'Free-text notes and natural language meal descriptions (e.g., "ate 2 eggs and toast")',
            "Optional photo URLs",
          ]}
        />

        <SubHeading>Symptom Logs</SubHeading>
        <BulletList
          items={[
            "Symptom type, severity, timing, and duration",
            "Free-text notes about symptoms",
            "Associations between meals and symptoms",
          ]}
        />

        <SubHeading>Derived Data</SubHeading>
        <BulletList
          items={[
            "Food-symptom correlations and trigger food identification",
            "Additive exposure tracking",
            "Personalized gut-health insights",
          ]}
        />
        <Text style={{ ...fonts.body, marginBottom: spacing.lg }}>
          We generate these insights locally from your own data to help you
          understand patterns. They are not shared externally.
        </Text>

        <SectionHeading>2. Data We Do NOT Collect</SectionHeading>
        <BulletList
          items={[
            "Location or GPS data",
            "Device identifiers or advertising IDs",
            "Contacts, call logs, or messages",
            "Analytics or behavioral tracking",
            "Push notification tokens (not yet implemented)",
            "Biometric data",
          ]}
        />

        <SectionHeading>3. How We Use Your Data</SectionHeading>
        <Text style={{ ...fonts.body, marginBottom: spacing.sm }}>
          We use your data to provide the core food diary and symptom tracking
          service, generate personalized insights and trigger food analysis,
          look up nutritional information for foods you log, authenticate your
          account, and track progress toward your nutrition goals.
        </Text>
        <Text
          style={{
            ...fonts.body,
            fontWeight: "600",
            marginBottom: spacing.lg,
          }}
        >
          We do not use your data for advertising, profiling, or sale to third
          parties.
        </Text>

        <SectionHeading>4. Third-Party Services</SectionHeading>
        <Text style={{ ...fonts.body, marginBottom: spacing.sm }}>
          When you search for or log a food, we may query external nutrition
          databases to retrieve nutritional data. These services receive only
          the food search text or barcode — never your email, name, health data,
          or any personal identifiers.
        </Text>
        <BulletList
          items={[
            "Edamam — Nutrition lookup and meal parsing",
            "USDA FoodData Central — Nutrition lookup",
            "Open Food Facts — Barcode-based food lookup",
            "CalorieNinjas — Fallback nutrition parsing",
          ]}
        />
        <Text style={{ ...fonts.body, marginBottom: spacing.lg }}>
          No other third-party services receive your data.
        </Text>

        <SectionHeading>5. Data Storage & Security</SectionHeading>
        <BulletList
          items={[
            "All data is stored on secured servers hosted on Microsoft Azure",
            "Passwords are hashed using industry-standard algorithms before storage",
            "Authentication uses short-lived JSON Web Tokens (JWTs) stored securely on your device",
            "API communication is encrypted via HTTPS/TLS",
            "IP addresses are used transiently for rate limiting and are not persisted",
          ]}
        />

        <SectionHeading>6. Your Rights</SectionHeading>
        <Text style={{ ...fonts.body, marginBottom: spacing.sm }}>
          You have full control over your data:
        </Text>
        <BulletList
          items={[
            "Access & Export — Export all your meal and health data at any time via the app",
            "Correction — Update your profile, preferences, and logged entries at any time",
            "Deletion — Delete your account and all associated data permanently (irreversible)",
            "Portability — Your exported data is provided in a standard format",
          ]}
        />
        <Text style={{ ...fonts.body, marginBottom: spacing.lg }}>
          If you are in the EU/EEA, you also have rights under the GDPR
          including the right to restrict processing and the right to object.
          Contact us to exercise these rights.
        </Text>

        <SectionHeading>7. Data Retention</SectionHeading>
        <BulletList
          items={[
            "Your data is retained for as long as your account is active",
            "When you delete your account, all data is permanently deleted immediately",
            "Standard infrastructure backups are purged within 30 days",
          ]}
        />

        <SectionHeading>8. Children's Privacy</SectionHeading>
        <Text style={{ ...fonts.body, marginBottom: spacing.lg }}>
          GutLens is not directed at children under 13 (or 16 in the EU/EEA). We
          do not knowingly collect data from children. If you believe a child
          has provided us with personal data, please contact us and we will
          delete it.
        </Text>

        <SectionHeading>9. Changes to This Policy</SectionHeading>
        <Text style={{ ...fonts.body, marginBottom: spacing.lg }}>
          We may update this privacy policy from time to time. Changes will be
          reflected by updating the "Last Updated" date at the top. Continued
          use of the app after changes constitutes acceptance.
        </Text>

        <SectionHeading>10. Contact</SectionHeading>
        <Text style={{ ...fonts.body, marginBottom: spacing.sm }}>
          If you have questions about this privacy policy or your data, contact
          us at:
        </Text>
        <TouchableOpacity
          onPress={() => Linking.openURL(`mailto:${CONTACT_EMAIL}`)}
          style={{
            flexDirection: "row",
            alignItems: "center",
            marginBottom: spacing.xl,
          }}
        >
          <Ionicons name="mail-outline" size={16} color={colors.primary} />
          <Text
            style={{
              color: colors.primary,
              fontWeight: "600",
              marginLeft: 6,
            }}
          >
            {CONTACT_EMAIL}
          </Text>
        </TouchableOpacity>
      </ScrollView>
    </SafeScreen>
  );
}

function SectionHeading({ children }: { children: string }) {
  const colors = useThemeColors();
  return (
    <Text
      style={{
        fontSize: 17,
        fontWeight: "700",
        color: colors.text,
        marginBottom: spacing.sm,
        marginTop: spacing.md,
      }}
    >
      {children}
    </Text>
  );
}

function SubHeading({ children }: { children: string }) {
  const colors = useThemeColors();
  return (
    <Text
      style={{
        fontSize: 14,
        fontWeight: "700",
        color: colors.textSecondary,
        marginBottom: spacing.xs,
        marginTop: spacing.sm,
      }}
    >
      {children}
    </Text>
  );
}

function BulletList({ items }: { items: string[] }) {
  const colors = useThemeColors();
  const fonts = useThemeFonts();
  return (
    <View style={{ marginBottom: spacing.md }}>
      {items.map((item, i) => (
        <View
          key={i}
          style={{
            flexDirection: "row",
            paddingRight: spacing.lg,
            marginBottom: 6,
          }}
        >
          <Text
            style={{ color: colors.textMuted, marginRight: 8, fontSize: 14 }}
          >
            •
          </Text>
          <Text style={{ ...fonts.body, flex: 1, fontSize: 14 }}>{item}</Text>
        </View>
      ))}
    </View>
  );
}
