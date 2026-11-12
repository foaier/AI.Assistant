import { useEffect, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent, AlertDialogDescription, AlertDialogFooter, AlertDialogHeader, AlertDialogTitle } from '@/components/ui/alert-dialog';
import { useI18n } from '@/context/i18nContext';
import { getKnowledgeEmbeddingProfile, saveKnowledgeEmbeddingProfile, type KnowledgeEmbeddingSelection } from '../services/publicAssistantService';

const selectionKey = (model: Omit<KnowledgeEmbeddingSelection, 'generation'>) =>
  JSON.stringify([model.providerId, model.providerRevision, model.model, model.dimension]);

export function KnowledgeEmbeddingPanel({ knowledgeBaseId }: { knowledgeBaseId: string }) {
  const { t } = useI18n();
  const e = (key: string) => t(`publicAssistant.embedding.${key}`);
  const cache = useQueryClient();
  const profile = useQuery({
    queryKey: ['assistant-knowledge-embedding', knowledgeBaseId],
    queryFn: () => getKnowledgeEmbeddingProfile(knowledgeBaseId),
    enabled: Boolean(knowledgeBaseId),
    refetchInterval: (query) => query.state.data?.status === 'rebuilding' ? 3000 : false,
  });
  const [selected, setSelected] = useState('');
  const [confirm, setConfirm] = useState(false);
  useEffect(() => {
    setSelected(profile.data?.target ? selectionKey(profile.data.target) : '');
  }, [knowledgeBaseId, profile.data?.revision]);
  const options = profile.data?.availableModels ?? [];
  const choice = options.find((model) => selectionKey(model) === selected);
  const currentKey = profile.data?.target ? selectionKey(profile.data.target) : '';
  const savedVersion = Boolean(currentKey && !options.some((model) => selectionKey(model) === currentKey));
  const unavailable = profile.data?.status === 'model_unavailable';
  const changed = Boolean(choice && selected !== currentKey);
  const save = useMutation({
    mutationFn: () => {
      if (!choice || !profile.data) throw new Error('selection_required');
      return saveKnowledgeEmbeddingProfile(knowledgeBaseId, {
        providerId: choice.providerId, providerRevision: choice.providerRevision,
        model: choice.model, dimension: choice.dimension, expectedRevision: profile.data.revision,
      });
    },
    onSuccess: (value) => {
      cache.setQueryData(['assistant-knowledge-embedding', knowledgeBaseId], value);
      void cache.invalidateQueries({ queryKey: ['assistant-knowledge-documents', knowledgeBaseId] });
    },
  });
  const knownStatuses = ['not_configured', 'pending_rebuild', 'rebuilding', 'ready', 'failed', 'model_unavailable'];
  const state = knownStatuses.includes(profile.data?.status ?? '') ? profile.data!.status : 'pending_rebuild';
  return <section className="space-y-4 rounded-lg border border-[var(--border-subtle)] p-4" data-testid="knowledge-embedding-panel">
    <div className="flex flex-wrap items-start justify-between gap-3">
      <div><h3 className="font-medium">{e('title')}</h3><p className="mt-1 max-w-2xl text-sm text-[var(--text-secondary)]">{e('help')}</p></div>
      {profile.data && <Badge variant={state === 'ready' ? 'success' : state === 'failed' || state === 'model_unavailable' ? 'destructive' : 'warning'} data-testid="knowledge-embedding-state">{e(state)}</Badge>}
    </div>
    {profile.isLoading ? <p role="status">{e('loading')}</p> : profile.isError ? <div role="alert" className="flex flex-wrap items-center gap-3 text-sm">{e('loadFailed')}<Button variant="outline" onClick={() => void profile.refetch()}>{e('retry')}</Button></div> : profile.data && <>
      {!options.length && <div className="rounded-md bg-[var(--surface-neutral-muted)] p-3 text-sm" data-testid="knowledge-embedding-empty"><p>{e('empty')}</p><Link className="mt-2 inline-block underline" to="/admin/system/assistant-models">{e('manageModels')}</Link></div>}
      {unavailable && <p role="alert" className="text-sm text-[var(--text-danger)]">{e('unavailableHelp')}</p>}
      <label className="block space-y-2 text-sm"><span>{e('model')}</span>
        <select className="h-10 w-full min-w-0 rounded-md border border-[var(--border-control)] bg-[var(--surface-card)] px-3" value={selected} disabled={save.isPending || !options.length} onChange={(event) => { setSelected(event.target.value); save.reset(); }} data-testid="knowledge-embedding-model">
          <option value="">{e('choose')}</option>
          {savedVersion && profile.data.target && <option value={currentKey} disabled>{profile.data.target.model} · {e(unavailable ? 'model_unavailable' : 'savedVersion')}</option>}
          {options.map((model) => <option key={selectionKey(model)} value={selectionKey(model)}>{model.providerName} · {model.model}</option>)}
        </select>
      </label>
      <div className="space-y-1 text-sm text-[var(--text-secondary)]" role="status">
        <p>{e('progress')} {profile.data.targetReadyDocumentCount} / {profile.data.documentCount}</p>
        {profile.data.active && <p>{e(profile.data.activeAvailable ? 'activeModel' : 'storedModel')} <span className="break-all">{profile.data.active.model}</span></p>}
        {profile.data.active && !profile.data.activeAvailable && <p role="alert" className="text-[var(--text-danger)]">{e('activeUnavailable')}</p>}
        {profile.data.activeAvailable && state !== 'ready' && <p>{e('keepActive')}</p>}
        {state === 'failed' && <p className="text-[var(--text-danger)]">{e(profile.data.lastErrorCode === 'assistant_embedding_provider_failed' ? 'providerFailureHelp' : 'failureHelp')}</p>}
        {state !== 'ready' && profile.data.target && <p>{e('rebuildHelp')}</p>}
      </div>
      {save.isError && <p role="alert" className="text-sm text-[var(--text-danger)]">{e('saveFailed')} <Button size="sm" variant="link" onClick={() => void profile.refetch()}>{e('refresh')}</Button></p>}
      {save.isSuccess && <p role="status" className="text-sm text-[var(--text-success)]">{e('saved')}</p>}
      <div className="flex flex-wrap justify-end gap-2"><Button type="button" variant="outline" disabled={save.isPending} onClick={() => void profile.refetch()}>{e('refresh')}</Button><Button type="button" disabled={!changed || save.isPending} data-testid="knowledge-embedding-save" onClick={() => profile.data!.documentCount > 0 ? setConfirm(true) : save.mutate()}>{e(save.isPending ? 'saving' : 'save')}</Button></div>
    </>}
    <AlertDialog open={confirm} onOpenChange={setConfirm}><AlertDialogContent><AlertDialogHeader><AlertDialogTitle>{e('confirmTitle')}</AlertDialogTitle><AlertDialogDescription>{e('confirmHelp')}</AlertDialogDescription></AlertDialogHeader><AlertDialogFooter><AlertDialogCancel>{e('cancel')}</AlertDialogCancel><AlertDialogAction onClick={() => save.mutate()} data-testid="knowledge-embedding-confirm">{e('confirm')}</AlertDialogAction></AlertDialogFooter></AlertDialogContent></AlertDialog>
  </section>;
}
