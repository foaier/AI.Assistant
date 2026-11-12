import { useRef, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Button } from '@/components/ui/button';
import { AssistantCountedInput } from './AssistantCountedField';
import { useI18n } from '@/context/i18nContext';
import { getAssistantPreviewMessages, submitAssistantPreview, type AssistantPreview } from '../services/publicAssistantService';
import { AssistantAnswerDisclosure } from './AssistantAnswerDisclosure';

export function AssistantTestChat({ assistantId, dirty, create }: {
  assistantId: string;
  dirty: boolean;
  create: () => Promise<AssistantPreview>;
}) {
  const { t } = useI18n();
  const label = (key: string) => t(`publicAssistant.testChat.${key}`);
  const [preview, setPreview] = useState<AssistantPreview>();
  const [text, setText] = useState('');
  const [waiting, setWaiting] = useState(false);
  const request = useRef<{ id: string; text: string }>();
  const cache = useQueryClient();
  const queryKey = ['assistant-preview-messages', assistantId, preview?.id];
  const messages = useQuery({
    queryKey,
    queryFn: async () => {
      const result = await getAssistantPreviewMessages(assistantId, preview!.id);
      setWaiting(result.running);
      return result;
    },
    enabled: Boolean(preview),
    retry: false,
    refetchInterval: waiting ? 800 : false,
  });
  const start = useMutation({
    mutationFn: create,
    onSuccess: (value) => { setPreview(value); setWaiting(false); request.current = undefined; send.reset(); },
  });
  const send = useMutation({
    mutationFn: async () => {
      const value = text.trim();
      if (!preview || !value) return;
      // Retrying a transport error keeps the same id so it cannot bill a second turn.
      if (request.current?.text !== value) request.current = { id: crypto.randomUUID(), text: value };
      await submitAssistantPreview(assistantId, preview.id, request.current.id, value);
    },
    onSuccess: async () => {
      setText(''); request.current = undefined; setWaiting(true);
      await cache.invalidateQueries({ queryKey });
    },
  });
  const expired = preview && new Date(preview.expiresAt).getTime() <= Date.now();
  const busy = start.isPending || send.isPending || waiting;
  return <div className="space-y-4 p-5" data-testid="assistant-test-chat">
    <p className="text-sm text-[var(--text-subtle)]">{label('help')}</p>
    <Button variant="outline" disabled={busy} onClick={() => start.mutate()} data-testid="assistant-test-start">
      {start.isPending ? label('starting') : preview ? label('restart') : label('start')}
    </Button>
    {start.isError && <p role="alert">{label('startFailed')}</p>}
    {preview && <>
      <p role="status" className="text-xs text-[var(--text-subtle)]" data-testid="assistant-test-state">
        {dirty ? label('changed') : expired ? label('expired') : label('ready')}
      </p>
      <div className="max-h-96 space-y-3 overflow-y-auto" role="log" aria-live="polite" aria-label={label('messages')}>
        {messages.data?.items.map(message => {
          let body: { text?: string; source?: string } = {};
          try { body = JSON.parse(message.contentJson); } catch { /* Preserve status when content is unavailable. */ }
          if (!body.text) return null;
          return <div key={message.id} data-testid={message.role === 1 ? 'assistant-test-user-message' : 'assistant-test-answer'}
            className={`max-w-[90%] whitespace-pre-wrap break-words rounded-xl border p-3 text-sm ${message.role === 1 ? 'ml-auto bg-[var(--surface-neutral-muted)]' : 'bg-[var(--surface-card)]'}`}>
            <p className="mb-1 text-xs text-[var(--text-subtle)]">{label(message.role === 1 ? 'you' : body.source === 'custom_qa' ? 'fixedAnswer' : 'assistant')}</p>
            {message.role === 1 ? (
              body.text
            ) : (
              <AssistantAnswerDisclosure>
                <div className="whitespace-pre-wrap break-words">{body.text}</div>
              </AssistantAnswerDisclosure>
            )}
          </div>;
        })}
        {waiting && <p role="status">{label('answering')}</p>}
      </div>
      {messages.isError && <div role="alert">{label('readFailed')} <Button variant="ghost" onClick={() => void messages.refetch()}>{label('retry')}</Button></div>}
      {messages.data?.failureCode && <p role="alert" data-testid="assistant-test-failed">{label('answerFailed')}</p>}
      {send.isError && <p role="alert">{label('sendFailed')}</p>}
      <form className="flex gap-2" onSubmit={event => { event.preventDefault(); if (!busy && text.trim() && !dirty && !expired) send.mutate(); }}>
        <AssistantCountedInput containerClassName="flex-1" aria-label={label('input')} placeholder={label('input')} value={text} onChange={event => setText(event.target.value)}
          maxLength={4000} disabled={busy || dirty || Boolean(expired)} data-testid="assistant-test-input" />
        <Button type="submit" disabled={busy || dirty || Boolean(expired) || !text.trim()} data-testid="assistant-test-send">{label(send.isError ? 'retry' : 'send')}</Button>
      </form>
    </>}
  </div>;
}
