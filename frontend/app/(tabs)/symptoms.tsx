import { useState, useCallback, useEffect, useMemo, useRef } from "react";
import {
  View,
  Text,
  TextInput,
  ScrollView,
  TouchableOpacity,
  ActivityIndicator,
  RefreshControl,
  BackHandler,
  Platform,
  Keyboard,
  KeyboardAvoidingView,
} from "react-native";
import { useGlobalSearchParams } from "expo-router";
import * as haptics from "../../src/utils/haptics";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { symptomApi, mealApi } from "../../src/api";
import { Ionicons } from "@expo/vector-icons";
import { toast } from "../../src/stores/toast";
import { confirm } from "../../src/utils/confirm";
import { ErrorState } from "../../components/ErrorState";
import { BottomSheet } from "../../components/BottomSheet";
import { EmptyState } from "../../components/EmptyState";
import {
  SymptomSkeleton,
  SymptomTypesSkeleton,
} from "../../components/SkeletonLoader";
import {
  shiftDate,
  formatDateLabel,
  toLocalDateStr,
  buildLoggedAt,
} from "../../src/utils/date";
import { getDeviceTimezoneId } from "../../src/utils/timezone";
import { severityColor } from "../../src/utils/colors";
import { radius, spacing, mealTypeEmoji } from "../../src/utils/theme";
import {
  useThemeColors,
  useThemeFonts,
  useThemeShadow,
} from "../../src/stores/theme";
import { PageTitle } from "../../components/PageTitle";
import { SafeScreen } from "../../components/SafeScreen";
import type {
  SymptomType,
  SymptomLog,
  MealLog,
  CreateSymptomRequest,
} from "../../src/types";
import { SeverityTimeline } from "../../src/components/charts/SeverityTimeline";

function SeverityDot({
  n,
  selected,
  onPress,
}: {
  n: number;
  selected: boolean;
  onPress: () => void;
}) {
  const colors = useThemeColors();
  const { shadowMd } = useThemeShadow();
  const bg = selected ? severityColor(n) : colors.borderLight;
  const size = selected ? 34 : 30;
  return (
    <TouchableOpacity
      onPress={() => {
        haptics.selection();
        onPress();
      }}
      hitSlop={{ top: 6, bottom: 6, left: 2, right: 2 }}
      accessibilityRole="radio"
      accessibilityLabel={`Severity ${n}`}
      accessibilityState={{ selected }}
      style={{
        width: size,
        height: size,
        borderRadius: size / 2,
        alignItems: "center",
        justifyContent: "center",
        backgroundColor: bg,
        borderWidth: selected ? 2 : 0,
        borderColor: selected ? severityColor(n) : "transparent",
        ...(selected ? shadowMd : {}),
      }}
    >
      <Text
        style={{
          fontSize: 13,
          fontWeight: "700",
          color: selected ? colors.textOnPrimary : colors.textMuted,
        }}
      >
        {n}
      </Text>
    </TouchableOpacity>
  );
}

function parseDurationToTimeSpan(input: string): string | undefined {
  if (!input.trim()) return undefined;
  const lower = input.trim().toLowerCase();

  // Already in HH:mm:ss or mm:ss format
  if (/^\d{1,2}:\d{2}(:\d{2})?$/.test(lower))
    return lower.includes(":") && lower.split(":").length === 2
      ? `00:${lower}`
      : lower;

  let totalMinutes = 0;
  const hourMatch = lower.match(/(\d+(?:\.\d+)?)\s*h(?:ours?|rs?)?/);
  const minMatch = lower.match(/(\d+(?:\.\d+)?)\s*m(?:in(?:utes?|s?)?)?/);

  if (hourMatch) totalMinutes += parseFloat(hourMatch[1]) * 60;
  if (minMatch) totalMinutes += parseFloat(minMatch[1]);

  // Plain number: treat as minutes
  if (!hourMatch && !minMatch) {
    const num = parseFloat(lower);
    if (!isNaN(num)) totalMinutes = num;
    else return undefined;
  }

  if (totalMinutes <= 0) return undefined;
  const h = Math.floor(totalMinutes / 60);
  const m = Math.floor(totalMinutes % 60);
  const s = Math.round((totalMinutes % 1) * 60);
  return `${String(h).padStart(2, "0")}:${String(m).padStart(2, "0")}:${String(s).padStart(2, "0")}`;
}

