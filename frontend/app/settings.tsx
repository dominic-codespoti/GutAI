import React, { useState } from "react";
import {
  View,
  Text,
  ScrollView,
  TouchableOpacity,
  TextInput,
  ActivityIndicator,
  Alert,
} from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { useAuthStore } from "../src/stores/auth";
import { authApi, userApi } from "../src/api";
import { toast } from "../src/stores/toast";
import Constants from "expo-constants";
import { SafeScreen } from "../components/SafeScreen";
import { useRouter } from "expo-router";

export default function SettingsScreen() {
  const { logout } = useAuthStore();
  const router = useRouter();
  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [changingPassword, setChangingPassword] = useState(false);
  const [showPasswordForm, setShowPasswordForm] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const appVersion = Constants.expoConfig?.version ?? "1.0.0";

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
      <ScrollView style={{ flex: 1, backgroundColor: "#f8fafc" }}>
        <View style={{ padding: 20 }}>
          {/* Change Password */}
          <View
            style={{
              backgroundColor: "#fff",
              borderRadius: 12,
              padding: 16,
              marginBottom: 12,
            }}
          >
            <TouchableOpacity
              onPress={() => setShowPasswordForm(!showPasswordForm)}
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
                  color="#334155"
                />
                <Text
                  style={{
                    fontSize: 16,
                    fontWeight: "600",
                    color: "#334155",
                    marginLeft: 12,
                  }}
                >
                  Change Password
                </Text>
              </View>
              <Ionicons
                name={showPasswordForm ? "chevron-up" : "chevron-down"}
                size={20}
                color="#94a3b8"
              />
            </TouchableOpacity>

            {showPasswordForm && (
              <View style={{ marginTop: 16 }}>
                <TextInput
                  placeholder="Current password"
                  value={currentPassword}
                  onChangeText={setCurrentPassword}
                  secureTextEntry
                  style={{
                    borderWidth: 1,
                    borderColor: "#e2e8f0",
                    borderRadius: 8,
                    padding: 12,
                    fontSize: 15,
                    color: "#0f172a",
                    marginBottom: 10,
                    backgroundColor: "#f8fafc",
                  }}
                />
                <TextInput
                  placeholder="New password (min 8 characters)"
                  value={newPassword}
                  onChangeText={setNewPassword}
                  secureTextEntry
                  style={{
                    borderWidth: 1,
                    borderColor: "#e2e8f0",
                    borderRadius: 8,
                    padding: 12,
                    fontSize: 15,
                    color: "#0f172a",
                    marginBottom: 12,
                    backgroundColor: "#f8fafc",
                  }}
                />
                <TouchableOpacity
                  onPress={handleChangePassword}
                  disabled={changingPassword}
                  style={{
                    backgroundColor: "#22c55e",
                    borderRadius: 8,
                    padding: 12,
                    alignItems: "center",
                  }}
                >
                  {changingPassword ? (
                    <ActivityIndicator color="#fff" size="small" />
                  ) : (
                    <Text style={{ color: "#fff", fontWeight: "600" }}>
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
              backgroundColor: "#fff",
              borderRadius: 12,
              padding: 16,
              marginBottom: 12,
            }}
          >
            <Text
              style={{
                fontSize: 16,
                fontWeight: "600",
                color: "#334155",
                marginBottom: 12,
              }}
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
              <Text style={{ color: "#64748b" }}>Version</Text>
              <Text style={{ fontWeight: "600", color: "#0f172a" }}>
                {appVersion}
              </Text>
            </View>
            <TouchableOpacity
              onPress={() => router.push("/sources")}
              style={{
                flexDirection: "row",
                alignItems: "center",
                justifyContent: "space-between",
                paddingVertical: 8,
                marginTop: 4,
                borderTopWidth: 1,
                borderTopColor: "#f1f5f9",
              }}
            >
              <View style={{ flexDirection: "row", alignItems: "center" }}>
                <Ionicons
                  name="document-text-outline"
                  size={16}
                  color="#64748b"
                />
                <Text style={{ color: "#64748b", marginLeft: 8 }}>
                  Sources & Medical Disclaimer
                </Text>
              </View>
              <Ionicons name="chevron-forward" size={16} color="#94a3b8" />
            </TouchableOpacity>
            <TouchableOpacity
              onPress={() => router.push("/privacy")}
              style={{
                flexDirection: "row",
                alignItems: "center",
                justifyContent: "space-between",
                paddingVertical: 8,
                marginTop: 4,
                borderTopWidth: 1,
                borderTopColor: "#f1f5f9",
              }}
            >
              <View style={{ flexDirection: "row", alignItems: "center" }}>
                <Ionicons
                  name="shield-checkmark-outline"
                  size={16}
                  color="#64748b"
                />
                <Text style={{ color: "#64748b", marginLeft: 8 }}>
                  Privacy Policy
                </Text>
              </View>
              <Ionicons name="chevron-forward" size={16} color="#94a3b8" />
            </TouchableOpacity>
          </View>

          {/* Danger Zone */}
          <View
            style={{
              backgroundColor: "#fff",
              borderRadius: 12,
              padding: 16,
              marginTop: 12,
              borderWidth: 1,
              borderColor: "#fecaca",
            }}
          >
            <Text
              style={{
                fontSize: 16,
                fontWeight: "600",
                color: "#dc2626",
                marginBottom: 12,
              }}
            >
              Danger Zone
            </Text>
            <TouchableOpacity
              onPress={handleDeleteAccount}
              disabled={deleting}
              style={{
                backgroundColor: "#fef2f2",
                borderRadius: 8,
                padding: 12,
                flexDirection: "row",
                alignItems: "center",
                justifyContent: "center",
              }}
            >
              {deleting ? (
                <ActivityIndicator size="small" color="#dc2626" />
              ) : (
                <>
                  <Ionicons name="trash-outline" size={18} color="#dc2626" />
                  <Text
                    style={{
                      color: "#dc2626",
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
    </SafeScreen>
  );
}
