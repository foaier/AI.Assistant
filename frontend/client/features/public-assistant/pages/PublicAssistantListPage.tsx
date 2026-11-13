import { useState } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { z } from 'zod';
import { zodResolver } from '@hookform/resolvers/zod';
import { Bot, Plus, ArrowRight, Trash2, AlertCircle, Loader2 } from 'lucide-react';
import { useNavigate } from 'react-router-dom';

import { AdminPageHeading } from '@/components/admin/AdminPageHeading';
import { Button } from '@/components/ui/button';
import { Card, CardContent } from '@/components/ui/card';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import { Form, FormControl, FormDescription, FormField, FormItem, FormLabel, FormMessage } from '@/components/ui/form';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog';
import { useI18n } from '@/context/i18nContext';

import {
  createAssistant,
  deleteAssistant,
  DEFAULT_ASSISTANT_CONFIGURATION,
  listAssistants,
} from '../services/publicAssistantService';
import type { AssistantListItem } from '../services/publicAssistantService';

// Match AssistantDraft.NormalizeName's trimmed UTF-16 length and the existing description limit.
const NAME_LIMIT = 120;
const DESCRIPTION_LIMIT = 500;
const FIELD_ERROR_CLASS_NAME = 'text-red-700 dark:text-red-300';
const createAssistantSchema = (t: (key: string) => string) => z.object({
  name: z.string().trim()
    .min(1, t('publicAssistant.create.nameRequired'))
    .max(NAME_LIMIT, t('publicAssistant.create.nameTooLong')),
  description: z.string().trim().max(DESCRIPTION_LIMIT, t('publicAssistant.create.descriptionTooLong')),
});
type CreateAssistantValues = z.infer<ReturnType<typeof createAssistantSchema>>;

