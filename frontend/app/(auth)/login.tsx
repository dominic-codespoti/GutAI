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
import {
  colors,
  shadow,
  shadowMd,
  radius,
  spacing,
  fonts,
} from "../../src/utils/theme";

const isValidEmail = (email: string) =>
  /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email);

export default function LoginScreen() {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [loading, setLoading] = useState(false);
  const login = useAuthStore((s) => s.login);

  const handleLogin = async () => {
    if (!email || !password) {
      toast.error("Please fill in all fields");
      return;
    }
    if (!isValidEmail(email)) {
      toast.error("Please enter a valid email address");
      return;
    }
    setLoading(true);
    try {
      await login(email, password);
    } catch (e: unknown) {
      const err = e as {
        response?: {
          data?: { detail?: string; title?: string; message?: string };
        };
      };
      const msg =
        err.response?.data?.detail ||
        err.response?.data?.title ||
        err.response?.data?.message ||
        "Invalid credentials";
      toast.error(msg);
    } finally {
      setLoading(false);
    }
  };

  return (
    <KeyboardAvoidingView
      behavior={Platform.OS === "ios" ? "padding" : "height"}
      style={{ flex: 1, backgroundColor: colors.bg }}
    >
      <View
        style={{
          flex: 1,
          justifyContent: "center",
          paddingHorizontal: spacing.xxl,
        }}
      >
        {/* Logo Area */}
        <View style={{ alignItems: "center", marginBottom: spacing.xxxl }}>
          <View
            style={{
              width: 72,
              height: 72,
              borderRadius: radius.lg,
              backgroundColor: colors.primaryBg,
              alignItems: "center",
              justifyContent: "center",
              marginBottom: spacing.lg,
              borderWidth: 2,
              borderColor: colors.primaryBorder,
              ...shadowMd,
            }}
          >
            <Ionicons name="leaf" size={36} color={colors.primary} />
          </View>
          <Text
            style={{
              fontSize: 36,
              fontWeight: "800",
              color: colors.primary,
              letterSpacing: -1,
            }}
          >
            GutAI
          </Text>
          <Text style={{ ...fonts.body, marginTop: 4 }}>
            Track your meals & gut health
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
            <Ionicons name="mail-outline" size={18} color={colors.textMuted} />
            <TextInput
              placeholder="Email"
              placeholderTextColor={colors.textLight}
              value={email}
              onChangeText={setEmail}
              autoCapitalize="none"
              keyboardType="email-address"
              style={{ flex: 1, padding: 14, fontSize: 16, color: colors.text }}
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
              marginBottom: spacing.xl,
            }}
          >
            <Ionicons
              name="lock-closed-outline"
              size={18}
              color={colors.textMuted}
            />
            <TextInput
              placeholder="Password"
              placeholderTextColor={colors.textLight}
              value={password}
              onChangeText={setPassword}
              secureTextEntry={!showPassword}
              style={{ flex: 1, padding: 14, fontSize: 16, color: colors.text }}
            />
            <TouchableOpacity onPress={() => setShowPassword(!showPassword)}>
              <Ionicons
                name={showPassword ? "eye-off" : "eye"}
                size={20}
                color={colors.textMuted}
              />
            </TouchableOpacity>
          </View>

          <TouchableOpacity
            onPress={handleLogin}
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
              <ActivityIndicator color="#fff" />
            ) : (
              <Text style={{ color: "#fff", fontSize: 16, fontWeight: "700" }}>
                Log In
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
            Don't have an account?{" "}
          </Text>
          <Link href="/(auth)/register">
            <Text style={{ color: colors.primary, fontWeight: "700" }}>
              Sign Up
            </Text>
          </Link>
        </View>
      </View>
    </KeyboardAvoidingView>
  );
}
