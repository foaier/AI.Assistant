// Spec: specs/136-public-assistant/results/P0-interaction-spec.md §7, §10C.
import '@testing-library/jest-dom/vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { KnowledgeQnaPanel } from './KnowledgeQnaPanel';
import { listKnowledgeQna, saveKnowledgeQna, testKnowledgeQna } from '../services/publicAssistantService';
import { publicAssistantMessages } from '@/i18n/publicAssistantMessages';
vi.mock('@/context/i18nContext', () => ({ useI18n: () => ({ t: (key: string) => (publicAssistantMessages.zhCN as Record<string, string>)[key] ?? key }) }));
vi.mock('../services/publicAssistantService', () => ({ listKnowledgeQna: vi.fn(), saveKnowledgeQna: vi.fn(), testKnowledgeQna: vi.fn() }));
const item = { id: 'q1', knowledgeBaseId: 'kb', question: '如何报名？', answer: '请在首页报名', enabled: true, showAsSuggestion: true, sortOrder: 1, updatedAt: '2026-09-29T00:00:00Z' };
function setup() {
  return render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })}><KnowledgeQnaPanel knowledgeBaseId="kb" /></QueryClientProvider>);
}
beforeEach(() => {
  vi.mocked(listKnowledgeQna).mockResolvedValue([item]);
  vi.mocked(saveKnowledgeQna).mockImplementation(async (_, input) => ({ ...item, ...input, id: input.id || 'new-id' }));
});
afterEach(() => { cleanup(); vi.resetAllMocks(); });
describe('Knowledge Q&A P0', () => {
  it('uses compact rows, search and filters rather than editable forms', async () => {
    setup();
    await screen.findByText('如何报名？');
    expect(screen.queryByTestId('knowledge-qna-question')).not.toBeInTheDocument();
    fireEvent.change(screen.getByTestId('knowledge-qna-search'), { target: { value: '不存在' } });
    expect(screen.getByText('没有符合条件的问题，请调整搜索或筛选。')).toBeInTheDocument();
    fireEvent.change(screen.getByTestId('knowledge-qna-search'), { target: { value: '' } });
    fireEvent.change(screen.getByTestId('knowledge-qna-filter'), { target: { value: 'disabled' } });
    expect(screen.queryByText('如何报名？')).not.toBeInTheDocument();
  });
  it('allows optional answers and persists suggestion state and sort', async () => {
    setup(); await screen.findByText('如何报名？');
    fireEvent.click(screen.getByTestId('knowledge-qna-add'));
    fireEvent.change(screen.getByTestId('knowledge-qna-question'), { target: { value: '活动时间' } });
    expect(screen.queryByTestId('knowledge-qna-sort')).not.toBeInTheDocument();
    fireEvent.click(screen.getByTestId('knowledge-qna-suggestion-toggle'));
    fireEvent.change(screen.getByTestId('knowledge-qna-sort'), { target: { value: '7' } });
    fireEvent.click(screen.getByTestId('knowledge-qna-save'));
    await waitFor(() => expect(saveKnowledgeQna).toHaveBeenCalledWith('kb', expect.objectContaining({ question: '活动时间', answer: '', showAsSuggestion: true, sortOrder: 7 })));
    await waitFor(() => expect(screen.queryByTestId('knowledge-qna-editor')).not.toBeInTheDocument());
  });
  it('blocks normalized duplicates without losing edits', async () => {
    setup(); await screen.findByText('如何报名？');
    fireEvent.click(screen.getByTestId('knowledge-qna-add'));
    fireEvent.change(screen.getByTestId('knowledge-qna-question'), { target: { value: '  如何报名?  ' } });
    fireEvent.click(screen.getByTestId('knowledge-qna-save'));
    expect(screen.getByRole('alert')).toHaveTextContent('这个问题已存在');
    expect(saveKnowledgeQna).not.toHaveBeenCalled();
  });
  it('preserves edits on failed save and confirms before closing', async () => {
    vi.mocked(saveKnowledgeQna).mockRejectedValue(new Error('offline'));
    setup(); await screen.findByText('如何报名？');
    fireEvent.click(screen.getByTestId('knowledge-qna-edit-q1'));
    fireEvent.change(screen.getByTestId('knowledge-qna-answer'), { target: { value: '新回答' } });
    fireEvent.click(screen.getByTestId('knowledge-qna-save'));
    await screen.findByText('保存失败，编辑内容已保留，请重试。');
    expect(screen.getByTestId('knowledge-qna-answer')).toHaveValue('新回答');
    fireEvent.click(screen.getByRole('button', { name: '取消' }));
    expect(screen.getByText('有未保存的修改，是否放弃？')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: '继续编辑' }));
    expect(screen.getByTestId('knowledge-qna-answer')).toHaveValue('新回答');
  });
  it('tests a saved record and does not pretend to generate an AI answer', async () => {
    vi.mocked(testKnowledgeQna).mockResolvedValue({ question: '如何报名？', matched: true, answer: null, answerMode: 'generated', providerInvoked: false });
    setup(); await screen.findByText('如何报名？');
    fireEvent.click(screen.getByTestId('knowledge-qna-test-q1'));
    fireEvent.click(screen.getByTestId('knowledge-qna-test-run'));
    expect(await screen.findByTestId('knowledge-qna-test-result')).toHaveTextContent('尚未生成回答');
    expect(screen.getByTestId('knowledge-qna-test-result')).toHaveTextContent('未调用 AI');
  });
});
