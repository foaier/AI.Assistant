// Spec: specs/136-public-assistant/requests/REQ-20261009-011-knowledge-embedding-profile-and-index-readiness.md
import '@testing-library/jest-dom/vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, expect, it, vi } from 'vitest';
import { KnowledgeDocumentsPanel, KnowledgeContentLookup } from './KnowledgeDocumentsPanel';
import { getKnowledgeDocumentPreview, listKnowledgeDocuments, testKnowledgeRetrieval, uploadKnowledgeDocument } from '../services/publicAssistantService';
import { publicAssistantMessages } from '@/i18n/publicAssistantMessages';
vi.mock('@/context/i18nContext', () => ({ useI18n: () => ({ t: (key: string) => (publicAssistantMessages.zhCN as Record<string, string>)[key] ?? key }) }));
vi.mock('../services/publicAssistantService', () => ({ listKnowledgeDocuments: vi.fn(), testKnowledgeRetrieval: vi.fn(), getKnowledgeDocumentPreview: vi.fn(), processKnowledgeDocument: vi.fn(), uploadKnowledgeDocument: vi.fn() }));
function setup(content: React.ReactNode) {
  return render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>{content}</QueryClientProvider>);
}
afterEach(() => { cleanup(); vi.resetAllMocks(); });

it.each([
  ['assistant_embedding_provider_failed', '模型服务未能完成文档整理'],
  ['https://secret.internal supplier-private-detail', '文档整理未完成'],
])('shows safe retry guidance for a failed document (%s)', async (lastErrorCode, message) => {
  vi.mocked(listKnowledgeDocuments).mockResolvedValue([{ id: 'failed-doc', knowledgeBaseId: 'kb', assetId: 'asset', fileName: 'guide.md', contentType: 'text/markdown', sizeBytes: 16559, currentVersionNo: 1, status: 3, indexStatus: 'failed', chunkCount: 0, lastErrorCode, updatedAt: '2026-11-09' }]);
  setup(<KnowledgeDocumentsPanel knowledgeBaseId="kb" />);
  expect(await screen.findByText(new RegExp(message))).toHaveTextContent('请重试');
  expect(screen.getByTestId('knowledge-document-process-failed-doc')).toBeEnabled();
  expect(document.body.textContent).not.toContain('检查模型配置');
  expect(document.body.textContent).not.toContain('secret.internal');
  expect(document.body.textContent).not.toContain('supplier-private-detail');
});

function selectDocument(sizeBytes: number, fileName = 'guide.docx') {
  const file = new File(['document'], fileName);
  Object.defineProperty(file, 'size', { value: sizeBytes });
  fireEvent.change(screen.getByTestId('knowledge-document-file'), { target: { files: [file] } });
  return file;
}

it('shows a 10 MB document upload limit in both supported source locales', async () => {
  vi.mocked(listKnowledgeDocuments).mockResolvedValue([]);
  setup(<KnowledgeDocumentsPanel knowledgeBaseId="kb" />);
  expect(await screen.findByText(/支持 TXT、Markdown、JSON、DOCX，最大 10 MB/)).toBeInTheDocument();
  expect(publicAssistantMessages.enUS['publicAssistant.p0.uploadHelp']).toContain('up to 10 MB');
});

it.each([10 * 1024 * 1024 + 1, 50 * 1024 * 1024])('blocks a %i-byte document with a size warning before upload', async (sizeBytes) => {
  vi.mocked(listKnowledgeDocuments).mockResolvedValue([]);
  setup(<KnowledgeDocumentsPanel knowledgeBaseId="kb" />);
  selectDocument(sizeBytes);
  expect(await screen.findByRole('alert')).toHaveTextContent('文件大小不能超过 10 MB');
  expect(screen.getByRole('alert')).toHaveClass('text-[var(--text-danger)]');
  expect(uploadKnowledgeDocument).not.toHaveBeenCalled();
  expect(screen.getByTestId('knowledge-document-upload')).toBeEnabled();
});

it.each([10 * 1024 * 1024 - 1, 10 * 1024 * 1024])('allows a %i-byte document to upload', async (sizeBytes) => {
  vi.mocked(listKnowledgeDocuments).mockResolvedValue([]);
  setup(<KnowledgeDocumentsPanel knowledgeBaseId="kb" />);
  const file = selectDocument(sizeBytes);
  await waitFor(() => expect(uploadKnowledgeDocument).toHaveBeenCalledWith('kb', file));
  expect(screen.queryByRole('alert')).not.toBeInTheDocument();
});

