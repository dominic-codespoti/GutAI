import { useEffect, useRef } from "react";
import { View, Platform } from "react-native";
import { captureRef } from "react-native-view-shot";
import * as Sharing from "expo-sharing";
import * as haptics from "../../src/utils/haptics";
import { toast } from "../../src/stores/toast";
import {
  useShareCardStore,
  type ShareCardRequest,
} from "../../src/stores/shareCard";
import {
  CardShell,
  CARD_WIDTH,
  TriggerFoodsContent,
  SafetyReportContent,
  StreakContent,
  WeeklySummaryContent,
} from "./CardShell";

function TemplateBody({ request }: { request: ShareCardRequest }) {
  switch (request.template) {
    case "triggerFoods":
      return <TriggerFoodsContent data={request.data} />;
    case "safetyReport":
      return <SafetyReportContent data={request.data} />;
    case "streak":
      return <StreakContent data={request.data} />;
    case "weeklySummary":
      return <WeeklySummaryContent data={request.data} />;
  }
}

const TITLES: Record<ShareCardRequest["template"], string> = {
  triggerFoods: "Top Trigger Foods",
  safetyReport: "Safety Report",
  streak: "Logging Streak",
  weeklySummary: "Weekly Summary",
};

/**
 * Mounted once at the tab layout level. Renders the requested template
 * off-screen, captures it as a PNG, opens the share sheet, then clears.
 */
export function ShareCardPortal() {
  const request = useShareCardStore((s) => s.request);
  const clear = useShareCardStore((s) => s.clearShareCard);
  const cardRef = useRef<View | null>(null);
  // Bump on every new request so the capture effect re-runs even for
  // identical consecutive payloads.
  const seqRef = useRef(0);

  useEffect(() => {
    if (!request || !cardRef.current) return;
    seqRef.current += 1;
    let cancelled = false;

    const run = async () => {
      try {
        // Give the off-screen tree a frame to lay out and rasterize.
        const { promise, resolve } = Promise.withResolvers<void>();
        requestAnimationFrame(() => resolve());
        await promise;
        if (cancelled || !cardRef.current) return;

        const uri = await captureRef(cardRef.current, {
          format: "png",
          quality: 1,
          result: "tmpfile",
          snapshotContentContainer: false,
        });
        if (cancelled) return;

        if (!(await Sharing.isAvailableAsync())) {
          toast.error("Sharing is not available on this device");
          return;
        }
        await Sharing.shareAsync(uri, {
          mimeType: "image/png",
          dialogTitle: `GutLens · ${TITLES[request.template]}`,
          UTI: "public.png",
        });
      } catch (err) {
        console.warn("Share card failed:", err);
        toast.error("Could not create share image");
      } finally {
        if (!cancelled) clear();
      }
    };

    void run();
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [request]);

  if (!request) return null;

  return (
    <View
      collapsable={false}
      style={{
        position: "absolute",
        top: 0,
        left: -CARD_WIDTH - 40,
        opacity: Platform.OS === "web" ? 0 : 1,
        width: CARD_WIDTH,
        pointerEvents: "none",
      }}
    >
      <View ref={cardRef} collapsable={false}>
        <CardShell
          title={TITLES[request.template]}
          subtitle={new Date().toLocaleDateString(undefined, {
            year: "numeric",
            month: "short",
            day: "numeric",
          })}
        >
          <TemplateBody request={request} />
        </CardShell>
      </View>
    </View>
  );
}

/** Imperative helper — fires a haptic and queues the share-card flow. */
export function useShareCard() {
  const showShareCard = useShareCardStore((s) => s.showShareCard);
  return (request: ShareCardRequest) => {
    haptics.light();
    showShareCard(request);
  };
}
