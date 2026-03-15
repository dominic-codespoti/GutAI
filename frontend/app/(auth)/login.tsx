import { useRef, useState } from "react";
import {
  View,
  Text,
  TextInput,
  TouchableOpacity,
  ActivityIndicator,
  KeyboardAvoidingView,
  Platform,
  ScrollView,
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
import * as haptics from "../../src/utils/haptics";

const isValidEmail = (email: string) =>
  /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email);

export default function LoginScreen() {
  const colors = useThemeColors();
  const fonts = useThemeFonts();
  const { shadow, shadowMd } = useThemeShadow();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [loading, setLoading] = useState(false);
  const login = useAuthStore((s) => s.login);
  const passwordRef = useRef<TextInput>(null);

  const handleLogin = async () => {
    haptics.medium();
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
    <SafeScreen>
      <KeyboardAvoidingView
        behavior={Platform.OS === "ios" ? "padding" : "height"}
        style={{ flex: 1 }}
      >
        <ScrollView
          contentContainerStyle={{
            flexGrow: 1,
            justifyContent: "center",
            paddingHorizontal: spacing.xxl,
          }}
          keyboardShouldPersistTaps="handled"
          showsVerticalScrollIndicator={false}
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
              GutLens
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
                autoCorrect={false}
                autoComplete="email"
                textContentType="emailAddress"
                keyboardType="email-address"
                maxLength={254}
                accessibilityLabel="Email address"
                returnKeyType="next"
                onSubmitEditing={() => passwordRef.current?.focus()}
                blurOnSubmit={false}
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
                autoCapitalize="none"
                autoCorrect={false}
                autoComplete="password"
                textContentType="password"
                maxLength={128}
                ref={passwordRef}
                accessibilityLabel="Password"
                returnKeyType="done"
                onSubmitEditing={handleLogin}
                style={{
                  flex: 1,
                  padding: 14,
                  fontSize: 16,
                  color: colors.text,
                }}
              />
              <TouchableOpacity
                onPress={() => setShowPassword(!showPassword)}
                accessibilityRole="button"
                accessibilityLabel={
                  showPassword ? "Hide password" : "Show password"
                }
              >
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
              accessibilityRole="button"
              accessibilityLabel="Log in"
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
            <Link
              href="/(auth)/register"
              accessibilityRole="link"
              accessibilityLabel="Sign up for an account"
            >
              <Text style={{ color: colors.primary, fontWeight: "700" }}>
                Sign Up
              </Text>
            </Link>
          </View>

          <View
            style={{
              flexDirection: "row",
              justifyContent: "center",
              alignItems: "center",
              marginTop: spacing.lg,
              gap: spacing.lg,
            }}
          >
            <Link
              href="/sources"
              style={{ flexDirection: "row", alignItems: "center" }}
              accessibilityRole="link"
              accessibilityLabel="Sources and disclaimer"
            >
              <View style={{ flexDirection: "row", alignItems: "center" }}>
                <Ionicons
                  name="document-text-outline"
                  size={14}
                  color={colors.textMuted}
                />
                <Text
                  style={{
                    color: colors.textMuted,
                    fontSize: 13,
                    marginLeft: 4,
                  }}
                >
                  Sources & Disclaimer
                </Text>
              </View>
            </Link>
            <Link
              href="/privacy"
              style={{ flexDirection: "row", alignItems: "center" }}
              accessibilityRole="link"
              accessibilityLabel="Privacy policy"
            >
              <View style={{ flexDirection: "row", alignItems: "center" }}>
                <Ionicons
                  name="shield-checkmark-outline"
                  size={14}
                  color={colors.textMuted}
                />
                <Text
                  style={{
                    color: colors.textMuted,
                    fontSize: 13,
                    marginLeft: 4,
                  }}
                >
                  Privacy Policy
                </Text>
              </View>
            </Link>
          </View>
        </ScrollView>
      </KeyboardAvoidingView>
    </SafeScreen>
  );
}
