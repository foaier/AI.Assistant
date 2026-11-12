import { apiClient } from "@/lib/ApiClient";
import { uploadAsset } from "@/services/assetService";
import type { AssistantQuickLink } from "./assistantPresentation";

export interface AssistantListItem {
  id: string;
  name: string;
  description?: string | null;
  draftRevision: number;
  updatedAt: string;
  updatedBy: string;
}
export interface Assistant {
  id: string;
  name: string;
  description?: string | null;
  draftRevision: number;
  updatedAt: string;
  updatedBy: string;
  configuration: AssistantConfiguration;
}

export interface AssistantConfiguration {
  quickLinks?: AssistantQuickLink[];
  quickLinksEnabled?: boolean;
  historyEnabled?: boolean;
  welcomeMessage: string;
  suggestedQuestions: string[];
  iconUrl: string;
  iconAssetId?: string | null;
  knowledgeBaseId?: string | null;
  businessPolicyId?: string | null;
  primaryColor: string;
  chatBackgroundColor?: string | null;
  capabilities: {
    knowledgeQa: boolean;
    webSearch: boolean;
    customQa: boolean;
  };
  retrieval: {
    topK: number;
    rerank: boolean;
  };
}

export const DEFAULT_ASSISTANT_CONFIGURATION: AssistantConfiguration = {
  quickLinks: [],
  quickLinksEnabled: true,
  historyEnabled: true,
  welcomeMessage: "你好，我是智能客服，有什么可以帮你？",
  suggestedQuestions: [],
  iconUrl: "",
  iconAssetId: null,
  knowledgeBaseId: null,
  businessPolicyId: null,
  primaryColor: "#2563eb",
  chatBackgroundColor: "#f0f5fc",
  capabilities: {
    knowledgeQa: true,
    webSearch: false,
    customQa: true,
  },
  retrieval: {
    topK: 5,
    rerank: true,
  },
};

function normalizeConfiguration(value: unknown): AssistantConfiguration {
  const source =
    value && typeof value === "object"
      ? (value as Partial<AssistantConfiguration>)
      : {};
  const capabilities = source.capabilities ?? {};
  const retrieval = source.retrieval ?? {};
  return {
    ...DEFAULT_ASSISTANT_CONFIGURATION,
    ...source,
    chatBackgroundColor:
      typeof source.chatBackgroundColor === "string"
        ? source.chatBackgroundColor
        : null,
    suggestedQuestions: Array.isArray(source.suggestedQuestions)
      ? source.suggestedQuestions.filter(
          (item): item is string => typeof item === "string",
        )
      : DEFAULT_ASSISTANT_CONFIGURATION.suggestedQuestions,
    capabilities: {
      ...DEFAULT_ASSISTANT_CONFIGURATION.capabilities,
      ...capabilities,
    },
    retrieval: {
      ...DEFAULT_ASSISTANT_CONFIGURATION.retrieval,
      ...retrieval,
    },
  };
}

export interface AssistantKnowledgeBase {
  id: string;
  name: string;
  description?: string | null;
  qnaCount: number;
  documentCount: number;
  updatedAt: string;
}

export type AssistantKnowledgeChunkingMode =
  | "Heading"
  | "Fixed"
  | "Hybrid"
  | 0
  | 1
  | 2;

export interface AssistantKnowledgeChunkingPolicy {
  mode: AssistantKnowledgeChunkingMode;
  maxChunkSize: number;
  minChunkSize: number;
  overlapSize: number;
  maxHeadingLevel: number;
  preserveHeadingPath: boolean;
  mergeShortChunks: boolean;
  revision: number;
  hasPendingDocuments: boolean;
}

export interface AssistantKnowledgeChunkPreview {
  documentId: string;
  estimatedChunkCount: number;
  segments: Array<{
    id: string;
    index: number;
    text: string;
    sourceLabel: string;
    available: boolean;
  }>;
}

export interface KnowledgeEmbeddingSelection {
  providerId: string;
  providerRevision: number;
  model: string;
  dimension: number;
  generation: number;
}

export type KnowledgeEmbeddingModel = Omit<
  KnowledgeEmbeddingSelection,
  "generation"
> & { providerName: string };
export interface KnowledgeEmbeddingProfile {
  revision: number;
  status: string;
  target: KnowledgeEmbeddingSelection | null;
  active: KnowledgeEmbeddingSelection | null;
  activeAvailable: boolean;
  documentCount: number;
  targetReadyDocumentCount: number;
  lastErrorCode: string | null;
  availableModels: KnowledgeEmbeddingModel[];
}

