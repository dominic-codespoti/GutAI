import { useState, useCallback, useEffect } from "react";
import {
  View,
  Text,
  ScrollView,
  TouchableOpacity,
  TextInput,
  ActivityIndicator,
  RefreshControl,
  BackHandler,
  Platform,
} from "react-native";
import { useRouter } from "expo-router";
import { useAuthStore } from "../../src/stores/auth";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { insightApi, userApi, foodApi } from "../../src/api";
import { Ionicons } from "@expo/vector-icons";
import { confirm } from "../../src/utils/confirm";
import { toast } from "../../src/stores/toast";
import { ErrorState } from "../../components/ErrorState";
import { BottomSheet } from "../../components/BottomSheet";
import { AllergyChips } from "../../components/AllergyChips";
import { GoalField } from "../../components/GoalField";
import type { UserFoodAlert, FoodAdditive } from "../../src/types";
import { ratingColor } from "../../src/utils/colors";
import { GUT_CONDITION_OPTIONS } from "../../src/utils/options";
import {
  colors,
  shadow,
  shadowMd,
  radius,
  spacing,
  fonts,
} from "../../src/utils/theme";
import { SafeScreen } from "../../components/SafeScreen";

const inputStyle = {
  borderWidth: 1,
  borderColor: colors.border,
  borderRadius: radius.sm,
  padding: spacing.md,
  fontSize: 15,
  color: colors.text,
  backgroundColor: colors.bg,
};

