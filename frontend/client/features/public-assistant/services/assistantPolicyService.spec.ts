import { beforeEach, expect, it, vi } from 'vitest';
import { apiClient } from '@/lib/ApiClient';
import { checkPlatformModelService, getPlatformModelServiceForEdit, listAvailableModelServices, listModelServices, listPlatformModelServices, savePlatformModelService, setPlatformModelServiceStatus, getAssistantWebSearchAvailability, getPlatformAssistantWebSearchConfig, savePlatformAssistantWebSearchConfig, setPlatformAssistantWebSearchStatus, type ModelService, type ModelServiceSettings, type AssistantWebSearchConfig } from './assistantPolicyService';

vi.mock('@/lib/ApiClient', () => ({ apiClient: { get: vi.fn(), post: vi.fn(), put: vi.fn() } }));
beforeEach(() => vi.clearAllMocks());

it('keeps organization provider reads separate from platform provider management', () => {
  const settings: ModelServiceSettings = { name: 'service', baseUrl: 'https://example.test', secretReference: 'token', secretVersion: 'v1', timeoutSeconds: 60, models: [] };
  const provider: ModelService = { id: 'provider/1', name: 'service', revision: 3, enabled: true, models: [], settings, };
  listModelServices();
  listAvailableModelServices();
  listPlatformModelServices();
  getPlatformModelServiceForEdit(provider.id);
  savePlatformModelService(settings);
  savePlatformModelService(settings, provider);
  setPlatformModelServiceStatus(provider);
  checkPlatformModelService(provider.id);
  expect(apiClient.get).toHaveBeenCalledWith('/assistant-models/providers');
  expect(apiClient.get).toHaveBeenCalledWith('/assistant-models/providers/available');
  expect(apiClient.get).toHaveBeenCalledWith('/system/assistant-models/providers');
  expect(apiClient.get).toHaveBeenCalledWith('/system/assistant-models/providers/provider%2F1/edit');
  expect(apiClient.post).toHaveBeenCalledWith('/system/assistant-models/providers', { settings, expectedRevision: 0 });
  expect(apiClient.put).toHaveBeenCalledWith('/system/assistant-models/providers/provider%2F1', { settings, expectedRevision: 3 });
  expect(apiClient.put).toHaveBeenCalledWith('/system/assistant-models/providers/provider%2F1/status', { enabled: false, expectedRevision: 3 });
  expect(apiClient.post).toHaveBeenCalledWith('/system/assistant-models/providers/provider%2F1/check', {});
});

it('keeps web search availability in the organization API and management on the platform API', () => {
  const settings = { endpoint: 'https://example.test', botId: 'bot', serviceName: 'search', apiKey: 'key', timeoutSeconds: 60 };
  const config: AssistantWebSearchConfig = { id: 'search', revision: 4, enabled: true, endpoint: settings.endpoint, botId: settings.botId, serviceName: settings.serviceName, credentialConfigured: true, credentialMasked: '***', timeoutSeconds: 60, };
  getAssistantWebSearchAvailability();
  getPlatformAssistantWebSearchConfig();
  savePlatformAssistantWebSearchConfig(settings, 0);
  savePlatformAssistantWebSearchConfig(settings, 4);
  setPlatformAssistantWebSearchStatus(config);
  expect(apiClient.get).toHaveBeenCalledWith('/assistant-models/web-search/availability');
  expect(apiClient.get).toHaveBeenCalledWith('/system/assistant-models/web-search');
  expect(apiClient.post).toHaveBeenCalledWith('/system/assistant-models/web-search', { settings, expectedRevision: 0 });
  expect(apiClient.put).toHaveBeenCalledWith('/system/assistant-models/web-search', { settings, expectedRevision: 4 });
  expect(apiClient.put).toHaveBeenCalledWith('/system/assistant-models/web-search/status', { enabled: false, expectedRevision: 4 });
});
