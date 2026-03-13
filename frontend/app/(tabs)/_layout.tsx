import { Tabs } from "expo-router";
import { Ionicons } from "@expo/vector-icons";
import {
  View,
  TouchableOpacity,
  Dimensions,
  Text,
  Animated,
} from "react-native";
import { useRouter } from "expo-router";
import { useThemeColors } from "../../src/stores/theme";
import Svg, { Path, Defs, Filter, FeDropShadow } from "react-native-svg";
import { useSafeAreaInsets } from "react-native-safe-area-context";

const { width: SCREEN_WIDTH } = Dimensions.get("window");
const TAB_BAR_HEIGHT = 58;
const CURVE_DEPTH = 24;
const NOTCH_WIDTH = 60;

function TabBarBackground() {
  const colors = useThemeColors();
  const w = SCREEN_WIDTH;
  const h = TAB_BAR_HEIGHT + CURVE_DEPTH;
  const cx = w / 2;
  const nw = NOTCH_WIDTH;
  const curveStart = cx - nw;
  const curveEnd = cx + nw;

  const d = [
    `M 0 ${CURVE_DEPTH}`,
    `L ${curveStart} ${CURVE_DEPTH}`,
    `C ${curveStart + 14} ${CURVE_DEPTH} ${cx - 28} 0 ${cx} 0`,
    `C ${cx + 28} 0 ${curveEnd - 14} ${CURVE_DEPTH} ${curveEnd} ${CURVE_DEPTH}`,
    `L ${w} ${CURVE_DEPTH}`,
    `L ${w} ${h}`,
    `L 0 ${h}`,
    `Z`,
  ].join(" ");

  const shadowH = h + 20;

  return (
    <Svg
      width={w}
      height={shadowH}
      style={{ position: "absolute", top: -CURVE_DEPTH - 10, left: 0 }}
    >
      <Defs>
        <Filter id="shadow" x="-10%" y="-10%" width="120%" height="130%">
          <FeDropShadow
            dx={0}
            dy={-3}
            stdDeviation={4}
            floodColor="#000"
            floodOpacity={0.08}
          />
        </Filter>
      </Defs>
      <Path d={d} fill={colors.card} filter="url(#shadow)" translateY={10} />
    </Svg>
  );
}

function CustomTabBar({
  state,
  descriptors,
  navigation,
}: {
  state: any;
  descriptors: any;
  navigation: any;
}) {
  const insets = useSafeAreaInsets();
  const colors = useThemeColors();

  return (
    <View
      style={{
        position: "absolute",
        bottom: 0,
        left: 0,
        right: 0,
        height: TAB_BAR_HEIGHT + insets.bottom,
        paddingBottom: insets.bottom,
        backgroundColor: colors.card,
      }}
    >
      <TabBarBackground />
      <View
        style={{
          flexDirection: "row",
          height: TAB_BAR_HEIGHT,
          alignItems: "flex-end",
          paddingBottom: 6,
        }}
      >
        {state.routes.map((route: any, index: number) => {
          const { options } = descriptors[route.key];
          const hidden = ["scan", "profile"];
          if (hidden.includes(route.name)) return null;

          const isFocused = state.index === index;
          const isMeals = route.name === "meals";
          const color = isFocused ? colors.primary : colors.textMuted;

          const onPress = () => {
            const event = navigation.emit({
              type: "tabPress",
              target: route.key,
              canPreventDefault: true,
            });
            if (!isFocused && !event.defaultPrevented) {
              navigation.navigate(route.name, route.params);
            }
          };

          if (isMeals) {
            return (
              <View key={route.key} style={{ flex: 1, alignItems: "center" }}>
                <TouchableOpacity
                  onPress={onPress}
                  activeOpacity={0.8}
                  style={{
                    width: 56,
                    height: 56,
                    borderRadius: 28,
                    backgroundColor: colors.primary,
                    alignItems: "center",
                    justifyContent: "center",
                    marginTop: -(CURVE_DEPTH + 8),
                    shadowColor: colors.primary,
                    shadowOffset: { width: 0, height: 4 },
                    shadowOpacity: 0.3,
                    shadowRadius: 8,
                    elevation: 8,
                  }}
                >
                  <Ionicons
                    name={isFocused ? "restaurant" : "restaurant-outline"}
                    size={24}
                    color={colors.textOnPrimary}
                  />
                </TouchableOpacity>
              </View>
            );
          }

          const iconMap: Record<string, [string, string]> = {
            index: ["home", "home-outline"],
            symptoms: ["pulse", "pulse-outline"],
            chat: ["chatbubble-ellipses", "chatbubble-ellipses-outline"],
            insights: ["analytics", "analytics-outline"],
          };
          const [activeIcon, inactiveIcon] = iconMap[route.name] ?? [
            "ellipse",
            "ellipse-outline",
          ];

          return (
            <AnimatedTabIcon
              key={route.key}
              iconName={isFocused ? activeIcon : inactiveIcon}
              size={22}
              color={color}
              label={options.title ?? route.name}
              isFocused={isFocused}
              onPress={onPress}
            />
          );
        })}
      </View>
    </View>
  );
}

