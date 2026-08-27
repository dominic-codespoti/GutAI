import { useEffect, useRef } from "react";
import {
  AppState,
  Platform,
  View,
  Text,
  TouchableOpacity,
  ActivityIndicator,
} from "react-native";
import * as SplashScreen from "expo-splash-screen";
import * as Notifications from "expo-notifications";
import { GestureHandlerRootView } from "react-native-gesture-handler";
import { Stack, useRouter, useSegments } from "expo-router";
import { QueryClientProvider } from "@tanstack/react-query";
import { SafeAreaProvider } from "react-native-safe-area-context";
import { StatusBar } from "expo-status-bar";
import { useAuthStore } from "../src/stores/auth";
import { useMealSheetStore } from "../src/stores/mealSheet";
import { useThemeStore, useThemeColors } from "../src/stores/theme";
import ToastContainer from "../components/Toast";
import { ErrorBoundary } from "../components/ErrorBoundary";
import { Ionicons } from "@expo/vector-icons";
import { queryClient } from "../src/queryClient";
import { api } from "../src/api/client";
import {
  loadReminderPrefs,
  syncStreakNudge,
} from "../src/utils/notifications";
import { toLocalDateStr } from "../src/utils/date";
import { getDeviceTimezoneId } from "../src/utils/timezone";
import {
  useSubscriptionStore,
  configurePurchases,
} from "../src/stores/subscription";
import * as haptics from "../src/utils/haptics";

// Hold the native splash visible until AuthGate hides it post-hydration.
SplashScreen.preventAutoHideAsync().catch(() => {});

