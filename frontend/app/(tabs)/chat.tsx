import React, { useState, useRef, useCallback, useEffect } from "react";
import {
  View,
  Text,
  TextInput,
  TouchableOpacity,
  FlatList,
  KeyboardAvoidingView,
  Platform,
  ActivityIndicator,
  StyleSheet,
  Linking,
} from "react-native";
import { useRouter } from "expo-router";
import { Ionicons } from "@expo/vector-icons";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { useThemeColors } from "../../src/stores/theme";
import * as haptics from "../../src/utils/haptics";
import { chatApi } from "../../src/api";
import Markdown from "react-native-markdown-display";
import {
  useSubscriptionStore,
  presentPaywall,
} from "../../src/stores/subscription";
import { confirm } from "../../src/utils/confirm";
import { toLocalDateStr } from "../../src/utils/date";
import { getDeviceTimezoneId } from "../../src/utils/timezone";
import { toast } from "../../src/stores/toast";
import { useChatStream, formatToolName } from "../../src/hooks/useChatStream";
import TypingIndicator from "../../src/components/TypingIndicator";

import type { ChatMessage, ToolResultSummary } from "../../src/types";

interface LocalMessage {
  id: string;
  role: "user" | "assistant";
  content: string;
  isStreaming?: boolean;
  toolStatus?: string;
  toolResults?: ToolResultSummary[];
}

/** Rich inline card for a completed coach tool (persistent, not transient). */
function ToolResultCard({ summary }: { summary: ToolResultSummary }) {
  const c = useThemeColors();
  const bg =
    summary.type === "triggers"
      ? c.warningBg
      : summary.type === "meal_logged"
        ? c.primaryBg
        : c.secondaryBg;
  const fg =
    summary.type === "triggers"
      ? c.warning
      : summary.type === "meal_logged"
        ? c.primary
        : c.secondary;
  const icon: keyof typeof Ionicons.glyphMap =
    summary.type === "meal_logged"
      ? "checkmark-circle"
      : summary.type === "meals_today"
        ? "restaurant-outline"
        : "warning-outline";

  let headline = "";
  if (summary.type === "meal_logged") {
    headline = `Logged${summary.mealType ? ` · ${summary.mealType}` : ""} · ${Math.round(summary.calories)} kcal`;
  } else if (summary.type === "meals_today") {
    headline = `${summary.count} meal${summary.count === 1 ? "" : "s"} today · ${Math.round(summary.calories)} kcal`;
  } else {
    headline = summary.top
      ? `Pattern found: ${summary.top}${summary.count > 1 ? ` (+${summary.count - 1})` : ""}`
      : "Trigger scan complete";
  }

  return (
    <View
      style={[
        trStyles.card,
        { backgroundColor: bg, borderColor: fg + "55" },
      ]}
      accessibilityRole="text"
      accessibilityLabel={headline}
    >
      <Ionicons name={icon} size={14} color={fg} />
      <Text style={[trStyles.text, { color: c.text }]} numberOfLines={1}>
        {headline}
      </Text>
    </View>
  );
}

