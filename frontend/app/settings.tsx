import React, { useEffect, useState } from "react";
import {
  View,
  Text,
  ScrollView,
  TouchableOpacity,
  TextInput,
  ActivityIndicator,
  Alert,
  KeyboardAvoidingView,
  Platform,
  Linking,
  Switch,
  Share,
} from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { useAuthStore } from "../src/stores/auth";
import { authApi, mealApi, userApi } from "../src/api";
import type { LinkedAccessToken, PairingCodeResponse } from "../src/types";
import { toast } from "../src/stores/toast";
import Constants from "expo-constants";
import { SafeScreen } from "../components/SafeScreen";
import { useRouter } from "expo-router";
import {
  useSubscriptionStore,
  presentPaywall,
} from "../src/stores/subscription";
import {
  useThemeColors,
  useThemeShadow,
  useThemeStore,
} from "../src/stores/theme";
import { radius, spacing } from "../src/utils/theme";
import { PRIVACY_POLICY_URL } from "../src/utils/constants";
import * as haptics from "../src/utils/haptics";
import {
  activeHealthBridge,
  getWriteMealsEnabled,
  setWriteMealsEnabled,
  syncHealthImport,
} from "../src/services/health";
import {
  loadReminderPrefs,
  saveReminderPrefs,
  ensureNotificationPermissionAsync,
  applyReminderSchedule,
  syncStreakNudge,
  REMINDER_TIME_CHOICES,
  DEFAULT_REMINDER_PREFS,
  type ReminderPrefs,
} from "../src/utils/notifications";
type ThemePref = "light" | "dark" | "system";
const THEME_OPTIONS: { value: ThemePref; label: string; icon: string }[] = [
  { value: "light", label: "Light", icon: "sunny-outline" },
  { value: "dark", label: "Dark", icon: "moon-outline" },
  { value: "system", label: "System", icon: "phone-portrait-outline" },
];

