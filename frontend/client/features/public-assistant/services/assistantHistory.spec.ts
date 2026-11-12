// Spec: REQ-20261105-013-customer-service-experience.md.
// Keeps only browser session access, a title/time, and a pending request id.
import { afterEach, describe, expect, it } from "vitest";
import {
  readAssistantHistory,
  writeAssistantHistory,
  type ChatHistoryEntry,
} from "./assistantHistory";

const session = {
  sessionId: "session-1",
  bearerToken: "secret-session-token",
  conversationId: "conversation-1",
  publishedVersionId: "version-1",
  expiresAt: "2099-01-01T00:00:00.000Z",
};
afterEach(() => localStorage.clear());

describe("assistantHistory", () => {
  it("persists the same-browser access credential without message text or stream tickets", () => {
    const entry: ChatHistoryEntry = {
      session,
      title: "Registration status?",
      updatedAt: "2026-11-05T00:00:00.000Z",
      pending: { id: "request-1" },
    };
    writeAssistantHistory("assistant-1", [entry]);
    const stored = localStorage.getItem("assistant-history:v1:assistant-1")!;
    expect(stored).toContain(session.bearerToken);
    expect(stored).toContain("request-1");
    expect(stored).not.toContain("streamTicket");
    expect(stored).not.toContain("text");
    expect(readAssistantHistory("assistant-1", "version-1")).toEqual([entry]);
  });

  it("drops expired and previous-publication sessions from the restorable history", () => {
    const expired = {
      ...session,
      sessionId: "expired",
      expiresAt: "2000-01-01T00:00:00.000Z",
    };
    const previous = {
      ...session,
      sessionId: "previous",
      publishedVersionId: "version-0",
    };
    writeAssistantHistory("assistant-1", [
      {
        session: expired,
        title: "Expired",
        updatedAt: "2026-11-05T00:00:00.000Z",
      },
      {
        session: previous,
        title: "Previous",
        updatedAt: "2026-11-05T00:00:00.000Z",
      },
    ]);
    expect(readAssistantHistory("assistant-1", "version-1")).toEqual([]);
  });
});
