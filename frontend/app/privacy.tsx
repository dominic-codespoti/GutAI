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

const CONTACT_EMAIL = "domcodespoti@gmail.com";

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
          Effective Date: February 24, 2026 · Last Updated: September 25, 2026
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
            "Meal drafts created from photos, Coach or connected AI assistant suggestions, text logging, or meal suggestions, including your edits and corrections",
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
          We generate personalized insights on our servers from your diary
          data and do not use them to train AI models. When you request meal
          suggestions, we send the screened candidate food list described below.
        </Text>
        <SubHeading>Operational Telemetry</SubHeading>
        <BulletList
          items={[
            "For each photo scan, the AI usage summary log records the operation name, scan correlation/draft ID, stage names, model-call counts, deployment names, input and output token counts, model time, and estimated cost. This includes usage for photo analysis, candidate selection, agent review, and web-nutrition extraction. Operation-tagged metrics report token counts, model time, and estimated cost when available. Telemetry is sent to Application Insights; its Log Analytics workspace is configured for 30-day retention. Estimated cost can be unavailable when deployment pricing is not configured.",
          ]}
        />

        <SectionHeading>2. Data We Do NOT Collect</SectionHeading>
        <BulletList
          items={[
            "Location or GPS data",
            "Device identifiers or advertising IDs",
            "Contacts, call logs, or messages",
            "Advertising or behavioral tracking data",
            "Push notification tokens — optional meal reminders are scheduled locally on your device and are never sent through our servers",
            "Biometric data",
          ]}
        />

        <SectionHeading>3. How We Use Your Data</SectionHeading>
        <Text style={{ ...fonts.body, marginBottom: spacing.sm }}>
          We use your data to provide the core food diary and symptom tracking
          service, generate personalized insights and trigger food analysis,
          look up nutritional information for foods you log, authenticate your
          account, track progress toward your nutrition goals, generate meal
          suggestions when you request them, and monitor AI service operation
          and usage costs.
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
            "USDA FoodData Central — Nutrition lookup",
            "Open Food Facts — Food name or barcode for nutrition lookup and barcode-based food lookup",
            "DuckDuckGo and Jina Reader — Food-name nutrition search query to DuckDuckGo; selected result URL to Jina Reader for optional web nutrition lookup (disabled by default pending review)",
            "Microsoft Azure OpenAI — Meal descriptions, Coach chat messages, meal photos you submit, and (if web grounding is enabled) selected web-page content are processed solely to generate your results (parsing, coaching replies, photo analysis). Meal-suggestion requests also send your remaining nutrition budget, any preferences you enter for that request, and a candidate food list with food names and nutrition/serving information. The candidate list is built from recent or safe foods and screened against saved allergies and dietary preferences; those saved lists are not sent as a separate list. Photo notes and food descriptions are sent with their request.",
          ]}
        />
        <Text style={{ ...fonts.body, marginBottom: spacing.lg }}>
          When Features:WebGrounding is enabled, web nutrition results are
          cached in Azure Table Storage using the normalized food name and
          region, not your user ID, and are not linked to your account.
          Successful results are reused for up to 180 days; not-found results
          for up to 7 days. Expired entries are not automatically purged; they
          remain stored unless a later lookup overwrites them.
        </Text>
        <Text style={{ ...fonts.body, marginBottom: spacing.lg }}>
          Apart from services listed here, AI providers operating connected
          assistants, and the optional health-platform sync described below,
          no other third-party services receive your data.
        </Text>

        <SubHeading>Optional AI Assistant Connections</SubHeading>
        <Text style={{ ...fonts.body, marginBottom: spacing.sm }}>
          If you connect an external AI assistant (for example, a chat app that
          supports MCP), it uses a personal access token (PAT). A PAT without
          write scope is read-only; a PAT with write scope can use the available
          write tools. Meals proposed by a connected assistant appear as drafts.
          They are logged only after you review and confirm them in GutLens or
          in the assistant chat; they are never logged automatically. The AI
          provider operating the assistant handles data passed through it under
          its own privacy policy.
        </Text>
        <Text style={{ ...fonts.body, marginBottom: spacing.lg }}>
          You can see connected assistants and revoke access in Settings →
          Connected AI Assistants. Access tokens are stored only as
          cryptographic hashes and cannot be viewed again after creation.
        </Text>
        <SubHeading>Optional Health Platform Sync</SubHeading>
        <Text style={{ ...fonts.body, marginBottom: spacing.sm }}>
          GutLens can optionally exchange meal data with Apple Health (iOS) and
          Google Health Connect (Android), with your permission.
        </Text>
        <BulletList
          items={[
            "Import — When you request an import, GutLens reads nutrition records from your device's health store and sends selected meal details to GutLens's API to create imported meal entries. Imported entries are labeled as estimates and never treated as verified data.",
            "Export — You can choose to have meals you log in GutLens written to your device's health store. This toggle is off by default.",
            "Health data is exchanged between the selected platform, your device, and GutLens. GutLens does not send health-sync records to other third-party services. You can revoke permission in iOS/Android system settings.",
            "GutLens skips records it originally wrote when reading, so enabling both directions never duplicates your meals.",
          ]}
        />

        <SectionHeading>5. Data Storage & Security</SectionHeading>
        <BulletList
          items={[
            "All data is stored on secured servers hosted on Microsoft Azure",
            "Passwords are hashed using industry-standard algorithms before storage",
            "Authentication uses short-lived JSON Web Tokens (JWTs) stored securely on your device (Expo SecureStore on mobile)",
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
            "Deletion — Delete your account in Settings. This is irreversible: account deletion removes your profile and identity, related credentials and alerts, and draft records; meal and symptom records are marked as deleted",
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
            "Your account profile and identity are retained while your account is active",
            "AI meal drafts from photos, Coach or connected AI assistant suggestions, text logging, and meal suggestions remain pending until you review them. Pending drafts expire after 24 hours if not reviewed.",
            "Reviewed or otherwise closed drafts, including discarded drafts and your corrections used to improve portion accuracy, are retained for up to 90 days from creation and then purged.",
            "Drafts and their correction data are deleted when you delete your account. Account deletion also removes related credentials and alerts and marks meal and symptom records as deleted.",
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
