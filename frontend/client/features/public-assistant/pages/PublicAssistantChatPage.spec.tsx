// Spec: specs/136-public-assistant/requests/REQ-20261105-013-customer-service-experience.md
// Covers the anonymous public chat's initial welcome and suggested question state.
import React from "react";
import "@testing-library/jest-dom/vitest";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import {
  act,
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
} from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import PublicAssistantChatPage from "./PublicAssistantChatPage";
import { LEGACY_ASSISTANT_CHAT_BACKGROUND } from "../services/assistantTheme";
import {
  AssistantStreamTimeoutError,
  cancelChat,
  createChat,
  loadChat,
  readChat,
  sendChat,
  streamChat,
} from "../services/assistantChatService";
import {
  readAssistantHistory,
  writeAssistantHistory,
} from "../services/assistantHistory";
import { formatDateTimeDisplay } from "@/shared/lib/dateTime";

vi.mock("@/context/i18nContext", () => ({
  useI18n: () => ({
    locale: "zhCN",
    t: (key: string) =>
      ({
        "publicAssistant.chat.loading": "正在加载…",
        "publicAssistant.chat.unavailable": "助手暂时不可用",
        "publicAssistant.chat.retry": "重试",
        "publicAssistant.chat.newChat": "新对话",
        "publicAssistant.chat.messages": "对话内容",
        "publicAssistant.chat.answering": "正在回答…",
        "publicAssistant.chat.answerFailed": "回答失败",
        "publicAssistant.chat.answerTimeout": "回复等待超时，请重新发送问题。",
        "publicAssistant.chat.connectionFailed": "连接中断",
        "publicAssistant.chat.stopFailed": "停止失败",
        "publicAssistant.chat.readFailed": "读取失败",
        "publicAssistant.chat.input": "输入问题",
        "publicAssistant.chat.suggestions": "您可以试着问我",
        "publicAssistant.chat.changeQuestions": "换一换",
        "publicAssistant.chat.showMore": "展开更多问题",
        "publicAssistant.chat.showFewer": "收起问题",
        "publicAssistant.chat.showMoreLinks": "显示全部入口",
        "publicAssistant.chat.showFewerLinks": "收起入口",
        "publicAssistant.chat.disclaimer": "内容由 AI 生成，仅供参考。",
        "publicAssistant.chat.send": "发送",
        "publicAssistant.chat.stop": "停止",
      })[key] ?? key,
  }),
}));

vi.mock("../services/assistantChatService", () => ({
  AssistantStreamTimeoutError: class AssistantStreamTimeoutError extends Error {},
  cancelChat: vi.fn(),
  createChat: vi.fn(),
  loadChat: vi.fn(),
  readChat: vi.fn(),
  sendChat: vi.fn(),
  streamChat: vi.fn(),
}));

const mockedLoadChat = vi.mocked(loadChat);
const mockedCancelChat = vi.mocked(cancelChat);
const mockedCreateChat = vi.mocked(createChat);
const mockedReadChat = vi.mocked(readChat);
const mockedSendChat = vi.mocked(sendChat);
const mockedStreamChat = vi.mocked(streamChat);

function renderPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={["/assistants/assistant-1"]}>
        <Routes>
          <Route
            path="/assistants/:assistantKey"
            element={<PublicAssistantChatPage />}
          />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe("PublicAssistantChatPage", () => {
  beforeEach(() => {
    mockedLoadChat.mockResolvedValue({
      displayName: "活动助手",
      welcomeMessage: "你好，我可以帮你查询活动信息。",
      primaryColor: "#2563eb",
      chatBackgroundColor: "#eef7f5",
      suggestedQuestions: ["如何查询报名状态？"],
      quickLinks: [
        {
          id: "register",
          label: "报名入口",
          url: "https://example.test/register",
          icon: "registration",
        },
      ],
      publishedVersionId: "version-1",
      anonymousChatEnabled: true,
    });
  });

  afterEach(() => {
    cleanup();
    localStorage.clear();
    vi.clearAllMocks();
  });

  it("shows the assistant welcome, suggested question, and accessible chat controls", async () => {
    renderPage();

    const chat = await screen.findByTestId("assistant-public-chat");
    expect(chat.style.background).toBe("rgb(238, 247, 245)");

    expect(
      await screen.findByRole("heading", { name: "活动助手" }),
    ).toBeInTheDocument();
    expect(screen.getByRole("log", { name: "对话内容" })).toBeInTheDocument();
    expect(
      screen.getByText("你好，我可以帮你查询活动信息。"),
    ).toBeInTheDocument();
    expect(
      screen.getAllByTestId("assistant-avatar")[0].querySelector("svg"),
    ).toHaveClass("lucide-message-circle-question-mark");
    expect(screen.getAllByTestId("assistant-avatar")).toHaveLength(1);
    expect(screen.getByTestId("assistant-welcome-title")).toHaveClass(
      "text-lg",
    );
    expect(
      screen.getByRole("button", { name: "如何查询报名状态？" }),
    ).toBeEnabled();
    expect(screen.getByRole("link", { name: /报名入口/ })).toHaveAttribute(
      "href",
      "https://example.test/register",
    );
    expect(screen.getByTestId("assistant-quick-link-icon")).toHaveAttribute(
      "data-icon",
      "registration",
    );
    expect(
      screen.getByRole("heading", { name: "您可以试着问我" }),
    ).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "新对话" })).toBeInTheDocument();
    expect(
      screen.getByRole("textbox", { name: "输入问题" }),
    ).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "发送" })).toBeDisabled();
    expect(mockedLoadChat).toHaveBeenCalledWith("assistant-1");
  });

  it("moves chat actions above the composer and hides quick links after the first message", async () => {
    const questions = [
      "如何查询报名状态？",
      "报名完成之后，如何查询参会凭证、修改参会人信息，以及确认展馆开放时间和交通路线？请告诉我详细操作步骤和需要提前准备的材料。",
      "如何查询报名确认编号 REGISTRATION-CONFIRMATION-REFERENCE-2026-EXHIBITION-VISITOR-APPLICATION-STATUS？",
      "展馆开放时间是几点？",
      "有哪些推荐酒店？",
      "如何申请发票？",
    ];
    mockedLoadChat.mockResolvedValue({
      displayName: "活动助手",
      welcomeMessage: "欢迎",
      primaryColor: "#2563eb",
      suggestedQuestions: questions,
      quickLinks: [
        {
          id: "register",
          label: "报名入口",
          url: "https://example.test/register",
          icon: "registration",
        },
      ],
      publishedVersionId: "version-1",
      anonymousChatEnabled: true,
    });
    const session = {
      sessionId: "session-actions",
      bearerToken: "actions-token",
      conversationId: "conversation-actions",
      publishedVersionId: "version-1",
      expiresAt: "2099-01-01T00:00:00Z",
    };
    mockedCreateChat.mockResolvedValue(session);
    mockedSendChat.mockResolvedValue({
      turnId: "turn-actions",
      runId: "run-actions",
      conversationId: session.conversationId,
      streamTicket: "ticket-actions",
      acceptedSequence: 0,
    });
    mockedStreamChat.mockImplementation(
      async (_key, _session, _turn, _transport, _sequence, onEvent) => {
        onEvent({
          type: "message.completed",
          sequence: 1,
          payload: { text: "报名即将开放" },
        });
        onEvent({ type: "run.completed", sequence: 2, payload: {} });
      },
    );

    renderPage();
    await screen.findByRole("heading", { name: "活动助手" });
    expect(screen.getByRole("link", { name: /报名入口/ })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "如何查询报名状态？" }));

    expect(
      await screen.findByTestId("assistant-public-composer-actions"),
    ).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /报名入口/ })).toBeNull();
    expect(screen.getByTestId("assistant-public-reset-composer")).toBeEnabled();
    expect(
      screen.getByTestId("assistant-public-composer-question-strip"),
    ).toHaveClass("grid", "grid-cols-1");
    const suggestions = screen.getAllByTestId(
      "assistant-public-composer-suggestion",
    );
    expect(suggestions.map((button) => button.textContent)).toEqual(
      questions.slice(0, 4),
    );
    for (const button of suggestions) {
      expect(button).toHaveClass(
        "w-full",
        "min-w-0",
        "min-h-11",
        "whitespace-normal",
      );
      expect(button.querySelector("span")).not.toHaveClass("line-clamp-2");
      expect(button.querySelector("span")).toHaveClass(
        "[overflow-wrap:anywhere]",
      );
    }
    expect(screen.queryByTestId("assistant-public-reset")).toBeNull();

    fireEvent.click(suggestions[1]);
    await waitFor(() => expect(mockedSendChat).toHaveBeenCalledTimes(2));
    expect(mockedSendChat).toHaveBeenLastCalledWith(
      "assistant-1",
      session,
      expect.any(String),
      questions[1],
    );
    await waitFor(() =>
      expect(
        screen.getByTestId("assistant-public-reset-composer"),
      ).toBeEnabled(),
    );
    fireEvent.click(
      screen.getByTestId("assistant-public-composer-change-questions"),
    );
    expect(
      screen
        .getAllByTestId("assistant-public-composer-suggestion")
        .map((button) => button.textContent),
    ).toEqual([questions[4], questions[5], questions[0], questions[1]]);

    fireEvent.click(screen.getByTestId("assistant-public-reset-composer"));
    await waitFor(() =>
      expect(
        screen.queryByTestId("assistant-public-composer-actions"),
      ).toBeNull(),
    );
    expect(screen.getByRole("link", { name: /报名入口/ })).toBeInTheDocument();
  });

  it("keeps the legacy gradient for published assistants without a background setting", async () => {
    mockedLoadChat.mockResolvedValue({
      displayName: "旧版助手",
      welcomeMessage: "欢迎",
      primaryColor: "#2563eb",
      suggestedQuestions: [],
      publishedVersionId: "version-legacy",
      anonymousChatEnabled: true,
    });

    renderPage();

    const chat = await screen.findByTestId("assistant-public-chat");
    expect(chat.style.background).toBe(LEGACY_ASSISTANT_CHAT_BACKGROUND);
  });

  it("renders an uploaded quick-link icon and falls back to its built-in icon on image failure", async () => {
    mockedLoadChat.mockResolvedValue({
      displayName: "活动助手",
      welcomeMessage: "欢迎",
      primaryColor: "#2563eb",
      suggestedQuestions: [],
      quickLinks: [
        {
          id: "visit",
          label: "参观指南",
          url: "/visit",
          icon: "map",
          iconUrl: "/api/media/assets/asset-visit/content",
        },
      ],
      publishedVersionId: "version-1",
      anonymousChatEnabled: true,
    });

    renderPage();

    const image = await screen.findByTestId("assistant-quick-link-image");
    expect(image).toHaveAttribute(
      "src",
      "/api/media/assets/asset-visit/content",
    );
    fireEvent.error(image);
    expect(
      await screen.findByTestId("assistant-quick-link-icon"),
    ).toHaveAttribute("data-icon", "map");
  });

  it("keeps long quick navigation compact and reveals all links on demand", async () => {
    const longLabel =
      "展馆很长的服务入口名称需要保持可读但不能撑高整组导航卡片";
    mockedLoadChat.mockResolvedValue({
      displayName: "活动助手",
      welcomeMessage: "欢迎",
      primaryColor: "#2563eb",
      suggestedQuestions: [],
      quickLinks: Array.from({ length: 8 }, (_, index) => ({
        id: `link-${index + 1}`,
        label: index === 7 ? longLabel : `导航入口 ${index + 1}`,
        url: `https://example.test/entry-${index + 1}`,
        icon: "link",
      })),
      publishedVersionId: "version-1",
      anonymousChatEnabled: true,
    });

    renderPage();

    expect(await screen.findAllByTestId("assistant-quick-link")).toHaveLength(
      6,
    );
    const toggle = screen.getByTestId("assistant-public-quick-links-toggle");
    expect(toggle).toHaveAttribute("aria-expanded", "false");
    fireEvent.click(toggle);
    expect(await screen.findAllByTestId("assistant-quick-link")).toHaveLength(
      8,
    );
    expect(screen.getByRole("link", { name: longLabel })).toBeInTheDocument();
    expect(toggle).toHaveAttribute("aria-expanded", "true");
  });

  it("renders a configured welcome title and supporting copy", async () => {
    mockedLoadChat.mockResolvedValue({
      displayName: "活动助手",
      welcomeMessage:
        "你好，今天想了解什么？\n报名、日程和参会问题都可以问我。",
      primaryColor: "#2563eb",
      suggestedQuestions: [],
      publishedVersionId: "version-1",
      anonymousChatEnabled: true,
    });

    renderPage();

    expect(
      await screen.findByTestId("assistant-welcome-title"),
    ).toHaveTextContent("你好，今天想了解什么？");
    expect(
      screen.getByTestId("assistant-welcome-description"),
    ).toHaveTextContent("报名、日程和参会问题都可以问我。");
  });

  it("rotates the suggested question set from the welcome state", async () => {
    mockedLoadChat.mockResolvedValue({
      displayName: "活动助手",
      welcomeMessage: "欢迎",
      primaryColor: "#2563eb",
      suggestedQuestions: [
        "问题一",
        "问题二",
        "问题三",
        "问题四",
        "问题五",
        "问题六",
      ],
      publishedVersionId: "version-1",
      anonymousChatEnabled: true,
    });

    renderPage();
    expect(
      await screen.findByRole("button", { name: "问题四" }),
    ).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "问题五" })).toBeNull();

    fireEvent.click(screen.getByRole("button", { name: "换一换" }));
    expect(
      await screen.findByRole("button", { name: "问题五" }),
    ).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "问题四" })).toBeNull();
  });

  it("subscribes to SSE even when a fast fixed answer is already persisted", async () => {
    const session = {
      sessionId: "session-quick",
      bearerToken: "test-token",
      conversationId: "conversation-quick",
      publishedVersionId: "version-1",
      expiresAt: "2099-01-01T00:00:00Z",
    };
    const turn = {
      turnId: "turn-quick",
      runId: "run-quick",
      conversationId: session.conversationId,
      streamTicket: "ticket-quick",
      acceptedSequence: 1,
    };
    mockedCreateChat.mockResolvedValue(session);
    mockedSendChat.mockResolvedValue(turn);
    mockedReadChat.mockResolvedValue({
      items: [
        {
          id: "question-quick",
          conversationId: session.conversationId,
          turnId: turn.turnId,
          runId: null,
          role: 1,
          contentJson: JSON.stringify({ text: "快速问题" }),
          status: 3,
        },
        {
          id: "answer-quick",
          conversationId: session.conversationId,
          turnId: turn.turnId,
          runId: turn.runId,
          role: 2,
          contentJson: JSON.stringify({ text: "快速回答" }),
          status: 3,
        },
      ],
    });
    mockedStreamChat.mockImplementation(
      async (_key, _session, _turn, transport, _sequence, onEvent) => {
        expect(transport).toBe("sse");
        onEvent({
          type: "message.completed",
          sequence: 2,
          payload: { text: "快速回答" },
        });
        onEvent({ type: "run.completed", sequence: 3, payload: {} });
      },
    );

    renderPage();
    const input = await screen.findByRole("textbox", { name: "输入问题" });
    fireEvent.change(input, { target: { value: "快速问题" } });
    fireEvent.click(screen.getByRole("button", { name: "发送" }));

    await waitFor(() => expect(mockedStreamChat).toHaveBeenCalledTimes(1));
    expect(await screen.findByText("快速回答")).toBeInTheDocument();
    await waitFor(() => expect(input).toBeEnabled());
    expect(screen.getByRole("button", { name: "发送" })).toBeDisabled();
  });

  it("cancels a stream that times out and makes the question retryable", async () => {
    const session = {
      sessionId: "session-timeout",
      bearerToken: "timeout-token",
      conversationId: "conversation-timeout",
      publishedVersionId: "version-1",
      expiresAt: "2099-01-01T00:00:00Z",
    };
    mockedCreateChat.mockResolvedValue(session);
    mockedSendChat
      .mockResolvedValueOnce({
        turnId: "turn-timeout",
        runId: "run-timeout",
        conversationId: session.conversationId,
        streamTicket: "ticket-timeout",
        acceptedSequence: 0,
      })
      .mockResolvedValue({
        turnId: "turn-retry",
        runId: "run-retry",
        conversationId: session.conversationId,
        streamTicket: "ticket-retry",
        acceptedSequence: 0,
      });
    mockedCancelChat.mockResolvedValue({});
    mockedReadChat.mockResolvedValue({ items: [] });
    mockedStreamChat.mockRejectedValueOnce(new AssistantStreamTimeoutError());

    renderPage();
    const input = await screen.findByRole("textbox", { name: "输入问题" });
    fireEvent.change(input, { target: { value: "查询活动安排" } });
    fireEvent.click(screen.getByRole("button", { name: "发送" }));

    expect(
      await screen.findByText("回复等待超时，请重新发送问题。"),
    ).toBeInTheDocument();
    expect(mockedCancelChat).toHaveBeenCalledWith(
      "assistant-1",
      session,
      "run-timeout",
    );
    expect(await screen.findByRole("button", { name: "重试" })).toBeEnabled();
    await waitFor(() => expect(input).toBeEnabled());

    const originalClientMessageId = mockedSendChat.mock.calls[0][2];
    fireEvent.click(screen.getByRole("button", { name: "重试" }));
    await waitFor(() => expect(mockedSendChat).toHaveBeenCalledTimes(2));
    expect(mockedSendChat.mock.calls[1][2]).not.toBe(originalClientMessageId);
    expect(mockedStreamChat).toHaveBeenCalledTimes(2);
  });

  it("stops an active answer and returns the composer to an idle state", async () => {
    const session = {
      sessionId: "session-stop",
      bearerToken: "stop-token",
      conversationId: "conversation-stop",
      publishedVersionId: "version-1",
      expiresAt: "2099-01-01T00:00:00Z",
    };
    mockedCreateChat.mockResolvedValue(session);
    mockedSendChat.mockResolvedValue({
      turnId: "turn-stop",
      runId: "run-stop",
      conversationId: session.conversationId,
      streamTicket: "ticket-stop",
      acceptedSequence: 0,
    });
    mockedCancelChat.mockResolvedValue({});
    mockedReadChat.mockResolvedValue({
      items: [
        {
          id: "question-stop",
          conversationId: session.conversationId,
          turnId: "turn-stop",
          clientMessageId: "request-stop",
          turnStatus: 5,
          role: 1,
          contentJson: JSON.stringify({ text: "停止这个回答" }),
          status: 3,
        },
      ],
    });
    mockedStreamChat.mockImplementation(
      async (_key, _session, _turn, _transport, _sequence, _onEvent, signal) =>
        await new Promise<void>((_resolve, reject) => {
          signal.addEventListener(
            "abort",
            () => reject(new DOMException("Aborted", "AbortError")),
            { once: true },
          );
        }),
    );

    renderPage();
    const input = await screen.findByRole("textbox", { name: "输入问题" });
    fireEvent.change(input, { target: { value: "停止这个回答" } });
    fireEvent.click(screen.getByRole("button", { name: "发送" }));

    const stop = await screen.findByRole("button", { name: "停止" });
    fireEvent.click(stop);

    await waitFor(() =>
      expect(mockedCancelChat).toHaveBeenCalledWith(
        "assistant-1",
        session,
        "run-stop",
      ),
    );
    await waitFor(() => expect(input).toBeEnabled());
    expect(
      screen.queryByRole("button", { name: "停止" }),
    ).not.toBeInTheDocument();
    expect(screen.getByTestId("assistant-public-question")).toHaveTextContent(
      "停止这个回答",
    );
  });

  it.each([1, 2])(
    "keeps a successful stop distinct from a failed history refresh (%i stop clicks)",
    async (clicks) => {
      const session = {
        sessionId: "session-stop-read",
        bearerToken: "token",
        conversationId: "conversation-stop-read",
        publishedVersionId: "version-1",
        expiresAt: "2099-01-01T00:00:00Z",
      };
      let acknowledge!: () => void;
      let rejectRead!: (reason: Error) => void;
      let signal!: AbortSignal;
      mockedCreateChat.mockResolvedValue(session);
      mockedSendChat.mockResolvedValue({
        turnId: "turn-stop-read",
        runId: "run-stop-read",
        conversationId: session.conversationId,
        streamTicket: "ticket",
        acceptedSequence: 0,
      });
      mockedCancelChat.mockImplementation(
        () =>
          new Promise((resolve) => {
            acknowledge = () => resolve({});
          }),
      );
      mockedReadChat.mockImplementation(
        () =>
          new Promise((_resolve, reject) => {
            rejectRead = reject;
          }),
      );
      mockedStreamChat.mockImplementation(
        async (
          _key,
          _session,
          _turn,
          _transport,
          _sequence,
          onEvent,
          value,
        ) => {
          signal = value;
          onEvent({
            type: "message.delta",
            sequence: 1,
            payload: { text: "已生成的部分" },
          });
          await new Promise<void>((_resolve, reject) =>
            value.addEventListener(
              "abort",
              () => reject(new DOMException("Aborted", "AbortError")),
              { once: true },
            ),
          );
        },
      );

      renderPage();
      const input = await screen.findByRole("textbox", { name: "输入问题" });
      fireEvent.change(input, { target: { value: "请停止" } });
      fireEvent.click(screen.getByRole("button", { name: "发送" }));
      await screen.findByText("已生成的部分");
      const stop = screen.getByRole("button", { name: "停止" });
      for (let click = 0; click < clicks; click++) fireEvent.click(stop);
      expect(mockedCancelChat).toHaveBeenCalledTimes(1);
      acknowledge();
      await waitFor(() => expect(signal.aborted).toBe(true));
      await waitFor(() => expect(mockedReadChat).toHaveBeenCalledTimes(1));
      expect(input).toBeDisabled();
      rejectRead(new Error("history unavailable"));

      expect(await screen.findByRole("alert")).toHaveTextContent("读取失败");
      expect(screen.queryByText("停止失败")).not.toBeInTheDocument();
      await waitFor(() => expect(input).toBeEnabled());
      expect(
        screen.queryByRole("button", { name: "重试" }),
      ).not.toBeInTheDocument();
      expect(screen.getByText("已生成的部分")).toBeInTheDocument();
      expect(
        readAssistantHistory("assistant-1", "version-1")[0].pending,
      ).toBeUndefined();
    },
  );

  it.each([true, false])(
    "reconciles a cancelled stream before a delayed stop response (HTTP success: %s)",
    async (success) => {
      const session = {
        sessionId: "session-terminal-first",
        bearerToken: "token",
        conversationId: "conversation-terminal-first",
        publishedVersionId: "version-1",
        expiresAt: "2099-01-01T00:00:00Z",
      };
      let finishStream!: () => void;
      let acknowledge!: () => void;
      mockedCreateChat.mockResolvedValue(session);
      mockedSendChat.mockResolvedValue({
        turnId: "turn-terminal-first",
        runId: "run-terminal-first",
        conversationId: session.conversationId,
        streamTicket: "ticket",
        acceptedSequence: 0,
      });
      mockedCancelChat.mockImplementation(
        () =>
          new Promise((resolve, reject) => {
            acknowledge = () =>
              success ? resolve({}) : reject(new Error("response lost"));
          }),
      );
      mockedReadChat.mockResolvedValue({
        items: [
          {
            id: "answer-terminal-first",
            conversationId: session.conversationId,
            turnId: "turn-terminal-first",
            role: 2,
            status: 4,
            contentJson: JSON.stringify({ text: "已保存的部分回答" }),
          },
        ],
      });
      mockedStreamChat.mockImplementation(
        async (_key, _session, _turn, _transport, _sequence, onEvent) => {
          onEvent({
            type: "message.delta",
            sequence: 1,
            payload: { text: "已保存的部分回答" },
          });
          await new Promise<void>((resolve) => {
            finishStream = () => {
              onEvent({ type: "run.cancelled", sequence: 2, payload: {} });
              resolve();
            };
          });
        },
      );

      renderPage();
      const input = await screen.findByRole("textbox", { name: "输入问题" });
      fireEvent.change(input, { target: { value: "请停止" } });
      fireEvent.click(screen.getByRole("button", { name: "发送" }));
      await screen.findByText("已保存的部分回答");
      fireEvent.click(screen.getByRole("button", { name: "停止" }));
      await act(async () => finishStream());
      expect(input).toBeDisabled();
      expect(mockedReadChat).not.toHaveBeenCalled();
      await act(async () => acknowledge());

      await waitFor(() => expect(input).toBeEnabled());
      expect(mockedReadChat).toHaveBeenCalledTimes(1);
      expect(screen.getByText("已保存的部分回答")).toBeInTheDocument();
      expect(screen.queryByRole("alert")).not.toBeInTheDocument();
      expect(
        screen.queryByRole("button", { name: "重试" }),
      ).not.toBeInTheDocument();
      expect(
        readAssistantHistory("assistant-1", "version-1")[0].pending,
      ).toBeUndefined();
    },
  );

  it("reports a rejected cancellation and allows another stop attempt while the answer remains active", async () => {
    mockedCreateChat.mockResolvedValue({
      sessionId: "session-reject",
      bearerToken: "token",
      conversationId: "conversation",
      publishedVersionId: "version-1",
      expiresAt: "2099-01-01T00:00:00Z",
    });
    mockedSendChat.mockResolvedValue({
      turnId: "turn",
      runId: "run",
      conversationId: "conversation",
      streamTicket: "ticket",
      acceptedSequence: 0,
    });
    mockedCancelChat
      .mockRejectedValueOnce(new Error("cancel unavailable"))
      .mockResolvedValue({});
    mockedReadChat.mockResolvedValue({ items: [] });
    let signal!: AbortSignal;
    mockedStreamChat.mockImplementation(
      async (_key, _session, _turn, _transport, _sequence, _onEvent, value) => {
        signal = value;
        await new Promise<void>((_resolve, reject) =>
          value.addEventListener(
            "abort",
            () => reject(new DOMException("Aborted", "AbortError")),
            { once: true },
          ),
        );
      },
    );
    renderPage();
    const input = await screen.findByRole("textbox", { name: "输入问题" });
    fireEvent.change(input, { target: { value: "请停止" } });
    fireEvent.click(screen.getByRole("button", { name: "发送" }));
    await waitFor(() => expect(mockedStreamChat).toHaveBeenCalled());
    fireEvent.click(screen.getByRole("button", { name: "停止" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("停止失败");
    expect(signal.aborted).toBe(false);
    expect(input).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "停止" }));
    await waitFor(() => expect(input).toBeEnabled());
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("keeps earlier turns visible while a later answer streams", async () => {
    const session = {
      sessionId: "session-1",
      bearerToken: "test-token",
      conversationId: "conversation-1",
      publishedVersionId: "version-1",
      expiresAt: "2099-01-01T00:00:00Z",
    };
    mockedCreateChat.mockResolvedValue(session);
    mockedSendChat
      .mockResolvedValueOnce({
        turnId: "turn-1",
        runId: "run-1",
        conversationId: session.conversationId,
        streamTicket: "ticket-1",
        acceptedSequence: 0,
      })
      .mockResolvedValueOnce({
        turnId: "turn-2",
        runId: "run-2",
        conversationId: session.conversationId,
        streamTicket: "ticket-2",
        acceptedSequence: 0,
      });

    const firstTurn = [
      {
        id: "question-1",
        conversationId: session.conversationId,
        turnId: "turn-1",
        runId: null,
        role: 1,
        contentJson: JSON.stringify({ text: "第一轮问题" }),
        status: 3,
      },
      {
        id: "answer-1",
        conversationId: session.conversationId,
        turnId: "turn-1",
        runId: "run-1",
        role: 2,
        contentJson: JSON.stringify({ text: "第一轮回答" }),
        status: 3,
      },
    ];
    const secondTurnPending = [
      ...firstTurn,
      {
        id: "question-2",
        conversationId: session.conversationId,
        turnId: "turn-2",
        runId: null,
        role: 1,
        contentJson: JSON.stringify({ text: "第二轮问题" }),
        status: 3,
      },
    ];
    const completeHistory = [
      ...secondTurnPending,
      {
        id: "answer-2",
        conversationId: session.conversationId,
        turnId: "turn-2",
        runId: "run-2",
        role: 2,
        contentJson: JSON.stringify({ text: "第二轮回答" }),
        status: 3,
      },
    ];

    let finishSecondStream!: () => void;
    const secondStream = new Promise<void>((resolve) => {
      finishSecondStream = resolve;
    });
    let readCount = 0;
    mockedReadChat.mockImplementation(async () => {
      readCount += 1;
      if (readCount === 1) return { items: firstTurn };
      if (readCount <= 7) return { items: secondTurnPending };
      return { items: completeHistory };
    });
    mockedStreamChat.mockImplementation(
      async (_key, _session, turn, _transport, _sequence, onEvent) => {
        onEvent({
          type: "message.delta",
          sequence: 1,
          payload: {
            text: turn.turnId === "turn-1" ? "第一轮回答" : "第二轮回答",
          },
        });
        onEvent({ type: "run.completed", sequence: 2, payload: {} });
        if (turn.turnId === "turn-2") await secondStream;
      },
    );
    renderPage();
    const input = await screen.findByRole("textbox", { name: "输入问题" });
    fireEvent.change(input, { target: { value: "第一轮问题" } });
    fireEvent.click(screen.getByRole("button", { name: "发送" }));
    await waitFor(() => expect(mockedStreamChat).toHaveBeenCalledTimes(1));
    expect(await screen.findByText("第一轮回答")).toBeInTheDocument();
    await waitFor(() => expect(input).toBeEnabled());

    fireEvent.change(input, { target: { value: "第二轮问题" } });
    fireEvent.click(screen.getByRole("button", { name: "发送" }));
    await waitFor(() => expect(mockedStreamChat).toHaveBeenCalledTimes(2));
    expect(
      await screen.findByText("第二轮回答", {}, { timeout: 4000 }),
    ).toBeInTheDocument();
    expect(screen.getByText("第一轮回答")).toBeInTheDocument();
    expect(screen.getByText("第二轮问题")).toBeInTheDocument();
    expect(screen.getAllByTestId("assistant-public-question")).toHaveLength(2);
    finishSecondStream();
    await waitFor(() =>
      expect(screen.getAllByTestId("assistant-public-answer")).toHaveLength(2),
    );
  }, 10000);

  it("returns the composer to idle when the run completes without waiting for history readback", async () => {
    const session = {
      sessionId: "session-complete",
      bearerToken: "complete-token",
      conversationId: "conversation-complete",
      publishedVersionId: "version-1",
      expiresAt: "2099-01-01T00:00:00Z",
    };
    mockedCreateChat.mockResolvedValue(session);
    mockedSendChat.mockResolvedValue({
      turnId: "turn-complete",
      runId: "run-complete",
      conversationId: session.conversationId,
      streamTicket: "ticket-complete",
      acceptedSequence: 0,
    });
    mockedReadChat.mockImplementation(() => new Promise(() => undefined));
    mockedStreamChat.mockImplementation(
      async (_key, _session, _turn, _transport, _sequence, onEvent) => {
        onEvent({
          type: "message.completed",
          sequence: 1,
          payload: { text: "完整的客服答复" },
        });
        onEvent({ type: "run.completed", sequence: 2, payload: {} });
      },
    );

    renderPage();
    const input = await screen.findByRole("textbox", { name: "输入问题" });
    fireEvent.change(input, { target: { value: "查询开放时间" } });
    fireEvent.click(screen.getByRole("button", { name: "发送" }));

    expect(await screen.findByText("完整的客服答复")).toBeInTheDocument();
    await waitFor(() => expect(input).toBeEnabled());
    expect(screen.queryByText("正在回答…")).toBeNull();
    expect(mockedReadChat).not.toHaveBeenCalled();
  });

  it("renders assistant markdown structure and links in completed replies", async () => {
    const session = {
      sessionId: "session-markdown",
      bearerToken: "markdown-token",
      conversationId: "conversation-markdown",
      publishedVersionId: "version-1",
      expiresAt: "2099-01-01T00:00:00Z",
    };
    mockedCreateChat.mockResolvedValue(session);
    mockedSendChat.mockResolvedValue({
      turnId: "turn-markdown",
      runId: "run-markdown",
      conversationId: session.conversationId,
      streamTicket: "ticket-markdown",
      acceptedSequence: 0,
    });
    mockedReadChat.mockResolvedValue({ items: [] });
    mockedStreamChat.mockImplementation(
      async (_key, _session, _turn, _transport, _sequence, onEvent) => {
        onEvent({
          type: "message.completed",
          sequence: 1,
          payload: {
            text: "## 办理安排\n\n**接待时间**：09:17。\n\n1. 从北门进入。\n2. [查看说明](https://example.test/help)",
          },
        });
        onEvent({ type: "run.completed", sequence: 2, payload: {} });
      },
    );

    renderPage();
    fireEvent.change(await screen.findByRole("textbox", { name: "输入问题" }), {
      target: { value: "怎么参观？" },
    });
    fireEvent.click(screen.getByRole("button", { name: "发送" }));

    const answer = await screen.findByTestId("assistant-public-answer");
    expect(answer.querySelector("h2")).toHaveTextContent("办理安排");
    expect(answer.querySelector("strong")).toHaveTextContent("接待时间");
    expect(answer.querySelector("ol li")).toHaveTextContent("从北门进入");
    expect(
      answer.querySelector('a[href="https://example.test/help"]'),
    ).toHaveTextContent("查看说明");
  });

  it("restores a pending same-browser request by client message id and resumes over SSE", async () => {
    const session = {
      sessionId: "session-restore",
      bearerToken: "browser-session",
      conversationId: "conversation-restore",
      publishedVersionId: "version-1",
      expiresAt: "2099-01-01T00:00:00.000Z",
    };
    writeAssistantHistory("assistant-1", [
      {
        session,
        title: "报名状态",
        updatedAt: new Date().toISOString(),
        pending: { id: "client-request-1" },
      },
    ]);
    mockedReadChat.mockResolvedValueOnce({
      items: [
        {
          id: "message-1",
          conversationId: session.conversationId,
          turnId: "turn-1",
          clientMessageId: "client-request-1",
          role: 1,
          contentJson: JSON.stringify({ text: "查询报名状态" }),
          status: 3,
        },
      ],
    });
    mockedReadChat.mockResolvedValue({
      items: [
        {
          id: "message-1",
          conversationId: session.conversationId,
          turnId: "turn-1",
          clientMessageId: "client-request-1",
          role: 1,
          contentJson: JSON.stringify({ text: "查询报名状态" }),
          status: 3,
        },
        {
          id: "message-2",
          conversationId: session.conversationId,
          turnId: "turn-1",
          runId: "run-1",
          clientMessageId: "client-request-1",
          role: 2,
          contentJson: JSON.stringify({ text: "已恢复的回答" }),
          status: 3,
        },
      ],
    });
    mockedSendChat.mockResolvedValue({
      turnId: "turn-1",
      runId: "run-1",
      conversationId: session.conversationId,
      streamTicket: "one-time-ticket",
      acceptedSequence: 1,
    });
    mockedStreamChat.mockImplementation(
      async (_key, _session, _turn, transport, _sequence, onEvent) => {
        expect(transport).toBe("sse");
        onEvent({
          type: "message.completed",
          sequence: 2,
          payload: { text: "已恢复的回答" },
        });
        onEvent({ type: "run.completed", sequence: 3, payload: {} });
      },
    );
    renderPage();
    await waitFor(() => expect(mockedSendChat).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(mockedStreamChat).toHaveBeenCalledTimes(1));
    expect(await screen.findByText("已恢复的回答")).toBeInTheDocument();
    expect(mockedSendChat).toHaveBeenCalledWith(
      "assistant-1",
      session,
      "client-request-1",
      "查询报名状态",
    );
    expect(mockedStreamChat).toHaveBeenCalledTimes(1);
    fireEvent.click(screen.getByTestId("assistant-public-history"));
    expect(
      await screen.findByTestId("assistant-history-panel"),
    ).toBeInTheDocument();
    expect(screen.getByTestId("assistant-history-entry")).toHaveTextContent(
      "报名状态",
    );
  });

  it("keeps activity time and order when opening and reconnecting an older pending conversation", async () => {
    const newer = {
      session: {
        sessionId: "session-newer",
        bearerToken: "newer-token",
        conversationId: "conversation-newer",
        publishedVersionId: "version-1",
        expiresAt: "2099-01-01T00:00:00.000Z",
      },
      title: "最近咨询",
      updatedAt: "2026-11-09T01:00:00.000Z",
    };
    const older = {
      session: {
        sessionId: "session-older",
        bearerToken: "older-token",
        conversationId: "conversation-older",
        publishedVersionId: "version-1",
        expiresAt: "2099-01-01T00:00:00.000Z",
      },
      title: "之前的咨询",
      updatedAt: "2026-11-08T22:15:00.000Z",
      pending: { id: "pending-older" },
    };
    writeAssistantHistory("assistant-1", [newer, older]);
    mockedReadChat.mockImplementation(async (_key, active) => ({
      items:
        active.conversationId === older.session.conversationId
          ? [
              {
                id: "old-question",
                conversationId: older.session.conversationId,
                turnId: "old-turn",
                clientMessageId: "pending-older",
                role: 1,
                status: 3,
                turnStatus: 2,
                contentJson: JSON.stringify({ text: "查询报名状态" }),
              },
            ]
          : [],
    }));
    mockedSendChat.mockResolvedValue({
      turnId: "old-turn",
      runId: "old-run",
      conversationId: older.session.conversationId,
      streamTicket: "old-ticket",
      acceptedSequence: 0,
    });
    let finishStream: (() => void) | undefined;
    mockedStreamChat.mockImplementation(
      async (_key, _active, _turn, _transport, _sequence, onEvent) => {
        await new Promise<void>((resolve) => {
          finishStream = resolve;
        });
        onEvent({
          type: "message.completed",
          sequence: 1,
          payload: { text: "恢复后的完整回答" },
        });
        onEvent({ type: "run.completed", sequence: 2, payload: {} });
      },
    );
    renderPage();
    await waitFor(() =>
      expect(mockedReadChat).toHaveBeenCalledWith("assistant-1", newer.session),
    );
    await waitFor(() =>
      expect(screen.getByRole("textbox", { name: "输入问题" })).toBeEnabled(),
    );
    fireEvent.click(screen.getByTestId("assistant-public-history"));
    fireEvent.click(
      (await screen.findAllByTestId("assistant-history-entry"))[1],
    );
    await waitFor(() => expect(mockedStreamChat).toHaveBeenCalledTimes(1));
    const resumedHistory = readAssistantHistory("assistant-1", "version-1");
    finishStream?.();
    expect(await screen.findByText("恢复后的完整回答")).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.getByRole("textbox", { name: "输入问题" })).toBeEnabled(),
    );
    expect(resumedHistory).toEqual([newer, older]);
    expect(readAssistantHistory("assistant-1", "version-1")).toEqual([
      newer,
      { ...older, pending: undefined },
    ]);
  });

  it("shows Beijing time and keeps saved chat history order unchanged when a visitor views a conversation", async () => {
    const newerSession = {
      sessionId: "session-newer",
      bearerToken: "browser-newer",
      conversationId: "conversation-newer",
      publishedVersionId: "version-1",
      expiresAt: "2099-01-01T00:00:00.000Z",
    };
    const olderSession = {
      sessionId: "session-older",
      bearerToken: "browser-older",
      conversationId: "conversation-older",
      publishedVersionId: "version-1",
      expiresAt: "2099-01-01T00:00:00.000Z",
    };
    const entries = [
      {
        session: newerSession,
        title: "最近咨询",
        updatedAt: "2026-11-09T01:00:00.000Z",
      },
      {
        session: olderSession,
        title: "展会开放时间是几点",
        updatedAt: "2026-11-08T22:15:00.000Z",
      },
    ];
    writeAssistantHistory("assistant-1", entries);
    mockedReadChat.mockResolvedValue({ items: [] });

    renderPage();
    await screen.findByRole("heading", { name: "活动助手" });
    await waitFor(() => expect(mockedReadChat).toHaveBeenCalled());
    fireEvent.click(screen.getByTestId("assistant-public-history"));
    const historyEntries = await screen.findAllByTestId(
      "assistant-history-entry",
    );
    expect(historyEntries[1].querySelector("time")).toHaveTextContent(
      "2026/11/9 06:15:00",
    );
    expect(historyEntries[1].querySelector("time")).toHaveAttribute(
      "dateTime",
      entries[1].updatedAt,
    );
    fireEvent.click(historyEntries[1]);

    await waitFor(() =>
      expect(readAssistantHistory("assistant-1", "version-1")).toEqual(entries),
    );
    expect(
      readAssistantHistory("assistant-1", "version-1").map(
        (entry) => entry.title,
      ),
    ).toEqual(["最近咨询", "展会开放时间是几点"]);
    expect(historyEntries[1].querySelector("time")).toHaveTextContent(
      formatDateTimeDisplay(entries[1].updatedAt, "zhCN"),
    );

    mockedSendChat.mockResolvedValue({
      turnId: "turn-new-activity",
      runId: "run-new-activity",
      conversationId: olderSession.conversationId,
      streamTicket: "ticket-new-activity",
      acceptedSequence: 0,
    });
    mockedStreamChat.mockImplementation(
      async (_key, _session, _turn, _transport, _sequence, onEvent) => {
        onEvent({
          type: "message.completed",
          sequence: 1,
          payload: { text: "新的咨询" },
        });
        onEvent({ type: "run.completed", sequence: 2, payload: {} });
      },
    );
    fireEvent.change(screen.getByRole("textbox", { name: "输入问题" }), {
      target: { value: "我还有一个问题" },
    });
    fireEvent.click(screen.getByRole("button", { name: "发送" }));
    await waitFor(() =>
      expect(
        readAssistantHistory("assistant-1", "version-1")[0].session.sessionId,
      ).toBe(olderSession.sessionId),
    );
  });

  it("does not persist or expose visitor history when the assistant disables it", async () => {
    mockedLoadChat.mockResolvedValue({
      displayName: "活动助手",
      welcomeMessage: "欢迎",
      primaryColor: "#2563eb",
      suggestedQuestions: [],
      publishedVersionId: "version-1",
      anonymousChatEnabled: true,
      historyEnabled: false,
    });
    renderPage();
    await screen.findByRole("heading", { name: "活动助手" });
    expect(
      screen.queryByTestId("assistant-public-history"),
    ).not.toBeInTheDocument();
    expect(localStorage.length).toBe(0);
  });

  it("does not submit a cancelled turn again when restoring a pending browser entry", async () => {
    const session = {
      sessionId: "session-cancelled",
      bearerToken: "browser-session",
      conversationId: "conversation-cancelled",
      publishedVersionId: "version-1",
      expiresAt: "2099-01-01T00:00:00.000Z",
    };
    writeAssistantHistory("assistant-1", [
      {
        session,
        title: "已停止的问题",
        updatedAt: new Date().toISOString(),
        pending: { id: "cancelled-request" },
      },
    ]);
    mockedReadChat.mockResolvedValue({
      items: [
        {
          id: "question-cancelled",
          conversationId: session.conversationId,
          turnId: "turn-cancelled",
          clientMessageId: "cancelled-request",
          role: 1,
          turnStatus: 5,
          status: 3,
          contentJson: JSON.stringify({ text: "已停止的问题" }),
        },
      ],
    });
    renderPage();
    expect(
      await screen.findByTestId("assistant-public-question"),
    ).toHaveTextContent("已停止的问题");
    await waitFor(() =>
      expect(screen.getByTestId("assistant-public-input")).toBeEnabled(),
    );
    expect(mockedSendChat).not.toHaveBeenCalled();
    expect(mockedStreamChat).not.toHaveBeenCalled();
    const saved = JSON.parse(
      localStorage.getItem("assistant-history:v1:assistant-1")!,
    );
    expect(saved[0].pending).toBeUndefined();
  });
});
