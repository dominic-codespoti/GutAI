import React, { useState } from "react";
import {
  View,
  Text,
  ScrollView,
  TouchableOpacity,
  TextInput,
  ActivityIndicator,
  Alert,
  Share,
} from "react-native";
import { useRouter } from "expo-router";
import { Ionicons } from "@expo/vector-icons";
import { useAuthStore } from "../src/stores/auth";
import { authApi, userApi, mealApi } from "../src/api";
import { toast } from "../src/stores/toast";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";

export default function SettingsScreen() {
  const router = useRouter();
  const { logout } = useAuthStore();
  const queryClient = useQueryClient();
  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [changingPassword, setChangingPassword] = useState(false);
  const [showPasswordForm, setShowPasswordForm] = useState(false);
  const [exporting, setExporting] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [editTimezone, setEditTimezone] = useState(false);
  const [timezone, setTimezone] = useState("");

  const { data: profile } = useQuery({
    queryKey: ["user-profile"],
    queryFn: () => userApi.getProfile().then((r) => r.data),
  });

  React.useEffect(() => {
    if (profile?.timezoneId) setTimezone(profile.timezoneId);
  }, [profile?.timezoneId]);

  const timezoneMutation = useMutation({
    mutationFn: (tz: string) => userApi.updateProfile({ timezoneId: tz }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["user-profile"] });
      toast.success("Timezone updated");
      setEditTimezone(false);
    },
    onError: () => toast.error("Failed to update timezone"),
  });

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

  const handleExport = async () => {
    setExporting(true);
    try {
      const { data } = await mealApi.export();
      const json = JSON.stringify(data, null, 2);
      await Share.share({
        message: json,
        title: "Gut AI Data Export",
      });
    } catch {
      toast.error("Failed to export data");
    } finally {
      setExporting(false);
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
    <ScrollView style={{ flex: 1, backgroundColor: "#f8fafc" }}>
      <View style={{ padding: 20 }}>
        <Text
          style={{
            fontSize: 24,
            fontWeight: "700",
            color: "#0f172a",
            marginBottom: 24,
          }}
        >
          Settings
        </Text>

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
              <Ionicons name="lock-closed-outline" size={20} color="#334155" />
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

        {/* Export Data */}
        <TouchableOpacity
          onPress={handleExport}
          disabled={exporting}
          style={{
            backgroundColor: "#fff",
            borderRadius: 12,
            padding: 16,
            marginBottom: 12,
            flexDirection: "row",
            alignItems: "center",
          }}
        >
          <Ionicons name="download-outline" size={20} color="#334155" />
          <Text
            style={{
              fontSize: 16,
              fontWeight: "600",
              color: "#334155",
              marginLeft: 12,
              flex: 1,
            }}
          >
            Export Data
          </Text>
          {exporting && <ActivityIndicator size="small" color="#22c55e" />}
        </TouchableOpacity>

        {/* Timezone */}
        <View
          style={{
            backgroundColor: "#fff",
            borderRadius: 12,
            padding: 16,
            marginTop: 12,
            marginBottom: 12,
          }}
        >
          <TouchableOpacity
            onPress={() => setEditTimezone(!editTimezone)}
            style={{
              flexDirection: "row",
              justifyContent: "space-between",
              alignItems: "center",
            }}
          >
            <View>
              <Text
                style={{ fontSize: 16, fontWeight: "600", color: "#334155" }}
              >
                🌐 Timezone
              </Text>
              <Text style={{ fontSize: 13, color: "#64748b", marginTop: 2 }}>
                {timezone || "Not set"}
              </Text>
            </View>
            <Ionicons
              name={editTimezone ? "chevron-up" : "chevron-down"}
              size={20}
              color="#94a3b8"
            />
          </TouchableOpacity>
          {editTimezone && (
            <View style={{ marginTop: 12 }}>
              <TextInput
                placeholder="e.g. America/New_York"
                value={timezone}
                onChangeText={setTimezone}
                style={{
                  borderWidth: 1,
                  borderColor: "#e2e8f0",
                  borderRadius: 8,
                  padding: 12,
                  fontSize: 15,
                  color: "#0f172a",
                  backgroundColor: "#f8fafc",
                }}
              />
              <View style={{ flexDirection: "row", gap: 8, marginTop: 8 }}>
                {[
                  "America/New_York",
                  "America/Chicago",
                  "America/Los_Angeles",
                  "Europe/London",
                  "Asia/Tokyo",
                ].map((tz) => (
                  <TouchableOpacity
                    key={tz}
                    onPress={() => setTimezone(tz)}
                    style={{
                      paddingVertical: 4,
                      paddingHorizontal: 8,
                      borderRadius: 6,
                      backgroundColor: timezone === tz ? "#22c55e" : "#f1f5f9",
                    }}
                  >
                    <Text
                      style={{
                        fontSize: 10,
                        color: timezone === tz ? "#fff" : "#64748b",
                        fontWeight: "600",
                      }}
                    >
                      {tz.split("/")[1]}
                    </Text>
                  </TouchableOpacity>
                ))}
              </View>
              <TouchableOpacity
                onPress={() => timezone && timezoneMutation.mutate(timezone)}
                disabled={!timezone || timezoneMutation.isPending}
                style={{
                  backgroundColor: timezone ? "#22c55e" : "#94a3b8",
                  borderRadius: 8,
                  padding: 12,
                  alignItems: "center",
                  marginTop: 8,
                }}
              >
                <Text style={{ color: "#fff", fontWeight: "600" }}>
                  {timezoneMutation.isPending ? "Saving..." : "Save Timezone"}
                </Text>
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
            <Text style={{ fontWeight: "600", color: "#0f172a" }}>1.0.0</Text>
          </View>
          <View
            style={{
              flexDirection: "row",
              justifyContent: "space-between",
              paddingVertical: 6,
            }}
          >
            <Text style={{ color: "#64748b" }}>Environment</Text>
            <Text style={{ fontWeight: "600", color: "#0f172a" }}>Local</Text>
          </View>
          <View
            style={{
              flexDirection: "row",
              justifyContent: "space-between",
              paddingVertical: 6,
            }}
          >
            <Text style={{ color: "#64748b" }}>Data Storage</Text>
            <Text style={{ fontWeight: "600", color: "#0f172a" }}>
              On-device only
            </Text>
          </View>
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
                  style={{ color: "#dc2626", fontWeight: "600", marginLeft: 8 }}
                >
                  Delete Account
                </Text>
              </>
            )}
          </TouchableOpacity>
        </View>
      </View>
    </ScrollView>
  );
}