export default function PublicAssistantListPage() {
  const { t } = useI18n();
  const navigate = useNavigate();
  const [createOpen, setCreateOpen] = useState(false);
  const [deleteTarget, setDeleteTarget] = useState<AssistantListItem | null>(null);
  const listQuery = useQuery({
    queryKey: ['public-assistants'],
    queryFn: listAssistants,
  });
  const createForm = useForm<CreateAssistantValues>({
    resolver: zodResolver(createAssistantSchema(t)),
    mode: 'onChange',
    defaultValues: { name: '', description: '' },
  });
  const createMutation = useMutation({
    mutationFn: (values: CreateAssistantValues) => createAssistant({
      name: values.name,
      description: values.description || undefined,
      configuration: DEFAULT_ASSISTANT_CONFIGURATION,
    }),
    onSuccess: (created) => {
      setCreateOpen(false);
      createForm.reset();
      navigate(`/admin/assistants/${encodeURIComponent(created.id)}`);
    },
    onError: (error) => {
      const message = error instanceof Error ? error.message : '';
      if (message === 'Name too long' || message === 'Name required') {
        createForm.setError('name', {
          type: 'server',
          message: message === 'Name too long'
            ? t('publicAssistant.create.nameTooLong')
            : t('publicAssistant.create.nameRequired'),
        }, { shouldFocus: true });
      } else {
        createForm.setError('root.server', { message: t('publicAssistant.create.failed') });
      }
    },
  });
  const changeCreateOpen = (open: boolean) => {
    if (createMutation.isPending) return;
    createForm.reset();
    createMutation.reset();
    setCreateOpen(open);
  };
  const deleteMutation = useMutation({
    mutationFn: (item: AssistantListItem) => deleteAssistant(item.id),
    onSuccess: async () => {
      setDeleteTarget(null);
      await listQuery.refetch();
    },
  });
  const items = listQuery.data ?? [];
  const error = listQuery.error;

  return (
    <div className="mx-auto min-h-full w-full max-w-[1440px] space-y-6" data-testid="public-assistant-list">
      <AdminPageHeading
        title={t('publicAssistant.title')}
        subtitle={t('publicAssistant.description')}
        actions={(
          <Button type="button" onClick={() => changeCreateOpen(true)} data-testid="public-assistant-create">
            <Plus className="mr-2 h-4 w-4" aria-hidden="true" />
            {t('publicAssistant.list.create')}
          </Button>
        )}
      />

      {error ? (
        <div className="rounded-md border border-[var(--border-danger)] px-3 py-2 text-sm text-[var(--text-danger)]" role="alert" data-testid="public-assistant-list-error">
          {error instanceof Error ? error.message : t('publicAssistant.list.loadFailed')}
        </div>
      ) : null}

      {listQuery.isLoading ? (
        <div className="text-sm text-[var(--text-subtle)]" data-testid="public-assistant-list-loading">
          {t('publicAssistant.list.loading')}
        </div>
      ) : items.length === 0 ? (
        <Card className="shadow-none">
          <CardContent className="flex min-h-56 flex-col items-center justify-center gap-3 text-center">
            <Bot className="h-10 w-10 text-[var(--text-subtle)]" aria-hidden="true" />
            <p className="text-sm text-[var(--text-subtle)]">{t('publicAssistant.list.empty')}</p>
            <Button type="button" variant="outline" onClick={() => changeCreateOpen(true)}>
              {t('publicAssistant.list.create')}
            </Button>
          </CardContent>
        </Card>
      ) : (
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
          {items.map((item) => (
            <Card key={item.id} className="shadow-none" data-testid={`public-assistant-card-${item.id}`}>
              <CardContent className="space-y-4 p-5">
                <div className="flex items-start gap-3">
                  <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-[var(--brand-primary-soft)] text-[var(--brand-primary)]">
                    <Bot className="h-5 w-5" aria-hidden="true" />
                  </div>
                  <div className="min-w-0">
                    <h2 className="truncate text-base font-semibold text-[var(--text-primary)]">{item.name}</h2>
                    <p className="mt-1 line-clamp-2 text-sm text-[var(--text-subtle)]">{item.description || '—'}</p>
                  </div>
                </div>
                <div className="flex items-center justify-between gap-3 text-xs text-[var(--text-subtle)]">
                  <span>v{item.draftRevision}</span>
                  <div className="flex gap-2">
                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      onClick={() => navigate(`/admin/assistants/${encodeURIComponent(item.id)}`)}
                      data-testid={`public-assistant-open-${item.id}`}
                    >
                      {t('publicAssistant.list.open')}
                      <ArrowRight className="ml-2 h-3.5 w-3.5" aria-hidden="true" />
                    </Button>
                    <Button
                      type="button"
                      variant="ghost"
                      size="sm"
                      className="text-[var(--text-danger)] hover:text-[var(--text-danger)]"
                      onClick={() => { deleteMutation.reset(); setDeleteTarget(item); }}
                      data-testid={`public-assistant-delete-${item.id}`}
                      aria-label={`${t('publicAssistant.list.delete')} ${item.name}`}
                    >
                      <Trash2 className="h-4 w-4" aria-hidden="true" />
                    </Button>
                  </div>
                </div>
              </CardContent>
            </Card>
          ))}
        </div>
      )}

      <Dialog open={createOpen} onOpenChange={changeCreateOpen}>
        <DialogContent
          data-testid="public-assistant-create-dialog"
          className="max-w-[560px] motion-reduce:animate-none"
          closeButtonClassName={createMutation.isPending ? 'hidden' : 'h-11 w-11 cursor-pointer right-2 top-2'}
        >
          <DialogHeader className="pr-8 text-left">
            <DialogTitle>{t('publicAssistant.create.title')}</DialogTitle>
            <DialogDescription>{t('publicAssistant.create.help')}</DialogDescription>
          </DialogHeader>
          <Form {...createForm}>
            <form
              noValidate
              aria-busy={createMutation.isPending}
              className="min-w-0 space-y-5"
              onSubmit={createForm.handleSubmit((values) => {
                if (!createMutation.isPending) createMutation.mutate(values);
              })}
            >
              <div className="space-y-3 pt-2">
                <FormField control={createForm.control} name="name" render={({ field, fieldState }) => (
                  <FormItem>
                    <FormLabel className={fieldState.invalid ? FIELD_ERROR_CLASS_NAME : undefined}>
                      {t('publicAssistant.create.name')}
                      <span className={`ml-1 ${FIELD_ERROR_CLASS_NAME}`} aria-hidden="true">*</span>
                    </FormLabel>
                    <FormControl>
                      <Input
                        {...field}
                        autoFocus
                        aria-required="true"
                        readOnly={createMutation.isPending}
                        placeholder={t('publicAssistant.create.namePlaceholder')}
                        className="h-11 text-base sm:text-sm aria-[invalid=true]:border-[var(--border-danger)]"
                        data-testid="public-assistant-create-name"
                      />
                    </FormControl>
                    <div className="flex items-start justify-between gap-3">
                      <FormDescription className="text-xs">{t('publicAssistant.create.nameHelp')}</FormDescription>
                      <span className={`shrink-0 text-xs tabular-nums ${field.value.trim().length > NAME_LIMIT ? FIELD_ERROR_CLASS_NAME : 'text-muted-foreground'}`}>
                        {field.value.trim().length} / {NAME_LIMIT}
                      </span>
                    </div>
                    <div className="min-h-5"><FormMessage role="alert" className={FIELD_ERROR_CLASS_NAME} /></div>
                  </FormItem>
                )} />
                <FormField control={createForm.control} name="description" render={({ field, fieldState }) => (
                  <FormItem>
                    <div className="flex items-center gap-2">
                      <FormLabel className={fieldState.invalid ? FIELD_ERROR_CLASS_NAME : undefined}>{t('publicAssistant.create.description')}</FormLabel>
                      <span className="text-xs text-muted-foreground">{t('publicAssistant.create.optional')}</span>
                    </div>
                    <FormControl>
                      <Textarea
                        {...field}
                        rows={3}
                        readOnly={createMutation.isPending}
                        placeholder={t('publicAssistant.create.descriptionPlaceholder')}
                        className="resize-y text-base sm:text-sm aria-[invalid=true]:border-[var(--border-danger)]"
                        data-testid="public-assistant-create-description"
                      />
                    </FormControl>
                    <div className="flex items-start justify-between gap-3">
                      <FormDescription className="text-xs">{t('publicAssistant.create.descriptionHelp')}</FormDescription>
                      <span className={`shrink-0 text-xs tabular-nums ${field.value.trim().length > DESCRIPTION_LIMIT ? FIELD_ERROR_CLASS_NAME : 'text-muted-foreground'}`}>
                        {field.value.trim().length} / {DESCRIPTION_LIMIT}
                      </span>
                    </div>
                    <div className="min-h-5"><FormMessage role="alert" className={FIELD_ERROR_CLASS_NAME} /></div>
                  </FormItem>
                )} />
              </div>
              {createForm.formState.errors.root?.server ? (
                <div role="alert" className={`flex items-start gap-2 rounded-lg border border-[var(--border-danger)] p-3 text-sm ${FIELD_ERROR_CLASS_NAME}`}>
                  <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
                  <p>{createForm.formState.errors.root.server.message}</p>
                </div>
              ) : null}
              <DialogFooter className="gap-2 border-t border-[var(--border-subtle)] pt-4 sm:space-x-0">
                <Button type="button" variant="outline" className="h-11 cursor-pointer sm:min-w-20" disabled={createMutation.isPending} onClick={() => changeCreateOpen(false)}>
                  {t('publicAssistant.create.cancel')}
                </Button>
                <Button type="submit" className="h-11 cursor-pointer sm:min-w-24" disabled={createMutation.isPending}>
                  {createMutation.isPending ? <Loader2 className="mr-2 h-4 w-4 animate-spin motion-reduce:animate-none" aria-hidden="true" /> : null}
                  {createMutation.isPending ? t('publicAssistant.create.creating') : t('publicAssistant.create.submit')}
                </Button>
              </DialogFooter>
            </form>
          </Form>
        </DialogContent>
      </Dialog>
      <AlertDialog open={deleteTarget !== null} onOpenChange={(open) => { if (!open && !deleteMutation.isPending) setDeleteTarget(null); }}>
        <AlertDialogContent data-testid="public-assistant-delete-dialog">
          <AlertDialogHeader>
            <AlertDialogTitle>{t('publicAssistant.list.deleteTitle')}</AlertDialogTitle>
            <AlertDialogDescription>
              {t('publicAssistant.list.deleteDescription').replace('{name}', deleteTarget?.name ?? '')}
            </AlertDialogDescription>
          </AlertDialogHeader>
          {deleteMutation.isError ? (
            <p role="alert" className="text-sm text-[var(--text-danger)]">
              {deleteMutation.error instanceof Error && deleteMutation.error.message.includes('published_revoke')
                ? t('publicAssistant.list.deletePublished')
                : t('publicAssistant.list.deleteFailed')}
            </p>
          ) : null}
          <AlertDialogFooter>
            <AlertDialogCancel disabled={deleteMutation.isPending}>{t('publicAssistant.create.cancel')}</AlertDialogCancel>
            <AlertDialogAction
              disabled={deleteMutation.isPending}
              onClick={(event) => {
                event.preventDefault();
                if (deleteTarget) deleteMutation.mutate(deleteTarget);
              }}
              data-testid="public-assistant-delete-confirm"
            >
              {deleteMutation.isPending ? t('publicAssistant.list.deleting') : t('publicAssistant.list.delete')}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}
