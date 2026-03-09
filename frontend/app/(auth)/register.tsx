import { useState } from "react";
import {
  View,
  Text,
  TextInput,
  TouchableOpacity,
  ActivityIndicator,
  KeyboardAvoidingView,
  Platform,
} from "react-native";
import { Link } from "expo-router";
import { Ionicons } from "@expo/vector-icons";
import { useAuthStore } from "../../src/stores/auth";
import { toast } from "../../src/stores/toast";
import { radius, spacing } from "../../src/utils/theme";
import {
  useThemeColors,
  useThemeFonts,
  useThemeShadow,
} from "../../src/stores/theme";
import { SafeScreen } from "../../components/SafeScreen";

const isValidEmail = (email: string) =>
  /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email);

export default function RegisterScreen() {
  const colors = useThemeColors();
  const fonts = useThemeFonts();
  const { shadow, shadowMd } = useThemeShadow();
  const [displayName, setDisplayName] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [loading, setLoading] = useState(false);
  const register = useAuthStore((s) => s.register);

  const handleRegister = async () => {
    if (!displayName || !email || !password) {
      toast.error("Please fill in all fields");
      return;
    }
    if (!isValidEmail(email)) {
      toast.error("Please enter a valid email address");
      return;
    }
    if (password.length < 8) {
      toast.error("Password must be at least 8 characters");
      return;
    }
    if (password !== confirmPassword) {
      toast.error("Passwords do not match");
      return;
    }
    setLoading(true);
    try {
      await register(email, password, displayName);
    } catch (e: unknown) {
      const err = e as {
        response?: {
          data?: {
            errors?: Record<string, string[]>;
            detail?: string;
            title?: string;
          };
        };
      };
      const errors = err.response?.data?.errors;
      if (errors) {
        const msg = Object.values(errors).flat().join("\n");
        toast.error(msg);
      } else {
        const msg =
          err.response?.data?.detail ||
          err.response?.data?.title ||
          "Registration failed. Please try again.";
        toast.error(msg);
      }
    } finally {
      setLoading(false);
    }
  };

  return (
    <SafeScreen>
      <KeyboardAvoidingView
        behavior={Platform.OS === "ios" ? "padding" : "height"}
        style={{ flex: 1 }}
      >
        <View
          style={{
            flex: 1,
            justifyContent: "center",
            paddingHorizontal: spacing.xxl,
          }}
        >
          {/* Header */}
          <View style={{ alignItems: "center", marginBottom: spacing.xxxl }}>
            <View
              style={{
                width: 64,
                height: 64,
                borderRadius: radius.lg,
                backgroundColor: colors.primaryBg,
                alignItems: "center",
                justifyContent: "center",
                marginBottom: spacing.md,
                borderWidth: 2,
                borderColor: colors.primaryBorder,
                ...shadowMd,
              }}
            >
              <Text style={{ fontSize: 30 }}>🌱</Text>
            </View>
            <Text
              style={{
                fontSize: 28,
                fontWeight: "800",
                color: colors.text,
                letterSpacing: -0.5,
              }}
            >
              Create Account
            </Text>
            <Text style={{ ...fonts.body, marginTop: 4 }}>
              Start tracking your meals & gut health
            </Text>
          </View>

          {/* Form */}
          <View
            style={{
              backgroundColor: colors.card,
              borderRadius: radius.lg,
              padding: spacing.xl,
              ...shadowMd,
            }}
          >
            <View
              style={{
                flexDirection: "row",
                alignItems: "center",
                backgroundColor: colors.bg,
                borderWidth: 1,
                borderColor: colors.border,
                borderRadius: radius.md,
                paddingHorizontal: 14,
                marginBottom: spacing.md,
              }}
            >
              <Ionicons
                name="person-outline"
                size={18}
                color={colors.textMuted}
              />
              <TextInput
                placeholder="Display Name"
                placeholderTextColor={colors.textLight}
                value={displayName}
                onChangeText={setDisplayName}
                maxLength={100}
                style={{
                  flex: 1,
                  padding: 14,
                  fontSize: 16,
                  color: colors.text,
                }}
              />
            </View>

            <View
              style={{
                flexDirection: "row",
                alignItems: "center",
                backgroundColor: colors.bg,
                borderWidth: 1,
                borderColor: colors.border,
                borderRadius: radius.md,
                paddingHorizontal: 14,
                marginBottom: spacing.md,
              }}
            >
              <Ionicons
                name="mail-outline"
                size={18}
                color={colors.textMuted}
              />
              <TextInput
                placeholder="Email"
                placeholderTextColor={colors.textLight}
                value={email}
                onChangeText={setEmail}
                autoCapitalize="none"
                keyboardType="email-address"
                maxLength={254}
                style={{
                  flex: 1,
                  padding: 14,
                  fontSize: 16,
                  color: colors.text,
                }}
              />
            </View>

            <View
              style={{
                flexDirection: "row",
                alignItems: "center",
                backgroundColor: colors.bg,
                borderWidth: 1,
                borderColor:
                  confirmPassword && password !== confirmPassword
                    ? colors.danger
                    : colors.border,
                borderRadius: radius.md,
                paddingHorizontal: 14,
                marginBottom: spacing.xl,
              }}
            >
              <Ionicons
                name="lock-closed-outline"
                size={18}
                color={colors.textMuted}
              />
              <TextInput
                placeholder="Password (min 8 characters)"
                placeholderTextColor={colors.textLight}
                value={password}
                onChangeText={setPassword}
                secureTextEntry={!showPassword}
                maxLength={128}
                style={{
                  flex: 1,
                  padding: 14,
                  fontSize: 16,
                  color: colors.text,
                }}
              />
              <TouchableOpacity onPress={() => setShowPassword(!showPassword)}>
                <Ionicons
                  name={showPassword ? "eye-off" : "eye"}
                  size={20}
                  color={colors.textMuted}
                />
              </TouchableOpacity>
            </View>

            <View
              style={{
                flexDirection: "row",
                alignItems: "center",
                backgroundColor: colors.bg,
                borderWidth: 1,
                borderColor: colors.border,
                borderRadius: radius.md,
                paddingHorizontal: 14,
                marginBottom: spacing.xl,
              }}
            >
              <Ionicons
                name="shield-checkmark-outline"
                size={18}
                color={colors.textMuted}
              />
              <TextInput
                placeholder="Confirm password"
                placeholderTextColor={colors.textLight}
                value={confirmPassword}
                onChangeText={setConfirmPassword}
                secureTextEntry={!showPassword}
                maxLength={128}
                style={{
                  flex: 1,
                  padding: 14,
                  fontSize: 16,
                  color: colors.text,
                }}
              />
              {confirmPassword !== "" && (
                <Ionicons
                  name={
                    password === confirmPassword
                      ? "checkmark-circle"
                      : "close-circle"
                  }
                  size={20}
                  color={
                    password === confirmPassword
                      ? colors.primary
                      : colors.danger
                  }
                />
              )}
            </View>

            <TouchableOpacity
              onPress={handleRegister}
              disabled={loading}
              style={{
                backgroundColor: colors.primary,
                borderRadius: radius.md,
                padding: 16,
                alignItems: "center",
                ...shadowMd,
              }}
            >
              {loading ? (
                <ActivityIndicator color={colors.textOnPrimary} />
              ) : (
                <Text
                  style={{
                    color: colors.textOnPrimary,
                    fontSize: 16,
                    fontWeight: "700",
                  }}
                >
                  Create Account
                </Text>
              )}
            </TouchableOpacity>
          </View>

          <View
            style={{
              flexDirection: "row",
              justifyContent: "center",
              marginTop: spacing.xl,
            }}
          >
            <Text style={{ color: colors.textSecondary }}>
              Already have an account?{" "}
            </Text>
            <Link href="/(auth)/login">
              <Text style={{ color: colors.primary, fontWeight: "700" }}>
                Log In
              </Text>
            </Link>
          </View>
        </View>
      </KeyboardAvoidingView>
    </SafeScreen>
  );
}
