import { useEffect, useRef } from "react";
import { View, Animated } from "react-native";

function SkeletonBlock({
  width,
  height,
  style,
}: {
  width: number | string;
  height: number;
  style?: any;
}) {
  const opacity = useRef(new Animated.Value(0.3)).current;

  useEffect(() => {
    const animation = Animated.loop(
      Animated.sequence([
        Animated.timing(opacity, {
          toValue: 0.7,
          duration: 800,
          useNativeDriver: true,
        }),
        Animated.timing(opacity, {
          toValue: 0.3,
          duration: 800,
          useNativeDriver: true,
        }),
      ]),
    );
    animation.start();
    return () => animation.stop();
  }, [opacity]);

  return (
    <Animated.View
      style={[
        {
          width: width as any,
          height,
          backgroundColor: "#e2e8f0",
          borderRadius: 6,
          opacity,
        },
        style,
      ]}
    />
  );
}

export function CardSkeleton() {
  return (
    <View
      style={{
        backgroundColor: "#fff",
        borderRadius: 12,
        padding: 16,
        marginBottom: 8,
      }}
    >
      <SkeletonBlock width="60%" height={16} />
      <SkeletonBlock width="40%" height={12} style={{ marginTop: 8 }} />
      <View style={{ flexDirection: "row", marginTop: 12, gap: 8 }}>
        <SkeletonBlock width="25%" height={10} />
        <SkeletonBlock width="25%" height={10} />
        <SkeletonBlock width="25%" height={10} />
      </View>
    </View>
  );
}

export function MealCardSkeleton() {
  return (
    <View
      style={{
        backgroundColor: "#fff",
        borderRadius: 12,
        padding: 16,
        marginBottom: 8,
      }}
    >
      <View style={{ flexDirection: "row", justifyContent: "space-between" }}>
        <View>
          <SkeletonBlock width={80} height={16} />
          <SkeletonBlock width={50} height={12} style={{ marginTop: 6 }} />
        </View>
        <SkeletonBlock width={60} height={16} />
      </View>
      <View
        style={{
          marginTop: 12,
          borderTopWidth: 1,
          borderTopColor: "#f1f5f9",
          paddingTop: 8,
        }}
      >
        <SkeletonBlock width="70%" height={12} />
      </View>
      <View style={{ marginTop: 8 }}>
        <SkeletonBlock width="55%" height={12} />
      </View>
    </View>
  );
}

export function DashboardSkeleton() {
  return (
    <View style={{ padding: 20 }}>
      <SkeletonBlock width={180} height={24} />
      <SkeletonBlock width={100} height={14} style={{ marginTop: 8 }} />
      <View
        style={{
          backgroundColor: "#fff",
          borderRadius: 16,
          padding: 20,
          marginTop: 20,
        }}
      >
        <SkeletonBlock width={120} height={16} />
        <View
          style={{
            flexDirection: "row",
            justifyContent: "space-between",
            marginTop: 12,
          }}
        >
          <SkeletonBlock width={80} height={36} />
          <SkeletonBlock width={60} height={20} />
        </View>
        <SkeletonBlock
          width="100%"
          height={8}
          style={{ marginTop: 12, borderRadius: 4 }}
        />
      </View>
      <View
        style={{
          backgroundColor: "#fff",
          borderRadius: 16,
          padding: 20,
          marginTop: 12,
        }}
      >
        <SkeletonBlock width={80} height={16} />
        <View
          style={{
            flexDirection: "row",
            justifyContent: "space-around",
            marginTop: 12,
          }}
        >
          <SkeletonBlock width={40} height={40} />
          <SkeletonBlock width={40} height={40} />
          <SkeletonBlock width={40} height={40} />
          <SkeletonBlock width={40} height={40} />
        </View>
      </View>
    </View>
  );
}

export function SymptomTypesSkeleton() {
  return (
    <View>
      {[
        [90, 110],
        [80, 100, 90],
        [100, 70, 110],
      ].map((widths, i) => (
        <View key={i} style={{ marginBottom: 14 }}>
          <SkeletonBlock width={80} height={12} style={{ marginBottom: 6 }} />
          <View style={{ flexDirection: "row", flexWrap: "wrap", gap: 6 }}>
            {widths.map((w, j) => (
              <SkeletonBlock
                key={j}
                width={w}
                height={36}
                style={{ borderRadius: 8 }}
              />
            ))}
          </View>
        </View>
      ))}
    </View>
  );
}

export function SymptomSkeleton() {
  return (
    <>
      {[1, 2, 3].map((i) => (
        <View
          key={i}
          style={{
            backgroundColor: "#fff",
            borderRadius: 10,
            padding: 14,
            marginBottom: 6,
            flexDirection: "row",
            alignItems: "center",
          }}
        >
          <SkeletonBlock
            width={32}
            height={32}
            style={{ borderRadius: 16, marginRight: 10 }}
          />
          <View style={{ flex: 1 }}>
            <SkeletonBlock width="50%" height={14} />
            <SkeletonBlock width="30%" height={10} style={{ marginTop: 6 }} />
          </View>
          <SkeletonBlock width={40} height={20} style={{ borderRadius: 4 }} />
        </View>
      ))}
    </>
  );
}

export function InsightsSkeleton() {
  return (
    <View>
      {[1, 2, 3].map((i) => (
        <View
          key={i}
          style={{
            backgroundColor: "#fff",
            borderRadius: 8,
            padding: 12,
            marginBottom: 4,
          }}
        >
          <View
            style={{
              flexDirection: "row",
              justifyContent: "space-between",
              marginBottom: 6,
            }}
          >
            <SkeletonBlock width={100} height={12} />
            <SkeletonBlock width={60} height={13} />
          </View>
          <SkeletonBlock width="100%" height={6} style={{ borderRadius: 3 }} />
        </View>
      ))}
    </View>
  );
}

export function ProfileSkeleton() {
  return (
    <View style={{ padding: 20 }}>
      <View
        style={{
          backgroundColor: "#fff",
          borderRadius: 16,
          padding: 20,
          alignItems: "center",
        }}
      >
        <SkeletonBlock
          width={72}
          height={72}
          style={{ borderRadius: 36, marginBottom: 12 }}
        />
        <SkeletonBlock width={140} height={20} />
        <SkeletonBlock width={180} height={14} style={{ marginTop: 6 }} />
      </View>
      <View
        style={{
          backgroundColor: "#fff",
          borderRadius: 12,
          padding: 16,
          marginTop: 12,
        }}
      >
        <SkeletonBlock width={100} height={16} style={{ marginBottom: 12 }} />
        {[1, 2, 3, 4, 5].map((i) => (
          <View
            key={i}
            style={{
              flexDirection: "row",
              justifyContent: "space-between",
              paddingVertical: 6,
            }}
          >
            <SkeletonBlock width={70} height={14} />
            <SkeletonBlock width={50} height={14} />
          </View>
        ))}
      </View>
    </View>
  );
}
