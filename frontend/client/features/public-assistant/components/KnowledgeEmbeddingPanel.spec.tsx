// Spec: specs/136-public-assistant/requests/REQ-20261009-011-knowledge-embedding-profile-and-index-readiness.md
import '@testing-library/jest-dom/vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { KnowledgeEmbeddingPanel } from './KnowledgeEmbeddingPanel';
import { getKnowledgeEmbeddingProfile, saveKnowledgeEmbeddingProfile, type KnowledgeEmbeddingProfile } from '../services/publicAssistantService';
import { publicAssistantMessages } from '@/i18n/publicAssistantMessages';
vi.mock('@/context/i18nContext', () => ({ useI18n: () => ({ t: (key: string) => (publicAssistantMessages.zhCN as Record<string, string>)[key] ?? key }) }));
const permissions = vi.hoisted(() => ({ isSuperAdmin: false }));
vi.mock('@/context/permissionContext', () => ({ usePermissions: () => ({ hasPermission: () => true, isSuperAdmin: permissions.isSuperAdmin }) }));
vi.mock('../services/publicAssistantService', () => ({ getKnowledgeEmbeddingProfile: vi.fn(), saveKnowledgeEmbeddingProfile: vi.fn() }));
const model = { providerId: 'p1', providerRevision: 3, providerName: '平台服务', model: 'embedding-v4', dimension: 1024 };
const profile: KnowledgeEmbeddingProfile = { revision: 0, status: 'not_configured', target: null, active: null, activeAvailable: false, documentCount: 1, targetReadyDocumentCount: 0, lastErrorCode: null, availableModels: [model] };
function setup() {
  const cache = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  render(<MemoryRouter><QueryClientProvider client={cache}><KnowledgeEmbeddingPanel knowledgeBaseId="kb" /></QueryClientProvider></MemoryRouter>);
  return cache;
}
beforeEach(() => { permissions.isSuperAdmin = false; vi.mocked(getKnowledgeEmbeddingProfile).mockResolvedValue(profile); });
afterEach(() => { cleanup(); vi.resetAllMocks(); });
describe('Knowledge embedding settings', () => {
  it.each([
    ['assistant_embedding_provider_failed', '模型服务未能完成文档整理'],
    ['https://secret.internal supplier-private-detail', '文档整理未完成'],
  ])('explains processing failure safely for %s', async (lastErrorCode, message) => {
    vi.mocked(getKnowledgeEmbeddingProfile).mockResolvedValue({ ...profile, status: 'failed', lastErrorCode });
    setup();
    expect(await screen.findByText(new RegExp(message))).toHaveTextContent('请重试');
    expect(document.body.textContent).not.toContain('检查模型配置');
    expect(document.body.textContent).not.toContain('secret.internal');
    expect(document.body.textContent).not.toContain('supplier-private-detail');
  });
  it('shows actionable empty state without exposing provider connection data', async () => {
    vi.mocked(getKnowledgeEmbeddingProfile).mockResolvedValue({ ...profile, availableModels: [] });
    setup();
    expect(await screen.findByTestId('knowledge-embedding-empty')).toHaveTextContent('请联系平台管理员接入或启用服务');
    expect(screen.getByTestId('knowledge-embedding-save')).toBeDisabled();
    expect(screen.queryByRole('link')).not.toBeInTheDocument();
  });
  it('offers the platform model management link to super administrators', async () => {
    permissions.isSuperAdmin = true;
    vi.mocked(getKnowledgeEmbeddingProfile).mockResolvedValue({ ...profile, availableModels: [] });
    setup();
    expect(await screen.findByRole('link')).toHaveAttribute('href', '/admin/system/assistant-models');
  });
  it('confirms existing-document impact and saves the exact revision and dimensions', async () => {
    vi.mocked(saveKnowledgeEmbeddingProfile).mockResolvedValue({ ...profile, revision: 1, status: 'pending_rebuild', target: { ...model, generation: 1 } });
    setup(); await screen.findByRole('option', { name: '平台服务 · embedding-v4' });
    fireEvent.change(screen.getByTestId('knowledge-embedding-model'), { target: { value: JSON.stringify(['p1', 3, 'embedding-v4', 1024]) } });
    fireEvent.click(screen.getByTestId('knowledge-embedding-save'));
    expect(saveKnowledgeEmbeddingProfile).not.toHaveBeenCalled();
    expect(screen.getByRole('alertdialog')).toHaveTextContent('保存设置不会立即整理文档');
    fireEvent.click(screen.getByTestId('knowledge-embedding-confirm'));
    await waitFor(() => expect(saveKnowledgeEmbeddingProfile).toHaveBeenCalledWith('kb', { providerId: 'p1', providerRevision: 3, model: 'embedding-v4', dimension: 1024, expectedRevision: 0 }));
    expect(await screen.findByTestId('knowledge-embedding-state')).toHaveTextContent('待整理');
  });
  it('retains a failed selection and sanitizes errors', async () => {
    vi.mocked(saveKnowledgeEmbeddingProfile).mockRejectedValue(new Error('https://secret.internal credential-material'));
    setup(); await screen.findByRole('option', { name: '平台服务 · embedding-v4' });
    const key = JSON.stringify(['p1', 3, 'embedding-v4', 1024]);
    fireEvent.change(screen.getByTestId('knowledge-embedding-model'), { target: { value: key } });
    fireEvent.click(screen.getByTestId('knowledge-embedding-save'));
    fireEvent.click(screen.getByTestId('knowledge-embedding-confirm'));
    expect(await screen.findByRole('alert')).toHaveTextContent('选择已保留');
    expect(screen.getByTestId('knowledge-embedding-model')).toHaveValue(key);
    expect(document.body.textContent).not.toContain('secret.internal');
  });
  it('does not overwrite an unsaved choice during a status-only refresh', async () => {
    const old = { ...model, model: 'old', generation: 1 };
    vi.mocked(getKnowledgeEmbeddingProfile).mockResolvedValue({ ...profile, revision: 4, target: old, active: old, activeAvailable: true, availableModels: [model, { ...model, model: 'old' }] });
    const cache = setup(); await screen.findByRole('option', { name: '平台服务 · embedding-v4' });
    const key = JSON.stringify(['p1', 3, 'embedding-v4', 1024]);
    fireEvent.change(screen.getByTestId('knowledge-embedding-model'), { target: { value: key } });
    cache.setQueryData(['assistant-knowledge-embedding', 'kb'], { ...profile, revision: 4, target: old, active: old, activeAvailable: true, status: 'rebuilding', availableModels: [model] });
    await screen.findByText('整理中');
    expect(screen.getByTestId('knowledge-embedding-model')).toHaveValue(key);
    expect(screen.getByText('新内容整理完成前，继续使用原来可用的内容回答。')).toBeInTheDocument();
  });
  it('reports a disabled active service without promising continued answers', async () => {
    const old = { ...model, generation: 1 };
    vi.mocked(getKnowledgeEmbeddingProfile).mockResolvedValue({ ...profile, revision: 1, status: 'model_unavailable', target: old, active: old, availableModels: [] });
    setup();
    expect(await screen.findByTestId('knowledge-embedding-state')).toHaveTextContent('模型不可用');
    expect(screen.getByText('原内容使用的模型服务已不可用，请联系平台管理员恢复或启用服务后再测试回答。')).toHaveAttribute('role', 'alert');
    expect(screen.queryByText('新内容整理完成前，继续使用原来可用的内容回答。')).not.toBeInTheDocument();
  });
});