function AnimatedTabIcon({
  iconName,
  size,
  color,
  label,
  isFocused,
  onPress,
}: {
  iconName: string;
  size: number;
  color: string;
  label: string;
  isFocused: boolean;
  onPress: () => void;
}) {
  const scale = new Animated.Value(1);

  const handlePressIn = () => {
    Animated.spring(scale, {
      toValue: 0.85,
      useNativeDriver: true,
      speed: 50,
      bounciness: 4,
    }).start();
  };

  const handlePressOut = () => {
    Animated.spring(scale, {
      toValue: 1,
      useNativeDriver: true,
      speed: 50,
      bounciness: 8,
    }).start();
  };

  return (
    <TouchableOpacity
      onPress={onPress}
      onPressIn={handlePressIn}
      onPressOut={handlePressOut}
      activeOpacity={0.7}
      style={{
        flex: 1,
        alignItems: "center",
        justifyContent: "center",
      }}
    >
      <Animated.View style={{ transform: [{ scale }], alignItems: "center" }}>
        <Ionicons name={iconName as any} size={size} color={color} />
        <Text
          style={{
            fontSize: 11,
            fontWeight: "600",
            color,
            marginTop: 2,
          }}
        >
          {label}
        </Text>
      </Animated.View>
    </TouchableOpacity>
  );
}

export default function TabLayout() {
  const router = useRouter();
  const insets = useSafeAreaInsets();
  const colors = useThemeColors();

  const handleBack = () => {
    if (router.canGoBack()) {
      router.back();
    } else {
      router.replace("/(tabs)");
    }
  };

  const headerRight = () => (
    <TouchableOpacity
      onPress={() => router.push("/(tabs)/profile")}
      style={{ marginRight: 14, padding: 4 }}
      hitSlop={8}
    >
      <Ionicons name="settings-outline" size={22} color={colors.textMuted} />
    </TouchableOpacity>
  );

  return (
    <Tabs
      backBehavior="history"
      tabBar={(props) => <CustomTabBar {...props} />}
      screenOptions={{
        tabBarActiveTintColor: colors.primary,
        tabBarInactiveTintColor: colors.textMuted,
        headerShown: true,
        headerStyle: { backgroundColor: colors.bg },
        headerShadowVisible: false,
        headerTintColor: colors.text,
        headerTitleStyle: { fontWeight: "700" as const, fontSize: 17 },
        headerRight,
        sceneStyle: {
          paddingBottom: TAB_BAR_HEIGHT + insets.bottom,
          backgroundColor: colors.bg,
        },
      }}
    >
      <Tabs.Screen
        name="index"
        options={{
          title: "Home",
          tabBarIcon: ({ color, focused }) => (
            <Ionicons
              name={focused ? "home" : "home-outline"}
              size={22}
              color={color}
            />
          ),
        }}
      />
      <Tabs.Screen
        name="symptoms"
        options={{
          title: "Symptoms",
          tabBarIcon: ({ color, focused }) => (
            <Ionicons
              name={focused ? "pulse" : "pulse-outline"}
              size={22}
              color={color}
            />
          ),
        }}
      />
      <Tabs.Screen
        name="meals"
        options={{
          title: "Meals",
          tabBarLabel: () => null,
        }}
      />
      <Tabs.Screen
        name="insights"
        options={{
          title: "Insights",
          tabBarIcon: ({ color, focused }) => (
            <Ionicons
              name={focused ? "analytics" : "analytics-outline"}
              size={22}
              color={color}
            />
          ),
        }}
      />
      <Tabs.Screen
        name="chat"
        options={{
          title: "Coach",
          headerShown: false,
          sceneStyle: { paddingBottom: 0, backgroundColor: colors.bg },
          tabBarIcon: ({ color, focused }) => (
            <Ionicons
              name={
                focused ? "chatbubble-ellipses" : "chatbubble-ellipses-outline"
              }
              size={22}
              color={color}
            />
          ),
        }}
      />
      <Tabs.Screen
        name="profile"
        options={{
          href: null,
          title: "Profile",
          headerRight: () => null,
        }}
      />
      <Tabs.Screen
        name="scan"
        options={{
          href: null,
          title: "Food Lookup",
          headerLeft: () => (
            <TouchableOpacity
              onPress={handleBack}
              style={{ marginLeft: 12, padding: 4 }}
            >
              <Ionicons name="chevron-back" size={24} color={colors.text} />
            </TouchableOpacity>
          ),
        }}
      />
    </Tabs>
  );
}
