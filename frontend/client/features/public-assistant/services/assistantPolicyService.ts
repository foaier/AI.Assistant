import { apiClient } from '@/lib/ApiClient';

export interface ModelOption { id: string; purpose: 'chat' | 'embedding' | 'rerank'; supportsTools: boolean; dimension?: number | null }
export interface ModelServiceSettings {
  name: string; baseUrl: string; secretReference: string; secretVersion: string;
  timeoutSeconds: number; models: ModelOption[];
}
export interface ModelService {
  id: string; name: string; revision: number; enabled: boolean; models: ModelOption[];
  settings: ModelServiceSettings | null; updatedAt: string;
}
export interface BusinessPolicySettings {
  name: string; businessCode: string; providerId: string; providerRevision: number; model: string;
  instructions: string; temperature: number; maxOutputTokens: number;
  knowledgeEnabled: boolean; retrievalLimit: number; requireEvidence: boolean; noAnswerMessage: string;
  webSearchEnabled: boolean; humanHandoffEnabled: boolean;
}
export interface BusinessPolicy { id: string; revision: number; enabled: boolean; settings: BusinessPolicySettings; updatedAt: string }
export interface ModelConnectionCheck { success: boolean; message: string; checkedAt: string }
export interface AssistantWebSearchConfig {
  id: string; revision: number; enabled: boolean; endpoint: string; botId: string;
  serviceName: string; credentialConfigured: boolean; credentialMasked: string;
  timeoutSeconds: number; updatedAt: string;
}
export interface AssistantWebSearchAvailability { configured: boolean; enabled: boolean; revision: number | null }
export interface AssistantWebSearchSettings {
  endpoint: string; botId: string; serviceName: string; apiKey: string; timeoutSeconds: number;
}
const root = '/assistant-models';
const platformRoot = '/system/assistant-models';
export const listModelServices = () => apiClient.get<ModelService[]>(`${root}/providers`);
export const listAvailableModelServices = () => apiClient.get<ModelService[]>(`${root}/providers/available`);
export const listBusinessPolicies = () => apiClient.get<BusinessPolicy[]>(`${root}/policies`);
export const saveBusinessPolicy = (settings: BusinessPolicySettings, existing?: BusinessPolicy) => existing
  ? apiClient.put<BusinessPolicy>(`${root}/policies/${encodeURIComponent(existing.id)}`, { settings, expectedRevision: existing.revision })
  : apiClient.post<BusinessPolicy>(`${root}/policies`, { settings, expectedRevision: 0 });
export const setBusinessPolicyStatus = (value: BusinessPolicy) => apiClient.put<BusinessPolicy>(`${root}/policies/${encodeURIComponent(value.id)}/status`, { enabled: !value.enabled, expectedRevision: value.revision });
export const getAssistantWebSearchAvailability = () => apiClient.get<AssistantWebSearchAvailability>(`${root}/web-search/availability`);

export const listPlatformModelServices = () => apiClient.get<ModelService[]>(`${platformRoot}/providers`);
export const getPlatformModelServiceForEdit = (id: string) => apiClient.get<ModelService>(`${platformRoot}/providers/${encodeURIComponent(id)}/edit`);
export const savePlatformModelService = (settings: ModelServiceSettings, existing?: ModelService) => existing
  ? apiClient.put<ModelService>(`${platformRoot}/providers/${encodeURIComponent(existing.id)}`, { settings, expectedRevision: existing.revision })
  : apiClient.post<ModelService>(`${platformRoot}/providers`, { settings, expectedRevision: 0 });
export const setPlatformModelServiceStatus = (value: ModelService) => apiClient.put<ModelService>(`${platformRoot}/providers/${encodeURIComponent(value.id)}/status`, { enabled: !value.enabled, expectedRevision: value.revision });
export const checkPlatformModelService = (id: string) => apiClient.post<ModelConnectionCheck>(`${platformRoot}/providers/${encodeURIComponent(id)}/check`, {});
export const getPlatformAssistantWebSearchConfig = () => apiClient.get<AssistantWebSearchConfig>(`${platformRoot}/web-search`);
export const savePlatformAssistantWebSearchConfig = (settings: AssistantWebSearchSettings, expectedRevision: number) =>
  expectedRevision > 0
    ? apiClient.put<AssistantWebSearchConfig>(`${platformRoot}/web-search`, { settings, expectedRevision })
    : apiClient.post<AssistantWebSearchConfig>(`${platformRoot}/web-search`, { settings, expectedRevision });
export const setPlatformAssistantWebSearchStatus = (config: AssistantWebSearchConfig) =>
  apiClient.put<AssistantWebSearchConfig>(`${platformRoot}/web-search/status`, { enabled: !config.enabled, expectedRevision: config.revision });