const trStyles = StyleSheet.create({
  card: {
    flexDirection: "row",
    alignItems: "center",
    gap: 6,
    borderWidth: 1,
    borderRadius: 999,
    paddingHorizontal: 10,
    paddingVertical: 5,
    alignSelf: "flex-start",
    maxWidth: "100%",
  },
  text: { fontSize: 12, fontWeight: "600", flexShrink: 1 },
});
export default function ChatScreen() {
  const colors = useThemeColors();
  const styles = getStyles(colors);
  const mdStyles = getMarkdownStyles(colors);
  const insets = useSafeAreaInsets();
  const router = useRouter();
  const queryClient = useQueryClient();
  const timezoneId = getDeviceTimezoneId();
  const flatListRef = useRef<FlatList>(null);
  const [input, setInput] = useState("");
  const [messages, setMessages] = useState<LocalMessage[]>([]);
  const [isStreaming, setIsStreaming] = useState(false);
  const { startStream, cancelStream } = useChatStream();
  const { isLoading, isFetching } = useQuery({
    queryKey: ["chatHistory"],
    queryFn: async () => {
      const res = await chatApi.getHistory(50);
      return res.data;
    },
    staleTime: 0,
    refetchOnMount: "always",
  });

  const historyData = queryClient.getQueryData<ChatMessage[]>(["chatHistory"]);
  useEffect(() => {
    if (!historyData || isStreaming || isFetching) return;
    setMessages(
      historyData
        .filter((m) => m.content.trim().length > 0)
        .map((m) => ({
          id: m.id,
          role: m.role,
          content: m.content,
        })),
    );
  }, [historyData, isStreaming, isFetching]);
  const { isPro, isLoaded: subLoaded } = useSubscriptionStore();

  // Contextual starter prompts from local time and already-cached app state.
  const suggestedPrompts = useCallback(() => {
    const h = new Date().getHours();
    const base =
      h < 11
        ? "Plan a gut-friendly breakfast"
        : h < 15
          ? "What should I have for lunch?"
          : h < 21
            ? "What's a good dinner option tonight?"
            : "How can I sleep better with less bloating?";
    const prompts = [base, "How's my nutrition today?"];
    const summary = queryClient.getQueryData<{ totalCalories: number; calorieGoal: number }>([
      "daily-summary",
      toLocalDateStr(),
      timezoneId,
    ]);
    if (summary && summary.calorieGoal > 0) {
      const remaining = Math.max(summary.calorieGoal - summary.totalCalories, 0);
      if (remaining < 300) {
        prompts.unshift(`I have only ${Math.round(remaining)} kcal left today — what should I eat?`);
      } else if (remaining > 800 && h >= 15) {
        prompts.push(`I still have ${Math.round(remaining)} kcal left — meal ideas?`);
      }
    }
    const triggers = queryClient.getQueryData<{ food: string }[]>(["trigger-foods", 30, timezoneId])
      ?? queryClient.getQueryData<{ food: string }[]>(["trigger-foods-dashboard", timezoneId]);
    if (triggers?.length) {
      prompts.push(`Tell me more about ${triggers[0].food}`);
    }
    return prompts.slice(0, 4);
  }, [queryClient, timezoneId]);
  const scrollToBottom = useCallback(() => {
    setTimeout(() => flatListRef.current?.scrollToEnd({ animated: true }), 100);
  }, []);

  const sendMessage = useCallback(async () => {
    const text = input.trim();
    if (!text || isStreaming) return;

    const assistantId = `assistant-${Date.now()}`;
    const userMsg: LocalMessage = {
      id: `user-${Date.now()}`,
      role: "user",
      content: text,
    };
    const assistantMsg: LocalMessage = {
      id: assistantId,
      role: "assistant",
      content: "",
      isStreaming: true,
    };

    setMessages((prev) => [...prev, userMsg, assistantMsg]);
    setInput("");
    setIsStreaming(true);
    scrollToBottom();

    await startStream(text, {
      onContent: (content) => {
        setMessages((prev) =>
          prev.map((m) =>
            m.id === assistantId
              ? { ...m, content: m.content + content }
              : m,
          ),
        );
        scrollToBottom();
      },
      onToolStatus: (toolName, status) => {
        const capStatus = status.charAt(0).toUpperCase() + status.slice(1);
        setMessages((prev) =>
          prev.map((m) =>
            m.id === assistantId
              ? { ...m, toolStatus: `${capStatus}: ${formatToolName(toolName)}` }
              : m,
          ),
        );
      },
      onToolResult: (_toolName, summary) => {
        if (!summary) return;
        setMessages((prev) =>
          prev.map((m) =>
            m.id === assistantId
              ? { ...m, toolResults: [...(m.toolResults ?? []), summary] }
              : m,
          ),
        );
        scrollToBottom();
      },
      onDone: () => {
        setIsStreaming(false);
        setMessages((prev) =>
          prev.map((m) =>
            m.id === assistantId
              ? { ...m, isStreaming: false, toolStatus: undefined }
              : m,
          ),
        );
        queryClient.invalidateQueries({ queryKey: ["chatHistory"] });
      },
      onError: (errorMessage) => {
        setIsStreaming(false);
        haptics.error();
        setMessages((prev) =>
          prev.map((m) =>
            m.id === assistantId
              ? { ...m, content: errorMessage, isStreaming: false }
              : m,
          ),
        );
      },
    });
  }, [input, isStreaming, scrollToBottom, queryClient, startStream]);

  const clearChat = useMutation({
    mutationFn: () => chatApi.clearHistory(),
    onSuccess: () => {
      setMessages([]);
      queryClient.setQueryData(["chatHistory"], []);
      toast.success("Chat history cleared");
    },
    onError: () => {
      toast.error("Failed to clear chat history");
      haptics.error();
    },
  });

  const handleClearChat = useCallback(() => {
    confirm(
      "Clear Chat History",
      "Are you sure you want to clear your conversation history? This action cannot be undone.",
      () => {
        if (isStreaming) {
          cancelStream();
          setIsStreaming(false);
        }
        clearChat.mutate();
      },
    );
  }, [isStreaming, cancelStream, clearChat]);

  useEffect(() => {
    return () => cancelStream();
  }, [cancelStream]);
  const handleLinkPress = useCallback(
    (url: string) => {
      try {
        if (url.startsWith("food://")) {
          const foodId = url.replace(/^food:\/\//, "");
          if (foodId) {
            haptics.light();
            router.push(`/food/${foodId}`);
          }
          return false;
        }
        if (/^https?:\/\//i.test(url)) {
          Linking.openURL(url).catch(() => {});
          return false;
        }
      } catch {
        // fail silent-safe on unroutable / invalid hrefs
      }
      return false;
    },
    [router],
  );


  const renderMessage = useCallback(
    ({ item }: { item: LocalMessage }) => {
      const isUser = item.role === "user";
      return (
        <View
          style={[
            styles.bubble,
            isUser ? styles.userBubble : styles.assistantBubble,
          ]}
        >
          {!isUser && (
            <View style={styles.avatarRow}>
              <View style={styles.avatar}>
                <Ionicons
                  name="sparkles"
                  size={14}
                  color={colors.textOnPrimary}
                />
              </View>
              <Text style={styles.avatarLabel}>GutLens Coach</Text>
            </View>
          )}
          {item.toolStatus && (
            <View style={styles.toolBadge}>
              <ActivityIndicator size="small" color={colors.primary} />
              <Text style={styles.toolText}>{item.toolStatus}</Text>
            </View>
          )}
          {isUser ? (
            <Text style={[styles.msgText, styles.userText]}>
              {item.content}
            </Text>
          ) : (
            <>
              {item.content ? (
                <Markdown style={mdStyles} onLinkPress={handleLinkPress}>
                  {item.content}
                </Markdown>
              ) : item.isStreaming && !item.toolStatus ? (
                <View style={styles.typingContainer}>
                  <TypingIndicator visible={true} />
                </View>
              ) : null}
              {item.isStreaming && item.content && (
                <View style={styles.cursorDot} />
              )}
              {!isUser && (item.toolResults?.length ?? 0) > 0 ? (
                <View style={styles.toolResultsRow}>
                  {item.toolResults!.map((tr, i) => (
                    <ToolResultCard key={`${tr.type}-${i}`} summary={tr} />
                  ))}
                </View>
              ) : null}
            </>
          )}
        </View>
      );
    },
    [colors, styles, mdStyles, handleLinkPress],
  );

  if (!subLoaded) {
    return (
      <View style={styles.container}>
        <View style={[styles.header, { paddingTop: insets.top + 8 }]}>
          <View style={styles.headerLeft}>
            <View style={styles.headerIcon}>
              <Ionicons name="sparkles" size={20} color={colors.textOnPrimary} />
            </View>
            <View>
              <Text style={styles.headerTitle}>AI Health Coach</Text>
              <Text style={styles.headerSubtitle}>Powered by GutLens</Text>
            </View>
          </View>
        </View>
        <View style={styles.loadingContainer}>
          <ActivityIndicator size="large" color={colors.primary} />
        </View>
      </View>
    );
  }

  if (!isPro) {
    return (
      <View style={styles.container}>
        <View style={[styles.header, { paddingTop: insets.top + 8 }]}>
          <View style={styles.headerLeft}>
            <View style={styles.headerIcon}>
              <Ionicons name="sparkles" size={20} color={colors.textOnPrimary} />
            </View>
            <View>
              <Text style={styles.headerTitle}>AI Health Coach</Text>
              <Text style={styles.headerSubtitle}>Powered by GutLens</Text>
            </View>
          </View>
        </View>
        <View style={styles.paywallGate}>
          <View style={styles.paywallIconWrap}>
            <Ionicons name="sparkles" size={40} color={colors.primary} />
          </View>
          <Text style={styles.paywallTitle}>Unlock AI Health Coach</Text>
          <Text style={styles.paywallBody}>
            Get unlimited access to your personal AI health coach. Ask
            questions, log meals by voice, track triggers, and receive
            personalized nutrition advice \u2014 all through natural conversation.
          </Text>
          <TouchableOpacity
            style={styles.paywallBtn}
            onPress={() => presentPaywall()}
            accessibilityRole="button"
            accessibilityLabel="Subscribe to Gut Lens Pro"
          >
            <Ionicons
              name="diamond-outline"
              size={18}
              color={colors.textOnPrimary}
              style={{ marginRight: 8 }}
            />
            <Text style={styles.paywallBtnText}>Subscribe to Gut Lens Pro</Text>
          </TouchableOpacity>
        </View>
      </View>
    );
  }

  return (
    <KeyboardAvoidingView
      style={styles.container}
      behavior={Platform.OS === "ios" ? "padding" : "height"}
      keyboardVerticalOffset={0}
    >
      <View style={[styles.header, { paddingTop: insets.top + 16 }]}>
        <View style={styles.headerLeft}>
          <View style={styles.headerIcon}>
            <Ionicons name="sparkles" size={20} color={colors.textOnPrimary} />
          </View>
          <View>
            <Text style={styles.headerTitle}>AI Health Coach</Text>
            <Text style={styles.headerSubtitle}>Powered by GutLens</Text>
          </View>
        </View>
        <TouchableOpacity
          onPress={handleClearChat}
          disabled={clearChat.isPending}
          style={[styles.clearBtn, clearChat.isPending && { opacity: 0.5 }]}
          accessibilityRole="button"
          accessibilityLabel="Clear chat"
        >
          {clearChat.isPending ? (
            <ActivityIndicator size="small" color={colors.textMuted} />
          ) : (
            <Ionicons name="trash-outline" size={20} color={colors.textMuted} />
          )}
        </TouchableOpacity>
      </View>

      {isLoading ? (
        <View style={styles.loadingContainer}>
          <ActivityIndicator size="large" color={colors.primary} />
        </View>
      ) : messages.length === 0 ? (
        <View style={styles.emptyContainer}>
          <View style={styles.emptyIcon}>
            <Ionicons
              name="chatbubbles-outline"
              size={48}
              color={colors.primaryLight}
            />
          </View>
          <Text style={styles.emptyTitle}>
            Ask me anything about your gut health
          </Text>
          <Text style={styles.emptySubtitle}>
            I can log meals, track symptoms, analyze trigger foods, and give
            personalized nutrition advice.
          </Text>
          <View style={styles.suggestions}>
            {suggestedPrompts().map((s) => (
              <TouchableOpacity
                key={s}
                style={styles.suggestionChip}
                onPress={() => setInput(s)}
                accessibilityRole="button"
                accessibilityLabel={s}
              >
                <Text style={styles.suggestionText}>{s}</Text>
              </TouchableOpacity>
            ))}
          </View>
        </View>
      ) : (
        <FlatList
          ref={flatListRef}
          data={messages}
          renderItem={renderMessage}
          keyExtractor={(item) => item.id}
          contentContainerStyle={styles.messageList}
          keyboardShouldPersistTaps="handled"
          onContentSizeChange={() =>
            flatListRef.current?.scrollToEnd({ animated: false })
          }
        />
      )}

      <View
        style={[
          styles.inputBar,
          { paddingBottom: Math.max(insets.bottom, 8) + 60 },
        ]}
      >
        <TextInput
          style={styles.textInput}
          value={input}
          onChangeText={setInput}
          placeholder="Ask about your gut health..."
          placeholderTextColor={colors.textMuted}
          multiline
          autoCapitalize="sentences"
          maxLength={2000}
          editable={!isStreaming}
          onSubmitEditing={sendMessage}
          blurOnSubmit={false}
          accessibilityLabel="Type a message"
        />
        <TouchableOpacity
          onPress={() => {
            haptics.light();
            sendMessage();
          }}
          disabled={!input.trim() || isStreaming}
          style={[
            styles.sendBtn,
            (!input.trim() || isStreaming) && styles.sendBtnDisabled,
          ]}
          accessibilityRole="button"
          accessibilityLabel="Send message"
        >
          {isStreaming ? (
            <ActivityIndicator size="small" color={colors.textOnPrimary} />
          ) : (
            <Ionicons name="send" size={18} color={colors.textOnPrimary} />
          )}
        </TouchableOpacity>
      </View>
    </KeyboardAvoidingView>
  );
}

const getMarkdownStyles = (colors: any) => ({
  body: { fontSize: 15, lineHeight: 22, color: colors.text },
  paragraph: { marginTop: 0, marginBottom: 6 },
  strong: { fontWeight: "700" as const },
  link: { color: colors.primary },
  code_inline: {
    backgroundColor: colors.borderLight,
    borderRadius: 4,
    paddingHorizontal: 4,
    fontSize: 13,
    fontFamily: Platform.OS === "ios" ? "Menlo" : "monospace",
    color: colors.textSecondary,
  },
  fence: {
    backgroundColor: colors.borderLight,
    borderRadius: 8,
    padding: 10,
    fontSize: 13,
    fontFamily: Platform.OS === "ios" ? "Menlo" : "monospace",
    color: colors.textSecondary,
  },
  bullet_list: { marginTop: 2, marginBottom: 6 },
  ordered_list: { marginTop: 2, marginBottom: 6 },
  list_item: { marginBottom: 2 },
  heading1: {
    fontSize: 20,
    fontWeight: "700" as const,
    color: colors.text,
    marginBottom: 6,
  },
  heading2: {
    fontSize: 18,
    fontWeight: "700" as const,
    color: colors.text,
    marginBottom: 4,
  },
  heading3: {
    fontSize: 16,
    fontWeight: "600" as const,
    color: colors.text,
    marginBottom: 4,
  },
});

const getStyles = (colors: any) =>
  StyleSheet.create({
    container: { flex: 1, backgroundColor: colors.bg },
    header: {
      flexDirection: "row",
      alignItems: "center",
      justifyContent: "space-between",
      paddingHorizontal: 16,
      paddingBottom: 12,
      backgroundColor: colors.card,
      borderBottomWidth: 1,
      borderBottomColor: colors.primaryBorder,
    },
    headerLeft: { flexDirection: "row", alignItems: "center", gap: 10 },
    headerIcon: {
      width: 36,
      height: 36,
      borderRadius: 18,
      backgroundColor: colors.primary,
      alignItems: "center",
      justifyContent: "center",
    },
    headerTitle: { fontSize: 17, fontWeight: "700", color: colors.text },
    headerSubtitle: { fontSize: 12, color: colors.textMuted },
    clearBtn: { padding: 8 },
    loadingContainer: {
      flex: 1,
      justifyContent: "center",
      alignItems: "center",
    },
    emptyContainer: {
      flex: 1,
      justifyContent: "center",
      alignItems: "center",
      paddingHorizontal: 32,
    },
    emptyIcon: { marginBottom: 16 },
    emptyTitle: {
      fontSize: 18,
      fontWeight: "700",
      color: colors.text,
      textAlign: "center",
      marginBottom: 8,
    },
    emptySubtitle: {
      fontSize: 14,
      color: colors.textSecondary,
      textAlign: "center",
      lineHeight: 20,
      marginBottom: 24,
    },
    suggestions: { gap: 8, width: "100%" },
    toolResultsRow: {
      flexDirection: "row",
      flexWrap: "wrap",
      gap: 6,
      marginTop: 6,
    },
    suggestionChip: {
      backgroundColor: colors.primaryBg,
      borderWidth: 1,
      borderColor: colors.primaryBorder,
      borderRadius: 12,
      paddingVertical: 10,
      paddingHorizontal: 16,
    },
    suggestionText: {
      fontSize: 14,
      color: colors.primary,
      fontWeight: "500",
    },
    messageList: { paddingHorizontal: 12, paddingVertical: 16 },
    bubble: {
      maxWidth: "85%",
      marginBottom: 12,
      borderRadius: 16,
      paddingHorizontal: 14,
      paddingVertical: 10,
    },
    userBubble: {
      alignSelf: "flex-end",
      backgroundColor: colors.primary,
      borderBottomRightRadius: 4,
    },
    assistantBubble: {
      alignSelf: "flex-start",
      backgroundColor: colors.card,
      borderBottomLeftRadius: 4,
      borderWidth: 1,
      borderColor: colors.border,
    },
    avatarRow: {
      flexDirection: "row",
      alignItems: "center",
      gap: 6,
      marginBottom: 4,
    },
    avatar: {
      width: 20,
      height: 20,
      borderRadius: 10,
      backgroundColor: colors.primary,
      alignItems: "center",
      justifyContent: "center",
    },
    avatarLabel: { fontSize: 11, fontWeight: "600", color: colors.textMuted },
    toolBadge: {
      flexDirection: "row",
      alignItems: "center",
      gap: 6,
      backgroundColor: colors.primaryBg,
      borderRadius: 8,
      paddingHorizontal: 8,
      paddingVertical: 4,
      marginBottom: 6,
      alignSelf: "flex-start",
    },
    toolText: { fontSize: 11, color: colors.primary, fontWeight: "500" },
    msgText: { fontSize: 15, lineHeight: 22 },
    userText: { color: colors.textOnPrimary },
    aiText: { color: colors.text },
    cursorDot: {
      width: 6,
      height: 6,
      borderRadius: 3,
      backgroundColor: colors.primary,
      marginTop: 4,
      opacity: 0.6,
    },
    typingContainer: {
      paddingVertical: 4,
      alignSelf: "flex-start",
    },
    inputBar: {
      flexDirection: "row",
      alignItems: "flex-end",
      paddingHorizontal: 12,
      paddingTop: 8,
      backgroundColor: colors.card,
      borderTopWidth: 1,
      borderTopColor: colors.border,
      gap: 8,
    },
    textInput: {
      flex: 1,
      backgroundColor: colors.bg,
      borderRadius: 20,
      paddingHorizontal: 16,
      paddingVertical: 10,
      fontSize: 15,
      color: colors.text,
      maxHeight: 100,
      borderWidth: 1,
      borderColor: colors.border,
    },
    sendBtn: {
      width: 40,
      height: 40,
      borderRadius: 20,
      backgroundColor: colors.primary,
      alignItems: "center",
      justifyContent: "center",
      marginBottom: 2,
    },
    sendBtnDisabled: { opacity: 0.5 },
    paywallGate: {
      flex: 1,
      justifyContent: "center",
      alignItems: "center",
      paddingHorizontal: 32,
    },
    paywallIconWrap: {
      width: 80,
      height: 80,
      borderRadius: 40,
      backgroundColor: colors.primaryBg,
      alignItems: "center",
      justifyContent: "center",
      marginBottom: 20,
    },
    paywallTitle: {
      fontSize: 22,
      fontWeight: "700",
      color: colors.text,
      textAlign: "center",
      marginBottom: 12,
    },
    paywallBody: {
      fontSize: 15,
      color: colors.textSecondary,
      textAlign: "center",
      lineHeight: 22,
      marginBottom: 28,
    },
    paywallBtn: {
      flexDirection: "row",
      alignItems: "center",
      justifyContent: "center",
      backgroundColor: colors.primary,
      borderRadius: 14,
      paddingVertical: 14,
      paddingHorizontal: 28,
      width: "100%",
    },
    paywallBtnText: {
      color: colors.textOnPrimary,
      fontSize: 16,
      fontWeight: "700",
    },
  });
