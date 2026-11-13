import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Plus, Trash2 } from 'lucide-react';
import { AdminPageHeading } from '@/components/admin/AdminPageHeading';
import { Button } from '@/components/ui/button';
import { AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent, AlertDialogDescription, AlertDialogFooter, AlertDialogHeader, AlertDialogTitle } from '@/components/ui/alert-dialog';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { useI18n } from '@/context/i18nContext';
import { createKnowledgeBase, deleteKnowledgeBase, listKnowledgeBases, type AssistantKnowledgeBase } from '../services/publicAssistantService';
import { KnowledgeChunkingPanel } from '../components/KnowledgeChunkingPanel';
import { KnowledgeEmbeddingPanel } from '../components/KnowledgeEmbeddingPanel';
import { KnowledgeQnaPanel } from '../components/KnowledgeQnaPanel';
import { KnowledgeDocumentsPanel } from '../components/KnowledgeDocumentsPanel';

export default function PublicAssistantKnowledgePage() {
  const { t } = useI18n();
  const k = (key: string) => t(`publicAssistant.knowledge.${key}`);
  const e = (key: string) => t(`publicAssistant.embedding.${key}`);
  const cache = useQueryClient();
  const [selectedId, setSelectedId] = useState('');
  const [newName, setNewName] = useState('');
  const [tab, setTab] = useState('qna');
  const [deleteTarget, setDeleteTarget] = useState<AssistantKnowledgeBase | null>(null);
  const bases = useQuery({ queryKey: ['assistant-knowledge-bases'], queryFn: listKnowledgeBases });
  const activeId = bases.data?.some((base) => base.id === selectedId) ? selectedId : bases.data?.[0]?.id ?? '';
  const create = useMutation({
    mutationFn: () => createKnowledgeBase({ name: newName.trim() }),
    onSuccess: (value) => {
      setNewName('');
      setSelectedId(value.id);
      void cache.invalidateQueries({ queryKey: ['assistant-knowledge-bases'] });
    },
  });
  const remove = useMutation({
    mutationFn: (base: AssistantKnowledgeBase) => deleteKnowledgeBase(base.id),
    onSuccess: async () => {
      setDeleteTarget(null);
      await cache.invalidateQueries({ queryKey: ['assistant-knowledge-bases'] });
    },
  });
  return <div className="mx-auto min-h-full w-full max-w-[1680px] space-y-5" data-testid="assistant-knowledge-page">
    <AdminPageHeading title={k('title')} subtitle={k('subtitle')} />
    <div className="grid gap-5 lg:grid-cols-[280px_minmax(0,1fr)]">
      <Card className="min-w-0 h-fit shadow-none">
        <CardHeader><CardTitle>{k('bases')}</CardTitle></CardHeader>
        <CardContent className="space-y-3">
          <form className="flex items-end gap-2" onSubmit={(event) => { event.preventDefault(); if (newName.trim() && !create.isPending) create.mutate(); }}>
            <label className="min-w-0 flex-1 space-y-2 text-sm"><span>{k('newBase')}</span><Input maxLength={120} value={newName} onChange={(event) => { setNewName(event.target.value); create.reset(); }} data-testid="knowledge-base-name" /></label>
            <Button type="submit" size="icon" disabled={!newName.trim() || create.isPending} aria-label={k('create')} data-testid="knowledge-base-create"><Plus aria-hidden className="h-4 w-4" /></Button>
          </form>
          {create.isError && <p role="alert" className="text-sm text-[var(--text-danger)]">{e('baseCreateFailed')}</p>}
          {bases.isLoading ? <p role="status">{e('loading')}</p> : bases.isError ? <div role="alert" className="space-y-2 text-sm"><p>{e('loadFailed')}</p><Button variant="outline" onClick={() => void bases.refetch()}>{e('retry')}</Button></div> : bases.data?.length ? bases.data.map((base) => <div key={base.id} className={`flex items-center gap-2 rounded-md border px-3 py-2 text-left text-sm ${base.id === activeId ? 'border-[var(--brand-primary)] bg-[var(--surface-brand-muted)]' : 'border-[var(--border-subtle)]'}`}><button type="button" aria-pressed={base.id === activeId} onClick={() => setSelectedId(base.id)} className="min-w-0 flex-1 text-left" data-testid={`knowledge-base-${base.id}`}><div className="break-words font-medium">{base.name}</div><div className="text-xs text-[var(--text-subtle)]">{e('qnaCount')} {base.qnaCount} · {e('documentCount')} {base.documentCount}</div></button><Button type="button" variant="ghost" size="icon" className="shrink-0 text-[var(--text-danger)]" aria-label={`${k('delete')} ${base.name}`} onClick={() => { remove.reset(); setDeleteTarget(base); }} data-testid={`knowledge-base-delete-${base.id}`}><Trash2 className="h-4 w-4" aria-hidden="true" /></Button></div>) : <p className="rounded-md border border-dashed p-4 text-center text-sm text-[var(--text-subtle)]">{k('empty')}</p>}
        </CardContent>
      </Card>
      <Card className="min-w-0 shadow-none"><CardContent className="p-4 sm:p-6">
        {activeId ? <Tabs key={activeId} value={tab} onValueChange={setTab}>
          <TabsList className="mb-4 flex h-auto flex-wrap justify-start">
            <TabsTrigger value="qna" data-testid="knowledge-qna-tab">{e('qnaTab')}</TabsTrigger>
            <TabsTrigger value="documents" data-testid="knowledge-documents-tab">{e('documentsTab')}</TabsTrigger>
            <TabsTrigger value="chunking" data-testid="knowledge-chunking-tab">{e('rulesTab')}</TabsTrigger>
          </TabsList>
          <TabsContent value="qna"><KnowledgeQnaPanel knowledgeBaseId={activeId} /></TabsContent>
          <TabsContent value="documents"><KnowledgeDocumentsPanel knowledgeBaseId={activeId} /></TabsContent>
          <TabsContent value="chunking" className="space-y-5"><KnowledgeEmbeddingPanel knowledgeBaseId={activeId} /><KnowledgeChunkingPanel knowledgeBaseId={activeId} /></TabsContent>
        </Tabs> : <p className="rounded-md border border-dashed p-8 text-center text-sm text-[var(--text-subtle)]">{k('empty')}</p>}
      </CardContent></Card>
    </div>
    <AlertDialog open={deleteTarget !== null} onOpenChange={(open) => { if (!open && !remove.isPending) setDeleteTarget(null); }}>
      <AlertDialogContent data-testid="knowledge-base-delete-dialog">
        <AlertDialogHeader><AlertDialogTitle>{k('deleteTitle')}</AlertDialogTitle><AlertDialogDescription>{k('deleteDescription').replace('{name}', deleteTarget?.name ?? '')}</AlertDialogDescription></AlertDialogHeader>
        {remove.isError && <p role="alert" className="text-sm text-[var(--text-danger)]">{remove.error instanceof Error && remove.error.message.includes('in_use') ? k('deleteInUse') : k('deleteFailed')}</p>}
        <AlertDialogFooter><AlertDialogCancel disabled={remove.isPending}>{t('publicAssistant.create.cancel')}</AlertDialogCancel><AlertDialogAction disabled={remove.isPending} onClick={(event) => { event.preventDefault(); if (deleteTarget) remove.mutate(deleteTarget); }} data-testid="knowledge-base-delete-confirm">{remove.isPending ? e('saving') : k('delete')}</AlertDialogAction></AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>;
}