it('clears the size warning when a valid document is selected next', async () => {
  vi.mocked(listKnowledgeDocuments).mockResolvedValue([]);
  setup(<KnowledgeDocumentsPanel knowledgeBaseId="kb" />);
  selectDocument(10 * 1024 * 1024 + 1);
  expect(await screen.findByRole('alert')).toHaveTextContent('文件大小不能超过 10 MB');
  const file = selectDocument(1024);
  await waitFor(() => expect(uploadKnowledgeDocument).toHaveBeenCalledWith('kb', file));
  expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  expect(screen.getByTestId('knowledge-document-file')).toHaveValue('');
});

it.each([[0, 'empty.docx'], [1024, 'unsupported.pdf']] as const)('still blocks invalid documents (%i bytes, %s)', async (sizeBytes, fileName) => {
  vi.mocked(listKnowledgeDocuments).mockResolvedValue([]);
  setup(<KnowledgeDocumentsPanel knowledgeBaseId="kb" />);
  selectDocument(sizeBytes, fileName);
  expect(await screen.findByRole('alert')).toHaveTextContent('上传失败，请检查文件格式和大小后重试。');
  expect(uploadKnowledgeDocument).not.toHaveBeenCalled();
});

it('never labels parsed content as available without a successful index', async () => {
  vi.mocked(listKnowledgeDocuments).mockResolvedValue([{ id: 'd1', knowledgeBaseId: 'kb', assetId: 'asset', fileName: 'guide.md', contentType: 'text/markdown', sizeBytes: 20, currentVersionNo: 1, status: 2, indexStatus: 'needs_embedding_config', chunkCount: 3, updatedAt: '2026-10-09' }]);
  setup(<KnowledgeDocumentsPanel knowledgeBaseId="kb" />);
  expect(await screen.findByTestId('knowledge-document-state-d1')).toHaveTextContent('内容已分段 · 待选择模型');
  expect(screen.queryByText('可用于回答')).not.toBeInTheDocument();
});
const lookupPreview = {
  documentId: 'd1', fileName: 'faq.md', contentType: 'text/markdown', status: 2,
  originalText: '', structure: [],
  segments: [{
    id: 'segment-1', index: 0, sourceLabel: 'faq.md · 1', available: false,
    text: `${'开场说明。'.repeat(40)}\n怎么报名参加会展？\n标准答案：请通过官方入口在线报名。\n${'文档末尾无需展示。'.repeat(40)}`,
  }],
};

it('searches literal text and returns only a matching excerpt without embedding readiness', async () => {
  vi.mocked(getKnowledgeDocumentPreview).mockResolvedValue(lookupPreview);
  setup(<KnowledgeContentLookup knowledgeBaseId="kb" documentId="d1" />);
  fireEvent.change(screen.getByTestId('knowledge-content-lookup-query'), { target: { value: '怎么报名' } });
  await waitFor(() => expect(screen.getByTestId('knowledge-content-lookup-run')).toBeEnabled());
  fireEvent.click(screen.getByTestId('knowledge-content-lookup-run'));

  const result = await screen.findByTestId('knowledge-content-lookup-result');
  expect(result).toHaveTextContent('怎么报名参加会展？');
  expect(result).toHaveTextContent('标准答案：请通过官方入口在线报名。');
  expect(result).not.toHaveTextContent('开场说明。');
  expect(result).not.toHaveTextContent('文档末尾无需展示。');
  expect(testKnowledgeRetrieval).not.toHaveBeenCalled();
});

it('shows an empty result instead of the document when the literal text is absent', async () => {
  vi.mocked(getKnowledgeDocumentPreview).mockResolvedValue(lookupPreview);
  setup(<KnowledgeContentLookup knowledgeBaseId="kb" documentId="d1" />);
  fireEvent.change(screen.getByTestId('knowledge-content-lookup-query'), { target: { value: '不存在的关键词' } });
  await waitFor(() => expect(screen.getByTestId('knowledge-content-lookup-run')).toBeEnabled());
  fireEvent.click(screen.getByTestId('knowledge-content-lookup-run'));

  const result = await screen.findByTestId('knowledge-content-lookup-result');
  expect(result).toHaveTextContent('没有找到匹配内容');
  expect(result).not.toHaveTextContent('怎么报名参加会展？');
  expect(testKnowledgeRetrieval).not.toHaveBeenCalled();
});
