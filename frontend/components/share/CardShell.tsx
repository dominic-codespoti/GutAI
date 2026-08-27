import { View, Text, StyleSheet } from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { shareCardColors as sc, spacing, radius } from "../../src/utils/theme";
import type {
  TriggerFoodCardData,
  SafetyReportCardData,
  StreakCardData,
  WeeklySummaryCardData,
} from "../../src/stores/shareCard";

/** Fixed card width — share cards are portrait social assets. */
export const CARD_WIDTH = 420;

export function CardShell({
  children,
  title,
  subtitle,
}: {
  children: React.ReactNode;
  title: string;
  subtitle?: string;
}) {
  return (
    <View style={[styles.card, { width: CARD_WIDTH }]}>
      <View style={styles.header}>
        <View style={styles.logoDisc}>
          <Ionicons name="nutrition" size={18} color={sc.primary} />
        </View>
        <Text style={styles.wordmark}>GutAI</Text>
        <View style={{ flex: 1 }} />
        {subtitle ? <Text style={styles.subtitle}>{subtitle}</Text> : null}
      </View>
      <Text style={styles.title}>{title}</Text>
      {children}
      <View style={[styles.footer, { borderTopColor: sc.border }]}>
        <Text style={styles.footerText}>Not medical advice · GutAI</Text>
      </View>
    </View>
  );
}

export function TriggerFoodsContent({ data }: { data: TriggerFoodCardData }) {
  const top = data.items.slice(0, 5);
  const max = Math.max(...top.map((t) => t.count), 1);
  return (
    <>
      <StatTitle>{`Top trigger foods · ${data.periodLabel}`}</StatTitle>
      {top.length === 0 ? (
        <EmptyLine>No trigger patterns yet</EmptyLine>
      ) : (
        top.map((t, i) => (
          <View key={`${t.food}-${i}`} style={styles.rowBlock}>
            <View style={styles.rowTop}>
              <Text style={styles.rowLabel} numberOfLines={1}>
                {t.food}
              </Text>
              <Text style={styles.rowValue}>
                {t.count} link{t.count === 1 ? "" : "s"}
              </Text>
            </View>
            <View style={styles.track}>
              <View
                style={{
                  width: `${Math.max((t.count / max) * 100, 4)}%`,
                  height: 10,
                  borderRadius: 5,
                  backgroundColor: sc.warning,
                }}
              />
            </View>
          </View>
        ))
      )}
    </>
  );
}

export function SafetyReportContent({ data }: { data: SafetyReportCardData }) {
  const flagged = data.flaggedAdditives.slice(0, 4);
  return (
    <>
      <StatTitle>{data.brand ? `${data.name} · ${data.brand}` : data.name}</StatTitle>
      {data.score != null ? (
        <View style={styles.scoreRow}>
          <Text style={[styles.scoreNum, { color: scoreColor(data.score) }]}>
            {data.score}
            <Text style={styles.scoreDenom}>/100</Text>
          </Text>
          <Text style={styles.scoreLabel}>{data.scoreLabel ?? "Safety Score"}</Text>
        </View>
      ) : null}
      <View style={styles.badgeWrap}>
        {data.gutRating ? <Badge text={`Gut: ${data.gutRating}`} /> : null}
        {data.fodmapStatus ? <Badge text={`FODMAP: ${data.fodmapStatus}`} /> : null}
        {data.giText ? <Badge text={data.giText} /> : null}
      </View>
      {flagged.length > 0 ? (
        <>
          <SectionLabel>Flagged additives</SectionLabel>
          {flagged.map((a, i) => (
            <Text key={`${a}-${i}`} style={styles.listItem}>
              • {a}
            </Text>
          ))}
        </>
      ) : (
        <EmptyLine>No flagged additives found</EmptyLine>
      )}
    </>
  );
}

export function StreakContent({ data }: { data: StreakCardData }) {
  return (
    <View style={styles.streakCenter}>
      <Text style={styles.flame}>🔥</Text>
      <Text style={styles.streakNum}>{data.days}</Text>
      <Text style={styles.streakWord}>day logging streak</Text>
    </View>
  );
}

export function WeeklySummaryContent({ data }: { data: WeeklySummaryCardData }) {
  return (
    <>
      <StatTitle>{`Your week · ${data.rangeLabel}`}</StatTitle>
      <View style={styles.statGrid}>
        <Stat label="Meals logged" value={`${data.mealsLogged}`} />
        {data.avgCalories != null ? (
          <Stat label="Avg calories/day" value={String(Math.round(data.avgCalories))} />
        ) : null}
        {data.symptomsLogged != null ? (
          <Stat label="Symptoms logged" value={String(data.symptomsLogged)} />
        ) : null}
      </View>
      {data.topTrigger ? (
        <>
          <SectionLabel>Pattern to watch</SectionLabel>
          <Text style={styles.listItem}>• {data.topTrigger}</Text>
        </>
      ) : null}
    </>
  );
}

