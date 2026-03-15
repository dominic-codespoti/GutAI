import { useState, useEffect } from "react";
import {
  View,
  Text,
  TouchableOpacity,
  ScrollView,
  ActivityIndicator,
  BackHandler,
  Platform,
  KeyboardAvoidingView,
} from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { useRouter } from "expo-router";
import { useAuthStore } from "../src/stores/auth";
import { userApi } from "../src/api";
import { toast } from "../src/stores/toast";
import { AllergyChips } from "../components/AllergyChips";
import { GoalField } from "../components/GoalField";
import {
  ALLERGY_OPTIONS,
  DIET_OPTIONS,
  GUT_CONDITION_OPTIONS,
} from "../src/utils/options";
import { radius, spacing } from "../src/utils/theme";
import { useThemeColors, useThemeFonts } from "../src/stores/theme";
import * as haptics from "../src/utils/haptics";
import { SafeScreen } from "../components/SafeScreen";

export default function OnboardingScreen() {
  const colors = useThemeColors();
  const fonts = useThemeFonts();
  const { setUser } = useAuthStore();
  const router = useRouter();
  const [step, setStep] = useState(0);
  const [selectedAllergies, setSelectedAllergies] = useState<string[]>([]);
  const [selectedDiet, setSelectedDiet] = useState("None");
  const [selectedConditions, setSelectedConditions] = useState<string[]>([]);
  const [calGoal, setCalGoal] = useState("2000");
  const [proteinGoal, setProteinGoal] = useState("50");
  const [carbGoal, setCarbGoal] = useState("250");
  const [fatGoal, setFatGoal] = useState("65");
  const [fiberGoal, setFiberGoal] = useState("25");
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    if (Platform.OS === "android") {
      const handler = BackHandler.addEventListener("hardwareBackPress", () => {
        if (step > 0) {
          setStep(step - 1);
          return true;
        }
        return false;
      });
      return () => handler.remove();
    }
  }, [step]);

  const toggleAllergy = (a: string) => {
    haptics.selection();
    setSelectedAllergies((prev) =>
      prev.includes(a) ? prev.filter((x) => x !== a) : [...prev, a],
    );
  };

  const toggleCondition = (c: string) => {
    haptics.selection();
    setSelectedConditions((prev) =>
      prev.includes(c) ? prev.filter((x) => x !== c) : [...prev, c],
    );
  };

  const finish = async () => {
    setSaving(true);
    try {
      const cal = Number(calGoal);
      const prot = Number(proteinGoal);
      const carb = Number(carbGoal);
      const fat = Number(fatGoal);
      const fib = Number(fiberGoal);
      if (cal <= 0 || cal > 10000) {
        toast.error("Calorie goal must be between 1 and 10,000");
        setSaving(false);
        return;
      }
      if (prot < 0 || carb < 0 || fat < 0 || fib < 0) {
        toast.error("Goal values cannot be negative");
        setSaving(false);
        return;
      }
      if (prot > 1000 || carb > 2000 || fat > 1000 || fib > 500) {
        toast.error("Macro goals seem too high — please check your values");
        setSaving(false);
        return;
      }

      const profileData: {
        onboardingCompleted: boolean;
        allergies?: string[];
        dietaryPreferences?: string[];
        gutConditions?: string[];
      } = { onboardingCompleted: true };
      if (selectedAllergies.length > 0)
        profileData.allergies = selectedAllergies;
      if (selectedDiet !== "None")
        profileData.dietaryPreferences = [selectedDiet];
      if (selectedConditions.length > 0)
        profileData.gutConditions = selectedConditions;
      await userApi.updateProfile(profileData);

      await userApi.updateGoals({
        dailyCalorieGoal: Number(calGoal) || 2000,
        dailyProteinGoalG: Number(proteinGoal) || 50,
        dailyCarbGoalG: Number(carbGoal) || 250,
        dailyFatGoalG: Number(fatGoal) || 65,
        dailyFiberGoalG: Number(fiberGoal) || 25,
      });

      const { data } = await userApi.getProfile();
      setUser(data);
      toast.success("Welcome to GutLens! 🎉");
      router.replace("/(tabs)");
    } catch {
      toast.error("Setup failed, you can update later in Profile");
      try {
        await userApi.updateProfile({ onboardingCompleted: true });
      } catch {
        // Best-effort fallback
      }
      try {
        const { data } = await userApi.getProfile();
        setUser(data);
      } catch {
        // If getProfile fails, manually update user to unblock navigation
        const currentUser = useAuthStore.getState().user;
        if (currentUser) {
          setUser({ ...currentUser, onboardingCompleted: true });
        }
      }
      router.replace("/(tabs)");
    } finally {
      setSaving(false);
    }
  };

  const steps = [
    // Step 0: Welcome
    <View key="welcome" style={{ alignItems: "center", paddingVertical: 40 }}>
      <Text style={{ fontSize: 48, marginBottom: spacing.lg }}>🥗</Text>
      <Text
        style={{
          ...fonts.h1,
          marginBottom: spacing.sm,
        }}
        accessibilityRole="header"
      >
        Welcome to GutLens
      </Text>
      <Text
        style={{
          ...fonts.body,
          textAlign: "center",
          lineHeight: 22,
        }}
      >
        Track meals, monitor gut health symptoms, and discover food-symptom
        correlations — all running locally on your device.
      </Text>
    </View>,

    // Step 1: Allergies
    <View key="allergies">
      <Text
        style={{
          ...fonts.h2,
          marginBottom: spacing.xs,
        }}
        accessibilityRole="header"
      >
        Any allergies?
      </Text>
      <Text style={{ ...fonts.body, marginBottom: spacing.lg }}>
        We'll flag these in food safety reports
      </Text>
      <AllergyChips selected={selectedAllergies} onToggle={toggleAllergy} />
    </View>,

    // Step 2: Diet
    <View key="diet">
      <Text
        style={{
          ...fonts.h2,
          marginBottom: spacing.xs,
        }}
        accessibilityRole="header"
      >
        Dietary preference?
      </Text>
      <Text style={{ ...fonts.body, marginBottom: spacing.lg }}>
        Optional — helps personalize insights
      </Text>
      {DIET_OPTIONS.map((d) => (
        <TouchableOpacity
          key={d}
          onPress={() => {
            haptics.selection();
            setSelectedDiet(d);
          }}
          accessibilityRole="radio"
          accessibilityState={{ selected: selectedDiet === d }}
          accessibilityLabel={d}
          style={{
            backgroundColor:
              selectedDiet === d ? colors.primaryBg : colors.card,
            borderWidth: 1,
            borderColor:
              selectedDiet === d ? colors.primaryLight : colors.border,
            borderRadius: radius.md,
            padding: 14,
            marginBottom: spacing.sm,
            flexDirection: "row",
            alignItems: "center",
          }}
        >
          <View
            style={{
              width: 20,
              height: 20,
              borderRadius: 10,
              borderWidth: 2,
              borderColor:
                selectedDiet === d ? colors.primaryLight : colors.textLight,
              backgroundColor:
                selectedDiet === d ? colors.primaryLight : "transparent",
              alignItems: "center",
              justifyContent: "center",
              marginRight: spacing.md,
            }}
          >
            {selectedDiet === d && (
              <Ionicons
                name="checkmark"
                size={14}
                color={colors.textOnPrimary}
              />
            )}
          </View>
          <Text
            style={{
              fontWeight: "600",
              color: selectedDiet === d ? colors.primary : colors.text,
            }}
          >
            {d}
          </Text>
        </TouchableOpacity>
      ))}
    </View>,

    // Step 3: Gut Conditions
    <View key="conditions">
      <Text
        style={{
          ...fonts.h2,
          marginBottom: spacing.xs,
        }}
        accessibilityRole="header"
      >
        Any gut conditions?
      </Text>
      <Text style={{ ...fonts.body, marginBottom: spacing.lg }}>
        Optional — we'll personalize food insights for you
      </Text>
      {GUT_CONDITION_OPTIONS.map((c) => {
        const active = selectedConditions.includes(c.id);
        return (
          <TouchableOpacity
            key={c.id}
            onPress={() => toggleCondition(c.id)}
            accessibilityRole="checkbox"
            accessibilityState={{ checked: active }}
            accessibilityLabel={c.label}
            style={{
              backgroundColor: active ? colors.primaryBg : colors.card,
              borderWidth: 1,
              borderColor: active ? colors.primaryLight : colors.border,
              borderRadius: radius.md,
              padding: 14,
              marginBottom: spacing.sm,
              flexDirection: "row",
              alignItems: "center",
            }}
          >
            <View
              style={{
                width: 20,
                height: 20,
                borderRadius: 4,
                borderWidth: 2,
                borderColor: active ? colors.primaryLight : colors.textLight,
                backgroundColor: active ? colors.primaryLight : "transparent",
                alignItems: "center",
                justifyContent: "center",
                marginRight: spacing.md,
              }}
            >
              {active && (
                <Ionicons
                  name="checkmark"
                  size={14}
                  color={colors.textOnPrimary}
                />
              )}
            </View>
            <Text style={{ fontSize: 18, marginRight: spacing.sm }}>
              {c.emoji}
            </Text>
            <View style={{ flex: 1 }}>
              <Text
                style={{
                  fontWeight: "600",
                  color: active ? colors.primary : colors.text,
                }}
              >
                {c.label}
              </Text>
              <Text
                style={{ fontSize: 12, color: colors.textMuted, marginTop: 1 }}
              >
                {c.description}
              </Text>
            </View>
          </TouchableOpacity>
        );
      })}
    </View>,

    // Step 4: Goals
    <View key="goals">
      <Text
        style={{
          ...fonts.h2,
          marginBottom: spacing.xs,
        }}
        accessibilityRole="header"
      >
        Set your daily goals
      </Text>
      <Text style={{ ...fonts.body, marginBottom: spacing.lg }}>
        You can change these anytime in Profile
      </Text>
      <GoalField
        label="Calories (cal)"
        value={calGoal}
        onChangeText={setCalGoal}
        maxLength={5}
      />
      <GoalField
        label="Protein (g)"
        value={proteinGoal}
        onChangeText={setProteinGoal}
        maxLength={4}
      />
      <GoalField
        label="Carbs (g)"
        value={carbGoal}
        onChangeText={setCarbGoal}
        maxLength={4}
      />
      <GoalField
        label="Fat (g)"
        value={fatGoal}
        onChangeText={setFatGoal}
        maxLength={4}
      />
      <GoalField
        label="Fiber (g)"
        value={fiberGoal}
        onChangeText={setFiberGoal}
        maxLength={4}
      />
    </View>,
  ];

  return (
    <SafeScreen>
      <KeyboardAvoidingView
        behavior={Platform.OS === "ios" ? "padding" : "height"}
        style={{ flex: 1 }}
        keyboardVerticalOffset={Platform.OS === "ios" ? 0 : 0}
      >
        <ScrollView
          style={{ flex: 1, backgroundColor: colors.bg }}
          contentContainerStyle={{ padding: spacing.xxl, paddingBottom: 100 }}
          keyboardShouldPersistTaps="handled"
        >
          {/* Progress dots */}
          <View
            style={{
              flexDirection: "row",
              justifyContent: "center",
              marginBottom: spacing.xxxl,
              gap: spacing.sm,
            }}
            accessibilityLabel={`Step ${step + 1} of ${steps.length}`}
            accessibilityRole="adjustable"
          >
            {steps.map((_, i) => (
              <View
                key={i}
                style={{
                  width: i === step ? 24 : 8,
                  height: 8,
                  borderRadius: 4,
                  backgroundColor:
                    i === step
                      ? colors.primaryLight
                      : i < step
                        ? colors.primaryBorder
                        : colors.border,
                }}
              />
            ))}
          </View>

          {steps[step]}

          {/* Navigation */}
          <View
            style={{
              flexDirection: "row",
              justifyContent: "space-between",
              marginTop: spacing.xxxl,
            }}
          >
            {step > 0 ? (
              <TouchableOpacity
                onPress={() => {
                  haptics.medium();
                  setStep(step - 1);
                }}
                style={{
                  paddingHorizontal: spacing.xxl,
                  paddingVertical: spacing.md,
                }}
                accessibilityRole="button"
                accessibilityLabel="Back"
              >
                <Text
                  style={{
                    color: colors.textMuted,
                    fontWeight: "600",
                    fontSize: 16,
                  }}
                >
                  Back
                </Text>
              </TouchableOpacity>
            ) : (
              <View />
            )}

            {step < steps.length - 1 ? (
              <TouchableOpacity
                onPress={() => {
                  haptics.medium();
                  setStep(step + 1);
                }}
                style={{
                  backgroundColor: colors.primaryLight,
                  paddingHorizontal: 28,
                  paddingVertical: spacing.md,
                  borderRadius: radius.md,
                }}
                accessibilityRole="button"
                accessibilityLabel="Next"
              >
                <Text
                  style={{
                    color: colors.textOnPrimary,
                    fontWeight: "700",
                    fontSize: 16,
                  }}
                >
                  Next
                </Text>
              </TouchableOpacity>
            ) : (
              <TouchableOpacity
                onPress={() => {
                  haptics.medium();
                  finish();
                }}
                disabled={saving}
                style={{
                  backgroundColor: colors.primaryLight,
                  paddingHorizontal: 28,
                  paddingVertical: spacing.md,
                  borderRadius: radius.md,
                }}
                accessibilityRole="button"
                accessibilityLabel="Get Started"
              >
                {saving ? (
                  <ActivityIndicator color={colors.textOnPrimary} />
                ) : (
                  <Text
                    style={{
                      color: colors.textOnPrimary,
                      fontWeight: "700",
                      fontSize: 16,
                    }}
                  >
                    Get Started
                  </Text>
                )}
              </TouchableOpacity>
            )}
          </View>

          {step === 0 && (
            <TouchableOpacity
              onPress={finish}
              style={{ alignItems: "center", marginTop: spacing.lg }}
              accessibilityRole="button"
              accessibilityLabel="Skip onboarding"
            >
              <Text style={{ color: colors.textMuted, fontSize: 14 }}>
                Skip setup →
              </Text>
            </TouchableOpacity>
          )}
        </ScrollView>
      </KeyboardAvoidingView>
    </SafeScreen>
  );
}
