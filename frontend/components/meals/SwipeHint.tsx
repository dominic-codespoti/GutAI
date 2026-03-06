import { useEffect, useState, useRef } from "react";
import {
  Animated,
  View,
  Text,
  TouchableOpacity,
  StyleSheet,
} from "react-native";
import { getItem, setItem } from "../../src/utils/storage";
import { Ionicons } from "@expo/vector-icons";
import { radius, spacing } from "../../src/utils/theme";
import { useThemeColors } from "../../src/stores/theme";

const HINT_KEY = "meals_swipe_hint_dismissed";


export function SwipeHint() {
  const colors = useThemeColors();
  const styles = StyleSheet.create({
    container: { marginHorizontal: spacing.md, marginBottom: spacing.sm },
    inner: { flexDirection: "row", alignItems: "center", backgroundColor: colors.primaryLight ?? "#dcfce7", paddingHorizontal: spacing.md, paddingVertical: 10, borderRadius: radius.sm, borderWidth: 1, borderColor: colors.primary + "30", },
    text: { flex: 1, fontSize: 13, color: colors.text, },
  });
  const [show, setShow] = useState(false);
  const opacity = useRef(new Animated.Value(0)).current;

  useEffect(() => {
    getItem(HINT_KEY).then((val: string | null) => {
      if (!val) {
        setShow(true);
        Animated.timing(opacity, {
          toValue: 1,
          duration: 400,
          delay: 800,
          useNativeDriver: true,
        }).start();
      }
    });
  }, []);

  const dismiss = () => {
    Animated.timing(opacity, {
      toValue: 0,
      duration: 200,
      useNativeDriver: true,
    }).start(() => {
      setShow(false);
      setItem(HINT_KEY, "1");
    });
  };

  if (!show) return null;

  return (
    <Animated.View style={[styles.container, { opacity }]}>
      <View style={styles.inner}>
        <Ionicons
          name="arrow-back"
          size={16}
          color={colors.primary}
          style={{ marginRight: 6 }}
        />
        <Text style={styles.text}>
          Swipe left to delete, right to swap a food item
        </Text>
        <TouchableOpacity onPress={dismiss} hitSlop={8}>
          <Ionicons name="close" size={16} color={colors.textMuted} />
        </TouchableOpacity>
      </View>
    </Animated.View>
  );
}

