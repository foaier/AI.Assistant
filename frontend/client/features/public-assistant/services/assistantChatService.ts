export interface ChatSession {
  sessionId: string;
  bearerToken: string;
  conversationId: string;
  publishedVersionId: string;
  expiresAt: string;
}
export interface ChatBootstrap {
  displayName: string;
  welcomeMessage: string;
  homeIconUrl?: string;
  primaryColor: string;
  chatBackgroundColor?: string | null;
  suggestedQuestions?: string[];
  publishedVersionId: string;
  anonymousChatEnabled: boolean;
  historyEnabled?: boolean;
  quickLinks?: Array<{
    id: string;
    label: string;
    url: string;
    icon: string;
    iconUrl?: string | null;
  }>;
}
export interface ChatTurn {
  turnId: string;
  runId: string;
  conversationId: string;
  streamTicket: string;
  acceptedSequence: number;
}
export interface ChatEvent {
  type: string;
  sequence: number;
  payload: { text?: string; source?: string; code?: string };
}
export class AssistantStreamTimeoutError extends Error {
  constructor() {
    super("assistant_stream_timeout");
  }
}
export interface ChatMessage {
  id: string;
  conversationId: string;
  turnId: string;
  runId?: string | null;
  clientMessageId?: string | null;
  turnStatus?: number | null;
  role: number;
  contentJson: string;
  status: number;
}
const root = (key: string) =>
  `/api/public/assistants/${encodeURIComponent(key)}`;
const auth = (session?: ChatSession): HeadersInit => ({
  "Content-Type": "application/json",
  ...(session
    ? {
        Authorization: `Bearer ${session.bearerToken}`,
        "X-Assistant-Session-Id": session.sessionId,
      }
    : {}),
});
export class AssistantRequestError extends Error {
  constructor(
    public status: number,
    public code: string,
  ) {
    super(code);
  }
}
async function request<T>(
  url: string,
  method = "GET",
  body?: unknown,
  session?: ChatSession,
  signal?: AbortSignal,
): Promise<T> {
  const response = await fetch(url, {
    method,
    headers: auth(session),
    body: body === undefined ? undefined : JSON.stringify(body),
    credentials: "omit",
    signal: signal ?? AbortSignal.timeout(30000),
  });
  const value = await response.json();
  if (!response.ok || value.isSuccess === false)
    throw new AssistantRequestError(
      response.status,
      value.businessMessage || "assistant_request_failed",
    );
  return value.returnObj ?? value;
}
export const loadChat = (key: string) =>
  request<ChatBootstrap>(`${root(key)}/bootstrap`);
export async function createChat(key: string): Promise<ChatSession> {
  const result = await request<{
    session: { id: string; publishedVersionId: string; expiresAt: string };
    bearerToken: string;
  }>(`${root(key)}/sessions`, "POST", { locale: "zh-cn" });
  const session = {
    sessionId: result.session.id,
    bearerToken: result.bearerToken,
    publishedVersionId: result.session.publishedVersionId,
    expiresAt: result.session.expiresAt,
    conversationId: "",
  };
  const conversation = await request<{ conversationId: string }>(
    `${root(key)}/conversations`,
    "POST",
    { locale: "zh-cn" },
    session,
  );
  return { ...session, conversationId: conversation.conversationId };
}
export const sendChat = (
  key: string,
  session: ChatSession,
  id: string,
  text: string,
) =>
  request<ChatTurn>(
    `${root(key)}/conversations/${session.conversationId}/turns`,
    "POST",
    { clientMessageId: id, text, messageType: "text", locale: "zh-cn" },
    session,
  );
export async function readChat(
  key: string,
  session: ChatSession,
): Promise<{ items: ChatMessage[] }> {
  const items: ChatMessage[] = [];
  const seen = new Set<string>();
  let cursor: string | undefined;
  do {
    const page = await request<{
      items: ChatMessage[];
      hasMore?: boolean;
      nextCursor?: string;
    }>(
      `${root(key)}/conversations/${session.conversationId}/messages?limit=100${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ""}`,
      "GET",
      undefined,
      session,
    );
    items.push(...page.items);
    if (!page.hasMore) break;
    if (!page.nextCursor || seen.has(page.nextCursor))
      throw new Error("assistant_history_cursor_invalid");
    cursor = page.nextCursor;
    seen.add(cursor);
  } while (true);
  return { items };
}
export const cancelChat = (key: string, session: ChatSession, runId: string) =>
  request(`${root(key)}/runs/${runId}/cancel`, "POST", {}, session);

export async function streamChat(
  key: string,
  session: ChatSession,
  turn: ChatTurn,
  transport: "sse" | "ws",
  sequence: number,
  onEvent: (event: ChatEvent) => void,
  signal: AbortSignal,
  timeoutMs = 120_000,
): Promise<void> {
  const timeoutController = new AbortController();
  let timeoutId: ReturnType<typeof setTimeout> | undefined;
  const resetTimeout = () => {
    clearTimeout(timeoutId);
    timeoutId = setTimeout(() => timeoutController.abort(), timeoutMs);
  };
  resetTimeout();
  const streamSignal = AbortSignal.any([signal, timeoutController.signal]);
  try {
    await streamChatCore(
      key,
      session,
      turn,
      transport,
      sequence,
      (event) => {
        resetTimeout();
        onEvent(event);
      },
      streamSignal,
    );
  } catch (error) {
    if (timeoutController.signal.aborted && !signal.aborted)
      throw new AssistantStreamTimeoutError();
    throw error;
  } finally {
    clearTimeout(timeoutId);
  }
}