export default function ProfileScreen() {
  const { user, logout, setUser } = useAuthStore();
  const router = useRouter();
  const queryClient = useQueryClient();
  const [editingProfile, setEditingProfile] = useState(false);
  const [editingGoals, setEditingGoals] = useState(false);
  const [displayName, setDisplayName] = useState("");
  const [selectedAllergies, setSelectedAllergies] = useState<string[]>([]);
  const [dietaryPreferences, setDietaryPreferences] = useState("");
  const [selectedConditions, setSelectedConditions] = useState<string[]>([]);
  const [calGoal, setCalGoal] = useState("");
  const [proteinGoal, setProteinGoal] = useState("");
  const [carbGoal, setCarbGoal] = useState("");
  const [fatGoal, setFatGoal] = useState("");
  const [fiberGoal, setFiberGoal] = useState("");
  const [showAdditiveBrowser, setShowAdditiveBrowser] = useState(false);
  const [additiveSearch, setAdditiveSearch] = useState("");

  const {
    data: correlations,
    isLoading: loadingCorr,
    isError: corrError,
    refetch: refetchCorr,
  } = useQuery({
    queryKey: ["correlations"],
    queryFn: () => insightApi.correlations(30).then((r) => r.data),
  });

  const {
    data: alerts,
    isLoading: loadingAlerts,
    isError: alertsError,
    refetch: refetchAlerts,
  } = useQuery({
    queryKey: ["alerts"],
    queryFn: () => userApi.getAlerts().then((r) => r.data),
  });

  const removeAlertMutation = useMutation({
    mutationFn: (additiveId: number) => userApi.removeAlert(additiveId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["alerts"] });
      toast.success("Alert removed");
    },
  });

  const addAlertMutation = useMutation({
    mutationFn: (additiveId: number) => userApi.addAlert(additiveId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["alerts"] });
      toast.success("Alert added");
    },
    onError: () => toast.error("Alert already exists or failed"),
  });

  const { data: allAdditives } = useQuery({
    queryKey: ["all-additives"],
    queryFn: () => foodApi.listAdditives().then((r) => r.data),
    enabled: showAdditiveBrowser,
  });

  const profileMutation = useMutation({
    mutationFn: (data: {
      displayName?: string;
      allergies?: string[];
      dietaryPreferences?: string[];
      gutConditions?: string[];
    }) => userApi.updateProfile(data),
    onSuccess: ({ data }) => {
      setUser(data);
      setEditingProfile(false);
      toast.success("Profile updated");
    },
    onError: () => toast.error("Failed to update profile"),
  });

  const goalsMutation = useMutation({
    mutationFn: (data: {
      dailyCalorieGoal: number;
      dailyProteinGoalG: number;
      dailyCarbGoalG: number;
      dailyFatGoalG: number;
      dailyFiberGoalG: number;
    }) => userApi.updateGoals(data),
    onSuccess: () => {
      userApi.getProfile().then(({ data }) => setUser(data));
      queryClient.invalidateQueries({ queryKey: ["daily-summary"] });
      setEditingGoals(false);
      toast.success("Goals updated");
    },
    onError: () => toast.error("Failed to update goals"),
  });

  const openProfileEdit = () => {
    setDisplayName(user?.displayName ?? "");
    setSelectedAllergies(user?.allergies ?? []);
    setDietaryPreferences((user?.dietaryPreferences ?? []).join(", "));
    setSelectedConditions(user?.gutConditions ?? []);
    setEditingProfile(true);
  };

  const openGoalsEdit = () => {
    setCalGoal(String(user?.dailyCalorieGoal ?? 2000));
    setProteinGoal(String(user?.dailyProteinGoalG ?? 50));
    setCarbGoal(String(user?.dailyCarbGoalG ?? 250));
    setFatGoal(String(user?.dailyFatGoalG ?? 65));
    setFiberGoal(String(user?.dailyFiberGoalG ?? 25));
    setEditingGoals(true);
  };

  const toggleAllergy = (a: string) => {
    setSelectedAllergies((prev) =>
      prev.includes(a) ? prev.filter((x) => x !== a) : [...prev, a],
    );
  };

  const toggleCondition = (c: string) => {
    setSelectedConditions((prev) =>
      prev.includes(c) ? prev.filter((x) => x !== c) : [...prev, c],
    );
  };

  const saveProfile = () => {
    profileMutation.mutate({
      displayName: displayName || undefined,
      allergies: selectedAllergies,
      dietaryPreferences: dietaryPreferences
        ? dietaryPreferences
            .split(",")
            .map((s) => s.trim())
            .filter(Boolean)
        : [],
      gutConditions: selectedConditions,
    });
  };

  const saveGoals = () => {
    goalsMutation.mutate({
      dailyCalorieGoal: Number(calGoal) || 2000,
      dailyProteinGoalG: Number(proteinGoal) || 50,
      dailyCarbGoalG: Number(carbGoal) || 250,
      dailyFatGoalG: Number(fatGoal) || 65,
      dailyFiberGoalG: Number(fiberGoal) || 25,
    });
  };

  const handleLogout = () => {
    confirm("Logout", "Are you sure?", logout);
  };

  const [refreshing, setRefreshing] = useState(false);
  const onRefresh = useCallback(async () => {
    setRefreshing(true);
    await Promise.all([refetchCorr(), refetchAlerts()]);
    setRefreshing(false);
  }, [refetchCorr, refetchAlerts]);

  useEffect(() => {
    if (Platform.OS === "android") {
      const handler = BackHandler.addEventListener("hardwareBackPress", () => {
        if (showAdditiveBrowser) {
          setShowAdditiveBrowser(false);
          return true;
        }
        if (editingGoals) {
          setEditingGoals(false);
          return true;
        }
        if (editingProfile) {
          setEditingProfile(false);
          return true;
        }
        return false;
      });
      return () => handler.remove();
    }
  }, [showAdditiveBrowser, editingGoals, editingProfile]);

  return (
    <SafeScreen edges={[]}>
      <ScrollView
        style={{ flex: 1, backgroundColor: colors.bg }}
        showsVerticalScrollIndicator={false}
        refreshControl={
          <RefreshControl
            refreshing={refreshing}
            onRefresh={onRefresh}
            tintColor={colors.primary}
          />
        }
      >
        <View style={{ padding: spacing.xl }}>
          {/* User Card */}
          <View
            style={{
              backgroundColor: colors.card,
              borderRadius: radius.lg,
              padding: spacing.xxl,
              alignItems: "center",
              marginBottom: spacing.lg,
              ...shadowMd,
            }}
          >
            <View
              style={{
                width: 80,
                height: 80,
                borderRadius: 40,
                backgroundColor: colors.primaryBg,
                alignItems: "center",
                justifyContent: "center",
                marginBottom: spacing.md,
                borderWidth: 3,
                borderColor: colors.primaryBorder,
              }}
            >
              <Text
                style={{
                  fontSize: 32,
                  fontWeight: "700",
                  color: colors.primary,
                }}
              >
                {user?.displayName?.[0]?.toUpperCase() ?? "?"}
              </Text>
            </View>
            <Text style={fonts.h2}>{user?.displayName}</Text>
            <Text style={{ ...fonts.body, marginTop: 2 }}>{user?.email}</Text>
            {user?.allergies && user.allergies.length > 0 && (
              <View
                style={{
                  flexDirection: "row",
                  alignItems: "center",
                  marginTop: spacing.sm,
                  backgroundColor: colors.warningBg,
                  paddingHorizontal: 10,
                  paddingVertical: 4,
                  borderRadius: radius.sm,
                }}
              >
                <Text
                  style={{
                    fontSize: 12,
                    color: colors.warning,
                    fontWeight: "600",
                  }}
                >
                  ⚠️ {user.allergies.join(", ")}
                </Text>
              </View>
            )}
            {user?.dietaryPreferences && user.dietaryPreferences.length > 0 && (
              <Text
                style={{
                  fontSize: 12,
                  color: colors.textSecondary,
                  marginTop: 4,
                }}
              >
                🥗 {user.dietaryPreferences.join(", ")}
              </Text>
            )}
            {user?.gutConditions && user.gutConditions.length > 0 && (
              <Text
                style={{
                  fontSize: 12,
                  color: colors.textSecondary,
                  marginTop: 4,
                }}
              >
                🫃 {user.gutConditions.join(", ")}
              </Text>
            )}
            <TouchableOpacity
              onPress={openProfileEdit}
              style={{
                marginTop: spacing.md,
                flexDirection: "row",
                alignItems: "center",
                backgroundColor: colors.secondaryBg,
                paddingHorizontal: 14,
                paddingVertical: 8,
                borderRadius: radius.sm,
              }}
            >
              <Ionicons name="pencil" size={14} color={colors.secondary} />
              <Text
                style={{
                  color: colors.secondary,
                  fontWeight: "600",
                  marginLeft: 6,
                  fontSize: 13,
                }}
              >
                Edit Profile
              </Text>
            </TouchableOpacity>
          </View>

          {/* Daily Goals */}
          <View
            style={{
              backgroundColor: colors.card,
              borderRadius: radius.md,
              padding: spacing.lg,
              marginBottom: spacing.md,
              ...shadow,
            }}
          >
            <View
              style={{
                flexDirection: "row",
                justifyContent: "space-between",
                alignItems: "center",
                marginBottom: spacing.md,
              }}
            >
              <Text style={fonts.h4}>Daily Goals</Text>
              <TouchableOpacity
                onPress={openGoalsEdit}
                style={{
                  flexDirection: "row",
                  alignItems: "center",
                  backgroundColor: colors.secondaryBg,
                  paddingHorizontal: 10,
                  paddingVertical: 6,
                  borderRadius: radius.sm,
                }}
              >
                <Ionicons name="pencil" size={12} color={colors.secondary} />
                <Text
                  style={{
                    color: colors.secondary,
                    fontWeight: "600",
                    marginLeft: 4,
                    fontSize: 12,
                  }}
                >
                  Edit
                </Text>
              </TouchableOpacity>
            </View>
            {[
              {
                label: "Calories",
                value: user?.dailyCalorieGoal ?? 0,
                unit: "cal",
                color: colors.primary,
              },
              {
                label: "Protein",
                value: user?.dailyProteinGoalG ?? 0,
                unit: "g",
                color: colors.protein,
              },
              {
                label: "Carbs",
                value: user?.dailyCarbGoalG ?? 0,
                unit: "g",
                color: colors.carbs,
              },
              {
                label: "Fat",
                value: user?.dailyFatGoalG ?? 0,
                unit: "g",
                color: colors.fat,
              },
              {
                label: "Fiber",
                value: user?.dailyFiberGoalG ?? 0,
                unit: "g",
                color: colors.fiber,
              },
            ].map(({ label, value, unit, color }) => (
              <View
                key={label}
                style={{
                  flexDirection: "row",
                  justifyContent: "space-between",
                  paddingVertical: 8,
                  borderBottomWidth: 1,
                  borderBottomColor: colors.divider,
                }}
              >
                <Text style={{ color: colors.textSecondary, fontSize: 14 }}>
                  {label}
                </Text>
                <Text style={{ fontWeight: "700", color, fontSize: 14 }}>
                  {value} {unit}
                </Text>
              </View>
            ))}
          </View>

          {/* Additive Alerts */}
          {alerts && alerts.length > 0 && (
            <View
              style={{
                backgroundColor: colors.card,
                borderRadius: radius.md,
                padding: spacing.lg,
                marginBottom: spacing.md,
                ...shadow,
              }}
            >
              <View
                style={{
                  flexDirection: "row",
                  alignItems: "center",
                  marginBottom: spacing.md,
                }}
              >
                <Text style={{ fontSize: 16, marginRight: 6 }}>⚠️</Text>
                <Text style={fonts.h4}>Food Additive Alerts</Text>
              </View>
              {alerts.map((a: UserFoodAlert) => (
                <View
                  key={a.additiveId}
                  style={{
                    flexDirection: "row",
                    justifyContent: "space-between",
                    alignItems: "center",
                    paddingVertical: 10,
                    borderTopWidth: 1,
                    borderTopColor: colors.divider,
                  }}
                >
                  <View style={{ flex: 1 }}>
                    <Text
                      style={{
                        fontWeight: "600",
                        color: colors.text,
                        fontSize: 14,
                      }}
                    >
                      {a.name}
                    </Text>
                    <Text
                      style={{
                        fontSize: 12,
                        color: colors.warning,
                        fontWeight: "500",
                      }}
                    >
                      {a.cspiRating}
                    </Text>
                  </View>
                  <TouchableOpacity
                    onPress={() =>
                      confirm(
                        "Remove Alert",
                        `Stop alerting for ${a.name}?`,
                        () => removeAlertMutation.mutate(a.additiveId),
                      )
                    }
                  >
                    <Ionicons
                      name="close-circle"
                      size={22}
                      color={colors.danger}
                    />
                  </TouchableOpacity>
                </View>
              ))}
            </View>
          )}

          {/* Browse Additives */}
          <TouchableOpacity
            onPress={() => setShowAdditiveBrowser(true)}
            style={{
              backgroundColor: colors.card,
              borderRadius: radius.md,
              padding: spacing.lg,
              marginBottom: spacing.md,
              flexDirection: "row",
              alignItems: "center",
              ...shadow,
            }}
          >
            <View
              style={{
                width: 36,
                height: 36,
                borderRadius: radius.sm,
                backgroundColor: colors.accentBg,
                alignItems: "center",
                justifyContent: "center",
                marginRight: spacing.md,
              }}
            >
              <Ionicons name="flask-outline" size={18} color={colors.accent} />
            </View>
            <Text
              style={{
                color: colors.text,
                fontWeight: "600",
                flex: 1,
                fontSize: 15,
              }}
            >
              Browse & Add Additive Alerts
            </Text>
            <Ionicons
              name="chevron-forward"
              size={18}
              color={colors.textMuted}
            />
          </TouchableOpacity>

          {/* Correlations */}
          {correlations && correlations.length > 0 && (
            <View
              style={{
                backgroundColor: colors.card,
                borderRadius: radius.md,
                padding: spacing.lg,
                marginBottom: spacing.md,
                ...shadow,
              }}
            >
              <Text style={{ ...fonts.h4, marginBottom: spacing.md }}>
                Food-Symptom Correlations
              </Text>
              {correlations.slice(0, 5).map((c, i) => (
                <View
                  key={i}
                  style={{
                    flexDirection: "row",
                    justifyContent: "space-between",
                    paddingVertical: 10,
                    borderTopWidth: i > 0 ? 1 : 0,
                    borderTopColor: colors.divider,
                  }}
                >
                  <View style={{ flex: 1 }}>
                    <Text
                      style={{
                        fontWeight: "600",
                        color: colors.text,
                        fontSize: 14,
                      }}
                    >
                      {c.foodOrAdditive}
                    </Text>
                    <Text style={{ fontSize: 12, color: colors.textSecondary }}>
                      → {c.symptomName}
                    </Text>
                  </View>
                  <View style={{ alignItems: "flex-end" }}>
                    <Text
                      style={{
                        fontWeight: "700",
                        color: colors.danger,
                        fontSize: 14,
                      }}
                    >
                      {c.occurrences}x
                    </Text>
                    <Text style={{ fontSize: 11, color: colors.textMuted }}>
                      avg {c.averageSeverity.toFixed(1)}/10
                    </Text>
                  </View>
                </View>
              ))}
            </View>
          )}

          {/* Settings */}
          <TouchableOpacity
            onPress={() => router.push("/settings")}
            style={{
              backgroundColor: colors.card,
              borderRadius: radius.md,
              padding: spacing.lg,
              flexDirection: "row",
              alignItems: "center",
              marginBottom: spacing.sm,
              ...shadow,
            }}
          >
            <View
              style={{
                width: 36,
                height: 36,
                borderRadius: radius.sm,
                backgroundColor: colors.borderLight,
                alignItems: "center",
                justifyContent: "center",
                marginRight: spacing.md,
              }}
            >
              <Ionicons
                name="settings-outline"
                size={18}
                color={colors.textSecondary}
              />
            </View>
            <Text
              style={{
                color: colors.text,
                fontWeight: "600",
                flex: 1,
                fontSize: 15,
              }}
            >
              Settings
            </Text>
            <Ionicons
              name="chevron-forward"
              size={18}
              color={colors.textMuted}
            />
          </TouchableOpacity>

          {/* Logout */}
          <TouchableOpacity
            onPress={handleLogout}
            style={{
              backgroundColor: colors.dangerBg,
              borderRadius: radius.md,
              padding: spacing.lg,
              flexDirection: "row",
              alignItems: "center",
              justifyContent: "center",
              marginTop: spacing.sm,
              borderWidth: 1,
              borderColor: colors.dangerBorder,
            }}
          >
            <Ionicons name="log-out-outline" size={20} color={colors.danger} />
            <Text
              style={{
                color: colors.danger,
                fontWeight: "600",
                marginLeft: 8,
                fontSize: 15,
              }}
            >
              Log Out
            </Text>
          </TouchableOpacity>
        </View>

        {/* Edit Profile Modal */}
        <BottomSheet
          visible={editingProfile}
          onClose={() => setEditingProfile(false)}
        >
          <Text style={{ ...fonts.h3, marginBottom: spacing.lg }}>
            Edit Profile
          </Text>
          <Text style={{ ...fonts.caption, marginBottom: 4 }}>
            Display Name
          </Text>
          <TextInput
            value={displayName}
            onChangeText={setDisplayName}
            style={inputStyle}
            placeholder="Your name"
            placeholderTextColor={colors.textLight}
          />
          <Text
            style={{
              ...fonts.caption,
              marginBottom: 4,
              marginTop: spacing.md,
            }}
          >
            Allergies
          </Text>
          <View style={{ marginBottom: spacing.sm }}>
            <AllergyChips
              selected={selectedAllergies}
              onToggle={toggleAllergy}
            />
          </View>

          <Text
            style={{
              ...fonts.caption,
              marginBottom: 4,
              marginTop: spacing.md,
            }}
          >
            Dietary Preferences
          </Text>
          <TextInput
            value={dietaryPreferences}
            onChangeText={setDietaryPreferences}
            style={inputStyle}
            placeholder="e.g. vegetarian, keto"
            placeholderTextColor={colors.textLight}
          />

          <Text
            style={{
              ...fonts.caption,
              marginBottom: 4,
              marginTop: spacing.md,
            }}
          >
            Gut Conditions
          </Text>
          <View
            style={{
              flexDirection: "row",
              flexWrap: "wrap",
              gap: 8,
              marginBottom: spacing.sm,
            }}
          >
            {GUT_CONDITION_OPTIONS.map((c) => {
              const active = selectedConditions.includes(c.id);
              return (
                <TouchableOpacity
                  key={c.id}
                  onPress={() => toggleCondition(c.id)}
                  style={{
                    backgroundColor: active
                      ? colors.primaryBg
                      : colors.borderLight,
                    borderWidth: 1,
                    borderColor: active ? colors.primaryLight : colors.border,
                    borderRadius: radius.full,
                    paddingHorizontal: spacing.lg,
                    paddingVertical: spacing.sm,
                    flexDirection: "row",
                    alignItems: "center",
                    gap: 4,
                  }}
                >
                  <Text style={{ fontSize: 14 }}>{c.emoji}</Text>
                  <Text
                    style={{
                      fontWeight: "600",
                      color: active ? colors.primary : colors.textMuted,
                      fontSize: 13,
                    }}
                  >
                    {c.label}
                  </Text>
                </TouchableOpacity>
              );
            })}
          </View>

          <View
            style={{
              flexDirection: "row",
              justifyContent: "flex-end",
              marginTop: spacing.xl,
              gap: 12,
            }}
          >
            <TouchableOpacity
              onPress={() => setEditingProfile(false)}
              style={{ paddingHorizontal: 20, paddingVertical: 10 }}
            >
              <Text style={{ color: colors.textMuted, fontWeight: "600" }}>
                Cancel
              </Text>
            </TouchableOpacity>
            <TouchableOpacity
              onPress={saveProfile}
              disabled={profileMutation.isPending}
              style={{
                backgroundColor: colors.primary,
                paddingHorizontal: 20,
                paddingVertical: 10,
                borderRadius: radius.sm,
              }}
            >
              {profileMutation.isPending ? (
                <ActivityIndicator color="#fff" size="small" />
              ) : (
                <Text style={{ color: "#fff", fontWeight: "600" }}>Save</Text>
              )}
            </TouchableOpacity>
          </View>
        </BottomSheet>

        {/* Edit Goals Modal */}
        <BottomSheet
          visible={editingGoals}
          onClose={() => setEditingGoals(false)}
        >
          <Text style={{ ...fonts.h3, marginBottom: spacing.lg }}>
            Edit Daily Goals
          </Text>
          {[
            { label: "Calories (cal)", value: calGoal, set: setCalGoal },
            { label: "Protein (g)", value: proteinGoal, set: setProteinGoal },
            { label: "Carbs (g)", value: carbGoal, set: setCarbGoal },
            { label: "Fat (g)", value: fatGoal, set: setFatGoal },
            { label: "Fiber (g)", value: fiberGoal, set: setFiberGoal },
          ].map(({ label, value, set }) => (
            <GoalField
              key={label}
              label={label}
              value={value}
              onChangeText={set}
            />
          ))}
          <View
            style={{
              flexDirection: "row",
              justifyContent: "flex-end",
              marginTop: spacing.xl,
              gap: 12,
            }}
          >
            <TouchableOpacity
              onPress={() => setEditingGoals(false)}
              style={{ paddingHorizontal: 20, paddingVertical: 10 }}
            >
              <Text style={{ color: colors.textMuted, fontWeight: "600" }}>
                Cancel
              </Text>
            </TouchableOpacity>
            <TouchableOpacity
              onPress={saveGoals}
              disabled={goalsMutation.isPending}
              style={{
                backgroundColor: colors.primary,
                paddingHorizontal: 20,
                paddingVertical: 10,
                borderRadius: radius.sm,
              }}
            >
              {goalsMutation.isPending ? (
                <ActivityIndicator color="#fff" size="small" />
              ) : (
                <Text style={{ color: "#fff", fontWeight: "600" }}>Save</Text>
              )}
            </TouchableOpacity>
          </View>
        </BottomSheet>

        {/* Additive Browser Modal */}
        <BottomSheet
          visible={showAdditiveBrowser}
          onClose={() => setShowAdditiveBrowser(false)}
          maxHeight="80%"
        >
          <View
            style={{
              flexDirection: "row",
              justifyContent: "space-between",
              alignItems: "center",
              marginBottom: spacing.lg,
            }}
          >
            <Text style={fonts.h3}>Browse Additives</Text>
            <TouchableOpacity
              onPress={() => setShowAdditiveBrowser(false)}
              style={{ padding: 4 }}
            >
              <Ionicons name="close" size={24} color={colors.textSecondary} />
            </TouchableOpacity>
          </View>
          <TextInput
            placeholder="Search additives..."
            placeholderTextColor={colors.textLight}
            value={additiveSearch}
            onChangeText={setAdditiveSearch}
            style={{ ...inputStyle, marginBottom: spacing.md }}
          />
          <ScrollView style={{ maxHeight: 400 }}>
            {(allAdditives ?? [])
              .filter(
                (a) =>
                  !additiveSearch ||
                  a.name.toLowerCase().includes(additiveSearch.toLowerCase()) ||
                  (a.eNumber &&
                    a.eNumber
                      .toLowerCase()
                      .includes(additiveSearch.toLowerCase())),
              )
              .map((add) => {
                const isWatched = (alerts ?? []).some(
                  (al: UserFoodAlert) => al.additiveId === add.id,
                );
                return (
                  <View
                    key={add.id}
                    style={{
                      flexDirection: "row",
                      alignItems: "center",
                      paddingVertical: 12,
                      borderBottomWidth: 1,
                      borderBottomColor: colors.divider,
                    }}
                  >
                    <View style={{ flex: 1 }}>
                      <Text
                        style={{
                          fontWeight: "600",
                          color: colors.text,
                          fontSize: 14,
                        }}
                      >
                        {add.name} {add.eNumber ? `(${add.eNumber})` : ""}
                      </Text>
                      <Text
                        style={{
                          fontSize: 12,
                          color: ratingColor(add.cspiRating),
                          fontWeight: "600",
                        }}
                      >
                        {add.cspiRating}
                      </Text>
                    </View>
                    {isWatched ? (
                      <View
                        style={{
                          backgroundColor: colors.primaryBg,
                          borderRadius: 6,
                          paddingHorizontal: 10,
                          paddingVertical: 6,
                        }}
                      >
                        <Text
                          style={{
                            fontSize: 12,
                            fontWeight: "600",
                            color: colors.primary,
                          }}
                        >
                          ✓ Watching
                        </Text>
                      </View>
                    ) : (
                      <TouchableOpacity
                        onPress={() => addAlertMutation.mutate(add.id)}
                        style={{
                          backgroundColor: colors.dangerBg,
                          borderRadius: 6,
                          paddingHorizontal: 10,
                          paddingVertical: 6,
                        }}
                      >
                        <Text
                          style={{
                            fontSize: 12,
                            fontWeight: "600",
                            color: colors.danger,
                          }}
                        >
                          + Watch
                        </Text>
                      </TouchableOpacity>
                    )}
                  </View>
                );
              })}
          </ScrollView>
        </BottomSheet>
      </ScrollView>
    </SafeScreen>
  );
}
