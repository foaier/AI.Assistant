import { afterEach, describe, expect, it, vi } from "vitest";

// Spec: REQ-20261105-013, SSE delivery and complete paginated visitor history.
import {
  AssistantStreamTimeoutError,
  readChat,
  streamChat,
  type ChatEvent,
  type ChatSession,
  type ChatTurn,
} from "./assistantChatService";

describe("assistantChatService streamChat", () => {
  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it("waits for the run terminal event after receiving the completed message", async () => {
    const encoder = new TextEncoder();
    let controller!: ReadableStreamDefaultController<Uint8Array>;
    const body = new ReadableStream<Uint8Array>({
      start(value) {
        controller = value;
      },
    });
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        new Response(body, {
          status: 200,
          headers: { "Content-Type": "text/event-stream" },
        }),
      ),
    );

    const session: ChatSession = {
      sessionId: "session-1",
      bearerToken: "token-1",
      conversationId: "conversation-1",
      publishedVersionId: "published-1",
      expiresAt: "2099-01-01T00:00:00.000Z",
    };
    const turn: ChatTurn = {
      turnId: "turn-1",
      runId: "run-1",
      conversationId: "conversation-1",
      streamTicket: "ticket-1",
      acceptedSequence: 0,
    };
    const received: string[] = [];
    let markMessageReceived!: () => void;
    const messageReceived = new Promise<void>((resolve) => {
      markMessageReceived = resolve;
    });
    let settled = false;
    const streaming = streamChat(
      "assistant-1",
      session,
      turn,
      "sse",
      0,
      (event) => {
        received.push(event.type);
        if (event.type === "message.completed") markMessageReceived();
      },
      new AbortController().signal,
    ).then(() => {
      settled = true;
    });

    controller.enqueue(
      encoder.encode(
        `data: ${JSON.stringify({ type: "message.completed", sequence: 2, payload: { text: "完整答案" } })}\n\n`,
      ),
    );
    await messageReceived;
    expect(settled).toBe(false);

    controller.enqueue(
      encoder.encode(
        `data: ${JSON.stringify({ type: "run.completed", sequence: 3, payload: {} })}\n\n`,
      ),
    );
    await expect(streaming).resolves.toBeUndefined();
    expect(received).toEqual(["message.completed", "run.completed"]);
  });

  it("accepts the server done marker after a completed message when run.completed was not delivered", async () => {
    const body = new ReadableStream<Uint8Array>({
      start(controller) {
        controller.enqueue(
          new TextEncoder().encode(
            [
              {
                type: "message.completed",
                sequence: 2,
                payload: { text: "完整答案" },
              },
              "data: [DONE]\n\n",
            ]
              .map((event) =>
                typeof event === "string"
                  ? event
                  : `data: ${JSON.stringify(event)}\n\n`,
              )
              .join(""),
          ),
        );
      },
    });
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        new Response(body, {
          status: 200,
          headers: { "Content-Type": "text/event-stream" },
        }),
      ),
    );

    const session: ChatSession = {
      sessionId: "session-done",
      bearerToken: "token-done",
      conversationId: "conversation-done",
      publishedVersionId: "published-done",
      expiresAt: "2099-01-01T00:00:00.000Z",
    };
    const turn: ChatTurn = {
      turnId: "turn-done",
      runId: "run-done",
      conversationId: session.conversationId,
      streamTicket: "ticket-done",
      acceptedSequence: 0,
    };
    const received: ChatEvent[] = [];

    await expect(
      streamChat(
        "assistant-done",
        session,
        turn,
        "sse",
        0,
        (event) => received.push(event),
        new AbortController().signal,
      ),
    ).resolves.toBeUndefined();

    expect(received.map((event) => event.type)).toEqual([
      "message.completed",
      "run.completed",
    ]);
    expect(received[1].sequence).toBe(3);
  });

  it("parses an SSE record when CRLF is split across network chunks", async () => {
    const encoder = new TextEncoder();
    const terminal = `data: ${JSON.stringify({ type: "run.completed", sequence: 1, payload: {} })}\r\n\r\n`;
    const splitAt = terminal.indexOf("\r\n\r\n") + 1;
    const body = new ReadableStream<Uint8Array>({
      start(controller) {
        controller.enqueue(encoder.encode(terminal.slice(0, splitAt)));
        controller.enqueue(encoder.encode(terminal.slice(splitAt)));
      },
    });
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        new Response(body, {
          status: 200,
          headers: { "Content-Type": "text/event-stream" },
        }),
      ),
    );

    const session: ChatSession = {
      sessionId: "session-1",
      bearerToken: "token-1",
      conversationId: "conversation-1",
      publishedVersionId: "published-1",
      expiresAt: "2099-01-01T00:00:00.000Z",
    };
    const turn: ChatTurn = {
      turnId: "turn-1",
      runId: "run-1",
      conversationId: "conversation-1",
      streamTicket: "ticket-1",
      acceptedSequence: 0,
    };
    const received: string[] = [];

    await expect(
      streamChat(
        "assistant-1",
        session,
        turn,
        "sse",
        0,
        (event) => received.push(event.type),
        new AbortController().signal,
      ),
    ).resolves.toBeUndefined();

    expect(received).toEqual(["run.completed"]);
  });

  it("keeps an active stream open beyond the idle timeout", async () => {
    vi.useFakeTimers();
    let controller!: ReadableStreamDefaultController<Uint8Array>;
    const body = new ReadableStream<Uint8Array>({ start(value) { controller = value; } });
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(body, { status: 200 })));
    const session: ChatSession = {
      sessionId: "session", bearerToken: "token", conversationId: "conversation",
      publishedVersionId: "published", expiresAt: "2099-01-01",
    };
    const turn: ChatTurn = {
      turnId: "turn", runId: "run", conversationId: session.conversationId,
      streamTicket: "ticket", acceptedSequence: 0,
    };
    const received: string[] = [];
    const streaming = streamChat("assistant", session, turn, "sse", 0,
      (event) => received.push(event.type), new AbortController().signal, 100);
    const emit = (type: string, sequence: number) => controller.enqueue(
      new TextEncoder().encode(`data: ${JSON.stringify({ type, sequence, payload: {} })}\n\n`),
    );

    emit("run.started", 1);
    await vi.advanceTimersByTimeAsync(90);
    emit("tool.call.completed", 2);
    await vi.advanceTimersByTimeAsync(90);
    emit("message.delta", 3);
    await vi.advanceTimersByTimeAsync(90);
    emit("run.completed", 4);
    await expect(streaming).resolves.toBeUndefined();
    expect(received).toEqual(["run.started", "tool.call.completed", "message.delta", "run.completed"]);
  });

  it("aborts a stream that never publishes a terminal event", async () => {
    const body = new ReadableStream<Uint8Array>({
      start(controller) {
        controller.enqueue(
          new TextEncoder().encode(
            `data: ${JSON.stringify({ type: "message.delta", sequence: 1, payload: { text: "部分回复" } })}\n\n`,
          ),
        );
      },
    });
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        new Response(body, {
          status: 200,
          headers: { "Content-Type": "text/event-stream" },
        }),
      ),
    );

    const session: ChatSession = {
      sessionId: "session-timeout",
      bearerToken: "token-timeout",
      conversationId: "conversation-timeout",
      publishedVersionId: "published-timeout",
      expiresAt: "2099-01-01T00:00:00.000Z",
    };
    const turn: ChatTurn = {
      turnId: "turn-timeout",
      runId: "run-timeout",
      conversationId: session.conversationId,
      streamTicket: "ticket-timeout",
      acceptedSequence: 0,
    };

    await expect(
      streamChat(
        "assistant-timeout",
        session,
        turn,
        "sse",
        0,
        () => {},
        new AbortController().signal,
        10,
      ),
    ).rejects.toBeInstanceOf(AssistantStreamTimeoutError);
  });

  it("reads every server page using the same anonymous session credentials", async () => {
    const session: ChatSession = {
      sessionId: "s1",
      bearerToken: "bearer1",
      conversationId: "c1",
      publishedVersionId: "v1",
      expiresAt: "2099-01-01",
    };
    const messages = Array.from({ length: 105 }, (_, i) => ({
      id: `message-${i}`,
      conversationId: "c1",
      turnId: `t${i}`,
      role: 1,
      status: 3,
      contentJson: "{}",
    }));
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(
        Response.json({
          isSuccess: true,
          returnObj: {
            items: messages.slice(0, 100),
            hasMore: true,
            nextCursor: "100",
          },
        }),
      )
      .mockResolvedValueOnce(
        Response.json({
          isSuccess: true,
          returnObj: { items: messages.slice(100), hasMore: false },
        }),
      );
    vi.stubGlobal("fetch", fetch);
    expect((await readChat("assistant-1", session)).items).toEqual(messages);
    expect(fetch.mock.calls[1][0]).toContain("&cursor=100");
    expect(fetch.mock.calls[1][1].headers.Authorization).toBe("Bearer bearer1");
    expect(fetch.mock.calls[1][1].headers["X-Assistant-Session-Id"]).toBe("s1");
  });
});