function AuthGate() {
  const { isAuthenticated, isLoading, isReconnecting, hydrate, connect, user } = useAuthStore();
  const c = useThemeColors();
  const segments = useSegments();
  const router = useRouter();
  const rcConfigured = useRef(false);

  const safeBack = () => {
    haptics.light();
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

  // Hold the native splash until auth hydration resolves — no white flash,
  // no content flash behind the splash graphic.
  const splashHidden = useRef(false);
  useEffect(() => {
    if (isLoading || splashHidden.current) return;
    splashHidden.current = true;
    SplashScreen.hideAsync().catch(() => {});
  }, [isLoading]);

  // Local reminder housekeeping and day/timezone refresh: re-sync the streak nudge
  // and refresh date-sensitive queries whenever the app comes to foreground or day boundaries cross.
  useEffect(() => {
    if (!isAuthenticated) return;
    let nudgeSub: Notifications.Subscription | undefined;
    let responseSub: Notifications.Subscription | undefined;
    let dayTimer: NodeJS.Timeout | undefined;
    let lastActiveDate = toLocalDateStr();
    let lastActiveTz = getDeviceTimezoneId();

    const resync = () => {
      void (async () => {
        const prefs = await loadReminderPrefs();
        const tz = getDeviceTimezoneId();
        const cached = queryClient.getQueryData<{ length: number }>([
          "meals",
          toLocalDateStr(),
          tz,
        ]);
        await syncStreakNudge(prefs, (cached?.length ?? 0) > 0);
      })();
    };

    const refreshDateContext = () => {
      const currentDate = toLocalDateStr();
      const currentTz = getDeviceTimezoneId();
      if (currentDate === lastActiveDate && currentTz === lastActiveTz) return;
      const previousDate = lastActiveDate;

      lastActiveDate = currentDate;
      const mealSheet = useMealSheetStore.getState();
      if (mealSheet.selectedDate === previousDate) {
        mealSheet.setDate(currentDate);
      }
      lastActiveTz = currentTz;
      for (const queryKey of [
        ["meals"],
        ["daily-summary"],
        ["symptoms-today"],
        ["symptom-history"],
        ["symptom-range-history"],
        ["nutrition-trends"],
        ["nutrition-by-meal-type"],
        ["additive-exposure"],
        ["correlations"],
        ["trigger-foods"],
        ["trigger-foods-dashboard"],
        ["food-diary-analysis"],
        ["elimination-diet-status"],
        ["streak"],
      ]) {
        void queryClient.invalidateQueries({ queryKey });
      }
    };

    const scheduleDayBoundary = () => {
      const now = new Date();
      const nextDay = new Date(now);
      nextDay.setHours(24, 0, 0, 0);
      dayTimer = setTimeout(() => {
        refreshDateContext();
        resync();
        scheduleDayBoundary();
      }, Math.max(nextDay.getTime() - now.getTime(), 1000));
    };

    resync();
    scheduleDayBoundary();

    const appState = AppState.addEventListener("change", (s) => {
      if (s === "active") {
        refreshDateContext();
        resync();
      }
    });
    responseSub = Notifications.addNotificationResponseReceivedListener(() => {
      router.push("/(tabs)/meals");
    });
    return () => {
      clearTimeout(dayTimer);
      dayTimer = undefined;
      appState.remove();
      nudgeSub?.remove();
      responseSub?.remove();
    };
  }, [isAuthenticated]);

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
    // When reconnecting, stay on current screen — don't push to login
    if (isReconnecting) return;
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
    <>
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
              style={{ marginRight: 8, padding: 10 }}
              hitSlop={{ top: 8, bottom: 8, left: 8, right: 8 }}
              accessibilityRole="button"
              accessibilityLabel="Go back"
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
              style={{ marginRight: 8, padding: 10 }}
              hitSlop={{ top: 8, bottom: 8, left: 8, right: 8 }}
              accessibilityRole="button"
              accessibilityLabel="Go back"
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
              style={{ marginRight: 8, padding: 10 }}
              hitSlop={{ top: 8, bottom: 8, left: 8, right: 8 }}
              accessibilityRole="button"
              accessibilityLabel="Go back"
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
              style={{ marginRight: 8, padding: 10 }}
              hitSlop={{ top: 8, bottom: 8, left: 8, right: 8 }}
              accessibilityRole="button"
              accessibilityLabel="Go back"
            >
              <Ionicons name="chevron-back" size={24} color={c.text} />
            </TouchableOpacity>
          ),
        }}
      />
    </Stack>
      {isReconnecting && !isAuthenticated && (
      <View
        style={{
          position: "absolute",
          top: 0,
          left: 0,
          right: 0,
          bottom: 0,
          backgroundColor: c.bg,
          alignItems: "center",
          justifyContent: "center",
          paddingHorizontal: 32,
        }}
      >
        <ActivityIndicator size="large" color={c.primary} />
        <Text
          style={{
            color: c.text,
            fontSize: 16,
            fontWeight: "600",
            marginTop: 20,
          }}
        >
          Reconnecting…
        </Text>
        <Text
          style={{
            color: c.textSecondary,
            fontSize: 14,
            marginTop: 8,
            textAlign: "center",
          }}
        >
          The server is waking up. This should only take a few seconds.
        </Text>
        <TouchableOpacity
          onPress={() => connect()}
          style={{
            marginTop: 24,
            paddingVertical: 12,
            paddingHorizontal: 24,
            borderRadius: 8,
            backgroundColor: c.primary,
          }}
          accessibilityRole="button"
          accessibilityLabel="Retry connection"
        >
          <Text style={{ color: c.textOnPrimary, fontWeight: "600", fontSize: 15 }}>
            Retry
          </Text>
        </TouchableOpacity>
      </View>
      )}
    </>
  );
}

export default function RootLayout() {
  const resolved = useThemeStore((s) => s.resolved);
  const c = useThemeColors();

  useEffect(() => {
    if (Platform.OS !== "web" || typeof document === "undefined") return;
    document.documentElement.style.backgroundColor = c.bg;
    document.body.style.backgroundColor = c.bg;
  }, [c.bg]);

  return (
    <ErrorBoundary>
      <SafeAreaProvider>
        <QueryClientProvider client={queryClient}>
          <StatusBar style={resolved === "dark" ? "light" : "dark"} />
          <GestureHandlerRootView style={{ flex: 1, backgroundColor: c.bg }}>
            <AuthGate />
            <ToastContainer />
          </GestureHandlerRootView>
        </QueryClientProvider>
      </SafeAreaProvider>
    </ErrorBoundary>
  );
}