export const getKnowledgeEmbeddingProfile = (knowledgeBaseId: string) =>
  apiClient.get<KnowledgeEmbeddingProfile>(
    `/knowledge-bases/${encodeURIComponent(knowledgeBaseId)}/embedding-profile`,
  );
export const saveKnowledgeEmbeddingProfile = (
  knowledgeBaseId: string,
  input: Omit<KnowledgeEmbeddingSelection, "generation"> & {
    expectedRevision: number;
  },
) =>
  apiClient.put<KnowledgeEmbeddingProfile>(
    `/knowledge-bases/${encodeURIComponent(knowledgeBaseId)}/embedding-profile`,
    input,
  );

export interface AssistantKnowledgeQna {
  id: string;
  knowledgeBaseId: string;
  question: string;
  answer?: string | null;
  enabled: boolean;
  sortOrder: number;
  showAsSuggestion: boolean;
  updatedAt: string;
}

export interface AssistantKnowledgeDocument {
  id: string;
  knowledgeBaseId: string;
  assetId: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  currentVersionNo: number;
  status: number;
  chunkCount: number;
  lastIndexJobStatus?: number | null;
  lastErrorCode?: string | null;
  indexStatus: string;
  indexGeneration?: number | null;
  updatedAt: string;
}

export interface AssistantKnowledgeDocumentPreview {
  documentId: string;
  fileName: string;
  contentType: string;
  status: number;
  originalText: string;
  structure: Array<{ kind: string; text: string; level: number }>;
  segments: Array<{
    id: string;
    index: number;
    text: string;
    sourceLabel: string;
    available: boolean;
  }>;
}

export interface AssistantKnowledgeRetrievalMatch {
  segmentId: string;
  documentId: string;
  documentName: string;
  segmentIndex: number;
  text: string;
  relevance: number;
}

export interface AssistantKnowledgeRetrievalTestResult {
  question: string;
  matches: AssistantKnowledgeRetrievalMatch[];
}

export interface AssistantKnowledgeQnaTestResult {
  question: string;
  matched: boolean;
  answer: string | null;
  answerMode: "direct" | "generated";
  providerInvoked: boolean;
}

export interface AssistantAnalyticsSummary {
  conversationCount: number;
  turnCount: number;
  completedTurnCount: number;
  failedTurnCount: number;
  runCount: number;
  toolCallCount: number;
}

export interface AssistantConversationAnalytics {
  conversationId: string;
  locale: string;
  status: number;
  lastTurnNo: number;
  createdAt: string;
  lastActivityAt: string;
}

export interface AssistantConversationsPage {
  items: AssistantConversationAnalytics[];
  totalCount: number;
  page: number;
  pageSize: number;
}

function normalizeAssistant(value: Assistant): Assistant {
  return {
    ...value,
    configuration: normalizeConfiguration(value.configuration),
  };
}

export async function listAssistants(): Promise<AssistantListItem[]> {
  return apiClient.get<AssistantListItem[]>("/assistants");
}

export async function createAssistant(input: {
  name: string;
  description?: string;
  configuration: AssistantConfiguration;
}): Promise<Assistant> {
  const result = await apiClient.post<Assistant>("/assistants", input);
  return normalizeAssistant(result);
}

export async function getAssistant(id: string): Promise<Assistant> {
  const result = await apiClient.get<Assistant>(
    `/assistants/${encodeURIComponent(id)}`,
  );
  return normalizeAssistant(result);
}

export const deleteAssistant = (id: string) =>
  apiClient.delete<{ deleted: boolean }>(
    `/assistants/${encodeURIComponent(id)}`,
  );

export async function saveAssistantDraft(
  id: string,
  input: {
    name: string;
    description?: string;
    configuration: AssistantConfiguration;
    expectedDraftRevision: number;
  },
): Promise<Assistant> {
  const result = await apiClient.put<Assistant>(
    `/assistants/${encodeURIComponent(id)}/draft`,
    input,
  );
  return normalizeAssistant(result);
}

export async function listKnowledgeBases(): Promise<AssistantKnowledgeBase[]> {
  return apiClient.get<AssistantKnowledgeBase[]>("/knowledge-bases");
}

