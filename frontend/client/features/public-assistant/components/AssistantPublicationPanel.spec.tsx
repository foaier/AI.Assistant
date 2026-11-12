import '@testing-library/jest-dom/vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { ApiError } from '@/lib/ApiClient';
import { publicAssistantMessages } from '@/i18n/publicAssistantMessages';
import { AssistantPublicationPanel } from './AssistantPublicationPanel';
import { getAssistantPublication, publishAssistant, revokeAssistant, type Assistant } from '../services/publicAssistantService';

vi.mock('@/context/i18nContext', () => ({
  useI18n: () => ({
    t: (key: string) => (publicAssistantMessages.zhCN as Record<string, string>)[key] ?? key,
  }),
}));

vi.mock('../services/publicAssistantService', () => ({
  getAssistantPublication: vi.fn(),
  publishAssistant: vi.fn(),
  revokeAssistant: vi.fn(),
}));

const assistant = { id: 'assistant-1', name: '客服', draftRevision: 2, configuration: {} } as Assistant;

beforeEach(() => {
  vi.mocked(getAssistantPublication).mockResolvedValue({ pointerRevision: 0, published: false, assistantKey: 'assistant-1', versionNo: 0 });
  vi.mocked(publishAssistant).mockResolvedValue(undefined);
  vi.mocked(revokeAssistant).mockResolvedValue(undefined);
});
afterEach(() => { cleanup(); vi.resetAllMocks(); });

it('explains that the linked knowledge base must finish indexing before publish', async () => {
  vi.mocked(publishAssistant).mockRejectedValue(new ApiError(1004, 'assistant_knowledge_index_not_ready'));
  render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><AssistantPublicationPanel assistantId="assistant-1" save={async () => assistant} /></QueryClientProvider>);
  fireEvent.click(await screen.findByTestId('assistant-publish'));
  fireEvent.click(screen.getByTestId('assistant-publication-confirm'));
  expect(await screen.findByRole('alert')).toHaveTextContent('关联知识库尚未就绪。请到「知识库」的「处理规则」选择文档理解模型，并在「文档知识」完成全部文档整理；显示“已就绪”后再发布。');
});

it('keeps a generic message for unknown publication failures', async () => {
  vi.mocked(publishAssistant).mockRejectedValue(new ApiError(1004, 'unknown_publication_failure'));
  render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><AssistantPublicationPanel assistantId="assistant-1" save={async () => assistant} /></QueryClientProvider>);
  fireEvent.click(await screen.findByTestId('assistant-publish'));
  fireEvent.click(screen.getByTestId('assistant-publication-confirm'));
  await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('发布失败，请检查助手设置或刷新页面后重试。'));
});

it('uses a separate fallback when unpublishing fails', async () => {
  vi.mocked(getAssistantPublication).mockResolvedValue({ pointerRevision: 1, published: true, assistantKey: 'assistant-1', versionNo: 1 });
  vi.mocked(revokeAssistant).mockRejectedValue(new ApiError(1004, 'unknown_revoke_failure'));
  render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><AssistantPublicationPanel assistantId="assistant-1" save={async () => assistant} /></QueryClientProvider>);
  fireEvent.click(await screen.findByTestId('assistant-revoke'));
  fireEvent.click(screen.getByTestId('assistant-publication-confirm'));
  expect(await screen.findByRole('alert')).toHaveTextContent('撤回发布失败，请刷新发布状态后重试。');
});

it('validates settings before opening the publish dialog and allows retry after correction', async () => {
  const validate = vi.fn().mockResolvedValue(false);
  const save = vi.fn().mockResolvedValue(assistant);
  render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><AssistantPublicationPanel assistantId="assistant-1" save={save} validate={validate} /></QueryClientProvider>);
  fireEvent.click(await screen.findByTestId('assistant-publish'));
  await waitFor(() => expect(validate).toHaveBeenCalledOnce());
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  expect(save).not.toHaveBeenCalled();
  expect(publishAssistant).not.toHaveBeenCalled();
  validate.mockResolvedValue(true);
  fireEvent.click(screen.getByTestId('assistant-publish'));
  await screen.findByRole('dialog');
  fireEvent.click(screen.getByTestId('assistant-publication-confirm'));
  await waitFor(() => expect(publishAssistant).toHaveBeenCalledWith('assistant-1', 2, 0));
});

it('prevents repeated publish clicks while settings validation is pending', async () => {
  let finishValidation!: (valid: boolean) => void;
  const validate = vi.fn(() => new Promise<boolean>(resolve => { finishValidation = resolve; }));
  render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><AssistantPublicationPanel assistantId="assistant-1" save={async () => assistant} validate={validate} /></QueryClientProvider>);
  const publish = await screen.findByTestId('assistant-publish');
  await waitFor(() => expect(publish).toBeEnabled());
  fireEvent.click(publish);
  await waitFor(() => expect(publish).toBeDisabled());
  fireEvent.click(publish);
  expect(validate).toHaveBeenCalledOnce();
  finishValidation(false);
  await waitFor(() => expect(publish).toBeEnabled());
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
});
