import { useRef, useCallback } from "react";
import EventSource, { MessageEvent } from "react-native-sse";
import { getItem } from "../utils/storage";
import { getDeviceTimezoneId } from "../utils/timezone";
import { BASE_URL } from "../api/client";
import type { ChatStreamEvent, ToolResultSummary } from "../types";

interface StreamHandlers {
  onContent: (text: string) => void;
  onToolStatus: (toolName: string, status: string) => void;
  onToolResult: (toolName: string, summary: ToolResultSummary | null) => void;
  onError: (errorMessage: string) => void;
  onDone: () => void;
}

export function useChatStream() {
  const streamRef = useRef<EventSource | null>(null);

  const startStream = useCallback(async (message: string, handlers: StreamHandlers) => {
    const token = await getItem("accessToken");
    const timezoneId = getDeviceTimezoneId();

    const es = new EventSource(`${BASE_URL}/api/chat/stream`, {
      method: "POST",
      headers: {
        Authorization: `Bearer ${token}`,
        "Content-Type": "application/json",
      },
      body: JSON.stringify({ message, timezoneId }),
    });

    streamRef.current = es;

    es.addEventListener("message", (event: MessageEvent) => {
      const data = event.data;
      if (data === "[DONE]") {
        es.close();
        streamRef.current = null;
        handlers.onDone();
        return;
      }

      try {
        const parsed: ChatStreamEvent = JSON.parse(data!);
        if (parsed.content) {
          handlers.onContent(parsed.content);
        } else if (parsed.tool_call) {
          handlers.onToolStatus(parsed.tool_call, parsed.status ?? "executing");
        } else if (parsed.tool_result) {
          handlers.onToolResult(parsed.tool_result, parsed.summary ?? null);
        } else if (parsed.error) {
          handlers.onError(parsed.error);
        }
      } catch {
        // Ignore malformed JSON events
      }
    });

    es.addEventListener("error", () => {
      es.close();
      streamRef.current = null;
      handlers.onError("Sorry, something went wrong. Please try again.");
    });
  }, []);

  const cancelStream = useCallback(() => {
    streamRef.current?.close();
    streamRef.current = null;
  }, []);

  return { startStream, cancelStream };
}

function formatToolName(name: string): string {
  return name.replace(/_/g, " ").replace(/\b\w/g, (c) => c.toUpperCase());
}

export { formatToolName };