export default function SymptomsScreen() {
  const colors = useThemeColors();
  const fonts = useThemeFonts();
  const { shadow, shadowMd } = useThemeShadow();
  const params = useGlobalSearchParams<{ date?: string }>();

  const initialDate = useMemo(() => {
    if (typeof params.date === "string" && /^\d{4}-\d{2}-\d{2}$/.test(params.date)) {
      return params.date;
    }
    return toLocalDateStr();
  }, [params.date]);

  const [viewMode, setViewMode] = useState<"today" | "history">("today");
  const [historyDays, setHistoryDays] = useState<7 | 30 | 90>(30);
  const [historyTypeId, setHistoryTypeId] = useState<number | null>(null);

  const [selectedType, setSelectedType] = useState<SymptomType | null>(null);
  const [severity, setSeverity] = useState(5);
  const [notes, setNotes] = useState("");
  const [duration, setDuration] = useState("");
  const [linkedMealId, setLinkedMealId] = useState<string | null>(null);
  const [editingSymptom, setEditingSymptom] = useState<SymptomLog | null>(null);
  const [editSeverity, setEditSeverity] = useState(5);
  const [editNotes, setEditNotes] = useState("");
  const [editLinkedMealId, setEditLinkedMealId] = useState<string | null>(null);
  const [editHour, setEditHour] = useState("12");
  const [editMinute, setEditMinute] = useState("00");
  const [selectedDate, setSelectedDate] = useState(initialDate);
  const previousTodayRef = useRef(toLocalDateStr());
  const now = new Date();
  const [symptomHour, setSymptomHour] = useState(String(now.getHours()).padStart(2, "0"));
  const [symptomMinute, setSymptomMinute] = useState(String(now.getMinutes()).padStart(2, "0"));
  const queryClient = useQueryClient();
  const timezoneId = getDeviceTimezoneId();
  const isToday = selectedDate === toLocalDateStr();

  useEffect(() => {
    if (typeof params.date === "string" && /^\d{4}-\d{2}-\d{2}$/.test(params.date)) {
      setSelectedDate(params.date);
    }
  }, [params.date]);
  useEffect(() => {
    const currentToday = toLocalDateStr();
    if (!params.date && selectedDate === previousTodayRef.current && selectedDate !== currentToday) {
      setSelectedDate(currentToday);
    }
    previousTodayRef.current = currentToday;
  }, [params.date, selectedDate]);

  useEffect(() => {
    setLinkedMealId(null);
  }, [selectedDate]);

  const {
    data: types,
    isLoading: typesLoading,
    isError: typesError,
    refetch: refetchTypes,
  } = useQuery({
    queryKey: ["symptom-types"],
    queryFn: () => symptomApi.types().then((r) => r.data),
  });

  const { data: todaysMeals } = useQuery({
    queryKey: ["meals", selectedDate, timezoneId],
    queryFn: () => mealApi.list(selectedDate).then((r) => r.data),
  });

  const {
    data: history,
    isLoading: historyLoading,
    isError: historyError,
    refetch: refetchTodaySymptoms,
  } = useQuery({
    queryKey: ["symptom-history", selectedDate, timezoneId],
    queryFn: () => symptomApi.list({ date: selectedDate }).then((r) => r.data),
  });

  const historyFromDate = useMemo(() => {
    const d = new Date();
    d.setDate(d.getDate() - historyDays);
    return toLocalDateStr(d);
  }, [historyDays]);

  const historyToDate = useMemo(() => toLocalDateStr(), []);

  const {
    data: allHistoryLogs,
    isLoading: allHistoryLoading,
    isError: allHistoryError,
    refetch: refetchHistoryLogs,
  } = useQuery({
    queryKey: [
      "symptom-range-history",
      historyFromDate,
      historyToDate,
      historyTypeId,
      timezoneId,
    ],
    queryFn: () =>
      symptomApi
        .history({
          from: historyFromDate,
          to: historyToDate,
          typeId: historyTypeId ?? undefined,
        })
        .then((r) => r.data),
    enabled: viewMode === "history",
  });

  const [refreshing, setRefreshing] = useState(false);
  const onRefresh = useCallback(async () => {
    setRefreshing(true);
    try {
      if (viewMode === "history") {
        await Promise.all([refetchHistoryLogs(), refetchTypes()]);
      } else {
        await Promise.all([refetchTodaySymptoms(), refetchTypes()]);
      }
    } finally {
      setRefreshing(false);
    }
  }, [viewMode, refetchHistoryLogs, refetchTodaySymptoms, refetchTypes]);
  const logMutation = useMutation({
    mutationFn: () => {
      return symptomApi.create({
        symptomTypeId: selectedType!.id,
        severity,
        occurredAt: buildLoggedAt(selectedDate, Number(symptomHour), Number(symptomMinute)),
        notes: notes.trim() || undefined,
        relatedMealLogId: linkedMealId ?? undefined,
        duration: parseDurationToTimeSpan(duration),
      });
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["symptom-history"] });
      queryClient.invalidateQueries({ queryKey: ["symptom-range-history"] });
      queryClient.invalidateQueries({ queryKey: ["symptoms-today"] });
      queryClient.invalidateQueries({ queryKey: ["correlations"] });
      queryClient.invalidateQueries({ queryKey: ["trigger-foods-dashboard"] });
      setSeverity(5);
      setNotes("");
      setDuration("");
      setLinkedMealId(null);
      const resetNow = new Date();
      setSymptomHour(String(resetNow.getHours()).padStart(2, "0"));
      setSymptomMinute(String(resetNow.getMinutes()).padStart(2, "0"));
      toast.success("Symptom recorded");
      haptics.success();
    },
    onError: () => {
      toast.error("Failed to log symptom");
      haptics.error();
    },
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, data }: { id: string; data: CreateSymptomRequest }) =>
      symptomApi.update(id, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["symptom-history"] });
      queryClient.invalidateQueries({ queryKey: ["symptom-range-history"] });
      queryClient.invalidateQueries({ queryKey: ["symptoms-today"] });
      queryClient.invalidateQueries({ queryKey: ["correlations"] });
      queryClient.invalidateQueries({ queryKey: ["trigger-foods-dashboard"] });
      toast.success("Symptom updated");
      haptics.success();
    },
    onError: () => {
      toast.error("Failed to update symptom");
      haptics.error();
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => symptomApi.delete(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["symptom-history"] });
      queryClient.invalidateQueries({ queryKey: ["symptom-range-history"] });
      queryClient.invalidateQueries({ queryKey: ["symptoms-today"] });
      queryClient.invalidateQueries({ queryKey: ["correlations"] });
      queryClient.invalidateQueries({ queryKey: ["trigger-foods-dashboard"] });
      haptics.heavy();
    },
    onError: () => {
      toast.error("Failed to delete symptom");
      haptics.error();
    },
  });
  const categoryOrder = [
    "Digestive",
    "Neurological",
    "Energy",
    "Skin",
    "Other",
  ];
  const categories = types
    ? [...new Set(types.map((t) => t.category))].sort(
        (a, b) =>
          (categoryOrder.indexOf(a) === -1 ? 99 : categoryOrder.indexOf(a)) -
          (categoryOrder.indexOf(b) === -1 ? 99 : categoryOrder.indexOf(b)),
      )
    : [];

  const getMealLabel = (meal: MealLog) => {
    const time = new Date(meal.loggedAt).toLocaleTimeString([], {
      hour: "2-digit",
      minute: "2-digit",
    });
    const items = meal.items
      .slice(0, 2)
      .map((i) => i.foodName)
      .join(", ");
    return `${meal.mealType} @ ${time}${items ? " — " + items : ""}`;
  };

  const getMealLabelById = (mealId: string) => {
    const meal = todaysMeals?.find((m) => m.id === mealId);
    if (!meal) return "Linked meal";
    const time = new Date(meal.loggedAt).toLocaleTimeString([], {
      hour: "2-digit",
      minute: "2-digit",
    });
    return `${meal.mealType} @ ${time}`;
  };

  const openEdit = (log: SymptomLog) => {
    setEditingSymptom(log);
    setEditSeverity(log.severity);
    setEditNotes(log.notes ?? "");
    setEditLinkedMealId(log.relatedMealLogId ?? null);
    const t = new Date(log.occurredAt);
    setEditHour(String(t.getHours()).padStart(2, "0"));
    setEditMinute(String(t.getMinutes()).padStart(2, "0"));
  };

  const saveEdit = () => {
    if (!editingSymptom) return;
    const origDate = toLocalDateStr(new Date(editingSymptom.occurredAt));
    updateMutation.mutate({
      id: editingSymptom.id,
      data: {
        symptomTypeId: editingSymptom.symptomTypeId,
        severity: editSeverity,
        occurredAt: buildLoggedAt(origDate, Number(editHour), Number(editMinute)),
        notes: editNotes.trim() || undefined,
        relatedMealLogId: editLinkedMealId ?? undefined,
        duration: parseDurationToTimeSpan(editingSymptom?.duration || ""),
      },
    });
  };

  useEffect(() => {
    if (Platform.OS === "android") {
      const handler = BackHandler.addEventListener("hardwareBackPress", () => {
        if (editingSymptom) {
          setEditingSymptom(null);
          return true;
        }
        if (selectedType) {
          setSelectedType(null);
          return true;
        }
        return false;
      });
      return () => handler.remove();
    }
  }, [editingSymptom, selectedType]);

  return (
    <SafeScreen edges={[]}>
      <KeyboardAvoidingView
        behavior={Platform.OS === "ios" ? "padding" : "height"}
        style={{ flex: 1 }}
      >
        <ScrollView
          style={{ flex: 1, backgroundColor: colors.bg }}
          showsVerticalScrollIndicator={false}
          keyboardShouldPersistTaps="handled"
          keyboardDismissMode="on-drag"
          refreshControl={
            <RefreshControl
              refreshing={refreshing}
              onRefresh={onRefresh}
              tintColor={colors.primary}
            />
          }
        >
          <View style={{ padding: spacing.xl }}>
            <PageTitle title="Symptoms" />
            {/* View Mode Toggle */}
            <View
              style={{
                flexDirection: "row",
                backgroundColor: colors.card,
                borderRadius: radius.md,
                padding: 4,
                marginBottom: spacing.lg,
                borderWidth: 1,
                borderColor: colors.borderLight,
                ...shadow,
              }}
            >
              <TouchableOpacity
                onPress={() => {
                  haptics.selection();
                  setViewMode("today");
                }}
                accessibilityRole="tab"
                accessibilityState={{ selected: viewMode === "today" }}
                accessibilityLabel="Today view"
                style={{
                  flex: 1,
                  paddingVertical: 10,
                  borderRadius: radius.sm,
                  backgroundColor:
                    viewMode === "today" ? colors.primary : "transparent",
                  alignItems: "center",
                  justifyContent: "center",
                }}
              >
                <Text
                  style={{
                    fontSize: 14,
                    fontWeight: "700",
                    color:
                      viewMode === "today"
                        ? colors.textOnPrimary
                        : colors.textSecondary,
                  }}
                >
                  Today
                </Text>
              </TouchableOpacity>
              <TouchableOpacity
                onPress={() => {
                  haptics.selection();
                  setViewMode("history");
                }}
                accessibilityRole="tab"
                accessibilityState={{ selected: viewMode === "history" }}
                accessibilityLabel="History view"
                style={{
                  flex: 1,
                  paddingVertical: 10,
                  borderRadius: radius.sm,
                  backgroundColor:
                    viewMode === "history" ? colors.primary : "transparent",
                  alignItems: "center",
                  justifyContent: "center",
                }}
              >
                <Text
                  style={{
                    fontSize: 14,
                    fontWeight: "700",
                    color:
                      viewMode === "history"
                        ? colors.textOnPrimary
                        : colors.textSecondary,
                  }}
                >
                  History
                </Text>
              </TouchableOpacity>
            </View>

            {viewMode === "today" ? (
              <>
                {/* Date Navigation */}
                <View
                  style={{
                    flexDirection: "row",
                    alignItems: "center",
                    justifyContent: "space-between",
                    backgroundColor: colors.card,
                    borderRadius: radius.md,
                    padding: spacing.md,
                    marginBottom: spacing.lg,
                    ...shadow,
                  }}
                >
                  <TouchableOpacity
                    onPress={() => setSelectedDate(shiftDate(selectedDate, -1))}
                    style={{ padding: 8 }}
                    accessibilityRole="button"
                    accessibilityLabel="Previous day"
                    hitSlop={{ top: 8, bottom: 8, left: 8, right: 8 }}
                  >
                    <Ionicons
                      name="chevron-back"
                      size={22}
                      color={colors.textSecondary}
                    />
                  </TouchableOpacity>
                  <TouchableOpacity
                    onPress={() => setSelectedDate(toLocalDateStr())}
                    accessibilityRole="button"
                    accessibilityLabel="Go to today"
                  >
                    <Text
                      style={{
                        fontSize: 16,
                        fontWeight: "700",
                        color: colors.text,
                        textAlign: "center",
                      }}
                    >
                      {formatDateLabel(selectedDate)}
                    </Text>
                    {!isToday && (
                      <Text
                        style={{
                          fontSize: 11,
                          color: colors.textMuted,
                          textAlign: "center",
                        }}
                      >
                        {selectedDate}
                      </Text>
                    )}
                  </TouchableOpacity>
                  <TouchableOpacity
                    onPress={() =>
                      !isToday && setSelectedDate(shiftDate(selectedDate, 1))
                    }
                    style={{ padding: 8, opacity: isToday ? 0.3 : 1 }}
                    accessibilityRole="button"
                    accessibilityLabel="Next day"
                    hitSlop={{ top: 8, bottom: 8, left: 8, right: 8 }}
                  >
                    <Ionicons
                      name="chevron-forward"
                      size={22}
                      color={colors.textSecondary}
                    />
                  </TouchableOpacity>
                </View>

                {/* Log Symptom Section */}
                <Text
                  style={{ ...fonts.h3, marginBottom: spacing.md }}
                  accessibilityRole="header"
                >
                  Log Symptom
                </Text>

                {typesLoading ? (
                  <SymptomTypesSkeleton />
                ) : typesError ? (
                  <ErrorState
                    message="Failed to load symptom types"
                    onRetry={() => refetchTypes()}
                  />
                ) : (
                  categories.map((category) => (
                    <View key={category} style={{ marginBottom: spacing.md }}>
                      <Text
                        style={{
                          fontSize: 13,
                          fontWeight: "600",
                          color: colors.textMuted,
                          marginBottom: 6,
                          textTransform: "uppercase",
                          letterSpacing: 0.5,
                        }}
                      >
                        {category}
                      </Text>
                      <View
                        style={{ flexDirection: "row", flexWrap: "wrap", gap: 6 }}
                      >
                        {types
                          ?.filter((t) => t.category === category)
                          .map((type) => {
                            const active = selectedType?.id === type.id;
                            return (
                              <TouchableOpacity
                                key={type.id}
                                onPress={() => {
                                  haptics.selection();
                                  setSelectedType(active ? null : type);
                                }}
                                accessibilityRole="button"
                                accessibilityLabel={type.name}
                                accessibilityState={{ selected: active }}
                                style={{
                                  backgroundColor: active
                                    ? colors.primary
                                    : colors.card,
                                  borderRadius: radius.sm,
                                  paddingHorizontal: 14,
                                  paddingVertical: 10,
                                  borderWidth: 1,
                                  borderColor: active
                                    ? colors.primary
                                    : colors.border,
                                  flexDirection: "row",
                                  alignItems: "center",
                                  gap: 6,
                                  ...(active ? shadowMd : shadow),
                                }}
                              >
                                <Text style={{ fontSize: 16 }}>{type.icon}</Text>
                                <Text
                                  style={{
                                    fontSize: 13,
                                    fontWeight: "600",
                                    color: active
                                      ? colors.textOnPrimary
                                      : colors.text,
                                  }}
                                >
                                  {type.name}
                                </Text>
                              </TouchableOpacity>
                            );
                          })}
                      </View>
                    </View>
                  ))
                )}

                {/* Severity & Details Panel */}
                {selectedType && (
                  <View
                    style={{
                      backgroundColor: colors.card,
                      borderRadius: radius.lg,
                      padding: spacing.xl,
                      marginTop: spacing.sm,
                      marginBottom: spacing.lg,
                      ...shadowMd,
                    }}
                  >
                    <View
                      style={{
                        flexDirection: "row",
                        alignItems: "center",
                        marginBottom: spacing.lg,
                      }}
                    >
                      <Text style={{ fontSize: 24, marginRight: spacing.sm }}>
                        {selectedType.icon}
                      </Text>
                      <Text style={{ ...fonts.h3, flex: 1 }}>
                        {selectedType.name}
                      </Text>
                      <View
                        style={{
                          backgroundColor: severityColor(severity) + "20",
                          borderRadius: radius.sm,
                          paddingHorizontal: 10,
                          paddingVertical: 4,
                        }}
                      >
                        <Text
                          style={{
                            fontSize: 14,
                            fontWeight: "800",
                            color: severityColor(severity),
                          }}
                        >
                          {severity}/10
                        </Text>
                      </View>
                    </View>

                    <Text style={{ ...fonts.h4, marginBottom: spacing.sm }}>
                      Severity
                    </Text>
                    <View
                      style={{
                        flexDirection: "row",
                        justifyContent: "space-between",
                        marginBottom: 4,
                      }}
                    >
                      {[1, 2, 3, 4, 5, 6, 7, 8, 9, 10].map((n) => (
                        <SeverityDot
                          key={n}
                          n={n}
                          selected={severity === n}
                          onPress={() => setSeverity(n)}
                        />
                      ))}
                    </View>
                    <View
                      style={{
                        flexDirection: "row",
                        justifyContent: "space-between",
                        marginBottom: spacing.lg,
                      }}
                    >
                      <Text style={{ fontSize: 11, color: colors.textMuted }}>
                        Mild
                      </Text>
                      <Text style={{ fontSize: 11, color: colors.textMuted }}>
                        Severe
                      </Text>
                    </View>

                    <Text style={{ ...fonts.h4, marginBottom: spacing.sm }}>
                      Time occurred
                    </Text>
                    <View
                      style={{
                        flexDirection: "row",
                        alignItems: "center",
                        gap: 4,
                        marginBottom: spacing.lg,
                      }}
                    >
                      <TextInput
                        value={symptomHour}
                        onChangeText={(t) => {
                          const cleaned = t.replace(/[^0-9]/g, "").slice(0, 2);
                          const num = Number(cleaned);
                          if (cleaned.length <= 1 || (num >= 0 && num <= 23)) {
                            setSymptomHour(cleaned);
                          }
                        }}
                        keyboardType="number-pad"
                        maxLength={2}
                        selectTextOnFocus
                        style={{
                          width: 48,
                          borderWidth: 1,
                          borderColor: colors.border,
                          borderRadius: radius.sm,
                          padding: spacing.sm,
                          fontSize: 16,
                          fontWeight: "700",
                          color: colors.text,
                          textAlign: "center",
                          backgroundColor: colors.bg,
                        }}
                      />
                      <Text
                        style={{
                          fontSize: 18,
                          fontWeight: "700",
                          color: colors.textSecondary,
                        }}
                      >
                        :
                      </Text>
                      <TextInput
                        value={symptomMinute}
                        onChangeText={(t) => {
                          const cleaned = t.replace(/[^0-9]/g, "").slice(0, 2);
                          const num = Number(cleaned);
                          if (cleaned.length <= 1 || (num >= 0 && num <= 59)) {
                            setSymptomMinute(cleaned);
                          }
                        }}
                        keyboardType="number-pad"
                        maxLength={2}
                        selectTextOnFocus
                        style={{
                          width: 48,
                          borderWidth: 1,
                          borderColor: colors.border,
                          borderRadius: radius.sm,
                          padding: spacing.sm,
                          fontSize: 16,
                          fontWeight: "700",
                          color: colors.text,
                          textAlign: "center",
                          backgroundColor: colors.bg,
                        }}
                      />
                      <Text
                        style={{
                          fontSize: 12,
                          color: colors.textMuted,
                          marginLeft: 4,
                        }}
                      >
                        {Number(symptomHour) >= 12 ? "PM" : "AM"}
                      </Text>
                    </View>

                    <Text style={{ ...fonts.h4, marginBottom: 6 }}>Notes</Text>
                    <TextInput
                      placeholder="How does it feel? What might have triggered it?"
                      placeholderTextColor={colors.textLight}
                      value={notes}
                      onChangeText={setNotes}
                      multiline
                      autoCapitalize="sentences"
                      maxLength={500}
                      style={{
                        borderWidth: 1,
                        borderColor: colors.border,
                        borderRadius: radius.sm,
                        padding: spacing.md,
                        fontSize: 14,
                        color: colors.text,
                        backgroundColor: colors.bg,
                        textAlignVertical: "top",
                        minHeight: 56,
                        marginBottom: spacing.md,
                      }}
                    />

                    <Text style={{ ...fonts.h4, marginBottom: 6 }}>
                      Duration (optional)
                    </Text>
                    <TextInput
                      placeholder="e.g., 30m, 1h, 45 minutes, 01:30"
                      placeholderTextColor={colors.textLight}
                      value={duration}
                      onChangeText={setDuration}
                      autoCapitalize="none"
                      maxLength={100}
                      style={{
                        borderWidth: 1,
                        borderColor: colors.border,
                        borderRadius: radius.sm,
                        padding: spacing.md,
                        fontSize: 14,
                        color: colors.text,
                        backgroundColor: colors.bg,
                      }}
                    />

                    {todaysMeals && todaysMeals.length > 0 && (
                      <View style={{ marginTop: spacing.lg }}>
                        <Text style={{ ...fonts.h4, marginBottom: 6 }}>
                          Link to a meal{" "}
                          <Text style={{ fontSize: 11, color: colors.textMuted }}>
                            (optional)
                          </Text>
                        </Text>
                        <View
                          style={{
                            flexDirection: "row",
                            flexWrap: "wrap",
                            gap: 6,
                          }}
                        >
                          {todaysMeals.map((meal) => {
                            const active = linkedMealId === meal.id;
                            return (
                              <TouchableOpacity
                                key={meal.id}
                                onPress={() =>
                                  setLinkedMealId(active ? null : meal.id)
                                }
                                accessibilityRole="button"
                                accessibilityState={{ selected: active }}
                                style={{
                                  backgroundColor: active
                                    ? colors.secondary
                                    : colors.bg,
                                  borderRadius: radius.sm,
                                  paddingHorizontal: 12,
                                  paddingVertical: 8,
                                  borderWidth: 1,
                                  borderColor: active
                                    ? colors.secondary
                                    : colors.border,
                                }}
                              >
                                <Text
                                  style={{
                                    fontSize: 12,
                                    fontWeight: "600",
                                    color: active
                                      ? colors.textOnPrimary
                                      : colors.textSecondary,
                                  }}
                                  numberOfLines={1}
                                >
                                  {getMealLabel(meal)}
                                </Text>
                              </TouchableOpacity>
                            );
                          })}
                        </View>
                      </View>
                    )}

                    <TouchableOpacity
                      onPress={() => logMutation.mutate()}
                      disabled={logMutation.isPending}
                      accessibilityRole="button"
                      accessibilityLabel="Save symptom"
                      style={{
                        backgroundColor: colors.primary,
                        borderRadius: radius.md,
                        padding: 14,
                        alignItems: "center",
                        marginTop: spacing.xl,
                        ...shadow,
                      }}
                    >
                      {logMutation.isPending ? (
                        <ActivityIndicator
                          color={colors.textOnPrimary}
                          size="small"
                        />
                      ) : (
                        <Text
                          style={{
                            color: colors.textOnPrimary,
                            fontWeight: "700",
                            fontSize: 15,
                          }}
                        >
                          Record Symptom
                        </Text>
                      )}
                    </TouchableOpacity>
                  </View>
                )}

                {/* Today's History Section */}
                <Text
                  style={{
                    ...fonts.h3,
                    marginBottom: spacing.md,
                    marginTop: spacing.sm,
                  }}
                  accessibilityRole="header"
                >
                  {isToday
                    ? "Today's Symptoms"
                    : `Symptoms for ${formatDateLabel(selectedDate)}`}
                </Text>

                {typesLoading ? (
                  <SymptomSkeleton />
                ) : typesError ? (
                  <ErrorState
                    message="Failed to load symptom types"
                    onRetry={refetchTypes}
                  />
                ) : historyLoading ? (
                  <SymptomSkeleton />
                ) : historyError ? (
                  <ErrorState
                    message="Failed to load symptoms"
                    onRetry={refetchTodaySymptoms}
                  />
                ) : history && history.length > 0 ? (
                  history.map((log) => (
                    <View
                      key={log.id}
                      style={{
                        backgroundColor: colors.card,
                        borderRadius: radius.md,
                        padding: spacing.lg,
                        marginBottom: spacing.sm,
                        flexDirection: "row",
                        alignItems: "center",
                        ...shadow,
                      }}
                    >
                      <View
                        style={{
                          width: 42,
                          height: 42,
                          borderRadius: radius.sm,
                          backgroundColor: severityColor(log.severity) + "15",
                          alignItems: "center",
                          justifyContent: "center",
                          marginRight: spacing.md,
                        }}
                      >
                        <Text style={{ fontSize: 20 }}>{log.icon}</Text>
                      </View>
                      <View style={{ flex: 1 }}>
                        <Text
                          style={{
                            fontWeight: "600",
                            color: colors.text,
                            fontSize: 15,
                          }}
                        >
                          {log.symptomName}
                        </Text>
                        <View
                          style={{
                            flexDirection: "row",
                            alignItems: "center",
                            marginTop: 3,
                            gap: 8,
                          }}
                        >
                          <View
                            style={{
                              backgroundColor: severityColor(log.severity) + "20",
                              borderRadius: 4,
                              paddingHorizontal: 6,
                              paddingVertical: 2,
                            }}
                          >
                            <Text
                              style={{
                                fontSize: 11,
                                fontWeight: "700",
                                color: severityColor(log.severity),
                              }}
                            >
                              {log.severity}/10
                            </Text>
                          </View>
                          <Text style={{ fontSize: 12, color: colors.textMuted }}>
                            {new Date(log.occurredAt).toLocaleTimeString([], {
                              hour: "2-digit",
                              minute: "2-digit",
                            })}
                          </Text>
                        </View>
                        {log.relatedMealLogId && (
                          <View
                            style={{
                              flexDirection: "row",
                              alignItems: "center",
                              marginTop: 4,
                            }}
                          >
                            <Ionicons
                              name="link-outline"
                              size={12}
                              color={colors.secondary}
                            />
                            <Text
                              style={{
                                fontSize: 11,
                                color: colors.secondary,
                                marginLeft: 4,
                              }}
                            >
                              {getMealLabelById(log.relatedMealLogId)}
                            </Text>
                          </View>
                        )}
                        {log.notes && (
                          <Text
                            style={{
                              fontSize: 12,
                              color: colors.textSecondary,
                              marginTop: 4,
                              fontStyle: "italic",
                            }}
                          >
                            "{log.notes}"
                          </Text>
                        )}
                      </View>
                      <View
                        style={{
                          flexDirection: "row",
                          gap: 8,
                          alignItems: "center",
                        }}
                      >
                        <TouchableOpacity
                          onPress={() => openEdit(log)}
                          style={{ padding: 8 }}
                          accessibilityRole="button"
                          accessibilityLabel="Edit symptom"
                          hitSlop={{ top: 4, bottom: 4, left: 4, right: 4 }}
                        >
                          <Ionicons
                            name="pencil-outline"
                            size={18}
                            color={colors.secondary}
                          />
                        </TouchableOpacity>
                        <TouchableOpacity
                          onPress={() =>
                            confirm("Delete", "Remove this symptom log?", () =>
                              deleteMutation.mutate(log.id),
                            )
                          }
                          style={{ padding: 8 }}
                          accessibilityRole="button"
                          accessibilityLabel="Delete symptom"
                          hitSlop={{ top: 4, bottom: 4, left: 4, right: 4 }}
                        >
                          <Ionicons
                            name="close-circle-outline"
                            size={20}
                            color={colors.danger}
                          />
                        </TouchableOpacity>
                      </View>
                    </View>
                  ))
                ) : (
                  <View
                    style={{
                      backgroundColor: colors.card,
                      borderRadius: radius.md,
                      padding: spacing.xxxl,
                      alignItems: "center",
                      ...shadow,
                    }}
                  >
                    <Text style={{ fontSize: 32 }}>😊</Text>
                    <Text
                      style={{
                        color: colors.primary,
                        marginTop: spacing.sm,
                        fontWeight: "600",
                        fontSize: 14,
                      }}
                    >
                      No symptoms logged — great!
                    </Text>
                  </View>
                )}
              </>
            ) : (
              <>
                {/* History Mode: Period Selector */}
                <Text
                  style={{ ...fonts.h4, marginBottom: spacing.xs }}
                  accessibilityRole="header"
                >
                  Time Range
                </Text>
                <View
                  style={{
                    flexDirection: "row",
                    gap: 8,
                    marginBottom: spacing.lg,
                  }}
                >
                  {([7, 30, 90] as const).map((days) => {
                    const active = historyDays === days;
                    return (
                      <TouchableOpacity
                        key={days}
                        onPress={() => {
                          haptics.selection();
                          setHistoryDays(days);
                        }}
                        accessibilityRole="radio"
                        accessibilityState={{ selected: active }}
                        accessibilityLabel={`Past ${days} days`}
                        style={{
                          flex: 1,
                          paddingVertical: 10,
                          borderRadius: radius.md,
                          backgroundColor: active
                            ? colors.primary
                            : colors.card,
                          alignItems: "center",
                          borderWidth: active ? 0 : 1,
                          borderColor: colors.borderLight,
                          ...shadow,
                        }}
                      >
                        <Text
                          style={{
                            fontSize: 13,
                            fontWeight: "700",
                            color: active
                              ? colors.textOnPrimary
                              : colors.textSecondary,
                          }}
                        >
                          {days} Days
                        </Text>
                      </TouchableOpacity>
                    );
                  })}
                </View>

                {/* History Mode: Type Filter */}
                <Text
                  style={{ ...fonts.h4, marginBottom: spacing.xs }}
                  accessibilityRole="header"
                >
                  Filter by Symptom
                </Text>
                <ScrollView
                  horizontal
                  showsHorizontalScrollIndicator={false}
                  contentContainerStyle={{
                    gap: 6,
                    paddingBottom: spacing.lg,
                  }}
                >
                  <TouchableOpacity
                    onPress={() => {
                      haptics.selection();
                      setHistoryTypeId(null);
                    }}
                    accessibilityRole="button"
                    accessibilityLabel="All types"
                    accessibilityState={{ selected: historyTypeId === null }}
                    style={{
                      backgroundColor:
                        historyTypeId === null
                          ? colors.primary
                          : colors.card,
                      borderRadius: radius.full,
                      paddingHorizontal: 14,
                      paddingVertical: 8,
                      borderWidth: 1,
                      borderColor:
                        historyTypeId === null
                          ? colors.primary
                          : colors.border,
                      flexDirection: "row",
                      alignItems: "center",
                      gap: 4,
                      ...(historyTypeId === null ? shadowMd : shadow),
                    }}
                  >
                    <Text
                      style={{
                        fontSize: 13,
                        fontWeight: "600",
                        color:
                          historyTypeId === null
                            ? colors.textOnPrimary
                            : colors.text,
                      }}
                    >
                      All Types
                    </Text>
                  </TouchableOpacity>
                  {types?.map((t) => {
                    const active = historyTypeId === t.id;
                    return (
                      <TouchableOpacity
                        key={t.id}
                        onPress={() => {
                          haptics.selection();
                          setHistoryTypeId(active ? null : t.id);
                        }}
                        accessibilityRole="button"
                        accessibilityLabel={t.name}
                        accessibilityState={{ selected: active }}
                        style={{
                          backgroundColor: active
                            ? colors.primary
                            : colors.card,
                          borderRadius: radius.full,
                          paddingHorizontal: 14,
                          paddingVertical: 8,
                          borderWidth: 1,
                          borderColor: active
                            ? colors.primary
                            : colors.border,
                          flexDirection: "row",
                          alignItems: "center",
                          gap: 6,
                          ...(active ? shadowMd : shadow),
                        }}
                      >
                        <Text style={{ fontSize: 14 }}>{t.icon}</Text>
                        <Text
                          style={{
                            fontSize: 13,
                            fontWeight: "600",
                            color: active
                              ? colors.textOnPrimary
                              : colors.text,
                          }}
                        >
                          {t.name}
                        </Text>
                      </TouchableOpacity>
                    );
                  })}
                </ScrollView>

                {allHistoryLogs && allHistoryLogs.length >= 3 && (
                  <View
                    style={{
                      backgroundColor: colors.card,
                      borderRadius: radius.lg,
                      padding: spacing.lg,
                      marginBottom: spacing.lg,
                      ...shadow,
                    }}
                  >
                    <Text
                      style={{
                        ...fonts.h4,
                        marginBottom: spacing.sm,
                      }}
                      accessibilityRole="header"
                    >
                      Severity over time
                    </Text>
                    <SeverityTimeline logs={allHistoryLogs} />
                  </View>
                )}
                {/* History Results List */}
                <View
                  style={{
                    flexDirection: "row",
                    alignItems: "center",
                    justifyContent: "space-between",
                    marginBottom: spacing.md,
                  }}
                >
                  <Text
                    style={{ ...fonts.h3 }}
                    accessibilityRole="header"
                  >
                    Symptom Log History
                  </Text>
                  {allHistoryLogs && (
                    <Text style={{ fontSize: 12, color: colors.textMuted }}>
                      {allHistoryLogs.length} {allHistoryLogs.length === 1 ? "entry" : "entries"}
                    </Text>
                  )}
                </View>

                {allHistoryLoading ? (
                  <SymptomSkeleton />
                ) : allHistoryError ? (
                  <ErrorState
                    message="Failed to load symptom history"
                    onRetry={refetchHistoryLogs}
                  />
                ) : allHistoryLogs && allHistoryLogs.length > 0 ? (
                  allHistoryLogs.map((log) => (
                    <View
                      key={log.id}
                      style={{
                        backgroundColor: colors.card,
                        borderRadius: radius.md,
                        padding: spacing.lg,
                        marginBottom: spacing.sm,
                        flexDirection: "row",
                        alignItems: "center",
                        ...shadow,
                      }}
                    >
                      <View
                        style={{
                          width: 42,
                          height: 42,
                          borderRadius: radius.sm,
                          backgroundColor: severityColor(log.severity) + "15",
                          alignItems: "center",
                          justifyContent: "center",
                          marginRight: spacing.md,
                        }}
                      >
                        <Text style={{ fontSize: 20 }}>{log.icon}</Text>
                      </View>
                      <View style={{ flex: 1 }}>
                        <Text
                          style={{
                            fontWeight: "600",
                            color: colors.text,
                            fontSize: 15,
                          }}
                        >
                          {log.symptomName}
                        </Text>
                        <View
                          style={{
                            flexDirection: "row",
                            alignItems: "center",
                            marginTop: 3,
                            gap: 8,
                          }}
                        >
                          <View
                            style={{
                              backgroundColor: severityColor(log.severity) + "20",
                              borderRadius: 4,
                              paddingHorizontal: 6,
                              paddingVertical: 2,
                            }}
                          >
                            <Text
                              style={{
                                fontSize: 11,
                                fontWeight: "700",
                                color: severityColor(log.severity),
                              }}
                            >
                              {log.severity}/10
                            </Text>
                          </View>
                          <Text style={{ fontSize: 12, color: colors.textMuted }}>
                            {formatDateLabel(toLocalDateStr(new Date(log.occurredAt)))},{" "}
                            {new Date(log.occurredAt).toLocaleTimeString([], {
                              hour: "2-digit",
                              minute: "2-digit",
                            })}
                          </Text>
                        </View>
                        {log.notes && (
                          <Text
                            style={{
                              fontSize: 12,
                              color: colors.textSecondary,
                              marginTop: 4,
                              fontStyle: "italic",
                            }}
                          >
                            "{log.notes}"
                          </Text>
                        )}
                      </View>
                      <View
                        style={{
                          flexDirection: "row",
                          gap: 8,
                          alignItems: "center",
                        }}
                      >
                        <TouchableOpacity
                          onPress={() => openEdit(log)}
                          style={{ padding: 8 }}
                          accessibilityRole="button"
                          accessibilityLabel="Edit symptom"
                          hitSlop={{ top: 4, bottom: 4, left: 4, right: 4 }}
                        >
                          <Ionicons
                            name="pencil-outline"
                            size={18}
                            color={colors.secondary}
                          />
                        </TouchableOpacity>
                        <TouchableOpacity
                          onPress={() =>
                            confirm("Delete", "Remove this symptom log?", () =>
                              deleteMutation.mutate(log.id),
                            )
                          }
                          style={{ padding: 8 }}
                          accessibilityRole="button"
                          accessibilityLabel="Delete symptom"
                          hitSlop={{ top: 4, bottom: 4, left: 4, right: 4 }}
                        >
                          <Ionicons
                            name="close-circle-outline"
                            size={20}
                            color={colors.danger}
                          />
                        </TouchableOpacity>
                      </View>
                    </View>
                  ))
                ) : (
                  <EmptyState
                    icon="medkit-outline"
                    title="No symptoms logged in this period"
                    compact
                  />
                )}
              </>
            )}
          </View>
        </ScrollView>
      </KeyboardAvoidingView>

      {/* Edit Symptom Modal */}
      <BottomSheet
        visible={!!editingSymptom}
        onClose={() => setEditingSymptom(null)}
      >
        <Text style={{ ...fonts.h3, marginBottom: 4 }}>Edit Symptom</Text>
        {editingSymptom && (
          <Text style={{ ...fonts.body, marginBottom: spacing.lg }}>
            {editingSymptom.icon} {editingSymptom.symptomName}
          </Text>
        )}

        <Text style={{ ...fonts.h4, marginBottom: spacing.sm }}>
          Severity: {editSeverity}/10
        </Text>
        <View
          style={{
            flexDirection: "row",
            justifyContent: "space-between",
            marginBottom: 4,
          }}
        >
          {[1, 2, 3, 4, 5, 6, 7, 8, 9, 10].map((n) => (
            <SeverityDot
              key={n}
              n={n}
              selected={editSeverity === n}
              onPress={() => setEditSeverity(n)}
            />
          ))}
        </View>
        <View
          style={{
            flexDirection: "row",
            justifyContent: "space-between",
            marginBottom: spacing.lg,
          }}
        >
          <Text style={{ fontSize: 11, color: colors.textMuted }}>Mild</Text>
          <Text style={{ fontSize: 11, color: colors.textMuted }}>Severe</Text>
        </View>

        <Text style={{ ...fonts.h4, marginBottom: spacing.sm }}>
          Time occurred
        </Text>
        <View
          style={{
            flexDirection: "row",
            alignItems: "center",
            gap: 4,
            marginBottom: spacing.lg,
          }}
        >
          <TextInput
            value={editHour}
            onChangeText={(t) => {
              const cleaned = t.replace(/[^0-9]/g, "").slice(0, 2);
              const num = Number(cleaned);
              if (cleaned.length <= 1 || (num >= 0 && num <= 23)) {
                setEditHour(cleaned);
              }
            }}
            keyboardType="number-pad"
            maxLength={2}
            selectTextOnFocus
            style={{
              width: 48,
              borderWidth: 1,
              borderColor: colors.border,
              borderRadius: radius.sm,
              padding: spacing.sm,
              fontSize: 16,
              fontWeight: "700",
              color: colors.text,
              textAlign: "center",
              backgroundColor: colors.bg,
            }}
          />
          <Text
            style={{
              fontSize: 18,
              fontWeight: "700",
              color: colors.textSecondary,
            }}
          >
            :
          </Text>
          <TextInput
            value={editMinute}
            onChangeText={(t) => {
              const cleaned = t.replace(/[^0-9]/g, "").slice(0, 2);
              const num = Number(cleaned);
              if (cleaned.length <= 1 || (num >= 0 && num <= 59)) {
                setEditMinute(cleaned);
              }
            }}
            keyboardType="number-pad"
            maxLength={2}
            selectTextOnFocus
            style={{
              width: 48,
              borderWidth: 1,
              borderColor: colors.border,
              borderRadius: radius.sm,
              padding: spacing.sm,
              fontSize: 16,
              fontWeight: "700",
              color: colors.text,
              textAlign: "center",
              backgroundColor: colors.bg,
            }}
          />
          <Text
            style={{
              fontSize: 12,
              color: colors.textMuted,
              marginLeft: 4,
            }}
          >
            {Number(editHour) >= 12 ? "PM" : "AM"}
          </Text>
        </View>

        <Text style={{ ...fonts.h4, marginBottom: 6 }}>Notes</Text>
        <TextInput
          value={editNotes}
          onChangeText={setEditNotes}
          multiline
          autoCapitalize="sentences"
          maxLength={500}
          placeholder="Add notes..."
          placeholderTextColor={colors.textLight}
          style={{
            borderWidth: 1,
            borderColor: colors.border,
            borderRadius: radius.sm,
            padding: spacing.md,
            fontSize: 14,
            color: colors.text,
            textAlignVertical: "top",
            minHeight: 56,
            marginBottom: spacing.md,
            backgroundColor: colors.bg,
          }}
        />

        <TextInput
          placeholder="Duration (e.g., 30 minutes, 2 hours)"
          placeholderTextColor={colors.textLight}
          value={editingSymptom?.duration || ""}
          onChangeText={(t) =>
            setEditingSymptom((prev) =>
              prev ? { ...prev, duration: t } : prev,
            )
          }
          autoCapitalize="none"
          maxLength={100}
          style={{
            borderWidth: 1,
            borderColor: colors.border,
            borderRadius: radius.sm,
            padding: spacing.md,
            fontSize: 14,
            color: colors.text,
            backgroundColor: colors.bg,
            marginBottom: spacing.md,
          }}
        />

        {todaysMeals && todaysMeals.length > 0 && (
          <>
            <Text style={{ ...fonts.h4, marginBottom: 6 }}>Linked Meal</Text>
            <View
              style={{
                flexDirection: "row",
                flexWrap: "wrap",
                gap: 6,
                marginBottom: spacing.md,
              }}
            >
              <TouchableOpacity
                onPress={() => setEditLinkedMealId(null)}
                accessibilityRole="button"
                style={{
                  backgroundColor:
                    editLinkedMealId === null
                      ? colors.secondary
                      : colors.borderLight,
                  borderRadius: radius.sm,
                  paddingHorizontal: 12,
                  paddingVertical: 6,
                }}
              >
                <Text
                  style={{
                    fontSize: 12,
                    color:
                      editLinkedMealId === null
                        ? colors.textOnPrimary
                        : colors.textMuted,
                  }}
                >
                  None
                </Text>
              </TouchableOpacity>
              {todaysMeals.map((meal) => {
                const active = editLinkedMealId === meal.id;
                return (
                  <TouchableOpacity
                    key={meal.id}
                    onPress={() => setEditLinkedMealId(meal.id)}
                    accessibilityRole="button"
                    style={{
                      backgroundColor: active
                        ? colors.secondary
                        : colors.borderLight,
                      borderRadius: radius.sm,
                      paddingHorizontal: 12,
                      paddingVertical: 6,
                    }}
                  >
                    <Text
                      style={{
                        fontSize: 12,
                        color: active
                          ? colors.textOnPrimary
                          : colors.textSecondary,
                      }}
                      numberOfLines={1}
                    >
                      {getMealLabel(meal)}
                    </Text>
                  </TouchableOpacity>
                );
              })}
            </View>
          </>
        )}

        <View
          style={{
            flexDirection: "row",
            justifyContent: "flex-end",
            gap: 12,
          }}
        >
          <TouchableOpacity
            onPress={() => setEditingSymptom(null)}
            accessibilityRole="button"
            accessibilityLabel="Cancel editing"
            style={{ paddingHorizontal: 20, paddingVertical: 10 }}
          >
            <Text style={{ color: colors.textMuted, fontWeight: "600" }}>
              Cancel
            </Text>
          </TouchableOpacity>
          <TouchableOpacity
            onPress={saveEdit}
            disabled={updateMutation.isPending}
            accessibilityRole="button"
            accessibilityLabel="Save changes"
            style={{
              backgroundColor: colors.primary,
              paddingHorizontal: 20,
              paddingVertical: 10,
              borderRadius: radius.sm,
            }}
          >
            {updateMutation.isPending ? (
              <ActivityIndicator color={colors.textOnPrimary} size="small" />
            ) : (
              <Text style={{ color: colors.textOnPrimary, fontWeight: "600" }}>
                Save
              </Text>
            )}
          </TouchableOpacity>
        </View>
      </BottomSheet>
    </SafeScreen>
  );
}
