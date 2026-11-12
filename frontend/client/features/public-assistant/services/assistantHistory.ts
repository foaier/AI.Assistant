import type { ChatSession } from "./assistantChatService";
export interface PendingChat {
  id: string;
}
export interface ChatHistoryEntry {
  session: ChatSession;
  title: string;
  updatedAt: string;
  pending?: PendingChat;
}
const key = (assistant: string) => `assistant-history:v1:${assistant}`;
export function readAssistantHistory(
  assistant: string,
  version: string,
): ChatHistoryEntry[] {
  try {
    const value: unknown = JSON.parse(
      localStorage.getItem(key(assistant)) ?? "[]",
    );
    if (!Array.isArray(value)) return [];
    return value
      .filter((entry): entry is ChatHistoryEntry => {
        const s = entry?.session;
        return (
          s &&
          ["sessionId", "bearerToken", "conversationId", "expiresAt"].every(
            (k) => typeof s[k] === "string" && s[k],
          ) &&
          s.publishedVersionId === version &&
          Date.parse(s.expiresAt) > Date.now() &&
          typeof entry.title === "string" &&
          typeof entry.updatedAt === "string" &&
          (!entry.pending || typeof entry.pending.id === "string")
        );
      })
      .slice(0, 30);
  } catch {
    return [];
  }
}
export function writeAssistantHistory(
  assistant: string,
  entries: ChatHistoryEntry[],
): void {
  try {
    if (entries.length)
      localStorage.setItem(
        key(assistant),
        JSON.stringify(
          entries.slice(0, 30).map((entry) => ({
            session: {
              sessionId: entry.session.sessionId,
              bearerToken: entry.session.bearerToken,
              conversationId: entry.session.conversationId,
              publishedVersionId: entry.session.publishedVersionId,
              expiresAt: entry.session.expiresAt,
            },
            title: entry.title.slice(0, 60),
            updatedAt: entry.updatedAt,
            ...(entry.pending ? { pending: { id: entry.pending.id } } : {}),
          })),
        ),
      );
    else localStorage.removeItem(key(assistant));
  } catch {
    /* Storage may be unavailable; in-memory chat remains usable. */
  }
}