export async function createKnowledgeBase(input: {
  name: string;
  description?: string;
}): Promise<AssistantKnowledgeBase> {
  return apiClient.post<AssistantKnowledgeBase>("/knowledge-bases", input);
}

export const deleteKnowledgeBase = (knowledgeBaseId: string) =>
  apiClient.delete<{ deleted: boolean }>(
    `/knowledge-bases/${encodeURIComponent(knowledgeBaseId)}`,
  );

export async function getKnowledgeChunkingPolicy(
  knowledgeBaseId: string,
): Promise<AssistantKnowledgeChunkingPolicy> {
  return apiClient.get<AssistantKnowledgeChunkingPolicy>(
    `/knowledge-bases/${encodeURIComponent(knowledgeBaseId)}/chunking-policy`,
  );
}

export async function saveKnowledgeChunkingPolicy(
  knowledgeBaseId: string,
  input: Omit<
    AssistantKnowledgeChunkingPolicy,
    "revision" | "hasPendingDocuments"
  > & { reprocessDocuments: boolean },
): Promise<AssistantKnowledgeChunkingPolicy> {
  return apiClient.put<AssistantKnowledgeChunkingPolicy>(
    `/knowledge-bases/${encodeURIComponent(knowledgeBaseId)}/chunking-policy`,
    input,
  );
}

export async function previewKnowledgeChunking(
  knowledgeBaseId: string,
  documentId: string,
  input: Omit<
    AssistantKnowledgeChunkingPolicy,
    "revision" | "hasPendingDocuments"
  > & { reprocessDocuments?: boolean },
): Promise<AssistantKnowledgeChunkPreview> {
  return apiClient.post<AssistantKnowledgeChunkPreview>(
    `/knowledge-bases/${encodeURIComponent(knowledgeBaseId)}/chunking-preview/${encodeURIComponent(documentId)}`,
    input,
  );
}

export async function listKnowledgeQna(
  knowledgeBaseId: string,
): Promise<AssistantKnowledgeQna[]> {
  return apiClient.get<AssistantKnowledgeQna[]>(
    `/knowledge-bases/${encodeURIComponent(knowledgeBaseId)}/qna`,
  );
}

export async function saveKnowledgeQna(
  knowledgeBaseId: string,
  input: {
    id?: string;
    question: string;
    answer?: string;
    enabled: boolean;
    sortOrder: number;
    showAsSuggestion: boolean;
  },
): Promise<AssistantKnowledgeQna> {
  return apiClient.put<AssistantKnowledgeQna>(
    `/knowledge-bases/${encodeURIComponent(knowledgeBaseId)}/qna`,
    input,
  );
}

export const deleteKnowledgeQna = (knowledgeBaseId: string, qnaId: string) =>
  apiClient.delete<{ deleted: boolean }>(
    `/knowledge-bases/${encodeURIComponent(knowledgeBaseId)}/qna/${encodeURIComponent(qnaId)}`,
  );

export async function testKnowledgeQna(
  knowledgeBaseId: string,
  qnaId: string,
  question: string,
): Promise<AssistantKnowledgeQnaTestResult> {
  return apiClient.post<AssistantKnowledgeQnaTestResult>(
    `/knowledge-bases/${encodeURIComponent(knowledgeBaseId)}/qna/${encodeURIComponent(qnaId)}/test`,
    { question },
  );
}

export async function listKnowledgeDocuments(
  knowledgeBaseId: string,
): Promise<AssistantKnowledgeDocument[]> {
  return apiClient.get<AssistantKnowledgeDocument[]>(
    `/knowledge-bases/${encodeURIComponent(knowledgeBaseId)}/documents`,
  );
}

export async function uploadKnowledgeDocument(
  knowledgeBaseId: string,
  file: File,
): Promise<AssistantKnowledgeDocument> {
  const formData = new FormData();
  formData.append("file", file);
  return apiClient.postFormData<AssistantKnowledgeDocument>(
    `/knowledge-bases/${encodeURIComponent(knowledgeBaseId)}/documents`,
    formData,
  );
}

export async function processKnowledgeDocument(
  knowledgeBaseId: string,
  documentId: string,
): Promise<unknown> {
  return apiClient.post(
    `/knowledge-bases/${encodeURIComponent(knowledgeBaseId)}/documents/${encodeURIComponent(documentId)}/process`,
    {},
  );
}

