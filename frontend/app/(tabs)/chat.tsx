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
} from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import EventSource from "react-native-sse";
import { colors } from "../../src/utils/theme";
import { chatApi } from "../../src/api";
import { getItem } from "../../src/utils/storage";
import Constants from "expo-constants";

import type { ChatMessage, ChatStreamEvent } from "../../src/types";

const BASE_URL =
  process.env.EXPO_PUBLIC_API_URL ||
  Constants.expoConfig?.extra?.apiUrl ||
  Platform.select({
    android: "http://10.0.2.2:5000",
    ios: "http://localhost:5000",
    default: "http://localhost:5000",
  });

interface LocalMessage {
  id: string;
  role: "user" | "assistant";
  content: string;
  isStreaming?: boolean;
  toolStatus?: string;
}

export default function ChatScreen() {
  const insets = useSafeAreaInsets();
  const queryClient = useQueryClient();
  const flatListRef = useRef<FlatList>(null);
  const [input, setInput] = useState("");
  const [messages, setMessages] = useState<LocalMessage[]>([]);
  const [isStreaming, setIsStreaming] = useState(false);
  const streamRef = useRef<EventSource | null>(null);

  // Load history on mount
  const { isLoading } = useQuery({
    queryKey: ["chatHistory"],
    queryFn: async () => {
      const res = await chatApi.getHistory(50);
      return res.data;
    },
    staleTime: Infinity,
  });

  const historyData = queryClient.getQueryData<ChatMessage[]>(["chatHistory"]);
  useEffect(() => {
    if (historyData && messages.length === 0) {
      setMessages(
        historyData.map((m) => ({
          id: m.id,
          role: m.role,
          content: m.content,
        })),
      );
    }
  }, [historyData]);

  const scrollToBottom = useCallback(() => {
    setTimeout(() => flatListRef.current?.scrollToEnd({ animated: true }), 100);
  }, []);

  const sendMessage = useCallback(async () => {
    const text = input.trim();
    if (!text || isStreaming) return;

    const userMsg: LocalMessage = {
      id: `user-${Date.now()}`,
      role: "user",
      content: text,
    };
    const assistantMsg: LocalMessage = {
      id: `assistant-${Date.now()}`,
      role: "assistant",
      content: "",
      isStreaming: true,
    };

    setMessages((prev) => [...prev, userMsg, assistantMsg]);
    setInput("");
    setIsStreaming(true);
    scrollToBottom();

    try {
      const token = await getItem("accessToken");
      const es = new EventSource(`${BASE_URL}/api/chat/stream`, {
        method: "POST",
        headers: {
          Authorization: `Bearer ${token}`,
          "Content-Type": "application/json",
        },
        body: JSON.stringify({ message: text }),
      });

      streamRef.current = es;

      es.addEventListener("message", (event: any) => {
        const data = event.data;
        if (data === "[DONE]") {
          es.close();
          streamRef.current = null;
          setIsStreaming(false);
          setMessages((prev) =>
            prev.map((m) =>
              m.id === assistantMsg.id
                ? { ...m, isStreaming: false, toolStatus: undefined }
                : m,
            ),
          );
          queryClient.invalidateQueries({ queryKey: ["chatHistory"] });
          return;
        }

        try {
          const parsed: ChatStreamEvent = JSON.parse(data);
          if (parsed.content) {
            setMessages((prev) =>
              prev.map((m) =>
                m.id === assistantMsg.id
                  ? { ...m, content: m.content + parsed.content }
                  : m,
              ),
            );
            scrollToBottom();
          } else if (parsed.tool_call) {
            setMessages((prev) =>
              prev.map((m) =>
                m.id === assistantMsg.id
                  ? {
                      ...m,
                      toolStatus: `${parsed.status}: ${formatToolName(parsed.tool_call)}`,
                    }
                  : m,
              ),
            );
          }
        } catch {}
      });

      es.addEventListener("error", () => {
        es.close();
        streamRef.current = null;
        setIsStreaming(false);
        setMessages((prev) =>
          prev.map((m) =>
            m.id === assistantMsg.id
              ? {
                  ...m,
                  content:
                    m.content ||
                    "Sorry, something went wrong. Please try again.",
                  isStreaming: false,
                }
              : m,
          ),
        );
      });
    } catch {
      setIsStreaming(false);
      setMessages((prev) =>
        prev.map((m) =>
          m.id === assistantMsg.id
            ? {
                ...m,
                content: "Failed to connect. Please try again.",
                isStreaming: false,
              }
            : m,
        ),
      );
    }
  }, [input, isStreaming, scrollToBottom, queryClient]);

  const clearChat = useMutation({
    mutationFn: () => chatApi.clearHistory(),
    onSuccess: () => {
      setMessages([]);
      queryClient.setQueryData(["chatHistory"], []);
    },
  });

  useEffect(() => {
    return () => {
      streamRef.current?.close();
    };
  }, []);

  const renderMessage = useCallback(({ item }: { item: LocalMessage }) => {
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
              <Ionicons name="sparkles" size={14} color="#fff" />
            </View>
            <Text style={styles.avatarLabel}>GutAI Coach</Text>
          </View>
        )}
        {item.toolStatus && (
          <View style={styles.toolBadge}>
            <ActivityIndicator size="small" color={colors.primary} />
            <Text style={styles.toolText}>{item.toolStatus}</Text>
          </View>
        )}
        <Text
          style={[styles.msgText, isUser ? styles.userText : styles.aiText]}
        >
          {item.content}
          {item.isStreaming && !item.content && !item.toolStatus && "…"}
        </Text>
        {item.isStreaming && item.content && <View style={styles.cursorDot} />}
      </View>
    );
  }, []);

  return (
    <KeyboardAvoidingView
      style={styles.container}
      behavior={Platform.OS === "ios" ? "padding" : undefined}
      keyboardVerticalOffset={0}
    >
      {/* Header */}
      <View style={[styles.header, { paddingTop: insets.top + 8 }]}>
        <View style={styles.headerLeft}>
          <View style={styles.headerIcon}>
            <Ionicons name="sparkles" size={20} color="#fff" />
          </View>
          <View>
            <Text style={styles.headerTitle}>AI Health Coach</Text>
            <Text style={styles.headerSubtitle}>Powered by GutAI</Text>
          </View>
        </View>
        <TouchableOpacity
          onPress={() => clearChat.mutate()}
          style={styles.clearBtn}
        >
          <Ionicons name="trash-outline" size={20} color={colors.textMuted} />
        </TouchableOpacity>
      </View>

      {/* Messages */}
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
            {[
              "What are my trigger foods?",
              "Log lunch: chicken salad and water",
              "How's my nutrition today?",
            ].map((s) => (
              <TouchableOpacity
                key={s}
                style={styles.suggestionChip}
                onPress={() => {
                  setInput(s);
                }}
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
          onContentSizeChange={() =>
            flatListRef.current?.scrollToEnd({ animated: false })
          }
        />
      )}

      {/* Input */}
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
          maxLength={2000}
          editable={!isStreaming}
          onSubmitEditing={sendMessage}
          blurOnSubmit={false}
        />
        <TouchableOpacity
          onPress={sendMessage}
          disabled={!input.trim() || isStreaming}
          style={[
            styles.sendBtn,
            (!input.trim() || isStreaming) && styles.sendBtnDisabled,
          ]}
        >
          {isStreaming ? (
            <ActivityIndicator size="small" color="#fff" />
          ) : (
            <Ionicons name="send" size={18} color="#fff" />
          )}
        </TouchableOpacity>
      </View>
    </KeyboardAvoidingView>
  );
}

function formatToolName(name?: string): string {
  if (!name) return "";
  return name.replace(/_/g, " ").replace(/\b\w/g, (c) => c.toUpperCase());
}

const styles = StyleSheet.create({
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
  loadingContainer: { flex: 1, justifyContent: "center", alignItems: "center" },
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
    borderColor: "#e2e8f0",
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
  userText: { color: "#fff" },
  aiText: { color: colors.text },
  cursorDot: {
    width: 6,
    height: 6,
    borderRadius: 3,
    backgroundColor: colors.primary,
    marginTop: 4,
    opacity: 0.6,
  },
  inputBar: {
    flexDirection: "row",
    alignItems: "flex-end",
    paddingHorizontal: 12,
    paddingTop: 8,
    backgroundColor: colors.card,
    borderTopWidth: 1,
    borderTopColor: "#e2e8f0",
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
    borderColor: "#e2e8f0",
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
});
