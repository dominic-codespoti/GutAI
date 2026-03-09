import { useEffect, useRef } from "react";
import { View, TouchableOpacity } from "react-native";
import { Stack, useRouter, useSegments } from "expo-router";
import { QueryClientProvider } from "@tanstack/react-query";
import { SafeAreaProvider } from "react-native-safe-area-context";
import { StatusBar } from "expo-status-bar";
import { useAuthStore } from "../src/stores/auth";
import { useThemeStore, useThemeColors } from "../src/stores/theme";
import ToastContainer from "../components/Toast";
import { ErrorBoundary } from "../components/ErrorBoundary";
import { Ionicons } from "@expo/vector-icons";
import { queryClient } from "../src/queryClient";
import { api } from "../src/api/client";
import {
  useSubscriptionStore,
  configurePurchases,
} from "../src/stores/subscription";

function AuthGate() {
  const { isAuthenticated, isLoading, hydrate, user } = useAuthStore();
  const c = useThemeColors();
  const segments = useSegments();
  const router = useRouter();
  const rcConfigured = useRef(false);

  const safeBack = () => {
    if (router.canGoBack()) {
      router.back();
    } else {
      router.replace("/(tabs)");
    }
  };

  useEffect(() => {
    hydrate();
    api.get("/health").catch(() => {});
  }, []);

  // Initialize RevenueCat when user is authenticated
  useEffect(() => {
    if (isAuthenticated && user?.id && !rcConfigured.current) {
      rcConfigured.current = true;
      configurePurchases(user.id).then(() => {
        useSubscriptionStore.getState().checkEntitlement();
      });
    }
    if (!isAuthenticated) {
      rcConfigured.current = false;
    }
  }, [isAuthenticated, user?.id]);

  useEffect(() => {
    if (isLoading) return;
    const inAuthGroup = segments[0] === "(auth)";
    const inOnboarding = segments[0] === "onboarding";
    const inPrivacy = segments[0] === "privacy";
    const inSources = segments[0] === "sources";
    if (!isAuthenticated && !inAuthGroup && !inPrivacy && !inSources) {
      router.replace("/(auth)/login");
    } else if (isAuthenticated && inAuthGroup) {
      if (user && !user.onboardingCompleted) {
        router.replace("/onboarding");
      } else {
        router.replace("/(tabs)");
      }
    } else if (
      isAuthenticated &&
      !inOnboarding &&
      user &&
      !user.onboardingCompleted
    ) {
      router.replace("/onboarding");
    } else if (isAuthenticated && inOnboarding && user?.onboardingCompleted) {
      router.replace("/(tabs)");
    }
  }, [isAuthenticated, isLoading, segments, user]);

  return (
    <Stack
      screenOptions={{
        headerShown: false,
        animation: "slide_from_right",
        animationDuration: 250,
      }}
    >
      <Stack.Screen name="(tabs)" options={{ animation: "none" }} />
      <Stack.Screen name="(auth)" options={{ animation: "fade" }} />
      <Stack.Screen name="onboarding" options={{ animation: "fade" }} />
      <Stack.Screen
        name="food/[id]"
        options={{
          headerShown: true,
          animation: "slide_from_right",
          title: "Food Details",
          headerBackTitle: "Back",
          headerStyle: { backgroundColor: c.bg },
          headerShadowVisible: false,
          headerTintColor: c.text,
          headerTitleStyle: { fontWeight: "700", fontSize: 17 },
          headerLeft: () => (
            <TouchableOpacity
              onPress={safeBack}
              style={{ marginRight: 8, padding: 4 }}
            >
              <Ionicons name="chevron-back" size={24} color={c.text} />
            </TouchableOpacity>
          ),
        }}
      />
      <Stack.Screen
        name="settings"
        options={{
          headerShown: true,
          animation: "slide_from_right",
          title: "Settings",
          headerBackTitle: "Back",
          headerStyle: { backgroundColor: c.bg },
          headerShadowVisible: false,
          headerTintColor: c.text,
          headerTitleStyle: { fontWeight: "700", fontSize: 17 },
          headerLeft: () => (
            <TouchableOpacity
              onPress={safeBack}
              style={{ marginRight: 8, padding: 4 }}
            >
              <Ionicons name="chevron-back" size={24} color={c.text} />
            </TouchableOpacity>
          ),
        }}
      />
      <Stack.Screen
        name="sources"
        options={{
          headerShown: true,
          animation: "slide_from_right",
          title: "Sources & Disclaimer",
          headerBackTitle: "Back",
          headerStyle: { backgroundColor: c.bg },
          headerShadowVisible: false,
          headerTintColor: c.text,
          headerTitleStyle: { fontWeight: "700", fontSize: 17 },
          headerLeft: () => (
            <TouchableOpacity
              onPress={safeBack}
              style={{ marginRight: 8, padding: 4 }}
            >
              <Ionicons name="chevron-back" size={24} color={c.text} />
            </TouchableOpacity>
          ),
        }}
      />
      <Stack.Screen
        name="privacy"
        options={{
          headerShown: true,
          animation: "slide_from_right",
          title: "Privacy Policy",
          headerBackTitle: "Back",
          headerStyle: { backgroundColor: c.bg },
          headerShadowVisible: false,
          headerTintColor: c.text,
          headerTitleStyle: { fontWeight: "700", fontSize: 17 },
          headerLeft: () => (
            <TouchableOpacity
              onPress={safeBack}
              style={{ marginRight: 8, padding: 4 }}
            >
              <Ionicons name="chevron-back" size={24} color={c.text} />
            </TouchableOpacity>
          ),
        }}
      />
    </Stack>
  );
}

export default function RootLayout() {
  const resolved = useThemeStore((s) => s.resolved);
  const c = useThemeColors();

  return (
    <ErrorBoundary>
      <SafeAreaProvider>
        <QueryClientProvider client={queryClient}>
          <StatusBar style={resolved === "dark" ? "light" : "dark"} />
          <View style={{ flex: 1, backgroundColor: c.bg }}>
            <AuthGate />
            <ToastContainer />
          </View>
        </QueryClientProvider>
      </SafeAreaProvider>
    </ErrorBoundary>
  );
}
