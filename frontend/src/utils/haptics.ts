import { Platform } from "react-native";
import * as Haptics from "expo-haptics";

const isNative = Platform.OS !== "web";

/** Light tap — tab switch, chip select, toggle */
export function selection() {
  if (isNative) Haptics.selectionAsync();
}

/** Subtle bump — button press, FAB open, favorite toggle */
export function light() {
  if (isNative) Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
}

/** Medium thud — long press, swipe action trigger */
export function medium() {
  if (isNative) Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
}

/** Heavy thud — destructive confirm trigger */
export function heavy() {
  if (isNative) Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Heavy);
}

/** Success buzz — form submit success, log success */
export function success() {
  if (isNative)
    Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
}

/** Warning buzz — destructive dialog appears */
export function warning() {
  if (isNative)
    Haptics.notificationAsync(Haptics.NotificationFeedbackType.Warning);
}

/** Error buzz — validation fail, network error */
export function error() {
  if (isNative)
    Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error);
}
