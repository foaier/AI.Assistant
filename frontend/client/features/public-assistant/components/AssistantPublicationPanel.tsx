import { useDateTime } from '@/shared/hooks/useDateTime';
import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { useI18n } from "@/context/i18nContext";
import { ApiError } from "@/lib/ApiClient";
import {
  getAssistantPublication,
  publishAssistant,
  revokeAssistant,
  type Assistant,
} from "../services/publicAssistantService";

const publicationErrorKeys: Record<string, string> = {
  assistant_knowledge_index_not_ready: "knowledgeIndexNotReady",
  knowledge_embedding_model_unavailable: "embeddingModelUnavailable",
  assistant_policy_required: "policyRequired",
  assistant_policy_unavailable: "policyUnavailable",
  assistant_provider_unavailable: "providerUnavailable",
  assistant_draft_revision_conflict: "revisionConflict",
  assistant_publication_pointer_revision_required: "revisionConflict",
  assistant_publication_pointer_revision_conflict: "revisionConflict",
};

export function AssistantPublicationPanel({
  assistantId,
  save,
  validate,
  disabled = false,
}: {
  assistantId: string;
  save: () => Promise<Assistant>;
  validate?: () => Promise<boolean>;
  disabled?: boolean;
}) {
  const { formatDateTimeDisplay } = useDateTime();
  const { t, locale } = useI18n();
  const label = (key: string) => t(`publicAssistant.publication.${key}`);
  const cache = useQueryClient();
  const key = ["assistant-publication", assistantId];
  const state = useQuery({
    queryKey: key,
    queryFn: () => getAssistantPublication(assistantId),
  });
  const [action, setAction] = useState<"publish" | "revoke">();
  const preflight = useMutation({
    mutationFn: async () => validate ? validate() : true,
    onSuccess: (valid) => {
      if (valid) setAction("publish");
    },
  });
  const failureMessage = (error: unknown) =>
    label(
      error instanceof ApiError && action === "publish"
        ? (publicationErrorKeys[error.message] ?? "failed")
        : action === "revoke"
          ? "revokeFailed"
          : "failed",
    );
  const [copied, setCopied] = useState(false);
  const [copyFailed, setCopyFailed] = useState(false);
  const mutation = useMutation({
    mutationFn: async () => {
      if (!state.data) throw new Error("publication_not_loaded");
      if (action === "revoke")
        await revokeAssistant(assistantId, state.data.pointerRevision);
      else {
        const draft = await save();
        await publishAssistant(
          assistantId,
          draft.draftRevision,
          state.data.pointerRevision,
        );
      }
    },
    onSuccess: async () => {
      setAction(undefined);
      await cache.invalidateQueries({ queryKey: key });
    },
  });
  const link = state.data?.published
    ? `${location.origin}/assistants/${encodeURIComponent(state.data.assistantKey)}`
    : "";
  return (
    <section
      className="flex flex-wrap items-center justify-between gap-3 rounded-lg border bg-[var(--surface-card)] p-4"
      data-testid="assistant-publication-panel"
    >
      <div>
        <p role="status">
          {state.isLoading
            ? label("loading")
            : state.isError
              ? label("loadFailed")
              : label(state.data?.published ? "published" : "draft")}
        </p>
        {state.data?.publishedAt && (
          <p className="mt-1 text-xs text-[var(--text-subtle)]">
            {formatDateTimeDisplay(state.data.publishedAt, locale)}
          </p>
        )}
      </div>
      <div className="flex flex-wrap gap-2">
        {state.isError && (
          <Button variant="outline" onClick={() => void state.refetch()}>
            {label("retry")}
          </Button>
        )}
        {link && (
          <>
            <Button variant="outline" asChild>
              <a
                href={link}
                target="_blank"
                rel="noopener noreferrer"
                data-testid="assistant-public-open"
              >
                {label("open")}
              </a>
            </Button>
            <Button
              variant="outline"
              onClick={() => {
                void navigator.clipboard
                  .writeText(link)
                  .then(() => {
                    setCopied(true);
                    setCopyFailed(false);
                  })
                  .catch(() => setCopyFailed(true));
              }}
            >
              {label(copied ? "copied" : "copy")}
            </Button>
            <Button
              variant="ghost"
              onClick={() => {
                mutation.reset();
                setAction("revoke");
              }}
              data-testid="assistant-revoke"
            >
              {label("revoke")}
            </Button>
          </>
        )}
        <Button
          disabled={!state.data || disabled || preflight.isPending || mutation.isPending}
          onClick={() => {
            mutation.reset();
            if (validate) preflight.mutate();
            else setAction("publish");
          }}
          data-testid="assistant-publish"
        >
          {label(state.data?.published ? "update" : "publish")}
        </Button>
      </div>
      {preflight.isError && <p role="alert" className="w-full text-sm text-red-700 dark:text-red-300">{label("failed")}</p>}
      {copyFailed && (
        <p role="alert" className="w-full break-all">
          {label("copyFailed")} {link}
        </p>
      )}
      <Dialog
        open={Boolean(action)}
        onOpenChange={(open) => {
          if (!open && !mutation.isPending) setAction(undefined);
        }}
      >
        <DialogContent>
          <DialogHeader>
            <DialogTitle>
              {label(action === "revoke" ? "revoke" : "publish")}
            </DialogTitle>
            <DialogDescription>
              {label(action === "revoke" ? "revokeHelp" : "publishHelp")}
            </DialogDescription>
          </DialogHeader>
          {mutation.isError && (
            <p role="alert">{failureMessage(mutation.error)}</p>
          )}
          <div className="flex justify-end gap-2">
            <Button
              variant="outline"
              disabled={mutation.isPending}
              onClick={() => setAction(undefined)}
            >
              {label("cancel")}
            </Button>
            <Button
              disabled={mutation.isPending}
              onClick={() => mutation.mutate()}
              data-testid="assistant-publication-confirm"
            >
              {label(mutation.isPending ? "working" : "confirm")}
            </Button>
          </div>
        </DialogContent>
      </Dialog>
    </section>
  );
}
