// Spec: specs/136-public-assistant/requests/REQ-20261105-012-platform-model-services-immediate-availability.md
import '@testing-library/jest-dom/vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import AssistantModelsPage from './AssistantModelsPage';
import { listModelServices, listPlatformModelServices, listAvailableModelServices, getPlatformModelServiceForEdit, listBusinessPolicies, savePlatformModelService, saveBusinessPolicy, checkPlatformModelService, getPlatformAssistantWebSearchConfig, savePlatformAssistantWebSearchConfig, setPlatformAssistantWebSearchStatus } from '../services/assistantPolicyService';
import { ApiError } from '@/lib/ApiClient';
import { publicAssistantMessages } from '@/i18n/publicAssistantMessages';
vi.mock('@/context/i18nContext', () => ({ useI18n: () => ({ t: (key: string) => (publicAssistantMessages.zhCN as Record<string, string>)[key] ?? key }) }));
vi.mock('@/context/permissionContext', () => ({ usePermissions: () => ({ isSuperAdmin: true, hasPermission: () => true }) }));
vi.mock('../services/assistantPolicyService', () => ({ listModelServices: vi.fn(), listPlatformModelServices: vi.fn(), listAvailableModelServices: vi.fn(), getPlatformModelServiceForEdit: vi.fn(), listBusinessPolicies: vi.fn(), savePlatformModelService: vi.fn(), saveBusinessPolicy: vi.fn(), checkPlatformModelService: vi.fn(), setPlatformModelServiceStatus: vi.fn(), setBusinessPolicyStatus: vi.fn(), getPlatformAssistantWebSearchConfig: vi.fn(), savePlatformAssistantWebSearchConfig: vi.fn(), setPlatformAssistantWebSearchStatus: vi.fn() }));
beforeEach(() => {
  vi.mocked(listModelServices).mockResolvedValue([]);
  vi.mocked(listPlatformModelServices).mockResolvedValue([]);
  vi.mocked(listAvailableModelServices).mockResolvedValue([]);
  vi.mocked(getPlatformModelServiceForEdit).mockResolvedValue({
    id: 'provider-1', name: 'Qwen', revision: 2, enabled: true,
    models: [{ id: 'qwen3.8-max', purpose: 'chat', supportsTools: false }],
    settings: { name: 'Qwen', baseUrl: 'https://models.example.test/v1', secretReference: 'sk-existing-token', secretVersion: 'v1', timeoutSeconds: 60, models: [{ id: 'qwen3.8-max', purpose: 'chat', supportsTools: false }] },
  });
  vi.mocked(listBusinessPolicies).mockResolvedValue([]);
  vi.mocked(getPlatformAssistantWebSearchConfig).mockResolvedValue({
    id: 'volc-agent-chat', revision: 0, enabled: false,
    endpoint: 'https://open.feedcoopapi.com/agent_api/agent/chat/completion',
    botId: '7602814290642355738', serviceName: 'ask_echo',
    credentialConfigured: false, credentialMasked: '', timeoutSeconds: 60,
  });
});
afterEach(() => { cleanup(); vi.resetAllMocks(); });
it('stores the platform web search key without reading it back or retaining it in the form after save', async () => {
  vi.mocked(getPlatformAssistantWebSearchConfig).mockResolvedValue({
    id: 'volc-agent-chat', revision: 1, enabled: true,
    endpoint: 'https://open.feedcoopapi.com/agent_api/agent/chat/completion',
    botId: '7602814290642355738', serviceName: 'ask_echo',
    credentialConfigured: true, credentialMasked: '••••••••••••', timeoutSeconds: 60,
  });
  vi.mocked(savePlatformAssistantWebSearchConfig).mockResolvedValue({
    id: 'volc-agent-chat', revision: 2, enabled: true,
    endpoint: 'https://open.feedcoopapi.com/agent_api/agent/chat/completion',
    botId: '7602814290642355738', serviceName: 'ask_echo',
    credentialConfigured: true, credentialMasked: '••••••••••••', timeoutSeconds: 60,
  });
  render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><AssistantModelsPage mode="platform" /></QueryClientProvider>);

  fireEvent.mouseDown(await screen.findByTestId('assistant-models-tab-webSearch'), { button: 0, ctrlKey: false });
  expect(await screen.findByTestId('assistant-web-search-credential-mask')).toHaveTextContent('••••••••••••');
  const key = await screen.findByTestId('assistant-web-search-credential');
  expect(key).toHaveValue('');
  expect(key).toHaveAttribute('placeholder', '已配置并脱敏显示，留空则保留当前值。');
  fireEvent.change(key, { target: { value: 'test-only-volc-key' } });
  fireEvent.click(screen.getByTestId('assistant-web-search-save'));

  await waitFor(() => expect(savePlatformAssistantWebSearchConfig).toHaveBeenCalledWith(expect.objectContaining({
    apiKey: 'test-only-volc-key',
  }), 1));
  await waitFor(() => expect(screen.getByTestId('assistant-web-search-credential')).toHaveValue(''));
  expect(getPlatformAssistantWebSearchConfig).not.toHaveBeenCalledWith(expect.stringContaining('key'));
});
it('separates business policies from platform services and their requests', async () => {
  const view = render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><AssistantModelsPage /></QueryClientProvider>);
  expect(screen.getByRole('heading', { name: '助手业务策略' })).toBeInTheDocument();
  expect(screen.queryByTestId('assistant-models-tab-providers')).not.toBeInTheDocument();
  await waitFor(() => expect(listBusinessPolicies).toHaveBeenCalled());
  expect(listPlatformModelServices).not.toHaveBeenCalled();
  expect(getPlatformAssistantWebSearchConfig).not.toHaveBeenCalled();
  view.unmount();
  vi.mocked(listBusinessPolicies).mockClear();
  vi.mocked(listAvailableModelServices).mockClear();
  render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><AssistantModelsPage mode="platform" /></QueryClientProvider>);
  expect(screen.getByRole('heading', { name: '模型服务' })).toBeInTheDocument();
  expect(screen.getByRole('tab', { name: '模型服务' })).toBeInTheDocument();
  expect(screen.getByRole('tab', { name: '联网搜索' })).toBeInTheDocument();
  expect(screen.queryByRole('tab', { name: /平台级/ })).not.toBeInTheDocument();
  expect(screen.queryByTestId('assistant-policy-create')).not.toBeInTheDocument();
  await waitFor(() => expect(listPlatformModelServices).toHaveBeenCalled());
  expect(listBusinessPolicies).not.toHaveBeenCalled();
  expect(listAvailableModelServices).not.toHaveBeenCalled();
});
it('keeps platform operational data out of the business policy query cache', async () => {
  const operationalProvider = (await getPlatformModelServiceForEdit('provider-1'));
  vi.mocked(listPlatformModelServices).mockResolvedValue([operationalProvider]);
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: Infinity } } });
  const platform = render(<QueryClientProvider client={client}><AssistantModelsPage mode="platform" /></QueryClientProvider>);
  expect(await screen.findByTestId('assistant-provider-check-provider-1')).toBeInTheDocument();
  platform.unmount();

  render(<QueryClientProvider client={client}><AssistantModelsPage /></QueryClientProvider>);
  await waitFor(() => expect(listModelServices).toHaveBeenCalledTimes(1));
  const providerQueries = client.getQueryCache().getAll().filter(query => query.queryKey[0] === 'assistant-model-services');
  expect(providerQueries).toHaveLength(2);
  expect(providerQueries.map(query => query.state.data)).toEqual(expect.arrayContaining([[operationalProvider], []]));
  expect(screen.queryByText('https://models.example.test/v1')).not.toBeInTheDocument();
});
it('places the connection check on each model service and shows its result in that card', async () => {
  vi.mocked(listPlatformModelServices).mockResolvedValue([{
    id: 'provider-1', name: 'Qwen', revision: 1, enabled: true,
    models: [{ id: 'qwen3.8-max', purpose: 'chat', supportsTools: false }],
    settings: null,
  }]);
  vi.mocked(checkPlatformModelService).mockResolvedValue({ success: false, message: '模型服务返回未授权', checkedAt: '2026-11-05T00:00:00Z' });
  render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><AssistantModelsPage mode="platform" /></QueryClientProvider>);

  expect(screen.queryByTestId('assistant-models-tab-checks')).not.toBeInTheDocument();
  fireEvent.mouseDown(screen.getByTestId('assistant-models-tab-providers'), { button: 0, ctrlKey: false });
  fireEvent.click(await screen.findByTestId('assistant-provider-check-provider-1'));

  expect(await screen.findByTestId('assistant-provider-check-result-provider-1')).toHaveAttribute('role', 'alert');
  expect(screen.getByTestId('assistant-provider-check-result-provider-1')).toHaveTextContent('模型服务返回未授权');
  expect(checkPlatformModelService).toHaveBeenCalledWith('provider-1');
});
it('offers enabled platform chat services to business policies without a separate grant', async () => {
  const model = { id: 'qwen-chat', purpose: 'chat' as const, supportsTools: false };
  const service = (id: string, name: string) => ({
    id, name, revision: 1, enabled: true, models: [model],
    settings: {
      name, baseUrl: 'https://models.example.test/v1', secretReference: '[configured]', secretVersion: 'v1',
      timeoutSeconds: 60, models: [model],
    },
  });
  vi.mocked(listModelServices).mockResolvedValue([
    service('provider-current', '平台模型一'),
    service('provider-other', '平台模型二'),
  ]);
  vi.mocked(listAvailableModelServices).mockResolvedValue([
    { ...service('provider-current', '平台模型一'), settings: null },
    { ...service('provider-other', '平台模型二'), settings: null },
  ]);
  render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><AssistantModelsPage /></QueryClientProvider>);
  fireEvent.click(await screen.findByTestId('assistant-policy-create'));
  const provider = screen.getByTestId('assistant-policy-provider');
  expect(provider).toHaveValue('provider-current');
  expect(provider.querySelector('option[value="provider-other"]')).toBeInTheDocument();
});
it('blocks editing a policy whose model service is no longer available and explains the administrator action', async () => {
  vi.mocked(listBusinessPolicies).mockResolvedValue([{
    id: 'policy-1', revision: 1, enabled: true,
    settings: {
      name: '客服策略', businessCode: 'customer_service', providerId: 'provider-revoked', providerRevision: 10,
      model: 'qwen3.8-max', instructions: '用中文答复。', temperature: 0.7, maxOutputTokens: 2000,
      knowledgeEnabled: false, retrievalLimit: 5, requireEvidence: true, noAnswerMessage: '请联系工作人员。',
      webSearchEnabled: false, humanHandoffEnabled: false,
    },
  }]);
  render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><AssistantModelsPage /></QueryClientProvider>);
  fireEvent.click(await screen.findByRole('button', { name: '编辑 客服策略' }));
  expect(await screen.findByRole('alert')).toHaveTextContent('所选模型服务已停用或不再提供对话模型。请选择列表中的其他服务；若没有可选项，请联系平台管理员接入或启用对话模型服务。选到可用服务前，策略不会提交。');
  expect(screen.getByTestId('assistant-policy-provider')).toHaveValue('provider-revoked');
  expect(screen.getByTestId('assistant-models-save')).toBeDisabled();
  fireEvent.click(screen.getByTestId('assistant-models-save'));
  expect(saveBusinessPolicy).not.toHaveBeenCalled();
});
it('explains the administrator action when the platform has no available chat model', async () => {
  render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><AssistantModelsPage /></QueryClientProvider>);
  fireEvent.click(await screen.findByTestId('assistant-policy-create'));
  expect(await screen.findByRole('status')).toHaveTextContent('平台尚无已启用的对话模型服务，请联系平台管理员接入或启用服务。');
  expect(screen.getByTestId('assistant-policy-provider')).toHaveValue('');
});
it.each(['provider', 'policy'])('returns keyboard focus to the %s create button after Escape', async (editor) => {
  render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><AssistantModelsPage mode={editor === 'provider' ? 'platform' : 'business'} /></QueryClientProvider>);
  const trigger = await screen.findByTestId(`assistant-${editor}-create`);
  trigger.focus();
  fireEvent.click(trigger);
  await waitFor(() => expect(screen.getByTestId(`assistant-${editor}-name`)).toHaveFocus());
  fireEvent.keyDown(document.activeElement!, { key: 'Escape' });
  await waitFor(() => expect(screen.queryByTestId('assistant-models-editor')).not.toBeInTheDocument());
  await waitFor(() => expect(trigger).toHaveFocus());
});
it('accepts an API Token for a provider and submits it for encrypted server storage', async () => {
  render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><AssistantModelsPage mode="platform" /></QueryClientProvider>);
  fireEvent.mouseDown(screen.getByTestId('assistant-models-tab-providers'), { button: 0, ctrlKey: false });
  fireEvent.click(await screen.findByTestId('assistant-provider-create'));
  expect(screen.getByTestId('assistant-provider-credential')).toHaveAttribute('type', 'password');
  expect(screen.getByTestId('assistant-provider-credential')).toHaveAttribute('placeholder', '粘贴 API Token');
  fireEvent.change(screen.getByTestId('assistant-provider-name'), { target: { value: '测试服务' } });
  fireEvent.change(screen.getByTestId('assistant-provider-endpoint'), { target: { value: 'https://fixture.test/v1' } });
  fireEvent.change(screen.getByTestId('assistant-provider-model-0'), { target: { value: 'test-model' } });
  fireEvent.change(screen.getByTestId('assistant-provider-credential'), { target: { value: 'sk-test-token' } });
  fireEvent.click(screen.getByTestId('assistant-models-save'));
  await waitFor(() => expect(savePlatformModelService).toHaveBeenCalledWith(expect.objectContaining({ secretReference: 'sk-test-token' }), undefined));
});
it('reads the saved provider token into the edit form and submits it when another setting changes', async () => {
  const provider = {
    id: 'provider-1', name: 'Qwen', revision: 2, enabled: true,
    models: [{ id: 'qwen3.8-max', purpose: 'chat' as const, supportsTools: false }],
    settings: {
      name: 'Qwen', baseUrl: 'https://models.example.test/v1', secretReference: '[configured]', secretVersion: 'v1',
      timeoutSeconds: 60, models: [{ id: 'qwen3.8-max', purpose: 'chat' as const, supportsTools: false }],
    },
  };
  vi.mocked(listPlatformModelServices).mockResolvedValue([provider]);
  vi.mocked(getPlatformModelServiceForEdit).mockResolvedValue({
    ...provider,
    settings: { ...provider.settings, secretReference: 'sk-existing-token' },
  });
  render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><AssistantModelsPage mode="platform" /></QueryClientProvider>);
  fireEvent.mouseDown(screen.getByTestId('assistant-models-tab-providers'), { button: 0, ctrlKey: false });
  fireEvent.click(await screen.findByRole('button', { name: '编辑 Qwen' }));

  const token = screen.getByTestId('assistant-provider-credential');
  await waitFor(() => expect(token).toHaveValue('sk-existing-token'));
  expect(token).toHaveAttribute('type', 'password');
  expect(screen.getByTestId('assistant-provider-credential-status')).toHaveTextContent('当前 API Token 已回填到密码框并以掩码显示');
  fireEvent.change(screen.getByTestId('assistant-provider-name'), { target: { value: 'Qwen 已更新' } });
  fireEvent.click(screen.getByTestId('assistant-models-save'));

  await waitFor(() => expect(savePlatformModelService).toHaveBeenCalledWith(
    expect.objectContaining({ name: 'Qwen 已更新', secretReference: 'sk-existing-token' }),
    expect.objectContaining({ id: 'provider-1', revision: 2 }),
  ));
});
it('blocks provider edits until the saved token has been read successfully', async () => {
  const provider = {
    id: 'provider-1', name: 'Qwen', revision: 2, enabled: true,
    models: [{ id: 'qwen3.8-max', purpose: 'chat' as const, supportsTools: false }],
    settings: { name: 'Qwen', baseUrl: 'https://models.example.test/v1', secretReference: '[configured]', secretVersion: 'v1', timeoutSeconds: 60, models: [{ id: 'qwen3.8-max', purpose: 'chat' as const, supportsTools: false }] },
  };
  vi.mocked(listPlatformModelServices).mockResolvedValue([provider]);
  vi.mocked(getPlatformModelServiceForEdit).mockRejectedValueOnce(new Error('temporary read failure'));
  render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><AssistantModelsPage mode="platform" /></QueryClientProvider>);
  fireEvent.mouseDown(screen.getByTestId('assistant-models-tab-providers'), { button: 0, ctrlKey: false });
  fireEvent.click(await screen.findByRole('button', { name: '编辑 Qwen' }));

  expect(await screen.findByTestId('assistant-provider-credential-error')).toHaveTextContent('无法读取已保存的 API Token');
  expect(screen.getByTestId('assistant-models-save')).toBeDisabled();
  fireEvent.change(screen.getByTestId('assistant-provider-credential'), { target: { value: 'sk-replacement-token' } });
  expect(screen.getByTestId('assistant-models-save')).toBeEnabled();
  fireEvent.change(screen.getByTestId('assistant-provider-credential'), { target: { value: '' } });
  expect(screen.getByTestId('assistant-models-save')).toBeDisabled();
  fireEvent.click(screen.getByRole('button', { name: '重试' }));
  await waitFor(() => expect(screen.getByTestId('assistant-provider-credential')).toHaveValue('sk-existing-token'));
  expect(screen.getByTestId('assistant-models-save')).toBeEnabled();
  expect(savePlatformModelService).not.toHaveBeenCalled();
});
it('explains how to fix an unavailable model service when saving a policy', async () => {
  vi.mocked(listAvailableModelServices).mockResolvedValue([{
    id: 'provider-1', name: 'Qwen', revision: 10, enabled: true,
    models: [{ id: 'qwen3.8-max', purpose: 'chat', supportsTools: false }], settings: null,
  }]);
  vi.mocked(saveBusinessPolicy).mockRejectedValue(new ApiError(1002, 'assistant_provider_unavailable'));
  render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><AssistantModelsPage /></QueryClientProvider>);
  fireEvent.click(await screen.findByTestId('assistant-policy-create'));
  const provider = screen.getByTestId('assistant-policy-provider');
  await waitFor(() => expect(provider.querySelector('option[value="provider-1"]')).not.toBeNull());
  fireEvent.change(provider, { target: { value: 'provider-1' } });
  fireEvent.change(screen.getByTestId('assistant-policy-name'), { target: { value: '客服策略' } });
  fireEvent.click(screen.getByTestId('assistant-models-save'));
  expect(await screen.findByRole('alert')).toHaveTextContent('所选模型服务已停用、不存在或所选版本不可用，因此策略未保存。');
  expect(screen.getByTestId('assistant-policy-name')).toHaveValue('客服策略');
});
it.each([
  ['assistant_provider_disabled', '所选模型服务已停用'],
  ['assistant_provider_revision_unavailable', '所选模型服务版本已不可用'],
])('explains %s when saving a policy', async (code, explanation) => {
  vi.mocked(listAvailableModelServices).mockResolvedValue([{
    id: 'provider-1', name: 'Qwen', revision: 10, enabled: true,
    models: [{ id: 'qwen3.8-max', purpose: 'chat', supportsTools: false }], settings: null,
  }]);
  vi.mocked(saveBusinessPolicy).mockRejectedValue(new ApiError(1002, code));
  render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><AssistantModelsPage /></QueryClientProvider>);
  fireEvent.click(await screen.findByTestId('assistant-policy-create'));
  const provider = screen.getByTestId('assistant-policy-provider');
  await waitFor(() => expect(provider.querySelector('option[value="provider-1"]')).not.toBeNull());
  fireEvent.change(provider, { target: { value: 'provider-1' } });
  fireEvent.change(screen.getByTestId('assistant-policy-name'), { target: { value: '客服策略' } });
  fireEvent.click(screen.getByTestId('assistant-models-save'));
  expect(await screen.findByRole('alert')).toHaveTextContent(explanation);
});
it('requires an explicit embedding dimension and clears it when changing purpose', async () => {
  render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><AssistantModelsPage mode="platform" /></QueryClientProvider>);
  fireEvent.mouseDown(screen.getByTestId('assistant-models-tab-providers'), { button: 0, ctrlKey: false });
  fireEvent.click(await screen.findByTestId('assistant-provider-create'));
  fireEvent.change(screen.getByTestId('assistant-provider-name'), { target: { value: '协议验收服务' } });
  fireEvent.change(screen.getByTestId('assistant-provider-endpoint'), { target: { value: 'https://fixture.test/v1' } });
  fireEvent.change(screen.getByTestId('assistant-provider-credential'), { target: { value: 'env://FIXTURE_ONLY' } });
  fireEvent.change(screen.getByTestId('assistant-provider-model-0'), { target: { value: 'embedding-fixture' } });
  fireEvent.change(screen.getByTestId('assistant-provider-purpose-0'), { target: { value: 'embedding' } });
  const dimension = screen.getByTestId('assistant-provider-dimension-0');
  expect(dimension).toBeRequired();
  expect(dimension).toHaveAttribute('min', '1');
  expect(dimension).toHaveAttribute('max', '32768');
  expect((dimension as HTMLInputElement).checkValidity()).toBe(false);
  fireEvent.change(dimension, { target: { value: '1024' } });
  fireEvent.click(screen.getByTestId('assistant-models-save'));
  await waitFor(() => expect(savePlatformModelService).toHaveBeenCalledWith(expect.objectContaining({ models: [expect.objectContaining({ purpose: 'embedding', dimension: 1024 })] }), undefined));
  fireEvent.click(await screen.findByTestId('assistant-provider-create'));
  fireEvent.change(screen.getByTestId('assistant-provider-purpose-0'), { target: { value: 'embedding' } });
  fireEvent.change(screen.getByTestId('assistant-provider-dimension-0'), { target: { value: '1024' } });
  fireEvent.change(screen.getByTestId('assistant-provider-purpose-0'), { target: { value: 'chat' } });
  expect(screen.queryByTestId('assistant-provider-dimension-0')).not.toBeInTheDocument();
  fireEvent.change(screen.getByTestId('assistant-provider-purpose-0'), { target: { value: 'embedding' } });
  expect(screen.getByTestId('assistant-provider-dimension-0')).toHaveValue(null);
});
it('shows immediate business identifier format feedback and clears it for a valid value', async () => {
  render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><AssistantModelsPage /></QueryClientProvider>);
  fireEvent.click(await screen.findByTestId('assistant-policy-create'));
  const businessCode = screen.getByTestId('assistant-policy-business');
  expect(businessCode).toHaveAttribute('pattern', '[a-z][a-z0-9_-]{0,79}');
  fireEvent.change(businessCode, { target: { value: 'Customer Service' } });
  expect(await screen.findByRole('alert')).toHaveTextContent('格式不正确：首位须为小写英文字母，其余只能是小写字母、数字、下划线或连字符，最多 80 位。');
  expect(businessCode).toHaveAttribute('aria-invalid', 'true');
  fireEvent.submit(businessCode.closest('form')!);
  expect(saveBusinessPolicy).not.toHaveBeenCalled();

  fireEvent.change(businessCode, { target: { value: 'customer_service' } });
  expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  expect(businessCode).toHaveAttribute('aria-invalid', 'false');

  fireEvent.change(businessCode, { target: { value: '' } });
  expect(await screen.findByRole('alert')).toHaveTextContent('请输入业务标识。');
});