export async function getKnowledgeDocumentPreview(
  knowledgeBaseId: string,
  documentId: string,
): Promise<AssistantKnowledgeDocumentPreview> {
  return apiClient.get<AssistantKnowledgeDocumentPreview>(
    `/knowledge-bases/${encodeURIComponent(knowledgeBaseId)}/documents/${encodeURIComponent(documentId)}/preview`,
  );
}

export async function testKnowledgeRetrieval(
  knowledgeBaseId: string,
  question: string,
): Promise<AssistantKnowledgeRetrievalTestResult> {
  return apiClient.post<AssistantKnowledgeRetrievalTestResult>(
    `/knowledge-bases/${encodeURIComponent(knowledgeBaseId)}/retrieval-test`,
    { question },
  );
}

export async function uploadAssistantIcon(
  file: File,
): Promise<{ assetId: string; publicUrl: string }> {
  const asset = await uploadAsset(file, { category: "org-brand" });
  return { assetId: asset.assetId, publicUrl: asset.publicUrl || asset.url };
}

export async function getAssistantAnalyticsSummary(
  assistantId: string,
): Promise<AssistantAnalyticsSummary> {
  return apiClient.get<AssistantAnalyticsSummary>(
    `/assistants/${encodeURIComponent(assistantId)}/analytics/summary`,
  );
}

export async function listAssistantConversations(
  assistantId: string,
  page = 1,
): Promise<AssistantConversationsPage> {
  const query = new URLSearchParams({ page: String(page), limit: "50" });
  return apiClient.get<AssistantConversationsPage>(
    `/assistants/${encodeURIComponent(assistantId)}/analytics/conversations?${query}`,
  );
}

export interface AssistantTranscript {
  items: Array<{
    id: string;
    role: number;
    status: number;
    contentJson: string;
    createdAt: string;
  }>;
  nextCursor?: string | null;
  hasMore: boolean;
}
export function getAssistantTranscript(
  assistantId: string,
  conversationId: string,
  cursor?: string,
): Promise<AssistantTranscript> {
  const query = new URLSearchParams({
    limit: "50",
    ...(cursor ? { cursor } : {}),
  });
  return apiClient.get(
    `/assistants/${encodeURIComponent(assistantId)}/analytics/conversations/${encodeURIComponent(conversationId)}/messages?${query}`,
  );
}

export interface AssistantPreview {
  id: string;
  conversationId: string;
  draftRevision: number;
  expiresAt: string;
  model: string;
}

export interface AssistantPreviewMessages {
  items: Array<{
    id: string;
    role: number;
    status: number;
    contentJson: string;
    createdAt: string;
  }>;
  running: boolean;
  failureCode?: string | null;
}

export function createAssistantPreview(
  assistantId: string,
  expectedDraftRevision: number,
): Promise<AssistantPreview> {
  return apiClient.post(
    `/assistants/${encodeURIComponent(assistantId)}/previews`,
    { expectedDraftRevision },
  );
}

export function submitAssistantPreview(
  assistantId: string,
  previewId: string,
  clientMessageId: string,
  text: string,
): Promise<{ runId: string }> {
  return apiClient.post(
    `/assistants/${encodeURIComponent(assistantId)}/previews/${encodeURIComponent(previewId)}/turns`,
    { clientMessageId, text },
  );
}

export function getAssistantPreviewMessages(
  assistantId: string,
  previewId: string,
): Promise<AssistantPreviewMessages> {
  return apiClient.get(
    `/assistants/${encodeURIComponent(assistantId)}/previews/${encodeURIComponent(previewId)}/messages`,
  );
}

export interface AssistantPublication {
  pointerRevision: number;
  published: boolean;
  assistantKey: string;
  versionNo: number;
  publishedAt?: string;
}
export const getAssistantPublication = (
  id: string,
): Promise<AssistantPublication> =>
  apiClient.get(`/assistants/${encodeURIComponent(id)}/publication`);
export const publishAssistant = (
  id: string,
  draftRevision: number,
  pointerRevision: number,
) =>
  apiClient.post(`/assistants/${encodeURIComponent(id)}/publish`, {
    expectedDraftRevision: draftRevision,
    expectedPointerRevision: pointerRevision,
  });
export const revokeAssistant = (id: string, pointerRevision: number) =>
  apiClient.post(`/assistants/${encodeURIComponent(id)}/revoke`, {
    expectedPointerRevision: pointerRevision,
  });
