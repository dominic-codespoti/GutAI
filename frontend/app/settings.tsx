import React, { useState } from "react";
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
} from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { useAuthStore } from "../src/stores/auth";
import { authApi, userApi } from "../src/api";
import { toast } from "../src/stores/toast";
import Constants from "expo-constants";
import { SafeScreen } from "../components/SafeScreen";
import { useRouter } from "expo-router";
import { useSubscriptionStore } from "../src/stores/subscription";
import {
  useThemeColors,
  useThemeShadow,
  useThemeStore,
} from "../src/stores/theme";
import { radius, spacing } from "../src/utils/theme";
import * as haptics from "../src/utils/haptics";

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
  const appVersion = Constants.expoConfig?.version ?? "1.0.0";
  const { restore } = useSubscriptionStore();

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
                onPress={() => router.push("/privacy")}
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