export default function SettingsScreen() {
  const { logout } = useAuthStore();
  const router = useRouter();
  const colors = useThemeColors();
  const { shadow } = useThemeShadow();
  const preference = useThemeStore((s) => s.preference);
  const setPreference = useThemeStore((s) => s.setPreference);
  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [changingPassword, setChangingPassword] = useState(false);
  const [showPasswordForm, setShowPasswordForm] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [restoring, setRestoring] = useState(false);
  const [showAiAssistants, setShowAiAssistants] = useState(false);
  const [tokens, setTokens] = useState<LinkedAccessToken[]>([]);
  const [loadingTokens, setLoadingTokens] = useState(false);
  const [generatingCode, setGeneratingCode] = useState(false);
  const [pairingCode, setPairingCode] = useState<PairingCodeResponse | null>(null);
  const [now, setNow] = useState(() => Date.now());
  const [revokingTokenId, setRevokingTokenId] = useState<string | null>(null);
  const appVersion = Constants.expoConfig?.version ?? "1.0.0";
  const { restore, isPro, isLoaded: subLoaded } = useSubscriptionStore();
  const [healthAvailable, setHealthAvailable] = useState<boolean | null>(null);
  const [healthConnected, setHealthConnected] = useState(false);
  const [connectingHealth, setConnectingHealth] = useState(false);
  const [writeMealsToHealth, setWriteMealsToHealth] = useState(false);
  const [importingHealth, setImportingHealth] = useState(false);

  const [exportingData, setExportingData] = useState(false);
  const [reminderPrefs, setReminderPrefs] = useState<ReminderPrefs>(DEFAULT_REMINDER_PREFS);
  const [reminderLoading, setReminderLoading] = useState(false);

  useEffect(() => {
    let mounted = true;
    loadReminderPrefs().then((prefs) => {
      if (mounted) setReminderPrefs(prefs);
    });
    return () => {
      mounted = false;
    };
  }, []);

  const handleToggleDailyReminder = (enabled: boolean) => {
    haptics.selection();
    if (!enabled) {
      const next = { ...reminderPrefs, dailyEnabled: false };
      setReminderPrefs(next);
      saveReminderPrefs(next).then(() => applyReminderSchedule(next));
      return;
    }

    Alert.alert(
      "Logging reminder",
      "Receive a daily device-local reminder to log your meals. You can choose the exact time that works best for your schedule.",
      [
        {
          text: "Not now",
          style: "cancel",
        },
        {
          text: "Enable",
          onPress: async () => {
            const granted = await ensureNotificationPermissionAsync();
            if (!granted) {
              toast.error("Notification permission denied");
              return;
            }
            const next = { ...reminderPrefs, dailyEnabled: true };
            setReminderPrefs(next);
            await saveReminderPrefs(next);
            const res = await applyReminderSchedule(next);
            if (res.denied) {
              setReminderPrefs((prev) => ({ ...prev, dailyEnabled: false }));
              await saveReminderPrefs({ ...next, dailyEnabled: false });
              toast.error("Notification permission denied");
            }
          },
        },
      ],
    );
  };

  const handleSelectDailyTime = async (choice: { hour: number; minute: number }) => {
    haptics.selection();
    const next: ReminderPrefs = {
      ...reminderPrefs,
      dailyHour: choice.hour,
      dailyMinute: choice.minute,
    };
    setReminderPrefs(next);
    await saveReminderPrefs(next);
    if (next.dailyEnabled) {
      await applyReminderSchedule(next);
    }
  };

  const handleToggleStreakNudge = (enabled: boolean) => {
    haptics.selection();
    if (!enabled) {
      const next = { ...reminderPrefs, nudgeEnabled: false };
      setReminderPrefs(next);
      saveReminderPrefs(next).then(() => syncStreakNudge(next, false));
      return;
    }

    Alert.alert(
      "Logging reminder",
      "Receive an evening device-local nudge if you haven't logged any meals today, keeping your streak alive.",
      [
        {
          text: "Not now",
          style: "cancel",
        },
        {
          text: "Enable",
          onPress: async () => {
            const granted = await ensureNotificationPermissionAsync();
            if (!granted) {
              toast.error("Notification permission denied");
              return;
            }
            const next = { ...reminderPrefs, nudgeEnabled: true };
            setReminderPrefs(next);
            await saveReminderPrefs(next);
            await syncStreakNudge(next, false);
          },
        },
      ],
    );
  };

  const handleSelectStreakHour = async (hour: number) => {
    haptics.selection();
    const next: ReminderPrefs = {
      ...reminderPrefs,
      nudgeHour: hour,
    };
    setReminderPrefs(next);
    await saveReminderPrefs(next);
    if (next.nudgeEnabled) {
      await syncStreakNudge(next, false);
    }
  };

  useEffect(() => {
    let mounted = true;
    if (!activeHealthBridge) {
      setHealthAvailable(false);
      return;
    }
    activeHealthBridge.isAvailable().then((avail) => {
      if (mounted) setHealthAvailable(avail);
    });
    getWriteMealsEnabled(activeHealthBridge.platformId).then((enabled) => {
      if (mounted) setWriteMealsToHealth(enabled);
    });
    return () => {
      mounted = false;
    };
  }, []);

  useEffect(() => {
    if (!pairingCode) return;
    setNow(Date.now());
    const interval = setInterval(() => {
      setNow(Date.now());
    }, 1000);
    return () => clearInterval(interval);
  }, [pairingCode]);

  const handleConnectHealth = async () => {
    if (!activeHealthBridge) return;
    setConnectingHealth(true);
    try {
      const granted = await activeHealthBridge.requestPermissions();
      if (granted) {
        setHealthConnected(true);
        toast.success(
          Platform.OS === "android"
            ? "Connected to Google Health Connect"
            : "Connected to Apple Health",
        );
        haptics.success();
      } else {
        toast.error("Permission was not granted");
      }
    } catch {
      toast.error("Failed to request health permissions");
    } finally {
      setConnectingHealth(false);
    }
  };

  const handleToggleWriteMeals = async (value: boolean) => {
    if (!activeHealthBridge) return;
    haptics.selection();
    setWriteMealsToHealth(value);
    await setWriteMealsEnabled(activeHealthBridge.platformId, value);
  };

  const handleImportHealthMeals = async () => {
    setImportingHealth(true);
    haptics.light();
    try {
      await syncHealthImport();
    } finally {
      setImportingHealth(false);
    }
  };


  const loadTokens = async () => {
    setLoadingTokens(true);
    try {
      const res = await userApi.listTokens();
      setTokens(res.data);
    } catch {
      toast.error("Failed to load connected assistants");
    } finally {
      setLoadingTokens(false);
    }
  };

  const toggleAiAssistants = () => {
    const nextState = !showAiAssistants;
    setShowAiAssistants(nextState);
    if (nextState) {
      loadTokens();
    }
  };

  const handleGeneratePairingCode = async () => {
    setGeneratingCode(true);
    try {
      const res = await userApi.createPairingCode();
      setPairingCode(res.data);
      haptics.success();
    } catch {
      toast.error("Failed to generate pairing code");
    } finally {
      setGeneratingCode(false);
    }
  };

  const handleRevokeToken = (token: LinkedAccessToken) => {
    Alert.alert(
      "Revoke Access",
      `Are you sure you want to revoke access for "${token.name}"? This assistant will no longer be able to access your data.`,
      [
        { text: "Cancel", style: "cancel" },
        {
          text: "Revoke",
          style: "destructive",
          onPress: async () => {
            setRevokingTokenId(token.id);
            try {
              await userApi.revokeToken(token.id);
              toast.success("Assistant access revoked");
              haptics.success();
              await loadTokens();
            } catch {
              toast.error("Failed to revoke access");
            } finally {
              setRevokingTokenId(null);
            }
          },
        },
      ],
    );
  };

  const formatLastUsed = (lastUsedAt: string | null) => {
    if (!lastUsedAt) return "never";
    const diffMs = Date.now() - new Date(lastUsedAt).getTime();
    if (diffMs < 0 || isNaN(diffMs)) return "just now";
    const minutes = Math.floor(diffMs / 60000);
    if (minutes < 1) return "just now";
    if (minutes < 60) return `${minutes}m ago`;
    const hours = Math.floor(minutes / 60);
    if (hours < 24) return `${hours}h ago`;
    const days = Math.floor(hours / 24);
    if (days < 30) return `${days}d ago`;
    return new Date(lastUsedAt).toLocaleDateString();
  };

  const formatCodeExpiry = (expiresAt: string) => {
    const diffMs = new Date(expiresAt).getTime() - now;
    const minutes = Math.max(0, Math.ceil(diffMs / 60000));
    return minutes > 0 ? `Expires in ${minutes} min` : "Expired";
  };
  const handleChangePassword = async () => {
    if (!currentPassword || !newPassword) {
      toast.error("Please fill in both fields");
      return;
    }
    if (newPassword.length < 8) {
      toast.error("New password must be at least 8 characters");
      return;
    }
    setChangingPassword(true);
    try {
      await authApi.changePassword({ currentPassword, newPassword });
      toast.success("Password changed successfully");
      setShowPasswordForm(false);
      setCurrentPassword("");
      setNewPassword("");
    } catch {
      toast.error("Failed to change password. Check your current password.");
    } finally {
      setChangingPassword(false);
    }
  };

  const handleDeleteAccount = () => {
    Alert.alert(
      "Delete Account",
      "This will permanently delete your account and all data. This action cannot be undone.",
      [
        { text: "Cancel", style: "cancel" },
        {
          text: "Delete",
          style: "destructive",
          onPress: async () => {
            setDeleting(true);
            try {
              await userApi.deleteAccount();
              toast.success("Account deleted");
              logout();
            } catch {
              toast.error("Failed to delete account");
            } finally {
              setDeleting(false);
            }
          },
        },
      ],
    );
  };
  const handleExportData = async () => {
    setExportingData(true);
    try {
      const res = await mealApi.export();
      await Share.share({
        title: "GutLens data export",
        message: JSON.stringify(res.data, null, 2),
      });
      haptics.success();
    } catch (err: unknown) {
      // If user cancelled the share dialog, do not treat as error
      if (err instanceof Error && err.name === "AbortError") return;
      toast.error("Failed to export data");
    } finally {
      setExportingData(false);
    }
  };

  return (
    <SafeScreen edges={["bottom"]}>
      <KeyboardAvoidingView
        behavior={Platform.OS === "ios" ? "padding" : "height"}
        style={{ flex: 1 }}
      >
        <ScrollView
          style={{ flex: 1, backgroundColor: colors.bg }}
          keyboardShouldPersistTaps="handled"
        >
          <View style={{ padding: 20 }}>
            {/* Appearance */}
            <View
              style={{
                backgroundColor: colors.card,
                borderRadius: 12,
                padding: 16,
                marginBottom: 12,
                ...shadow,
              }}
            >
              <Text
                style={{
                  fontSize: 16,
                  fontWeight: "600",
                  color: colors.text,
                  marginBottom: 12,
                }}
                accessibilityRole="header"
              >
                Appearance
              </Text>
              <View style={{ flexDirection: "row", gap: 8 }}>
                {THEME_OPTIONS.map((opt) => {
                  const active = preference === opt.value;
                  return (
                    <TouchableOpacity
                      key={opt.value}
                      onPress={() => {
                        haptics.selection();
                        setPreference(opt.value);
                      }}
                      accessibilityRole="button"
                      accessibilityLabel={`${opt.label} theme`}
                      accessibilityState={{ selected: active }}
                      style={{
                        flex: 1,
                        flexDirection: "row",
                        alignItems: "center",
                        justifyContent: "center",
                        gap: 6,
                        paddingVertical: 10,
                        borderRadius: radius.sm,
                        backgroundColor: active ? colors.primaryBg : colors.bg,
                        borderWidth: 1.5,
                        borderColor: active ? colors.primary : colors.border,
                      }}
                    >
                      <Ionicons
                        name={opt.icon as any}
                        size={16}
                        color={active ? colors.primary : colors.textMuted}
                      />
                      <Text
                        style={{
                          fontSize: 13,
                          fontWeight: "600",
                          color: active ? colors.primary : colors.textSecondary,
                        }}
                      >
                        {opt.label}
                      </Text>
                    </TouchableOpacity>
                  );
                })}
              </View>
            </View>

            {/* Subscription */}
            <View
              style={{
                backgroundColor: colors.card,
                borderRadius: 12,
                padding: 16,
                marginBottom: 12,
              }}
            >
              <View
                style={{
                  flexDirection: "row",
                  alignItems: "center",
                  justifyContent: "space-between",
                }}
              >
                <View style={{ flexDirection: "row", alignItems: "center" }}>
                  <Ionicons
                    name="diamond-outline"
                    size={20}
                    color={colors.accent}
                  />
                  <Text
                    style={{
                      fontSize: 16,
                      fontWeight: "600",
                      color: colors.text,
                      marginLeft: 12,
                    }}
                  >
                    GutLens Pro
                  </Text>
                </View>
                {!subLoaded ? (
                  <ActivityIndicator size="small" color={colors.textMuted} />
                ) : isPro ? (
                  <View
                    style={{
                      backgroundColor: colors.accentBg,
                      borderRadius: radius.full,
                      paddingHorizontal: 10,
                      paddingVertical: 3,
                    }}
                  >
                    <Text
                      style={{
                        fontSize: 12,
                        fontWeight: "700",
                        color: colors.accent,
                      }}
                    >
                      Active
                    </Text>
                  </View>
                ) : (
                  <View
                    style={{
                      backgroundColor: colors.bg,
                      borderWidth: 1,
                      borderColor: colors.border,
                      borderRadius: radius.full,
                      paddingHorizontal: 10,
                      paddingVertical: 3,
                    }}
                  >
                    <Text
                      style={{
                        fontSize: 12,
                        fontWeight: "700",
                        color: colors.textMuted,
                      }}
                    >
                      Free
                    </Text>
                  </View>
                )}
              </View>
              <Text
                style={{
                  fontSize: 13,
                  color: colors.textSecondary,
                  marginTop: 8,
                  lineHeight: 18,
                }}
              >
                {isPro
                  ? "All AI features unlocked — thanks for supporting GutLens."
                  : "AI meal photo scans, nutrition-label parsing, Describe-with-AI and your AI Coach."}
              </Text>
              {!subLoaded ? null : !isPro ? (
                <TouchableOpacity
                  onPress={async () => {
                    haptics.light();
                    const ok = await presentPaywall();
                    if (ok) toast.success("Welcome to GutLens Pro!");
                  }}
                  accessibilityRole="button"
                  accessibilityLabel="Upgrade to GutLens Pro"
                  style={{
                    marginTop: 12,
                    backgroundColor: colors.accent,
                    borderRadius: radius.sm,
                    paddingVertical: 10,
                    alignItems: "center",
                  }}
                >
                  <Text
                    style={{
                      color: colors.textOnPrimary,
                      fontWeight: "700",
                      fontSize: 14,
                    }}
                  >
                    Upgrade to Pro
                  </Text>
                </TouchableOpacity>
              ) : null}
            </View>

            {/* Restore Purchases */}
            <View
              style={{
                backgroundColor: colors.card,
                borderRadius: 12,
                padding: 16,
                marginBottom: 12,
              }}
            >
              <TouchableOpacity
                onPress={async () => {
                  setRestoring(true);
                  try {
                    const restored = await restore();
                    if (restored) {
                      toast.success("Purchases restored successfully");
                    } else {
                      toast.info("No purchases found to restore");
                    }
                  } catch {
                    toast.error("Failed to restore purchases");
                  } finally {
                    setRestoring(false);
                  }
                }}
                disabled={restoring}
                accessibilityRole="button"
                accessibilityLabel="Restore Purchases"
                style={{
                  flexDirection: "row",
                  alignItems: "center",
                  justifyContent: "space-between",
                }}
              >
                <View style={{ flexDirection: "row", alignItems: "center" }}>
                  <Ionicons
                    name="refresh-circle-outline"
                    size={20}
                    color={colors.textSecondary}
                  />
                  <Text
                    style={{
                      fontSize: 16,
                      fontWeight: "600",
                      color: colors.textSecondary,
                      marginLeft: 12,
                    }}
                  >
                    Restore Purchases
                  </Text>
                </View>
                {restoring ? (
                  <ActivityIndicator size="small" color={colors.textMuted} />
                ) : (
                  <Ionicons
                    name="chevron-forward"
                    size={20}
                    color={colors.textMuted}
                  />
                )}
              </TouchableOpacity>
            </View>
            {/* Health Platforms */}
            {Platform.OS !== "web" && (
              <View
                style={{
                  backgroundColor: colors.card,
                  borderRadius: 12,
                  padding: 16,
                  marginBottom: 12,
                  ...shadow,
                }}
              >
                <View
                  style={{
                    flexDirection: "row",
                    alignItems: "center",
                    justifyContent: "space-between",
                    marginBottom: 12,
                  }}
                >
                  <View style={{ flexDirection: "row", alignItems: "center" }}>
                    <Ionicons
                      name="heart-outline"
                      size={20}
                      color={colors.primary}
                    />
                    <Text
                      style={{
                        fontSize: 16,
                        fontWeight: "600",
                        color: colors.text,
                        marginLeft: 12,
                      }}
                      accessibilityRole="header"
                    >
                      {Platform.OS === "android"
                        ? "Google Health Connect"
                        : "Apple Health"}
                    </Text>
                  </View>
                  <Text
                    style={{
                      fontSize: 12,
                      fontWeight: "500",
                      color:
                        healthAvailable === false
                          ? colors.danger
                          : healthConnected
                          ? colors.primary
                          : colors.textMuted,
                    }}
                  >
                    {healthAvailable === null
                      ? "Checking..."
                      : healthAvailable === false
                      ? "Unavailable"
                      : healthConnected
                      ? "Connected"
                      : "Available"}
                  </Text>
                </View>

                <Text
                  style={{
                    fontSize: 13,
                    color: colors.textMuted,
                    lineHeight: 18,
                    marginBottom: 16,
                  }}
                >
                  {Platform.OS === "android"
                    ? "Sync meals with Google Health Connect to import nutrition history or automatically export meals logged in GutLens."
                    : "Sync meals with Apple HealthKit to import nutrition history or automatically export meals logged in GutLens."
                  }
                </Text>

                {healthAvailable !== false && (
                  <>
                    {!healthConnected && (
                      <TouchableOpacity
                        onPress={handleConnectHealth}
                        disabled={connectingHealth}
                        accessibilityRole="button"
                        accessibilityLabel={`Connect ${
                          Platform.OS === "android"
                            ? "Health Connect"
                            : "Apple Health"
                        }`}
                        style={{
                          flexDirection: "row",
                          alignItems: "center",
                          justifyContent: "center",
                          backgroundColor: colors.primaryBg,
                          borderColor: colors.primary,
                          borderWidth: 1,
                          borderRadius: radius.md,
                          paddingVertical: 10,
                          marginBottom: 14,
                          gap: 6,
                        }}
                      >
                        {connectingHealth ? (
                          <ActivityIndicator
                            size="small"
                            color={colors.primary}
                          />
                        ) : (
                          <>
                            <Ionicons
                              name="link-outline"
                              size={16}
                              color={colors.primary}
                            />
                            <Text
                              style={{
                                fontSize: 14,
                                fontWeight: "600",
                                color: colors.primary,
                              }}
                            >
                              Connect Permissions
                            </Text>
                          </>
                        )}
                      </TouchableOpacity>
                    )}

                    {/* Save meals toggle */}
                    <View
                      style={{
                        flexDirection: "row",
                        alignItems: "center",
                        justifyContent: "space-between",
                        paddingVertical: 8,
                        borderTopWidth: 1,
                        borderTopColor: colors.border,
                      }}
                    >
                      <View style={{ flex: 1, marginRight: 12 }}>
                        <Text
                          style={{
                            fontSize: 14,
                            fontWeight: "500",
                            color: colors.text,
                          }}
                        >
                          {Platform.OS === "android"
                            ? "Save meals to Health Connect"
                            : "Save meals to Apple Health"}
                        </Text>
                        <Text
                          style={{
                            fontSize: 12,
                            color: colors.textMuted,
                            marginTop: 2,
                          }}
                        >
                          Automatically write newly logged meals
                        </Text>
                      </View>
                      <Switch
                        value={writeMealsToHealth}
                        onValueChange={handleToggleWriteMeals}
                        trackColor={{
                          false: colors.border,
                          true: colors.primary,
                        }}
                        accessibilityRole="switch"
                        accessibilityLabel={
                          Platform.OS === "android"
                            ? "Save meals to Health Connect toggle"
                            : "Save meals to Apple Health toggle"
                        }
                      />
                    </View>

                    {/* Import history button */}
                    <TouchableOpacity
                      onPress={handleImportHealthMeals}
                      disabled={importingHealth}
                      accessibilityRole="button"
                      accessibilityLabel="Import nutrition history"
                      style={{
                        flexDirection: "row",
                        alignItems: "center",
                        justifyContent: "center",
                        backgroundColor: colors.primary,
                        borderRadius: radius.md,
                        paddingVertical: 10,
                        marginTop: 12,
                        gap: 8,
                      }}
                    >
                      {importingHealth ? (
                        <ActivityIndicator size="small" color={colors.textOnPrimary} />
                      ) : (
                        <>
                          <Ionicons
                            name="download-outline"
                            size={16}
                            color={colors.textOnPrimary}
                          />
                          <Text
                            style={{
                              fontSize: 14,
                              fontWeight: "600",
                              color: colors.textOnPrimary,
                            }}
                          >
                            Import History
                          </Text>
                        </>
                      )}
                    </TouchableOpacity>
                  </>
                )}
              </View>
            )}

            {/* Reminders */}
            {Platform.OS !== "web" && (
              <View
                style={{
                  backgroundColor: colors.card,
                  borderRadius: 12,
                  padding: 16,
                  marginBottom: 12,
                  ...shadow,
                }}
              >
                <View
                  style={{
                    flexDirection: "row",
                    alignItems: "center",
                    marginBottom: 12,
                  }}
                >
                  <Ionicons
                    name="notifications-outline"
                    size={20}
                    color={colors.primary}
                  />
                  <Text
                    style={{
                      fontSize: 16,
                      fontWeight: "600",
                      color: colors.text,
                      marginLeft: 12,
                    }}
                    accessibilityRole="header"
                  >
                    Reminders
                  </Text>
                </View>

                <Text
                  style={{
                    fontSize: 13,
                    color: colors.textMuted,
                    lineHeight: 18,
                    marginBottom: 16,
                  }}
                >
                  Local, private reminders on this device. No notification tokens are sent to any server.
                </Text>

                {/* Daily Logging Reminder */}
                <View
                  style={{
                    paddingVertical: 8,
                    borderTopWidth: 1,
                    borderTopColor: colors.border,
                  }}
                >
                  <View
                    style={{
                      flexDirection: "row",
                      alignItems: "center",
                      justifyContent: "space-between",
                    }}
                  >
                    <View style={{ flex: 1, marginRight: 12 }}>
                      <Text
                        style={{
                          fontSize: 14,
                          fontWeight: "500",
                          color: colors.text,
                        }}
                      >
                        Daily logging reminder
                      </Text>
                      <Text
                        style={{
                          fontSize: 12,
                          color: colors.textMuted,
                          marginTop: 2,
                        }}
                      >
                        A daily nudge to log your meals
                      </Text>
                    </View>
                    <Switch
                      value={reminderPrefs.dailyEnabled}
                      onValueChange={handleToggleDailyReminder}
                      trackColor={{
                        false: colors.border,
                        true: colors.primary,
                      }}
                      accessibilityRole="switch"
                      accessibilityLabel="Daily logging reminder toggle"
                    />
                  </View>

                  <ScrollView
                    horizontal
                    showsHorizontalScrollIndicator={false}
                    contentContainerStyle={{ gap: 8, paddingTop: 12 }}
                  >
                    {REMINDER_TIME_CHOICES.map((choice) => {
                      const isSelected =
                        reminderPrefs.dailyHour === choice.hour &&
                        reminderPrefs.dailyMinute === choice.minute;
                      return (
                        <TouchableOpacity
                          key={`${choice.hour}:${choice.minute}`}
                          onPress={() => handleSelectDailyTime(choice)}
                          accessibilityRole="button"
                          accessibilityLabel={`Reminder time ${choice.label}`}
                          accessibilityState={{ selected: isSelected }}
                          style={{
                            paddingHorizontal: 12,
                            paddingVertical: 6,
                            borderRadius: radius.full,
                            backgroundColor: isSelected
                              ? colors.primary
                              : colors.bg,
                            borderWidth: 1,
                            borderColor: isSelected
                              ? colors.primary
                              : colors.border,
                          }}
                        >
                          <Text
                            style={{
                              fontSize: 13,
                              fontWeight: "600",
                              color: isSelected
                                ? colors.textOnPrimary
                                 : colors.text,
                            }}
                          >
                            {choice.label}
                          </Text>
                        </TouchableOpacity>
                      );
                    })}
                  </ScrollView>
                </View>

                {/* Streak Protection Nudge */}
                <View
                  style={{
                    paddingVertical: 8,
                    marginTop: 8,
                    borderTopWidth: 1,
                    borderTopColor: colors.border,
                  }}
                >
                  <View
                    style={{
                      flexDirection: "row",
                      alignItems: "center",
                      justifyContent: "space-between",
                    }}
                  >
                    <View style={{ flex: 1, marginRight: 12 }}>
                      <Text
                        style={{
                          fontSize: 14,
                          fontWeight: "500",
                          color: colors.text,
                        }}
                      >
                        Streak protection nudge
                      </Text>
                      <Text
                        style={{
                          fontSize: 12,
                          color: colors.textMuted,
                          marginTop: 2,
                        }}
                      >
                        Evening nudge only if nothing was logged today
                      </Text>
                    </View>
                    <Switch
                      value={reminderPrefs.nudgeEnabled}
                      onValueChange={handleToggleStreakNudge}
                      trackColor={{
                        false: colors.border,
                        true: colors.primary,
                      }}
                      accessibilityRole="switch"
                      accessibilityLabel="Streak protection nudge toggle"
                    />
                  </View>

                  <ScrollView
                    horizontal
                    showsHorizontalScrollIndicator={false}
                    contentContainerStyle={{ gap: 8, paddingTop: 12 }}
                  >
                    {REMINDER_TIME_CHOICES.map((choice) => {
                      const isSelected = reminderPrefs.nudgeHour === choice.hour;
                      return (
                        <TouchableOpacity
                          key={`nudge-${choice.hour}`}
                          onPress={() => handleSelectStreakHour(choice.hour)}
                          accessibilityRole="button"
                          accessibilityLabel={`Streak nudge hour ${choice.label}`}
                          accessibilityState={{ selected: isSelected }}
                          style={{
                            paddingHorizontal: 12,
                            paddingVertical: 6,
                            borderRadius: radius.full,
                            backgroundColor: isSelected
                              ? colors.primary
                              : colors.bg,
                            borderWidth: 1,
                            borderColor: isSelected
                              ? colors.primary
                              : colors.border,
                          }}
                        >
                          <Text
                            style={{
                              fontSize: 13,
                              fontWeight: "600",
                              color: isSelected
                                ? colors.textOnPrimary
                                : colors.text,
                            }}
                          >
                            {choice.label}
                          </Text>
                        </TouchableOpacity>
                      );
                    })}
                  </ScrollView>
                </View>
              </View>
            )}

            {/* Connected AI Assistants */}
            <View
              style={{
                backgroundColor: colors.card,
                borderRadius: 12,
                padding: 16,
                marginBottom: 12,
                ...shadow,
              }}
            >
              <TouchableOpacity
                onPress={toggleAiAssistants}
                accessibilityRole="button"
                accessibilityLabel="Connected AI Assistants"
                accessibilityState={{ expanded: showAiAssistants }}
                style={{
                  flexDirection: "row",
                  alignItems: "center",
                  justifyContent: "space-between",
                }}
              >
                <View style={{ flexDirection: "row", alignItems: "center" }}>
                  <Ionicons
                    name="sparkles-outline"
                    size={20}
                    color={colors.textSecondary}
                  />
                  <Text
                    style={{
                      fontSize: 16,
                      fontWeight: "600",
                      color: colors.textSecondary,
                      marginLeft: 12,
                    }}
                  >
                    Connected AI Assistants
                  </Text>
                </View>
                <Ionicons
                  name={showAiAssistants ? "chevron-up" : "chevron-down"}
                  size={20}
                  color={colors.textMuted}
                />
              </TouchableOpacity>

              {showAiAssistants && (
                <View style={{ marginTop: 16 }}>
                  {/* Explainer */}
                  <Text
                    style={{
                      fontSize: 14,
                      color: colors.textMuted,
                      lineHeight: 20,
                      marginBottom: 14,
                    }}
                  >
                    Connect external AI assistants (like Claude or ChatGPT) to securely read your meal and symptom logs via MCP. All connections are strictly read-only.
                  </Text>

                  {/* Generate Pairing Code Button */}
                  <TouchableOpacity
                    onPress={handleGeneratePairingCode}
                    disabled={generatingCode}
                    accessibilityRole="button"
                    accessibilityLabel="Generate Pairing Code"
                    style={{
                      backgroundColor: colors.primary,
                      borderRadius: 8,
                      paddingVertical: 12,
                      paddingHorizontal: 16,
                      alignItems: "center",
                      justifyContent: "center",
                      flexDirection: "row",
                      marginBottom: pairingCode ? 16 : 20,
                    }}
                  >
                    {generatingCode ? (
                      <ActivityIndicator
                        color={colors.textOnPrimary}
                        size="small"
                      />
                    ) : (
                      <>
                        <Ionicons
                          name="key-outline"
                          size={18}
                          color={colors.textOnPrimary}
                          style={{ marginRight: 8 }}
                        />
                        <Text
                          style={{
                            color: colors.textOnPrimary,
                            fontWeight: "600",
                            fontSize: 15,
                          }}
                        >
                          Generate Pairing Code
                        </Text>
                      </>
                    )}
                  </TouchableOpacity>

                  {/* Active Pairing Code Card */}
                  {pairingCode && (
                    <View
                      style={{
                        backgroundColor: colors.bg,
                        borderRadius: 8,
                        borderWidth: 1,
                        borderColor: colors.border,
                        padding: 16,
                        marginBottom: 20,
                        alignItems: "center",
                      }}
                    >
                      <Text
                        style={{
                          fontSize: 12,
                          fontWeight: "600",
                          color: colors.textMuted,
                          textTransform: "uppercase",
                          letterSpacing: 0.5,
                          marginBottom: 8,
                        }}
                      >
                        Single-Use Pairing Code
                      </Text>
                      <Text
                        style={{
                          fontSize: 28,
                          fontWeight: "800",
                          letterSpacing: 4,
                          fontFamily: Platform.OS === "ios" ? "Menlo" : "monospace",
                          color: colors.primary,
                          marginBottom: 6,
                        }}
                        selectable
                      >
                        {pairingCode.code}
                      </Text>
                      <Text
                        style={{
                          fontSize: 13,
                          fontWeight: "500",
                          color: colors.warning,
                          marginBottom: 10,
                        }}
                      >
                        {formatCodeExpiry(pairingCode.expiresAt)}
                      </Text>
                      <Text
                        style={{
                          fontSize: 13,
                          color: colors.textSecondary,
                          textAlign: "center",
                          lineHeight: 18,
                        }}
                      >
                        Ask your AI assistant to call <Text style={{ fontFamily: Platform.OS === "ios" ? "Menlo" : "monospace", fontWeight: "600", color: colors.text }}>gutai_link_account</Text> with this code within 10 minutes to link your account.
                      </Text>
                    </View>
                  )}

                  {/* Connected Assistants List Header */}
                  <View
                    style={{
                      borderTopWidth: 1,
                      borderTopColor: colors.divider,
                      paddingTop: 16,
                    }}
                  >
                    <Text
                      style={{
                        fontSize: 14,
                        fontWeight: "600",
                        color: colors.textSecondary,
                        marginBottom: 10,
                      }}
                      accessibilityRole="header"
                    >
                      Linked Assistants
                    </Text>

                    {loadingTokens ? (
                      <View style={{ paddingVertical: 20, alignItems: "center" }}>
                        <ActivityIndicator size="small" color={colors.primary} />
                      </View>
                    ) : tokens.length === 0 ? (
                      <Text
                        style={{
                          color: colors.textMuted,
                          fontSize: 14,
                          fontStyle: "italic",
                          paddingVertical: 8,
                        }}
                      >
                        No AI assistants connected
                      </Text>
                    ) : (
                      tokens.map((token, index) => (
                        <View
                          key={token.id}
                          style={{
                            flexDirection: "row",
                            alignItems: "center",
                            justifyContent: "space-between",
                            paddingVertical: 12,
                            borderTopWidth: index === 0 ? 0 : 1,
                            borderTopColor: colors.divider,
                          }}
                        >
                          <View style={{ flex: 1, marginRight: 12 }}>
                            <View
                              style={{
                                flexDirection: "row",
                                alignItems: "center",
                                gap: 6,
                                marginBottom: 2,
                              }}
                            >
                              <Text
                                style={{
                                  fontSize: 15,
                                  fontWeight: "600",
                                  color: colors.text,
                                }}
                              >
                                {token.name}
                              </Text>
                              <Text
                                style={{
                                  fontSize: 12,
                                  fontFamily: Platform.OS === "ios" ? "Menlo" : "monospace",
                                  color: colors.textMuted,
                                }}
                              >
                                ({token.prefix}…)
                              </Text>
                            </View>

                            <Text
                              style={{
                                fontSize: 12,
                                color: colors.textMuted,
                                marginBottom: 2,
                              }}
                            >
                              Scopes: {token.scopes.join(", ") || "none"}
                            </Text>

                            <Text
                              style={{
                                fontSize: 12,
                                color: colors.textMuted,
                              }}
                            >
                              Last active: {formatLastUsed(token.lastUsedAt)}
                            </Text>
                          </View>

                          <TouchableOpacity
                            onPress={() => handleRevokeToken(token)}
                            disabled={revokingTokenId === token.id}
                            accessibilityRole="button"
                            accessibilityLabel={`Revoke access for ${token.name}`}
                            style={{
                              padding: 8,
                              borderRadius: radius.sm,
                              backgroundColor: colors.dangerBg,
                            }}
                          >
                            {revokingTokenId === token.id ? (
                              <ActivityIndicator
                                size="small"
                                color={colors.danger}
                              />
                            ) : (
                              <Ionicons
                                name="trash-outline"
                                size={18}
                                color={colors.danger}
                              />
                            )}
                          </TouchableOpacity>
                        </View>
                      ))
                    )}
                  </View>
                </View>
              )}
            </View>

            {/* Change Password */}
            <View
              style={{
                backgroundColor: colors.card,
                borderRadius: 12,
                padding: 16,
                marginBottom: 12,
              }}
            >
              <TouchableOpacity
                onPress={() => setShowPasswordForm(!showPasswordForm)}
                accessibilityRole="button"
                accessibilityLabel="Change Password"
                accessibilityState={{ expanded: showPasswordForm }}
                style={{
                  flexDirection: "row",
                  alignItems: "center",
                  justifyContent: "space-between",
                }}
              >
                <View style={{ flexDirection: "row", alignItems: "center" }}>
                  <Ionicons
                    name="lock-closed-outline"
                    size={20}
                    color={colors.textSecondary}
                  />
                  <Text
                    style={{
                      fontSize: 16,
                      fontWeight: "600",
                      color: colors.textSecondary,
                      marginLeft: 12,
                    }}
                  >
                    Change Password
                  </Text>
                </View>
                <Ionicons
                  name={showPasswordForm ? "chevron-up" : "chevron-down"}
                  size={20}
                  color={colors.textMuted}
                />
              </TouchableOpacity>

              {showPasswordForm && (
                <View style={{ marginTop: 16 }}>
                  <TextInput
                    placeholder="Current password"
                    placeholderTextColor={colors.textMuted}
                    value={currentPassword}
                    onChangeText={setCurrentPassword}
                    secureTextEntry
                    autoCapitalize="none"
                    autoCorrect={false}
                    autoComplete="password"
                    textContentType="password"
                    style={{
                      borderWidth: 1,
                      borderColor: colors.border,
                      borderRadius: 8,
                      padding: 12,
                      fontSize: 15,
                      color: colors.text,
                      marginBottom: 10,
                      backgroundColor: colors.bg,
                    }}
                  />
                  <TextInput
                    placeholder="New password (min 8 characters)"
                    placeholderTextColor={colors.textMuted}
                    value={newPassword}
                    onChangeText={setNewPassword}
                    secureTextEntry
                    autoCapitalize="none"
                    autoCorrect={false}
                    autoComplete="new-password"
                    textContentType="newPassword"
                    style={{
                      borderWidth: 1,
                      borderColor: colors.border,
                      borderRadius: 8,
                      padding: 12,
                      fontSize: 15,
                      color: colors.text,
                      marginBottom: 12,
                      backgroundColor: colors.bg,
                    }}
                  />
                  <TouchableOpacity
                    onPress={handleChangePassword}
                    disabled={changingPassword}
                    accessibilityRole="button"
                    accessibilityLabel="Update Password"
                    style={{
                      backgroundColor: colors.primaryLight,
                      borderRadius: 8,
                      padding: 12,
                      alignItems: "center",
                    }}
                  >
                    {changingPassword ? (
                      <ActivityIndicator
                        color={colors.textOnPrimary}
                        size="small"
                      />
                    ) : (
                      <Text
                        style={{
                          color: colors.textOnPrimary,
                          fontWeight: "600",
                        }}
                      >
                        Update Password
                      </Text>
                    )}
                  </TouchableOpacity>
                </View>
              )}
            </View>

            {/* App Info */}
            <View
              style={{
                backgroundColor: colors.card,
                borderRadius: 12,
                padding: 16,
                marginBottom: 12,
              }}
            >
              <Text
                style={{
                  fontSize: 16,
                  fontWeight: "600",
                  color: colors.textSecondary,
                  marginBottom: 12,
                }}
                accessibilityRole="header"
              >
                About
              </Text>
              <View
                style={{
                  flexDirection: "row",
                  justifyContent: "space-between",
                  paddingVertical: 6,
                }}
              >
                <Text style={{ color: colors.textMuted }}>Version</Text>
                <Text style={{ fontWeight: "600", color: colors.text }}>
                  {appVersion}
                </Text>
              </View>
              <TouchableOpacity
                onPress={() => router.push("/sources")}
                accessibilityRole="link"
                accessibilityLabel="Sources and Medical Disclaimer"
                style={{
                  flexDirection: "row",
                  alignItems: "center",
                  justifyContent: "space-between",
                  paddingVertical: 8,
                  marginTop: 4,
                  borderTopWidth: 1,
                  borderTopColor: colors.divider,
                }}
              >
                <View style={{ flexDirection: "row", alignItems: "center" }}>
                  <Ionicons
                    name="document-text-outline"
                    size={16}
                    color={colors.textMuted}
                  />
                  <Text style={{ color: colors.textMuted, marginLeft: 8 }}>
                    Sources & Medical Disclaimer
                  </Text>
                </View>
                <Ionicons
                  name="chevron-forward"
                  size={16}
                  color={colors.textMuted}
                />
              </TouchableOpacity>
              <TouchableOpacity
                onPress={() => {
                  Linking.openURL(PRIVACY_POLICY_URL).catch(() => {});
                }}
                accessibilityRole="link"
                accessibilityLabel="Privacy Policy"
                style={{
                  flexDirection: "row",
                  alignItems: "center",
                  justifyContent: "space-between",
                  paddingVertical: 8,
                  marginTop: 4,
                  borderTopWidth: 1,
                  borderTopColor: colors.divider,
                }}
              >
                <View style={{ flexDirection: "row", alignItems: "center" }}>
                  <Ionicons
                    name="shield-checkmark-outline"
                    size={16}
                    color={colors.textMuted}
                  />
                  <Text style={{ color: colors.textMuted, marginLeft: 8 }}>
                    Privacy Policy
                  </Text>
                </View>
                <Ionicons
                  name="chevron-forward"
                  size={16}
                  color={colors.textMuted}
                />
              </TouchableOpacity>
              <TouchableOpacity
                onPress={handleExportData}
                disabled={exportingData}
                accessibilityRole="button"
                accessibilityLabel="Export my data"
                style={{
                  flexDirection: "row",
                  alignItems: "center",
                  justifyContent: "space-between",
                  paddingVertical: 8,
                  marginTop: 4,
                  borderTopWidth: 1,
                  borderTopColor: colors.divider,
                }}
              >
                <View style={{ flexDirection: "row", alignItems: "center" }}>
                  <Ionicons
                    name="download-outline"
                    size={16}
                    color={colors.textMuted}
                  />
                  <Text style={{ color: colors.textMuted, marginLeft: 8 }}>
                    Export My Data
                  </Text>
                </View>
                {exportingData ? (
                  <ActivityIndicator size="small" color={colors.primary} />
                ) : (
                  <Ionicons
                    name="chevron-forward"
                    size={16}
                    color={colors.textMuted}
                  />
                )}
              </TouchableOpacity>
            </View>

            {/* Danger Zone */}
            <View
              style={{
                backgroundColor: colors.card,
                borderRadius: 12,
                padding: 16,
                marginTop: 12,
                borderWidth: 1,
                borderColor: colors.dangerBorder,
              }}
            >
              <Text
                style={{
                  fontSize: 16,
                  fontWeight: "600",
                  color: colors.danger,
                  marginBottom: 12,
                }}
                accessibilityRole="header"
              >
                Danger Zone
              </Text>
              <TouchableOpacity
                onPress={handleDeleteAccount}
                disabled={deleting}
                accessibilityRole="button"
                accessibilityLabel="Delete Account"
                style={{
                  backgroundColor: colors.dangerBg,
                  borderRadius: 8,
                  padding: 12,
                  flexDirection: "row",
                  alignItems: "center",
                  justifyContent: "center",
                }}
              >
                {deleting ? (
                  <ActivityIndicator size="small" color={colors.danger} />
                ) : (
                  <>
                    <Ionicons
                      name="trash-outline"
                      size={18}
                      color={colors.danger}
                    />
                    <Text
                      style={{
                        color: colors.danger,
                        fontWeight: "600",
                        marginLeft: 8,
                      }}
                    >
                      Delete Account
                    </Text>
                  </>
                )}
              </TouchableOpacity>
            </View>
          </View>
        </ScrollView>
      </KeyboardAvoidingView>
    </SafeScreen>
  );
}
