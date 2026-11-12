// Spec: specs/136-public-assistant/results/P0-interaction-spec.md
// Covers local live preview, tab consolidation, and canonical draft save behavior.
// Upload focus regression: REQ-20261108-001; native pickers must not scroll the admin shell away.
// Pagination: specs/136-public-assistant/spec.md — 81 conversations must remain reachable across pages.
import React from "react";
import "@testing-library/jest-dom/vitest";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import {
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from "@testing-library/react";
import { Link, MemoryRouter, Route, Routes } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import PublicAssistantWorkbenchPage, {
  themeReadable,
} from "./PublicAssistantWorkbenchPage";
import {
  assistantThemePresets,
  assistantThemeReadable,
} from "../services/assistantTheme";
import {
  getAssistant,
  getAssistantAnalyticsSummary,
  listAssistantConversations,
  getAssistantTranscript,
  listKnowledgeBases,
  listKnowledgeQna,
  saveAssistantDraft,
  uploadAssistantIcon,
  type Assistant,
} from "../services/publicAssistantService";
import {
  getAssistantWebSearchAvailability,
  listBusinessPolicies,
} from "../services/assistantPolicyService";
import { publicAssistantMessages } from "@/i18n/publicAssistantMessages";

vi.mock("@/context/i18nContext", () => ({
  useI18n: () => ({
    locale: "zhCN",
    t: (key: string, values?: Record<string, string | number>) =>
      (
        ({
          "publicAssistant.description":
            "配置一个助手，并实时预览用户看到的聊天体验。",
          "publicAssistant.list.loading": "正在加载助手…",
          "publicAssistant.list.loadFailed": "助手加载失败",
          "publicAssistant.workbench.back": "返回助手列表",
          "publicAssistant.workbench.save": "保存草稿",
          "publicAssistant.workbench.saving": "保存中…",
          "publicAssistant.workbench.saved": "草稿已保存",
          "publicAssistant.workbench.saveFailed": "草稿保存失败",
          "publicAssistant.tabs.settings": "助手设置",
          "publicAssistant.tabs.knowledge": "知识管理",
          "publicAssistant.tabs.capabilities": "能力与策略",
          "publicAssistant.tabs.analytics": "会话与分析",
          "publicAssistant.settings.noKnowledgeBase": "不关联知识库",
          "publicAssistant.settings.qnaHint":
            "推荐问题和直接回答请在“知识库”菜单中维护。",
          "publicAssistant.knowledge.openManager": "打开知识库管理",
          "publicAssistant.knowledge.empty": "请先创建知识库，再维护 Q&A。",
          "publicAssistant.knowledge.enabled": "启用",
          "publicAssistant.knowledge.disabled": "已停用",
          "publicAssistant.analytics.conversations": "会话数",
          "publicAssistant.analytics.turns": "Turn 数",
          "publicAssistant.analytics.completed": "已完成",
          "publicAssistant.analytics.failed": "失败",
          "publicAssistant.analytics.runs": "Run 数",
          "publicAssistant.analytics.tools": publicAssistantMessages.zhCN["publicAssistant.analytics.tools"],
          "publicAssistant.analytics.toolsHelp": publicAssistantMessages.zhCN["publicAssistant.analytics.toolsHelp"],
          "publicAssistant.analytics.recentConversations": "最近会话",
          "publicAssistant.analytics.turnsShort": "轮",
          "publicAssistant.analytics.empty": "暂无会话记录。",
          "publicAssistant.analytics.previousPage": "上一页",
          "publicAssistant.analytics.nextPage": "下一页",
          "publicAssistant.analytics.pageInfo":
            "第 {page} / {pages} 页 · 共 {total} 条会话",
          "publicAssistant.settings.name": "助手名称",
          "publicAssistant.settings.description": "描述",
          "publicAssistant.settings.welcome": "欢迎语",
          "publicAssistant.settings.welcomeTitle": "欢迎标题",
          "publicAssistant.settings.welcomeDescription": "补充说明",
          "publicAssistant.settings.welcomeHelp":
            publicAssistantMessages.zhCN["publicAssistant.settings.welcomeHelp"],
          "publicAssistant.settings.icon": "首页图标 URL",
          "publicAssistant.settings.color": "主题色",
          "publicAssistant.settings.chatBackground": "聊天背景",
          "publicAssistant.settings.chatBackgroundHelp":
            "未设置时沿用当前聊天背景。",
          "publicAssistant.settings.themeContrast": "颜色对比度不足",
          "publicAssistant.settings.suggestions": "推荐问题",
          "publicAssistant.settings.suggestionPlaceholder": "每行一个问题",
          "publicAssistant.preview.title": "实时预览",
          "publicAssistant.preview.online": "本地草稿预览",
          "publicAssistant.preview.input": "输入消息…",
          "publicAssistant.capability.knowledgeQa": "知识库问答",
          "publicAssistant.capability.webSearch": "联网搜索",
          "publicAssistant.capability.customQa": "自定义问题 Q&A",
          "publicAssistant.capability.rerank": "检索结果重排",
          "publicAssistant.capability.topK": "召回数量 Top K",
          "publicAssistant.comingSoon": "此页签将在下一实现切片中接入。",
        })[key] ??
        ((key.startsWith("publicAssistant.create.") || key.startsWith("publicAssistant.settings."))
          ? (publicAssistantMessages.zhCN as Record<string, string>)[key] ?? key
          : key)
      ).replace(/\{(\w+)\}/g, (match, name) => String(values?.[name] ?? match)),
  }),
}));

vi.mock("../services/publicAssistantService", () => ({
  DEFAULT_ASSISTANT_CONFIGURATION: {
    welcomeMessage: "你好，我是智能客服，有什么可以帮你？",
    suggestedQuestions: [
      "如何报名？",
      "活动什么时候开始？",
      "如何联系主办方？",
    ],
    iconUrl: "",
    primaryColor: "#2563eb",
    capabilities: { knowledgeQa: true, webSearch: false, customQa: true },
    retrieval: { topK: 5, rerank: true },
  },
  listAssistants: vi.fn(),
  createAssistant: vi.fn(),
  getAssistant: vi.fn(),
  saveAssistantDraft: vi.fn(),
  listKnowledgeBases: vi.fn(),
  listKnowledgeQna: vi.fn(),
  uploadAssistantIcon: vi.fn(),
  getAssistantAnalyticsSummary: vi.fn(),
  listAssistantConversations: vi.fn(),
  getAssistantTranscript: vi.fn(),
}));
vi.mock("../services/assistantPolicyService", () => ({
  getAssistantWebSearchAvailability: vi.fn(),
  listBusinessPolicies: vi.fn(),
}));

const mockedGetAssistant = vi.mocked(getAssistant);
const mockedSaveAssistantDraft = vi.mocked(saveAssistantDraft);
const mockedUploadAssistantIcon = vi.mocked(uploadAssistantIcon);
const mockedListKnowledgeBases = vi.mocked(listKnowledgeBases);
const mockedListKnowledgeQna = vi.mocked(listKnowledgeQna);
const mockedGetAssistantAnalyticsSummary = vi.mocked(
  getAssistantAnalyticsSummary,
);
const mockedListAssistantConversations = vi.mocked(listAssistantConversations);
const mockedGetAssistantTranscript = vi.mocked(getAssistantTranscript);
const mockedListBusinessPolicies = vi.mocked(listBusinessPolicies);
const mockedGetAssistantWebSearchAvailability = vi.mocked(
  getAssistantWebSearchAvailability,
);

const baseAssistant: Assistant = {
  id: "assistant-1",
  name: "默认客服",
  description: "默认描述",
  draftRevision: 7,
  updatedAt: "2026-09-24T00:00:00.000Z",
  updatedBy: "admin-1",
  configuration: {
    welcomeMessage: "欢迎咨询默认客服",
    suggestedQuestions: ["报名方式", "活动时间"],
    iconUrl: "",
    primaryColor: "#2563eb",
    capabilities: { knowledgeQa: true, webSearch: false, customQa: true },
    retrieval: { topK: 5, rerank: true },
  },
};

function renderPage(withNavigation = false) {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={["/admin/assistants/assistant-1"]}>
        {withNavigation && (
          <>
            <Link to="/admin/assistants/assistant-1">Assistant one</Link>
            <Link to="/admin/assistants/assistant-2">Assistant two</Link>
          </>
        )}
        <Routes>
          <Route
            path="/admin/assistants/:assistantId"
            element={<PublicAssistantWorkbenchPage />}
          />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

function conversationPage(page: number, totalCount = 81) {
  const offset = (page - 1) * 50;
  return {
    items: Array.from(
      { length: Math.max(0, Math.min(50, totalCount - offset)) },
      (_, index) => ({
        conversationId: `conversation-${String(offset + index + 1).padStart(3, "0")}`,
        locale: "zh-cn",
        status: 1,
        lastTurnNo: 1,
        createdAt: "2026-11-10T00:00:00Z",
        lastActivityAt: "2026-11-10T00:00:00Z",
      }),
    ),
    totalCount,
    page,
    pageSize: 50,
  };
}

describe("PublicAssistantWorkbenchPage", () => {
  beforeEach(() => {
    mockedGetAssistant.mockResolvedValue(baseAssistant);
    mockedSaveAssistantDraft.mockResolvedValue({
      ...baseAssistant,
      name: "保存后的客服",
      draftRevision: 8,
    });
    mockedUploadAssistantIcon.mockResolvedValue({
      assetId: "asset-link-icon",
      publicUrl: "/api/media/assets/asset-link-icon/content",
    });
    mockedListKnowledgeBases.mockResolvedValue([]);
    mockedListKnowledgeQna.mockResolvedValue([]);
    mockedGetAssistantWebSearchAvailability.mockResolvedValue({
      configured: false,
      enabled: false,
      revision: null,
    });
    mockedGetAssistantAnalyticsSummary.mockResolvedValue({
      conversationCount: 0,
      turnCount: 0,
      completedTurnCount: 0,
      failedTurnCount: 0,
      runCount: 0,
      toolCallCount: 0,
    });
    mockedListAssistantConversations.mockResolvedValue({
      items: [],
      totalCount: 0,
      page: 1,
      pageSize: 50,
    });
    mockedListBusinessPolicies.mockResolvedValue([
      {
        id: "policy-1",
        revision: 2,
        enabled: true,
        updatedAt: "",
        settings: {
          name: "客服策略",
          businessCode: "customer_service",
          providerId: "provider-1",
          providerRevision: 1,
          model: "model-1",
          instructions: "只回答客服业务问题。",
          temperature: 0.7,
          maxOutputTokens: 2000,
          knowledgeEnabled: true,
          retrievalLimit: 5,
          requireEvidence: true,
          noAnswerMessage: "暂无法确认。",
          webSearchEnabled: false,
          humanHandoffEnabled: false,
        },
      },
    ]);
  });

  afterEach(() => {
    cleanup();
    vi.clearAllMocks();
  });

  it("updates the H5 preview from local form state without saving or calling runtime tools", async () => {
    renderPage();

    expect(
      await screen.findByTestId("assistant-workbench"),
    ).toBeInTheDocument();
    expect(screen.getByTestId("assistant-preview-name")).toHaveTextContent(
      "默认客服",
    );
    expect(screen.getByTestId("assistant-settings-welcome-title")).toHaveValue(
      "欢迎咨询默认客服",
    );
    expect(
      screen.getByTestId("assistant-settings-welcome-description"),
    ).toHaveValue("");
    expect(screen.getByTestId("assistant-preview-welcome")).toHaveTextContent(
      "欢迎咨询默认客服",
    );

    fireEvent.change(screen.getByTestId("assistant-settings-name"), {
      target: { value: "展会助手" },
    });
    fireEvent.change(screen.getByTestId("assistant-settings-welcome-title"), {
      target: { value: "你好，今天想了解什么？" },
    });
    fireEvent.change(
      screen.getByTestId("assistant-settings-welcome-description"),
      {
        target: { value: "报名、日程和参会问题都可以问我。" },
      },
    );
    await waitFor(() => {
      expect(screen.getByTestId("assistant-preview-name")).toHaveTextContent(
        "展会助手",
      );
      expect(screen.getByTestId("assistant-preview-welcome")).toHaveTextContent(
        "你好，今天想了解什么？报名、日程和参会问题都可以问我。",
      );
      expect(
        screen.getByTestId("assistant-preview-welcome-title"),
      ).toHaveTextContent("你好，今天想了解什么？");
      expect(
        screen.getByTestId("assistant-preview-welcome-description"),
      ).toHaveTextContent("报名、日程和参会问题都可以问我。");
      expect(screen.getByTestId("assistant-preview-entry-0")).toHaveTextContent(
        "报名方式",
      );
    });
    expect(mockedSaveAssistantDraft).not.toHaveBeenCalled();
  });

  it("keeps web search disabled until the platform Agent configuration is enabled", async () => {
    renderPage();
    fireEvent.mouseDown(
      await screen.findByTestId("assistant-tab-capabilities"),
    );
    const webSearch = await screen.findByTestId(
      "assistant-capability-web-search",
    );
    expect(webSearch).toBeDisabled();
    expect(
      screen.getByTestId("assistant-web-search-readiness"),
    ).toBeInTheDocument();
  });

  it("allows enabling web search when the platform Agent is ready", async () => {
    mockedGetAssistantWebSearchAvailability.mockResolvedValue({
      configured: true,
      enabled: true,
      revision: 2,
    });
    renderPage();
    fireEvent.mouseDown(
      await screen.findByTestId("assistant-tab-capabilities"),
    );
    const webSearch = await screen.findByTestId(
      "assistant-capability-web-search",
    );
    await waitFor(() => expect(webSearch).toBeEnabled());
    fireEvent.click(webSearch);
    expect(webSearch).toBeChecked();
  });

  it("keeps the workbench consolidated into four tabs with live knowledge and analytics surfaces", async () => {
    renderPage();

    await screen.findByTestId("assistant-workbench");
    fireEvent.mouseDown(screen.getByTestId("assistant-tab-knowledge"));
    expect(
      screen.getByTestId("assistant-knowledge-preview"),
    ).toBeInTheDocument();
    expect(screen.getByText("不关联知识库")).toBeInTheDocument();

    fireEvent.mouseDown(screen.getByTestId("assistant-tab-capabilities"));
    expect(screen.getByText("知识库问答")).toBeInTheDocument();
    expect(screen.getByText("联网搜索")).toBeInTheDocument();
    expect(screen.getByText("自定义问题 Q&A")).toBeInTheDocument();
    expect(screen.getByText("检索结果重排")).toBeInTheDocument();
    expect(screen.getByText("召回数量 Top K")).toBeInTheDocument();

    fireEvent.mouseDown(screen.getByTestId("assistant-tab-analytics"));
    expect(await screen.findByText("会话数")).toBeInTheDocument();
  });

  it.each([0, 7])("shows the server tool count %i separately from answered turns and explains its scope", async (toolCallCount) => {
    mockedGetAssistantAnalyticsSummary.mockResolvedValue({
      conversationCount: 80,
      turnCount: 120,
      completedTurnCount: 120,
      failedTurnCount: 0,
      runCount: 120,
      toolCallCount,
    });
    renderPage();
    await screen.findByTestId("assistant-workbench");
    fireEvent.mouseDown(screen.getByTestId("assistant-tab-analytics"));
    const label = await screen.findByText("工具调用次数");
    await waitFor(() => expect(within(label.parentElement!).getByText(String(toolCallCount))).toBeInTheDocument());
    expect(within(label.parentElement!).getByText(String(toolCallCount))).toHaveAttribute("aria-describedby", "assistant-analytics-tools-help");
    expect(screen.queryByText("能力使用次数")).not.toBeInTheDocument();
    expect(screen.getByRole("note")).toHaveTextContent("不包含固定问答、知识库检索或普通模型回答");
    expect(screen.getByRole("note")).toHaveTextContent("失败或拒绝");
  });

  it("saves through the canonical draft command with the current revision", async () => {
    renderPage();

    await screen.findByTestId("assistant-workbench");
    fireEvent.change(screen.getByTestId("assistant-settings-name"), {
      target: { value: "保存后的客服" },
    });
    fireEvent.change(screen.getByTestId("assistant-settings-welcome-title"), {
      target: { value: "保存后的标题" },
    });
    fireEvent.change(
      screen.getByTestId("assistant-settings-welcome-description"),
      {
        target: { value: "保存后的补充说明" },
      },
    );
    fireEvent.click(screen.getByTestId("assistant-save-draft"));

    await waitFor(() => {
      expect(mockedSaveAssistantDraft).toHaveBeenCalledWith(
        "assistant-1",
        expect.objectContaining({
          name: "保存后的客服",
          expectedDraftRevision: 7,
          configuration: expect.objectContaining({
            welcomeMessage: "保存后的标题\n保存后的补充说明",
            retrieval: expect.objectContaining({ topK: 5, rerank: true }),
          }),
        }),
      );
    });
    expect(await screen.findByText("草稿已保存")).toBeInTheDocument();
  });

  it.each([
    ["name", 120, "名称不能超过 120 字，请缩短后重试"],
    ["description", 500, "描述不能超过 500 字，请缩短后重试"],
  ] as const)(
    "preserves an overlong loaded %s and focuses it before saving",
    async (field, limit, message) => {
      mockedGetAssistant.mockResolvedValue({
        ...baseAssistant,
        [field]: "字".repeat(limit + 1),
      });
      renderPage();
      await screen.findByTestId("assistant-workbench");
      const input = screen.getByTestId(`assistant-settings-${field}`);
      fireEvent.click(screen.getByTestId("assistant-save-draft"));
      expect(await screen.findByText(message)).toBeVisible();
      expect(input).toHaveValue("字".repeat(limit + 1));
      expect(input).toHaveAttribute("aria-invalid", "true");
      expect(input).toHaveAccessibleDescription(
        expect.stringContaining(message),
      );
      expect(
        screen.getByTestId(`assistant-settings-${field}-count`),
      ).toHaveTextContent(`${limit + 1} / ${limit}`);
      fireEvent.click(screen.getByTestId("assistant-save-draft"));
      await waitFor(() => expect(input).toHaveFocus());
      expect(mockedSaveAssistantDraft).not.toHaveBeenCalled();
    },
  );

  it.each([
    ["assistant-settings-name", 120],
    ["assistant-settings-description", 500],
    ["assistant-question-0", 200],
    ["assistant-preview-input", 500],
  ] as const)("limits new input and counts characters in %s", async (testId, limit) => {
    renderPage();
    await screen.findByTestId("assistant-workbench");
    const input = screen.getByTestId(testId);
    fireEvent.change(input, { target: { value: "字".repeat(limit + 1) } });
    expect(input).toHaveValue("字".repeat(limit));
    expect(input).toHaveAttribute("maxlength", String(limit));
    expect(screen.getByTestId(`${testId}-count`)).toHaveTextContent(`${limit} / ${limit}`);
    expect(input).toHaveAccessibleDescription(expect.stringContaining(`${limit} / ${limit}`));
    fireEvent.change(input, { target: { value: "新" + "字".repeat(limit - 1) } });
    expect(input).toHaveValue("新" + "字".repeat(limit - 1));
    fireEvent.change(input, { target: { value: "短" } });
    expect(input).toHaveValue("短");
    expect(screen.getByTestId(`${testId}-count`)).toHaveTextContent(`1 / ${limit}`);
  });

  it("shares the welcome budget and saves at the combined boundary", async () => {
    renderPage();
    await screen.findByTestId("assistant-workbench");
    const title = screen.getByTestId("assistant-settings-welcome-title");
    const description = screen.getByTestId("assistant-settings-welcome-description");
    fireEvent.change(title, { target: { value: "题".repeat(1500) } });
    expect(description).toHaveAttribute("maxlength", "499");
    fireEvent.change(description, { target: { value: "描".repeat(600) } });
    expect(description).toHaveValue("描".repeat(499));
    expect(title).toHaveAttribute("maxlength", "1500");
    expect(screen.getByTestId("assistant-settings-welcome-title-count")).toHaveTextContent("1500 / 1500");
    expect(screen.getByTestId("assistant-settings-welcome-description-count")).toHaveTextContent("499 / 499");
    fireEvent.click(screen.getByTestId("assistant-save-draft"));
    await waitFor(() => expect(mockedSaveAssistantDraft).toHaveBeenCalledWith(
      "assistant-1", expect.objectContaining({ configuration: expect.objectContaining({
        welcomeMessage: "题".repeat(1500) + "\n" + "描".repeat(499),
      }) }),
    ));
  });

  it.each([["label", 60], ["url", 2000]] as const)("limits navigation %s", async (field, limit) => {
    renderPage();
    await screen.findByTestId("assistant-workbench");
    fireEvent.click(screen.getByTestId("assistant-navigation-collapse"));
    fireEvent.click(screen.getByTestId("assistant-add-navigation"));
    const input = screen.getByTestId(`assistant-navigation-${field}-0`);
    fireEvent.change(input, { target: { value: "a".repeat(limit + 10) } });
    expect(input).toHaveValue("a".repeat(limit));
    expect(screen.getByTestId(`assistant-navigation-${field}-0-count`)).toHaveTextContent(`${limit} / ${limit}`);
  });

  it("allows a 2000-unit title with an empty description and releases shared space after deletion", async () => {
    renderPage();
    await screen.findByTestId("assistant-workbench");
    const title = screen.getByTestId("assistant-settings-welcome-title");
    const description = screen.getByTestId("assistant-settings-welcome-description");
    fireEvent.change(title, { target: { value: "题".repeat(2001) } });
    expect(title).toHaveValue("题".repeat(2000));
    expect(description).toHaveAttribute("maxlength", "0");
    fireEvent.change(description, { target: { value: "说明" } });
    expect(description).toHaveValue("");
    fireEvent.change(title, { target: { value: "题".repeat(1998) } });
    expect(description).toHaveAttribute("maxlength", "1");
    fireEvent.change(description, { target: { value: "说明" } });
    expect(description).toHaveValue("说");
    expect(title).toHaveAttribute("maxlength", "1998");
  });

  it("accepts exact length limits and clears stale success when editing", async () => {
    renderPage();
    await screen.findByTestId("assistant-workbench");
    fireEvent.click(screen.getByTestId("assistant-save-draft"));
    expect(await screen.findByText("草稿已保存")).toBeVisible();
    mockedSaveAssistantDraft.mockClear();
    fireEvent.change(screen.getByTestId("assistant-settings-name"), {
      target: { value: "字".repeat(121) },
    });
    expect(screen.queryByText("草稿已保存")).not.toBeInTheDocument();
    fireEvent.change(screen.getByTestId("assistant-settings-name"), {
      target: { value: "字".repeat(120) },
    });
    fireEvent.change(screen.getByTestId("assistant-settings-description"), {
      target: { value: "描".repeat(500) },
    });
    fireEvent.click(screen.getByTestId("assistant-save-draft"));
    await waitFor(() =>
      expect(mockedSaveAssistantDraft).toHaveBeenCalledWith(
        "assistant-1",
        expect.objectContaining({
          name: "字".repeat(120),
          description: "描".repeat(500),
        }),
      ),
    );
    expect(
      screen.queryByTestId("assistant-validation-error"),
    ).not.toBeInTheDocument();
  });

  it("returns to settings and focuses a hidden invalid field when saving from another tab", async () => {
    renderPage();
    await screen.findByTestId("assistant-workbench");
    fireEvent.change(screen.getByTestId("assistant-settings-name"), {
      target: { value: " " },
    });
    fireEvent.mouseDown(screen.getByTestId("assistant-tab-analytics"));
    fireEvent.click(screen.getByTestId("assistant-save-draft"));
    await waitFor(() =>
      expect(screen.getByTestId("assistant-settings-name")).toHaveFocus(),
    );
    expect(mockedSaveAssistantDraft).not.toHaveBeenCalled();
  });

  it("only shows enabled selected Q&A, changes color immediately, and labels examples honestly", async () => {
    mockedGetAssistant.mockResolvedValue({
      ...baseAssistant,
      configuration: {
        ...baseAssistant.configuration,
        knowledgeBaseId: "kb-1",
      },
    });
    mockedListKnowledgeQna.mockResolvedValue([
      {
        id: "q1",
        knowledgeBaseId: "kb-1",
        question: "推荐问题",
        answer: "实际回答",
        enabled: true,
        showAsSuggestion: true,
        sortOrder: 1,
        updatedAt: "",
      },
      {
        id: "q2",
        knowledgeBaseId: "kb-1",
        question: "非推荐问题",
        answer: "",
        enabled: true,
        showAsSuggestion: false,
        sortOrder: 2,
        updatedAt: "",
      },
      {
        id: "q3",
        knowledgeBaseId: "kb-1",
        question: "已停用推荐",
        answer: "",
        enabled: false,
        showAsSuggestion: true,
        sortOrder: 3,
        updatedAt: "",
      },
    ]);
    renderPage();
    await waitFor(() =>
      expect(screen.getByTestId("assistant-preview-entry-2")).toHaveTextContent(
        "推荐问题",
      ),
    );
    expect(screen.getByTestId("assistant-preview-entry-0")).toHaveTextContent(
      "报名方式",
    );
    expect(screen.getByTestId("assistant-preview-entry-1")).toHaveTextContent(
      "活动时间",
    );
    expect(
      screen.queryByTestId("assistant-preview-entry-3"),
    ).not.toBeInTheDocument();
    fireEvent.click(screen.getByTestId("assistant-theme-preset-teal"));
    expect(screen.getByTestId("assistant-theme-preset-teal")).toHaveAttribute(
      "aria-pressed",
      "true",
    );
    expect(
      screen.getByTestId("assistant-preview-name").parentElement,
    ).toHaveStyle({ backgroundColor: "#0f766e" });
    expect(screen.getByTestId("assistant-theme-primary-color")).toHaveValue(
      "#0f766e",
    );
    expect(
      screen.getByTestId("assistant-theme-chat-background-color"),
    ).toHaveValue("#eef7f5");
    expect(screen.getByTestId("assistant-preview-shell")).toHaveStyle({
      background: "#eef7f5",
    });
    fireEvent.click(screen.getByTestId("assistant-preview-entry-2"));
    expect(
      screen.getByText("publicAssistant.p0.exampleReply"),
    ).toBeInTheDocument();
    expect(screen.queryByText("实际回答")).not.toBeInTheDocument();
    expect(screen.getByTestId("assistant-preview-mode-chat")).toBeEnabled();
    fireEvent.click(screen.getByTestId("assistant-preview-mode-chat"));
    expect(screen.getByTestId("assistant-test-start")).toBeInTheDocument();
    expect(mockedSaveAssistantDraft).not.toHaveBeenCalled();
  });

  it("edits quick links and owned suggestions and exposes the selected policy instructions", async () => {
    mockedGetAssistant.mockResolvedValue({
      ...baseAssistant,
      configuration: {
        ...baseAssistant.configuration,
        businessPolicyId: "policy-1",
      },
    });
    renderPage();
    await screen.findByTestId("assistant-presentation-editor");
    fireEvent.click(screen.getByTestId("assistant-navigation-collapse"));
    expect(screen.getByTestId("assistant-system-prompt")).toHaveTextContent(
      "只回答客服业务问题。",
    );
    expect(screen.getByTestId("assistant-edit-policy")).toHaveAttribute(
      "href",
      "/admin/assistant-models",
    );
    fireEvent.click(screen.getByTestId("assistant-add-question"));
    fireEvent.change(screen.getByTestId("assistant-question-2"), {
      target: { value: "如何修改报名信息？" },
    });
    fireEvent.click(screen.getByTestId("assistant-add-navigation"));
    fireEvent.change(screen.getByTestId("assistant-navigation-label-0"), {
      target: { value: "报名入口" },
    });
    fireEvent.change(screen.getByTestId("assistant-navigation-url-0"), {
      target: { value: "/registration" },
    });
    fireEvent.click(screen.getByTestId("assistant-save-draft"));
    await waitFor(() =>
      expect(mockedSaveAssistantDraft).toHaveBeenCalledWith(
        "assistant-1",
        expect.objectContaining({
          configuration: expect.objectContaining({
            suggestedQuestions: ["报名方式", "活动时间", "如何修改报名信息？"],
            quickLinks: [
              expect.objectContaining({
                label: "报名入口",
                url: "/registration",
                enabled: true,
              }),
            ],
          }),
        }),
      ),
    );
  });

  it("keeps native image picker focus inside its visible control without submitting the draft", async () => {
    renderPage();
    await screen.findByTestId("assistant-presentation-editor");
    fireEvent.change(screen.getByTestId("assistant-settings-name"), {
      target: { value: "尚未保存的助手名称" },
    });
    fireEvent.click(screen.getByTestId("assistant-navigation-collapse"));
    fireEvent.click(screen.getByTestId("assistant-add-navigation"));

    for (const testId of [
      "assistant-settings-icon-upload",
      "assistant-navigation-icon-upload-0",
    ]) {
      const input = screen.getByTestId(testId);
      const trigger = input.closest("label")!;
      // A clipped, absolutely positioned sr-only input without a local containing
      // block makes Chromium scroll the outer document when the label focuses it.
      expect(trigger).toHaveClass("relative");
      expect(input).toHaveClass("absolute", "inset-0");
      expect(input).not.toHaveClass("sr-only");
      fireEvent.click(trigger);
      fireEvent.change(input, { target: { files: [] } });
    }
    expect(screen.getByTestId("assistant-settings-name")).toHaveValue(
      "尚未保存的助手名称",
    );
    expect(screen.getByTestId("assistant-navigation-collapse")).toHaveAttribute(
      "aria-expanded",
      "true",
    );
    expect(mockedUploadAssistantIcon).not.toHaveBeenCalled();
    expect(mockedSaveAssistantDraft).not.toHaveBeenCalled();
  });

  it("keeps navigation settings compact and saves a custom uploaded link icon", async () => {
    renderPage();
    await screen.findByTestId("assistant-presentation-editor");

    expect(screen.getByTestId("assistant-navigation-enabled")).toBeVisible();
    expect(screen.getByTestId("assistant-navigation-items")).not.toBeVisible();
    fireEvent.click(screen.getByTestId("assistant-navigation-collapse"));
    expect(await screen.findByTestId("assistant-add-navigation")).toBeVisible();
    fireEvent.click(screen.getByTestId("assistant-add-navigation"));
    fireEvent.change(screen.getByTestId("assistant-navigation-label-0"), {
      target: { value: "参观指南" },
    });
    fireEvent.change(screen.getByTestId("assistant-navigation-url-0"), {
      target: { value: "/visit" },
    });
    const file = new File(["image"], "visit-icon.png", { type: "image/png" });
    fireEvent.change(screen.getByTestId("assistant-navigation-icon-upload-0"), {
      target: { files: [file] },
    });

    await waitFor(() =>
      expect(mockedUploadAssistantIcon).toHaveBeenCalledWith(file),
    );
    await screen.findByTestId("assistant-navigation-icon-remove-0");
    fireEvent.click(screen.getByTestId("assistant-save-draft"));
    await waitFor(() =>
      expect(mockedSaveAssistantDraft).toHaveBeenCalledWith(
        "assistant-1",
        expect.objectContaining({
          configuration: expect.objectContaining({
            quickLinks: [
              expect.objectContaining({
                label: "参观指南",
                url: "/visit",
                icon: "link",
                iconAssetId: "asset-link-icon",
                iconUrl: "/api/media/assets/asset-link-icon/content",
              }),
            ],
          }),
        }),
      ),
    );
  });

  it("opens a paginated transcript from the recent conversation list", async () => {
    mockedListAssistantConversations.mockResolvedValue({
      items: [
        {
          conversationId: "conversation-1",
          locale: "zh-cn",
          status: 1,
          lastTurnNo: 1,
          createdAt: "2026-11-05T00:00:00Z",
          lastActivityAt: "2026-11-05T00:00:00Z",
        },
      ],
      totalCount: 1,
      page: 1,
      pageSize: 50,
    });
    mockedGetAssistantTranscript.mockResolvedValue({
      items: [
        {
          id: "message-1",
          role: 1,
          status: 3,
          contentJson: JSON.stringify({ text: "咨询问题" }),
          createdAt: "2026-11-05T00:00:00Z",
        },
        {
          id: "message-2",
          role: 2,
          status: 3,
          contentJson: JSON.stringify({
            text: "## 参观安排\n\n**开放时间**：09:17。\n\n- 从北门进入。",
          }),
          createdAt: "2026-11-05T00:01:00Z",
        },
      ],
      hasMore: false,
    });
    renderPage();
    await screen.findByTestId("assistant-workbench");
    fireEvent.mouseDown(screen.getByTestId("assistant-tab-analytics"));
    fireEvent.click(
      await screen.findByTestId("assistant-open-transcript-conversation-1"),
    );
    const transcriptMessages = await screen.findAllByTestId(
      "assistant-transcript-message",
    );
    expect(transcriptMessages).toHaveLength(2);
    expect(transcriptMessages[0]).toHaveTextContent("咨询问题");
    expect(transcriptMessages[1].querySelector("h2")).toHaveTextContent(
      "参观安排",
    );
    expect(transcriptMessages[1].querySelector("strong")).toHaveTextContent(
      "开放时间",
    );
    expect(transcriptMessages[1].querySelector("ul li")).toHaveTextContent(
      "从北门进入",
    );
    expect(mockedGetAssistantTranscript).toHaveBeenCalledWith(
      "assistant-1",
      "conversation-1",
      undefined,
    );
  });

  it("pages through all 81 conversations and opens a transcript on page two", async () => {
    mockedGetAssistantAnalyticsSummary.mockResolvedValue({
      conversationCount: 81,
      turnCount: 121,
      completedTurnCount: 121,
      failedTurnCount: 0,
      runCount: 121,
      toolCallCount: 0,
    });
    mockedListAssistantConversations.mockImplementation(async (_, page = 1) =>
      conversationPage(page),
    );
    mockedGetAssistantTranscript.mockResolvedValue({
      items: [],
      hasMore: false,
    });
    renderPage();
    await screen.findByTestId("assistant-workbench");
    fireEvent.mouseDown(screen.getByTestId("assistant-tab-analytics"));
    await screen.findByTestId("assistant-open-transcript-conversation-001");
    expect(screen.getAllByTestId(/^assistant-open-transcript-/)).toHaveLength(
      50,
    );
    expect(
      screen.getByTestId("assistant-conversations-previous"),
    ).toBeDisabled();
    expect(screen.getByTestId("assistant-conversations-next")).toBeEnabled();
    expect(
      screen.getByTestId("assistant-conversations-pagination"),
    ).toHaveTextContent("第 1 / 2 页 · 共 81 条会话");
    expect(mockedListAssistantConversations).toHaveBeenCalledWith(
      "assistant-1",
      1,
    );

    fireEvent.click(screen.getByTestId("assistant-conversations-next"));
    await screen.findByTestId("assistant-open-transcript-conversation-081");
    expect(screen.getAllByTestId(/^assistant-open-transcript-/)).toHaveLength(
      31,
    );
    expect(
      screen.queryByTestId("assistant-open-transcript-conversation-001"),
    ).not.toBeInTheDocument();
    expect(screen.getByTestId("assistant-conversations-next")).toBeDisabled();
    expect(
      screen.getByTestId("assistant-conversations-previous"),
    ).toBeEnabled();
    expect(
      screen.getByTestId("assistant-conversations-pagination"),
    ).toHaveTextContent("第 2 / 2 页 · 共 81 条会话");
    expect(mockedListAssistantConversations).toHaveBeenCalledWith(
      "assistant-1",
      2,
    );
    fireEvent.click(
      screen.getByTestId("assistant-open-transcript-conversation-081"),
    );
    await waitFor(() =>
      expect(mockedGetAssistantTranscript).toHaveBeenCalledWith(
        "assistant-1",
        "conversation-081",
        undefined,
      ),
    );
  });

  it("disables navigation during loading, retries the failed page, and returns to page one", async () => {
    let failPage!: (error: Error) => void;
    mockedListAssistantConversations
      .mockResolvedValueOnce(conversationPage(1))
      .mockImplementationOnce(
        () =>
          new Promise((_, reject) => {
            failPage = reject;
          }),
      )
      .mockImplementation(async (_, page = 1) => conversationPage(page));
    renderPage();
    await screen.findByTestId("assistant-workbench");
    fireEvent.mouseDown(screen.getByTestId("assistant-tab-analytics"));
    await screen.findByTestId("assistant-open-transcript-conversation-001");
    fireEvent.click(screen.getByTestId("assistant-conversations-next"));
    await waitFor(() =>
      expect(mockedListAssistantConversations).toHaveBeenCalledTimes(2),
    );
    expect(screen.getByTestId("assistant-conversations-next")).toBeDisabled();
    expect(
      screen.getByTestId("assistant-conversations-previous"),
    ).toBeDisabled();
    failPage(new Error("temporary failure"));
    await screen.findByTestId("assistant-conversations-error");
    expect(screen.queryByText("暂无会话记录。")).not.toBeInTheDocument();
    fireEvent.click(screen.getByTestId("assistant-conversations-retry"));
    await screen.findByTestId("assistant-open-transcript-conversation-081");
    expect(mockedListAssistantConversations).toHaveBeenLastCalledWith(
      "assistant-1",
      2,
    );
    fireEvent.click(screen.getByTestId("assistant-conversations-previous"));
    await screen.findByTestId("assistant-open-transcript-conversation-001");
    expect(mockedListAssistantConversations).toHaveBeenLastCalledWith(
      "assistant-1",
      1,
    );
  });

  it("starts from page one when switching assistants and switching back", async () => {
    mockedGetAssistant.mockImplementation(async (id) => ({
      ...baseAssistant,
      id,
    }));
    mockedListAssistantConversations.mockImplementation(async (_, page = 1) =>
      conversationPage(page),
    );
    renderPage(true);
    await screen.findByTestId("assistant-workbench");
    fireEvent.mouseDown(screen.getByTestId("assistant-tab-analytics"));
    await screen.findByTestId("assistant-open-transcript-conversation-001");
    fireEvent.click(screen.getByTestId("assistant-conversations-next"));
    await screen.findByTestId("assistant-open-transcript-conversation-081");
    fireEvent.click(screen.getByText("Assistant two"));
    await waitFor(() =>
      expect(mockedListAssistantConversations).toHaveBeenCalledWith(
        "assistant-2",
        1,
      ),
    );
    expect(mockedListAssistantConversations).not.toHaveBeenCalledWith(
      "assistant-2",
      2,
    );
    await screen.findByTestId("assistant-open-transcript-conversation-001");
    fireEvent.click(screen.getByText("Assistant one"));
    await screen.findByTestId("assistant-open-transcript-conversation-001");
    expect(
      screen.getByTestId("assistant-conversations-previous"),
    ).toBeDisabled();
  });

  it.each([0, 50])(
    "does not offer another page for %i conversations",
    async (totalCount) => {
      mockedListAssistantConversations.mockResolvedValue(
        conversationPage(1, totalCount),
      );
      renderPage();
      await screen.findByTestId("assistant-workbench");
      fireEvent.mouseDown(screen.getByTestId("assistant-tab-analytics"));
      await waitFor(() =>
        expect(
          screen.getByTestId("assistant-conversations-pagination"),
        ).toHaveTextContent(`共 ${totalCount} 条会话`),
      );
      expect(
        screen.getByTestId("assistant-conversations-previous"),
      ).toBeDisabled();
      expect(screen.getByTestId("assistant-conversations-next")).toBeDisabled();
      if (totalCount === 0)
        expect(screen.getByText("暂无会话记录。")).toBeInTheDocument();
    },
  );

  it("previews and saves a custom chat background color", async () => {
    renderPage();
    await screen.findByTestId("assistant-workbench");
    const backgroundColor = screen.getByTestId(
      "assistant-theme-chat-background-color",
    );
    fireEvent.change(backgroundColor, { target: { value: "#eaf4f0" } });
    expect(screen.getByTestId("assistant-preview-shell")).toHaveStyle({
      background: "#eaf4f0",
    });
    fireEvent.click(screen.getByTestId("assistant-save-draft"));

    await waitFor(() =>
      expect(mockedSaveAssistantDraft).toHaveBeenCalledWith(
        "assistant-1",
        expect.objectContaining({
          configuration: expect.objectContaining({
            primaryColor: "#2563eb",
            chatBackgroundColor: "#eaf4f0",
          }),
        }),
      ),
    );
  });

  it("blocks a custom background that fails text contrast validation", async () => {
    renderPage();
    await screen.findByTestId("assistant-workbench");
    fireEvent.change(
      screen.getByTestId("assistant-theme-chat-background-color"),
      { target: { value: "#000000" } },
    );

    expect(
      screen.getByTestId("assistant-theme-contrast-error"),
    ).toBeInTheDocument();
    fireEvent.click(screen.getByTestId("assistant-save-draft"));
    await waitFor(() =>
      expect(
        screen.getByTestId("assistant-validation-error"),
      ).toBeInTheDocument(),
    );
    expect(mockedSaveAssistantDraft).not.toHaveBeenCalled();
    expect(themeReadable("#2563eb")).toBe(true);
    expect(themeReadable("#ffffff")).toBe(false);
  });

  it("keeps every built-in theme pair above the required contrast thresholds", () => {
    for (const preset of assistantThemePresets) {
      expect(
        assistantThemeReadable(preset.primaryColor, preset.chatBackgroundColor),
      ).toBe(true);
    }
  });
});
