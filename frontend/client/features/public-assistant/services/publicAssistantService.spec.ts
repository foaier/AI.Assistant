// Spec: specs/136-public-assistant/spec.md — 管理端会话列表分页。
import { beforeEach, describe, expect, it, vi } from "vitest";

import { apiClient } from "@/lib/ApiClient";

import {
  DEFAULT_ASSISTANT_CONFIGURATION,
  getAssistant,
  listAssistants,
  listAssistantConversations,
  saveAssistantDraft,
} from "./publicAssistantService";

vi.mock("@/lib/ApiClient", () => ({
  apiClient: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
  },
}));

describe("publicAssistantService", () => {
  beforeEach(() => vi.clearAllMocks());

  it("requests the first conversation page and preserves pagination metadata", async () => {
    const page = { items: [], totalCount: 81, page: 1, pageSize: 50 };
    vi.mocked(apiClient.get).mockResolvedValue(page);
    expect(await listAssistantConversations("a1")).toEqual(page);
    expect(apiClient.get).toHaveBeenCalledWith(
      "/assistants/a1/analytics/conversations?page=1&limit=50",
    );
  });

  it("sends the selected page and encodes the assistant id", async () => {
    vi.mocked(apiClient.get).mockResolvedValue({
      items: [],
      totalCount: 81,
      page: 2,
      pageSize: 50,
    });
    await listAssistantConversations("a/b", 2);
    expect(apiClient.get).toHaveBeenCalledWith(
      "/assistants/a%2Fb/analytics/conversations?page=2&limit=50",
    );
  });

  it("uses canonical assistant list route", async () => {
    vi.mocked(apiClient.get).mockResolvedValue([]);

    await listAssistants();

    expect(apiClient.get).toHaveBeenCalledWith("/assistants");
  });

  it("normalizes missing configuration on detail read", async () => {
    vi.mocked(apiClient.get).mockResolvedValue({
      id: "a1",
      name: "客服",
      draftRevision: 1,
      updatedAt: "2026-09-24T00:00:00Z",
      updatedBy: "u1",
      configuration: {},
    });

    const result = await getAssistant("a1");

    expect(result.configuration.capabilities).toEqual(
      DEFAULT_ASSISTANT_CONFIGURATION.capabilities,
    );
    expect(result.configuration.chatBackgroundColor).toBeNull();
    expect(apiClient.get).toHaveBeenCalledWith("/assistants/a1");
  });

  it("saves a draft through the canonical draft endpoint", async () => {
    vi.mocked(apiClient.put).mockResolvedValue({
      id: "a1",
      name: "客服",
      draftRevision: 2,
      updatedAt: "2026-09-24T00:00:00Z",
      updatedBy: "u1",
      configuration: DEFAULT_ASSISTANT_CONFIGURATION,
    });

    await saveAssistantDraft("a1", {
      name: "客服",
      configuration: DEFAULT_ASSISTANT_CONFIGURATION,
      expectedDraftRevision: 1,
    });

    expect(apiClient.put).toHaveBeenCalledWith(
      "/assistants/a1/draft",
      expect.objectContaining({ expectedDraftRevision: 1 }),
    );
  });
});