async function streamChatCore(
  key: string,
  session: ChatSession,
  turn: ChatTurn,
  transport: "sse" | "ws",
  sequence: number,
  onEvent: (event: ChatEvent) => void,
  signal: AbortSignal,
): Promise<void> {
  const query = new URLSearchParams({
    ticket: turn.streamTicket,
    conversationId: session.conversationId,
    afterSequence: String(sequence),
  });
  const url = `${root(key)}/runs/${turn.runId}/events${transport === "ws" ? "/ws" : ""}?${query}`;
  const terminal = (event: ChatEvent) =>
    ["run.completed", "run.failed", "run.cancelled", "run.expired"].includes(
      event.type,
    );
  if (transport === "ws") {
    await new Promise<void>((resolve, reject) => {
      const address = new URL(url, location.href);
      address.protocol = location.protocol === "https:" ? "wss:" : "ws:";
      const socket = new WebSocket(address);
      let done = false;
      const finish = (error?: Error) => {
        if (done) return;
        done = true;
        signal.removeEventListener("abort", abort);
        socket.close();
        error ? reject(error) : resolve();
      };
      const abort = () => finish(new DOMException("Aborted", "AbortError"));
      signal.addEventListener("abort", abort, { once: true });
      if (signal.aborted) {
        abort();
        return;
      }
      socket.onopen = () =>
        socket.send(
          JSON.stringify({
            sessionId: session.sessionId,
            bearerToken: session.bearerToken,
          }),
        );
      socket.onmessage = (message) => {
        try {
          const event = JSON.parse(message.data) as ChatEvent;
          onEvent(event);
          if (terminal(event)) finish();
        } catch {
          finish(new Error("assistant_stream_invalid"));
        }
      };
      socket.onerror = () => finish(new Error("assistant_stream_disconnected"));
      socket.onclose = () => {
        if (!done) finish(new Error("assistant_stream_disconnected"));
      };
    });
    return;
  }
  const response = await fetch(url, {
    headers: auth(session),
    signal,
    credentials: "omit",
  });
  if (!response.ok || !response.body)
    throw new Error("assistant_stream_failed");
  const reader = response.body.getReader();
  const cancelReader = () => {
    void reader.cancel(signal.reason).catch(() => {});
  };
  signal.addEventListener("abort", cancelReader, { once: true });
  if (signal.aborted) cancelReader();
  const decoder = new TextDecoder();
  let buffer = "";
  let terminalSeen = false;
  let messageCompletedSeen = false;
  let lastSeenSequence = sequence;
  try {
    while (true) {
      const result = await reader.read();
      if (result.done) {
        if (signal.aborted)
          throw signal.reason ?? new DOMException("Aborted", "AbortError");
        if (terminalSeen) return;
        throw new Error("assistant_stream_disconnected");
      }
      // Keep raw line endings until the complete SSE record is available. A
      // network read may split the CRLF pair across chunks, so normalizing each
      // chunk independently can lose the record boundary and leave the UI busy.
      buffer += decoder.decode(result.value, { stream: true });
      let boundary: { index: number; length: number } | undefined;
      while ((boundary = findSseBoundary(buffer))) {
        const part = buffer.slice(0, boundary.index);
        buffer = buffer.slice(boundary.index + boundary.length);
        const data = part
          .split(/\r\n|\n|\r/)
          .filter((line) => line.startsWith("data:"))
          .map((line) => line.slice(5).trimStart())
          .join("\n");
        if (!data) continue;
        if (data === "[DONE]") {
          if (!messageCompletedSeen)
            throw new Error("assistant_stream_completed_without_message");
          onEvent({
            type: "run.completed",
            sequence: lastSeenSequence + 1,
            payload: {},
          });
          terminalSeen = true;
          return;
        }
        const event = JSON.parse(data) as ChatEvent;
        lastSeenSequence = Math.max(lastSeenSequence, event.sequence);
        if (event.type === "message.completed") messageCompletedSeen = true;
        onEvent(event);
        if (terminal(event)) {
          terminalSeen = true;
          return;
        }
      }
    }
  } finally {
    // Message completion only closes the answer body; the run terminal event
    // remains the completion boundary for both SSE and WebSocket clients.
    await reader.cancel().catch(() => {});
    signal.removeEventListener("abort", cancelReader);
    reader.releaseLock();
  }
}

function findSseBoundary(
  value: string,
): { index: number; length: number } | undefined {
  const candidates = ["\r\n\r\n", "\n\n", "\r\r"];
  let found: { index: number; length: number } | undefined;
  for (const delimiter of candidates) {
    const index = value.indexOf(delimiter);
    if (index >= 0 && (!found || index < found.index))
      found = { index, length: delimiter.length };
  }
  return found;
}
