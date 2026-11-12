// Spec: specs/136-public-assistant/results/answer-disclosure-2026-11-09.md
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
  within,
} from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { AssistantTestChat } from "./AssistantTestChat";
import { AssistantTranscript } from "./AssistantTranscript";
import PublicAssistantChatPage from "../pages/PublicAssistantChatPage";
import { writeAssistantHistory } from "../services/assistantHistory";
import {
  getAssistantPreviewMessages,
  getAssistantTranscript,
} from "../services/publicAssistantService";
import { loadChat, readChat } from "../services/assistantChatService";

vi.mock("@/context/i18nContext", () => ({
  useI18n: () => ({
    t: (key: string) =>
      ({
        "publicAssistant.answerDisclosure.expand": "展开完整回答",
        "publicAssistant.answerDisclosure.collapse": "收起回答",
      })[key] ?? key,
  }),
}));
vi.mock("../services/publicAssistantService", () => ({
  getAssistantPreviewMessages: vi.fn(),
  submitAssistantPreview: vi.fn(),
  getAssistantTranscript: vi.fn(),
}));
vi.mock("../services/assistantChatService", () => ({
  loadChat: vi.fn(),
  readChat: vi.fn(),
  createChat: vi.fn(),
  sendChat: vi.fn(),
  streamChat: vi.fn(),
  cancelChat: vi.fn(),
  AssistantStreamTimeoutError: class extends Error {},
}));

const fullAnswer =
  "## 报名说明\n\n" +
  "请填写报名信息并确认参会日期。\n\n".repeat(20) +
  "[查看报名须知](https://example.test/register)\n\n回答末尾：请保存报名凭证。";
const message = (id: string, role = 2, text = fullAnswer) => ({
  id,
  role,
  status: 3,
  contentJson: JSON.stringify({ text }),
  turnId: `turn-${id}`,
  runId: null,
  conversationId: "conversation-1",
});
let height: number;
let observers: ResizeObserverCallback[];

function mount(children: React.ReactNode) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false, gcTime: 0 } },
  });
  return render(
    <QueryClientProvider client={client}>{children}</QueryClientProvider>,
  );
}
function transcript(items = [message("answer-1")]) {
  vi.mocked(getAssistantTranscript).mockResolvedValue({
    items,
    hasMore: false,
  });
  return mount(
    <AssistantTranscript
      assistantId="assistant-1"
      conversationId="conversation-1"
      onClose={() => {}}
    />,
  );
}