/* ── shared bits ── */

function StatTitle({ children }: { children: React.ReactNode }) {
  return <Text style={styles.statTitle}>{children}</Text>;
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <View style={styles.statCell}>
      <Text style={styles.statValue}>{value}</Text>
      <Text style={styles.statLabel}>{label}</Text>
    </View>
  );
}

function Badge({ text }: { text: string }) {
  return (
    <View style={styles.badge}>
      <Text style={styles.badgeText} numberOfLines={1}>
        {text}
      </Text>
    </View>
  );
}

function SectionLabel({ children }: { children: React.ReactNode }) {
  return <Text style={styles.sectionLabel}>{children}</Text>;
}

function EmptyLine({ children }: { children: React.ReactNode }) {
  return <Text style={styles.emptyLine}>{children}</Text>;
}

function scoreColor(n: number) {
  if (n >= 70) return sc.primary;
  if (n >= 40) return sc.warning;
  return sc.danger;
}

const styles = StyleSheet.create({
  card: {
    backgroundColor: sc.bg,
    borderRadius: radius.lg,
    padding: spacing.xl,
    borderWidth: 1,
    borderColor: sc.border,
  },
  header: {
    flexDirection: "row",
    alignItems: "center",
    gap: 8,
    marginBottom: spacing.md,
  },
  logoDisc: {
    width: 28,
    height: 28,
    borderRadius: 14,
    backgroundColor: sc.bgAccent,
    alignItems: "center",
    justifyContent: "center",
  },
  wordmark: { fontSize: 16, fontWeight: "800", color: sc.text },
  subtitle: { fontSize: 11, color: sc.textMuted },
  title: { fontSize: 12, fontWeight: "700", color: sc.textMuted, marginBottom: 2 },
  statTitle: {
    fontSize: 20,
    fontWeight: "800",
    color: sc.text,
    marginBottom: spacing.md,
  },
  rowBlock: { marginBottom: 10 },
  rowTop: { flexDirection: "row", justifyContent: "space-between", marginBottom: 4 },
  rowLabel: { fontSize: 14, fontWeight: "600", color: sc.text, flex: 1, marginRight: 8 },
  rowValue: { fontSize: 12, color: sc.textMuted },
  track: { height: 10, borderRadius: 5, backgroundColor: "#f1f5f9", overflow: "hidden" },
  scoreRow: { alignItems: "flex-start", marginVertical: spacing.sm },
  scoreNum: { fontSize: 52, fontWeight: "800", lineHeight: 56 },
  scoreDenom: { fontSize: 20, fontWeight: "600", color: sc.textMuted },
  scoreLabel: { fontSize: 13, color: sc.textMuted, marginTop: -4 },
  badgeWrap: { flexDirection: "row", flexWrap: "wrap", gap: 6, marginVertical: spacing.sm },
  badge: {
    backgroundColor: sc.bgAccent,
    borderRadius: 999,
    paddingHorizontal: 10,
    paddingVertical: 4,
    alignSelf: "flex-start",
  },
  badgeText: { fontSize: 12, fontWeight: "700", color: sc.primary },
  sectionLabel: {
    fontSize: 11,
    fontWeight: "700",
    color: sc.textMuted,
    marginTop: spacing.md,
    marginBottom: 4,
  },
  listItem: { fontSize: 13, color: sc.text, marginBottom: 3 },
  emptyLine: { fontSize: 13, color: sc.textMuted },
  streakCenter: { alignItems: "center", paddingVertical: spacing.lg },
  flame: { fontSize: 56 },
  streakNum: { fontSize: 64, fontWeight: "800", color: sc.text, lineHeight: 72 },
  streakWord: { fontSize: 15, color: sc.textMuted, fontWeight: "600" },
  statGrid: { flexDirection: "row", flexWrap: "wrap", gap: spacing.lg },
  statCell: { minWidth: 90 },
  statValue: { fontSize: 26, fontWeight: "800", color: sc.text },
  statLabel: { fontSize: 11, color: sc.textMuted },
  footer: { borderTopWidth: StyleSheet.hairlineWidth, marginTop: spacing.lg, paddingTop: spacing.sm },
  footerText: { fontSize: 10, color: sc.textMuted, textAlign: "center" },
});
