import { useEffect, useRef } from "react";
import { View, TouchableOpacity } from "react-native";
import { Stack, useRouter, useSegments } from "expo-router";
import { QueryClientProvider } from "@tanstack/react-query";
import { SafeAreaProvider } from "react-native-safe-area-context";
import { StatusBar } from "expo-status-bar";
import { useAuthStore } from "../src/stores/auth";
import ToastContainer from "../components/Toast";
import { ErrorBoundary } from "../components/ErrorBoundary";
import { Ionicons } from "@expo/vector-icons";
import { queryClient } from "../src/queryClient";
import {
  useSubscriptionStore,
  configurePurchases,
} from "../src/stores/subscription";

function AuthGate() {
  const { isAuthenticated, isLoading, hydrate, user } = useAuthStore();
  const segments = useSegments();
  const router = useRouter();
  const rcConfigured = useRef(false);

  useEffect(() => {
    hydrate();
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
      }}
    >
      <Stack.Screen name="(tabs)" />
      <Stack.Screen name="(auth)" />
      <Stack.Screen name="onboarding" />
      <Stack.Screen
        name="food/[id]"
        options={{
          headerShown: true,
          title: "Food Details",
          headerBackTitle: "Back",
          headerStyle: { backgroundColor: "#f8fafc" },
          headerShadowVisible: false,
          headerTintColor: "#0f172a",
          headerTitleStyle: { fontWeight: "700", fontSize: 17 },
          headerLeft: () => (
            <TouchableOpacity
              onPress={() => router.back()}
              style={{ marginRight: 8, padding: 4 }}
            >
              <Ionicons name="chevron-back" size={24} color="#0f172a" />
            </TouchableOpacity>
          ),
        }}
      />
      <Stack.Screen
        name="settings"
        options={{
          headerShown: true,
          title: "Settings",
          headerBackTitle: "Back",
          headerStyle: { backgroundColor: "#f8fafc" },
          headerShadowVisible: false,
          headerTintColor: "#0f172a",
          headerTitleStyle: { fontWeight: "700", fontSize: 17 },
          headerLeft: () => (
            <TouchableOpacity
              onPress={() => router.back()}
              style={{ marginRight: 8, padding: 4 }}
            >
              <Ionicons name="chevron-back" size={24} color="#0f172a" />
            </TouchableOpacity>
          ),
        }}
      />
      <Stack.Screen
        name="sources"
        options={{
          headerShown: true,
          title: "Sources & Disclaimer",
          headerBackTitle: "Back",
          headerStyle: { backgroundColor: "#f8fafc" },
          headerShadowVisible: false,
          headerTintColor: "#0f172a",
          headerTitleStyle: { fontWeight: "700", fontSize: 17 },
          headerLeft: () => (
            <TouchableOpacity
              onPress={() => router.back()}
              style={{ marginRight: 8, padding: 4 }}
            >
              <Ionicons name="chevron-back" size={24} color="#0f172a" />
            </TouchableOpacity>
          ),
        }}
      />
      <Stack.Screen
        name="privacy"
        options={{
          headerShown: true,
          title: "Privacy Policy",
          headerBackTitle: "Back",
          headerStyle: { backgroundColor: "#f8fafc" },
          headerShadowVisible: false,
          headerTintColor: "#0f172a",
          headerTitleStyle: { fontWeight: "700", fontSize: 17 },
          headerLeft: () => (
            <TouchableOpacity
              onPress={() => router.back()}
              style={{ marginRight: 8, padding: 4 }}
            >
              <Ionicons name="chevron-back" size={24} color="#0f172a" />
            </TouchableOpacity>
          ),
        }}
      />
    </Stack>
  );
}

export default function RootLayout() {
  return (
    <ErrorBoundary>
      <SafeAreaProvider>
        <QueryClientProvider client={queryClient}>
          <StatusBar style="auto" />
          <View style={{ flex: 1 }}>
            <AuthGate />
            <ToastContainer />
          </View>
        </QueryClientProvider>
      </SafeAreaProvider>
    </ErrorBoundary>
  );
}