describe("long assistant answers across chat surfaces", () => {
  beforeEach(() => {
    height = 600;
    observers = [];
    vi.spyOn(HTMLElement.prototype, "scrollHeight", "get").mockImplementation(
      () => height,
    );
    vi.stubGlobal(
      "ResizeObserver",
      class {
        constructor(callback: ResizeObserverCallback) {
          observers.push(callback);
        }
        observe() {}
        disconnect() {}
        unobserve() {}
      },
    );
    localStorage.clear();
  });
  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
    vi.clearAllMocks();
  });

  it("folds the admin test answer and preserves the complete text when toggled", async () => {
    vi.mocked(getAssistantPreviewMessages).mockResolvedValue({
      items: [message("preview-answer")],
      running: false,
    });
    mount(
      <AssistantTestChat
        assistantId="assistant-1"
        dirty={false}
        create={async () => ({
          id: "preview-1",
          conversationId: "conversation-preview",
          draftRevision: 1,
          model: "model-1",
          expiresAt: "2099-01-01T00:00:00Z",
        })}
      />,
    );
    fireEvent.click(screen.getByTestId("assistant-test-start"));
    const answer = await screen.findByTestId("assistant-test-answer");
    const toggle = within(answer).getByRole("button", { name: "展开完整回答" });
    expect(toggle).toHaveAttribute("aria-expanded", "false");
    expect(
      answer.querySelector("[aria-hidden='true'][inert]"),
    ).toBeInTheDocument();
    fireEvent.click(toggle);
    expect(toggle).toHaveAccessibleName("收起回答");
    expect(answer.querySelector("[aria-hidden='true'][inert]")).toBeNull();
    expect(answer).toHaveTextContent("回答末尾：请保存报名凭证。");
    fireEvent.click(toggle);
    expect(toggle).toHaveAttribute("aria-expanded", "false");
  });

  it("folds restored H5 answers without folding the visitor question", async () => {
    const session = {
      sessionId: "session-1",
      bearerToken: "token",
      conversationId: "conversation-1",
      publishedVersionId: "version-1",
      expiresAt: "2099-01-01T00:00:00Z",
    };
    writeAssistantHistory("assistant-1", [
      { session, title: "怎么报名", updatedAt: "2026-11-09T03:00:00Z" },
    ]);
    vi.mocked(loadChat).mockResolvedValue({
      displayName: "报名助手",
      welcomeMessage: "欢迎咨询",
      primaryColor: "#2563eb",
      suggestedQuestions: [],
      publishedVersionId: "version-1",
      anonymousChatEnabled: true,
    });
    vi.mocked(readChat).mockResolvedValue({
      items: [message("question", 1, "怎么报名"), message("h5-answer")],
    });
    mount(
      <MemoryRouter initialEntries={["/assistants/assistant-1"]}>
        <Routes>
          <Route
            path="/assistants/:assistantKey"
            element={<PublicAssistantChatPage />}
          />
        </Routes>
      </MemoryRouter>,
    );
    const answer = await screen.findByTestId("assistant-public-answer");
    expect(
      within(answer).getByRole("button", { name: "展开完整回答" }),
    ).toHaveAttribute("aria-expanded", "false");
    expect(
      within(screen.getByTestId("assistant-public-question")).queryByRole(
        "button",
      ),
    ).toBeNull();
  });

  it("keeps transcript answers independent and preserves Markdown and safe links", async () => {
    transcript([message("answer-1"), message("answer-2")]);
    const answers = await screen.findAllByTestId(
      "assistant-transcript-message",
    );
    const first = within(answers[0]).getByRole("button", {
      name: "展开完整回答",
    });
    const second = within(answers[1]).getByRole("button", {
      name: "展开完整回答",
    });
    expect(first.getAttribute("aria-controls")).not.toBe(
      second.getAttribute("aria-controls"),
    );
    fireEvent.click(first);
    expect(first).toHaveAttribute("aria-expanded", "true");
    expect(second).toHaveAttribute("aria-expanded", "false");
    expect(
      within(answers[0]).getByRole("heading", { name: "报名说明" }),
    ).toBeInTheDocument();
    expect(
      within(answers[0]).getByRole("link", { name: "查看报名须知" }),
    ).toHaveAttribute("href", "https://example.test/register");
  });

  it("does not add a toggle when the rendered answer fits the preview", async () => {
    height = 240;
    transcript();
    await screen.findByTestId("assistant-transcript-message");
    expect(screen.queryByRole("button", { name: "展开完整回答" })).toBeNull();
  });

  it("removes the disclosure when a wider layout brings the answer below the threshold", async () => {
    transcript();
    await screen.findByRole("button", { name: "展开完整回答" });
    height = 120;
    act(() =>
      observers.forEach((callback) => callback([], {} as ResizeObserver)),
    );
    await waitFor(() =>
      expect(screen.queryByRole("button", { name: "展开完整回答" })).toBeNull(),
    );
  });

  it("remeasures after wrapping changes and retains a manual expansion while content grows", async () => {
    height = 120;
    transcript();
    await screen.findByTestId("assistant-transcript-message");
    height = 600;
    act(() =>
      observers.forEach((callback) => callback([], {} as ResizeObserver)),
    );
    const toggle = await screen.findByRole("button", { name: "展开完整回答" });
    fireEvent.click(toggle);
    height = 900;
    act(() =>
      observers.forEach((callback) => callback([], {} as ResizeObserver)),
    );
    await waitFor(() =>
      expect(toggle).toHaveAttribute("aria-expanded", "true"),
    );
  });
});
