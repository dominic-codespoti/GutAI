import { useState } from "react";
import {
  View,
  Text,
  TouchableOpacity,
  TextInput,
  ScrollView,
  ActivityIndicator,
} from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { useRouter } from "expo-router";
import { useAuthStore } from "../src/stores/auth";
import { userApi } from "../src/api";
import { toast } from "../src/stores/toast";

const ALLERGY_OPTIONS = [
  "Peanuts",
  "Tree Nuts",
  "Milk",
  "Eggs",
  "Wheat",
  "Soy",
  "Fish",
  "Shellfish",
  "Sesame",
];
const DIET_OPTIONS = [
  "None",
  "Vegetarian",
  "Vegan",
  "Keto",
  "Paleo",
  "Gluten-Free",
  "Low-FODMAP",
  "Mediterranean",
];

export default function OnboardingScreen() {
  const { setUser } = useAuthStore();
  const router = useRouter();
  const [step, setStep] = useState(0);
  const [selectedAllergies, setSelectedAllergies] = useState<string[]>([]);
  const [selectedDiet, setSelectedDiet] = useState("None");
  const [calGoal, setCalGoal] = useState("2000");
  const [proteinGoal, setProteinGoal] = useState("50");
  const [carbGoal, setCarbGoal] = useState("250");
  const [fatGoal, setFatGoal] = useState("65");
  const [fiberGoal, setFiberGoal] = useState("25");
  const [saving, setSaving] = useState(false);

  const toggleAllergy = (a: string) => {
    setSelectedAllergies((prev) =>
      prev.includes(a) ? prev.filter((x) => x !== a) : [...prev, a],
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

      const profileData: {
        onboardingCompleted: boolean;
        allergies?: string[];
        dietaryPreferences?: string[];
      } = { onboardingCompleted: true };
      if (selectedAllergies.length > 0)
        profileData.allergies = selectedAllergies;
      if (selectedDiet !== "None")
        profileData.dietaryPreferences = [selectedDiet];
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
      toast.success("Welcome to GutAI! 🎉");
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
      <Text style={{ fontSize: 48, marginBottom: 16 }}>🥗</Text>
      <Text
        style={{
          fontSize: 28,
          fontWeight: "800",
          color: "#0f172a",
          marginBottom: 8,
        }}
      >
        Welcome to GutAI
      </Text>
      <Text
        style={{
          fontSize: 15,
          color: "#64748b",
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
          fontSize: 22,
          fontWeight: "700",
          color: "#0f172a",
          marginBottom: 4,
        }}
      >
        Any allergies?
      </Text>
      <Text style={{ fontSize: 14, color: "#64748b", marginBottom: 16 }}>
        We'll flag these in food safety reports
      </Text>
      <View style={{ flexDirection: "row", flexWrap: "wrap", gap: 8 }}>
        {ALLERGY_OPTIONS.map((a) => (
          <TouchableOpacity
            key={a}
            onPress={() => toggleAllergy(a)}
            style={{
              backgroundColor: selectedAllergies.includes(a)
                ? "#dcfce7"
                : "#f1f5f9",
              borderWidth: 1,
              borderColor: selectedAllergies.includes(a)
                ? "#22c55e"
                : "#e2e8f0",
              borderRadius: 20,
              paddingHorizontal: 16,
              paddingVertical: 8,
            }}
          >
            <Text
              style={{
                fontWeight: "600",
                color: selectedAllergies.includes(a) ? "#15803d" : "#64748b",
              }}
            >
              {a}
            </Text>
          </TouchableOpacity>
        ))}
      </View>
    </View>,

    // Step 2: Diet
    <View key="diet">
      <Text
        style={{
          fontSize: 22,
          fontWeight: "700",
          color: "#0f172a",
          marginBottom: 4,
        }}
      >
        Dietary preference?
      </Text>
      <Text style={{ fontSize: 14, color: "#64748b", marginBottom: 16 }}>
        Optional — helps personalize insights
      </Text>
      {DIET_OPTIONS.map((d) => (
        <TouchableOpacity
          key={d}
          onPress={() => setSelectedDiet(d)}
          style={{
            backgroundColor: selectedDiet === d ? "#dcfce7" : "#fff",
            borderWidth: 1,
            borderColor: selectedDiet === d ? "#22c55e" : "#e2e8f0",
            borderRadius: 10,
            padding: 14,
            marginBottom: 8,
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
              borderColor: selectedDiet === d ? "#22c55e" : "#cbd5e1",
              backgroundColor: selectedDiet === d ? "#22c55e" : "transparent",
              alignItems: "center",
              justifyContent: "center",
              marginRight: 12,
            }}
          >
            {selectedDiet === d && (
              <Ionicons name="checkmark" size={14} color="#fff" />
            )}
          </View>
          <Text
            style={{
              fontWeight: "600",
              color: selectedDiet === d ? "#15803d" : "#334155",
            }}
          >
            {d}
          </Text>
        </TouchableOpacity>
      ))}
    </View>,

    // Step 3: Goals
    <View key="goals">
      <Text
        style={{
          fontSize: 22,
          fontWeight: "700",
          color: "#0f172a",
          marginBottom: 4,
        }}
      >
        Set your daily goals
      </Text>
      <Text style={{ fontSize: 14, color: "#64748b", marginBottom: 16 }}>
        You can change these anytime in Profile
      </Text>
      <GoalField
        label="Calories (cal)"
        value={calGoal}
        onChangeText={setCalGoal}
      />
      <GoalField
        label="Protein (g)"
        value={proteinGoal}
        onChangeText={setProteinGoal}
      />
      <GoalField
        label="Carbs (g)"
        value={carbGoal}
        onChangeText={setCarbGoal}
      />
      <GoalField label="Fat (g)" value={fatGoal} onChangeText={setFatGoal} />
      <GoalField
        label="Fiber (g)"
        value={fiberGoal}
        onChangeText={setFiberGoal}
      />
    </View>,
  ];

  return (
    <ScrollView
      style={{ flex: 1, backgroundColor: "#f8fafc" }}
      contentContainerStyle={{ padding: 24, paddingBottom: 100 }}
    >
      {/* Progress dots */}
      <View
        style={{
          flexDirection: "row",
          justifyContent: "center",
          marginBottom: 32,
          gap: 8,
        }}
      >
        {steps.map((_, i) => (
          <View
            key={i}
            style={{
              width: i === step ? 24 : 8,
              height: 8,
              borderRadius: 4,
              backgroundColor:
                i === step ? "#22c55e" : i < step ? "#86efac" : "#e2e8f0",
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
          marginTop: 32,
        }}
      >
        {step > 0 ? (
          <TouchableOpacity
            onPress={() => setStep(step - 1)}
            style={{ paddingHorizontal: 24, paddingVertical: 12 }}
          >
            <Text style={{ color: "#64748b", fontWeight: "600", fontSize: 16 }}>
              Back
            </Text>
          </TouchableOpacity>
        ) : (
          <View />
        )}

        {step < steps.length - 1 ? (
          <TouchableOpacity
            onPress={() => setStep(step + 1)}
            style={{
              backgroundColor: "#22c55e",
              paddingHorizontal: 28,
              paddingVertical: 12,
              borderRadius: 10,
            }}
          >
            <Text style={{ color: "#fff", fontWeight: "700", fontSize: 16 }}>
              Next
            </Text>
          </TouchableOpacity>
        ) : (
          <TouchableOpacity
            onPress={finish}
            disabled={saving}
            style={{
              backgroundColor: "#22c55e",
              paddingHorizontal: 28,
              paddingVertical: 12,
              borderRadius: 10,
            }}
          >
            {saving ? (
              <ActivityIndicator color="#fff" />
            ) : (
              <Text style={{ color: "#fff", fontWeight: "700", fontSize: 16 }}>
                Get Started
              </Text>
            )}
          </TouchableOpacity>
        )}
      </View>

      {step === 0 && (
        <TouchableOpacity
          onPress={finish}
          style={{ alignItems: "center", marginTop: 16 }}
        >
          <Text style={{ color: "#94a3b8", fontSize: 14 }}>Skip setup →</Text>
        </TouchableOpacity>
      )}
    </ScrollView>
  );
}

function GoalField({
  label,
  value,
  onChangeText,
}: {
  label: string;
  value: string;
  onChangeText: (t: string) => void;
}) {
  return (
    <View style={{ marginBottom: 12 }}>
      <Text style={{ fontSize: 13, color: "#64748b", marginBottom: 4 }}>
        {label}
      </Text>
      <TextInput
        value={value}
        onChangeText={onChangeText}
        keyboardType="numeric"
        style={{
          borderWidth: 1,
          borderColor: "#e2e8f0",
          borderRadius: 8,
          padding: 12,
          fontSize: 16,
          color: "#0f172a",
          backgroundColor: "#fff",
        }}
      />
    </View>
  );
}
