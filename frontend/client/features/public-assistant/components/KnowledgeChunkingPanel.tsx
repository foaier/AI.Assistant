import { useEffect, useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Eye, RotateCcw, Save } from 'lucide-react';

import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Switch } from '@/components/ui/switch';

import {
  getKnowledgeChunkingPolicy,
  listKnowledgeDocuments,
  previewKnowledgeChunking,
  saveKnowledgeChunkingPolicy,
  type AssistantKnowledgeChunkingMode,
  type AssistantKnowledgeChunkingPolicy,
} from '../services/publicAssistantService';

type PolicyForm = Omit<AssistantKnowledgeChunkingPolicy, 'revision' | 'hasPendingDocuments'>;

const defaults: PolicyForm = {
  mode: 2,
  maxChunkSize: 1200,
  minChunkSize: 200,
  overlapSize: 160,
  maxHeadingLevel: 3,
  preserveHeadingPath: true,
  mergeShortChunks: true,
};

function toMode(value: AssistantKnowledgeChunkingMode): 0 | 1 | 2 {
  if (typeof value === 'number') return value as 0 | 1 | 2;
  return value.toLowerCase() === 'heading' ? 0 : value.toLowerCase() === 'fixed' ? 1 : 2;
}

export function KnowledgeChunkingPanel({ knowledgeBaseId }: { knowledgeBaseId: string }) {
  const client = useQueryClient();
  const policy = useQuery({ queryKey: ['assistant-knowledge-chunking-policy', knowledgeBaseId], queryFn: () => getKnowledgeChunkingPolicy(knowledgeBaseId), enabled: Boolean(knowledgeBaseId) });
  const documents = useQuery({ queryKey: ['assistant-knowledge-documents', knowledgeBaseId], queryFn: () => listKnowledgeDocuments(knowledgeBaseId), enabled: Boolean(knowledgeBaseId) });
  const [form, setForm] = useState<PolicyForm>(defaults);
  const [selectedDocumentId, setSelectedDocumentId] = useState('');
  const [confirmReprocess, setConfirmReprocess] = useState(false);

  useEffect(() => {
    if (!policy.data) return;
    setForm({ ...policy.data, mode: toMode(policy.data.mode) });
  }, [policy.data]);

  const selectedDocument = useMemo(() => documents.data?.find((item) => item.id === selectedDocumentId) ?? documents.data?.[0], [documents.data, selectedDocumentId]);
  const preview = useMutation({ mutationFn: () => previewKnowledgeChunking(knowledgeBaseId, selectedDocument?.id ?? '', form) });
  const save = useMutation({
    mutationFn: (reprocessDocuments: boolean) => saveKnowledgeChunkingPolicy(knowledgeBaseId, { ...form, reprocessDocuments }),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['assistant-knowledge-chunking-policy', knowledgeBaseId] });
      void client.invalidateQueries({ queryKey: ['assistant-knowledge-documents', knowledgeBaseId] });
      void client.invalidateQueries({ queryKey: ['assistant-knowledge-embedding', knowledgeBaseId] });
      void client.invalidateQueries({ queryKey: ['assistant-knowledge-document-preview', knowledgeBaseId] });
    },
  });

  const update = <K extends keyof PolicyForm>(key: K, value: PolicyForm[K]) => {
    setForm((current) => ({ ...current, [key]: value }));
    // A preview belongs to the exact rule set it was generated from. Clear it as soon as
    // a rule changes so stale content cannot be mistaken for the new result.
    preview.reset();
  };
  const invalid = form.maxChunkSize < 200 || form.maxChunkSize > 8000 || form.minChunkSize < 0 || form.minChunkSize > form.maxChunkSize || form.overlapSize < 0 || form.overlapSize >= form.maxChunkSize;

  return <div className="space-y-5" data-testid="knowledge-chunking-panel">
    {policy.isError ? <div className="flex flex-wrap items-center justify-between gap-3 rounded-md border border-dashed p-4 text-sm" role="alert"><span>处理规则加载失败，请重试。</span><Button type="button" variant="outline" onClick={() => void policy.refetch()}>重试</Button></div> : null}
    {documents.isError ? <div className="flex flex-wrap items-center justify-between gap-3 rounded-md border border-dashed p-4 text-sm" role="alert"><span>预览文档加载失败，请重试。</span><Button type="button" variant="outline" onClick={() => void documents.refetch()}>重试</Button></div> : null}
    <div className="rounded-lg border border-[var(--border-subtle)] bg-[var(--surface-neutral-muted)] p-4 text-sm">
      <div className="flex flex-wrap items-start justify-between gap-3"><div><div className="font-medium">文档处理规则</div><div className="mt-1 text-[var(--text-secondary)]">规则作用于整个知识库。修改后，已整理文档需要重新整理才会更新检索结果。</div></div>{policy.data?.hasPendingDocuments ? <Badge variant="warning">有文档等待整理</Badge> : <Badge variant="success">规则已生效</Badge>}</div>
    </div>
    <div className="grid gap-3 md:grid-cols-3">
      {[{ value: 0 as const, label: '按标题结构', description: '按标题层级组织内容' }, { value: 1 as const, label: '按内容大小', description: '按长度拆分并保留重叠' }, { value: 2 as const, label: '智能混合', description: '先按标题，再处理超长内容' }].map((item) => <button key={item.value} type="button" onClick={() => update('mode', item.value)} aria-pressed={form.mode === item.value} className={`rounded-lg border p-4 text-left transition ${form.mode === item.value ? 'border-[var(--brand-primary)] bg-[var(--surface-brand-muted)]' : 'border-[var(--border-subtle)]'}`} data-testid={`knowledge-chunking-mode-${item.value}`}><div className="font-medium">{item.label}</div><div className="mt-1 text-xs text-[var(--text-subtle)]">{item.description}</div></button>)}
    </div>
    <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
      <label className="space-y-2 text-sm"><span>最大内容长度</span><Input type="number" min={200} max={8000} value={form.maxChunkSize} onChange={(event) => update('maxChunkSize', Number(event.target.value))} data-testid="knowledge-chunking-max-size" /></label>
      <label className="space-y-2 text-sm"><span>最小内容长度</span><Input type="number" min={0} max={form.maxChunkSize} value={form.minChunkSize} onChange={(event) => update('minChunkSize', Number(event.target.value))} data-testid="knowledge-chunking-min-size" /></label>
      <label className="space-y-2 text-sm"><span>重叠长度</span><Input type="number" min={0} max={Math.max(0, form.maxChunkSize - 1)} value={form.overlapSize} onChange={(event) => update('overlapSize', Number(event.target.value))} data-testid="knowledge-chunking-overlap" /></label>
      <label className="space-y-2 text-sm"><span>标题层级</span><Input type="number" min={1} max={6} value={form.maxHeadingLevel} onChange={(event) => update('maxHeadingLevel', Number(event.target.value))} disabled={form.mode === 1} data-testid="knowledge-chunking-heading-level" /></label>
    </div>
    <div className="grid gap-3 sm:grid-cols-2"><label className="flex items-center justify-between rounded-lg border border-[var(--border-subtle)] p-3 text-sm"><span>保留标题路径</span><Switch checked={form.preserveHeadingPath} onCheckedChange={(checked) => update('preserveHeadingPath', checked)} disabled={form.mode === 1} data-testid="knowledge-chunking-heading-path" /></label><label className="flex items-center justify-between rounded-lg border border-[var(--border-subtle)] p-3 text-sm"><span>合并过短内容</span><Switch checked={form.mergeShortChunks} onCheckedChange={(checked) => update('mergeShortChunks', checked)} data-testid="knowledge-chunking-merge-short" /></label></div>
    {invalid ? <div className="text-sm text-[var(--text-danger)]" role="alert">请检查长度设置：最大长度需在 200 到 8000 之间，最小长度和重叠长度不能超过最大长度。</div> : null}
    <div className="rounded-lg border border-[var(--border-subtle)] p-4"><div className="flex flex-wrap items-center justify-between gap-3"><div><div className="font-medium">切片效果预览</div><div className="mt-1 text-sm text-[var(--text-subtle)]">预览不会保存规则，也不会更新检索索引。</div></div><div className="flex flex-wrap items-end gap-2"><label className="space-y-1 text-sm"><span>预览文档</span><select className="h-10 rounded-md border border-[var(--border-subtle)] bg-transparent px-3 text-sm" aria-label="选择预览文档" value={selectedDocument?.id ?? ''} onChange={(event) => { setSelectedDocumentId(event.target.value); preview.reset(); }} data-testid="knowledge-chunking-document"><option value="">选择文档</option>{documents.data?.map((item) => <option key={item.id} value={item.id}>{item.fileName}</option>)}</select></label><Button type="button" variant="outline" disabled={!selectedDocument || invalid || preview.isPending} onClick={() => preview.mutate()} data-testid="knowledge-chunking-preview"><Eye className="mr-2 h-4 w-4" />{preview.isPending ? '预览中…' : '预览切片'}</Button></div></div>{preview.error ? <div className="mt-3 text-sm text-[var(--text-danger)]" role="alert">切片预览失败，请检查文档状态后重试。</div> : null}{preview.data ? <div className="mt-4 space-y-3"><div className="text-sm text-[var(--text-secondary)]">预计生成 {preview.data.estimatedChunkCount} 段内容</div>{preview.data.segments.map((segment) => <div key={segment.id} className="rounded-md bg-[var(--surface-neutral-muted)] p-3"><div className="mb-1 text-xs text-[var(--text-subtle)]">内容段 {segment.index + 1}</div><div className="whitespace-pre-wrap text-sm leading-6">{segment.text}</div></div>)}</div> : <div className="mt-4 rounded-md border border-dashed p-5 text-center text-sm text-[var(--text-subtle)]">选择一份文档后查看切片效果。</div>}</div>
    <div className="flex flex-wrap justify-end gap-2"><Button type="button" variant="outline" disabled={invalid || save.isPending} onClick={() => { if (policy.data) setForm({ ...policy.data, mode: toMode(policy.data.mode) }); }}><RotateCcw className="mr-2 h-4 w-4" />恢复已保存</Button><Button type="button" variant="outline" disabled={invalid || save.isPending} onClick={() => save.mutate(false)} data-testid="knowledge-chunking-save"><Save className="mr-2 h-4 w-4" />保存规则</Button><Button type="button" disabled={invalid || save.isPending} onClick={() => setConfirmReprocess(true)} data-testid="knowledge-chunking-save-reprocess">保存并重新整理文档</Button></div>
    {confirmReprocess ? <div className="rounded-lg border border-[var(--border-subtle)] bg-[var(--surface-neutral-muted)] p-4 text-sm" role="alertdialog" aria-label="确认重新整理文档"><div className="font-medium">确认重新整理文档？</div><p className="mt-1 text-[var(--text-secondary)]">这会为现有文档创建新的内容版本并重新建立检索索引，旧版本仍会保留。</p><div className="mt-3 flex justify-end gap-2"><Button type="button" variant="outline" onClick={() => setConfirmReprocess(false)}>取消</Button><Button type="button" onClick={() => { setConfirmReprocess(false); save.mutate(true); }} data-testid="knowledge-chunking-confirm-reprocess">确认整理</Button></div></div> : null}
    {save.isSuccess ? <div className="text-sm text-[var(--text-success)]" role="status">处理规则已保存。</div> : null}
    {save.isError ? <div className="text-sm text-[var(--text-danger)]" role="alert">规则保存失败，请稍后重试。</div> : null}
  </div>;
}
