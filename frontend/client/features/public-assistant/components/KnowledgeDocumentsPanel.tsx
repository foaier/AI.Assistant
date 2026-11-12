import { useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from "@/components/ui/sheet";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { useI18n } from "@/context/i18nContext";
import {
  getKnowledgeDocumentPreview,
  listKnowledgeDocuments,
  processKnowledgeDocument,
  uploadKnowledgeDocument,
  type AssistantKnowledgeDocument,
} from "../services/publicAssistantService";

const maxDocumentSizeBytes = 10 * 1024 * 1024;
const maxLookupMatches = 20;
const lookupContextChars = 120;

type KnowledgeLookupMatch = {
  id: string;
  sourceLabel: string;
  excerpt: string;
  matchStart: number;
  matchLength: number;
};

function escapeRegExp(value: string) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

function findLiteralMatches(
  segments: Array<{ id: string; sourceLabel: string; text: string }>,
  term: string,
): KnowledgeLookupMatch[] {
  if (!term) return [];

  const matches: KnowledgeLookupMatch[] = [];
  const pattern = new RegExp(escapeRegExp(term), "giu");
  for (const segment of segments) {
    pattern.lastIndex = 0;
    let match: RegExpExecArray | null;
    while (
      (match = pattern.exec(segment.text)) &&
      matches.length < maxLookupMatches
    ) {
      const start = match.index;
      const matchLength = match[0].length;
      const minimumStart = Math.max(0, start - lookupContextChars);
      const lineStart = segment.text.lastIndexOf("\n", start - 1) + 1;
      const excerptStart = lineStart >= minimumStart ? lineStart : minimumStart;
      const maximumEnd = Math.min(
        segment.text.length,
        start + matchLength + lookupContextChars * 2,
      );
      const nextLineEnd = segment.text.indexOf("\n", start + matchLength);
      const followingLineEnd =
        nextLineEnd < 0 ? -1 : segment.text.indexOf("\n", nextLineEnd + 1);
      const contextEnd =
        followingLineEnd >= 0 && followingLineEnd <= maximumEnd
          ? followingLineEnd
          : nextLineEnd;
      const excerptEnd =
        contextEnd >= 0 && contextEnd <= maximumEnd ? contextEnd : maximumEnd;
      const prefix = excerptStart > 0 ? "…" : "";
      const suffix = excerptEnd < segment.text.length ? "…" : "";

      matches.push({
        id: `${segment.id}:${start}`,
        sourceLabel: segment.sourceLabel,
        excerpt: `${prefix}${segment.text.slice(excerptStart, excerptEnd)}${suffix}`,
        matchStart: prefix.length + start - excerptStart,
        matchLength,
      });
      pattern.lastIndex = start + matchLength;
    }
    if (matches.length >= maxLookupMatches) break;
  }
  return matches;
}

export function KnowledgeContentLookup({
  knowledgeBaseId,
  documentId,
}: {
  knowledgeBaseId: string;
  documentId: string;
}) {
  const { t } = useI18n();
  const p = (key: string) => t(`publicAssistant.p0.${key}`);
  const [question, setQuestion] = useState("");
  const [submittedQuestion, setSubmittedQuestion] = useState("");
  const preview = useQuery({
    queryKey: [
      "assistant-knowledge-document-preview",
      knowledgeBaseId,
      documentId,
    ],
    queryFn: () => getKnowledgeDocumentPreview(knowledgeBaseId, documentId),
  });
  const matches = findLiteralMatches(
    preview.data?.segments ?? [],
    submittedQuestion,
  );
  return (
    <section
      className="space-y-4"
      data-testid="knowledge-document-content-lookup"
    >
      <p className="text-sm text-[var(--text-subtle)]">{p("lookupHelp")}</p>
      {preview.isLoading && <p role="status">{p("loading")}</p>}
      {preview.isError && (
        <div role="alert" className="space-y-2">
          <p>{p("loadFailed")}</p>
          <Button variant="outline" onClick={() => void preview.refetch()}>
            {p("retry")}
          </Button>
        </div>
      )}
      <form
        onSubmit={(e) => {
          e.preventDefault();
          if (!preview.isError && question.trim())
            setSubmittedQuestion(question.trim());
        }}
        className="flex flex-wrap items-end gap-2"
      >
        <label className="min-w-0 flex-1 space-y-2 text-sm">
          {p("keyword")}
          <Input
            maxLength={500}
            value={question}
            onChange={(e) => {
              setQuestion(e.target.value);
              setSubmittedQuestion("");
            }}
            data-testid="knowledge-content-lookup-query"
          />
        </label>
        <Button
          disabled={preview.isLoading || preview.isError || !question.trim()}
          data-testid="knowledge-content-lookup-run"
        >
          {p("lookupRun")}
        </Button>
      </form>
      {submittedQuestion && !preview.isError && !preview.isLoading && (
        <div
          role="status"
          className="space-y-3"
          data-testid="knowledge-content-lookup-result"
        >
          {matches.length === 0 ? (
            <p>{p("lookupNone")}</p>
          ) : (
            matches.map((item) => (
              <article className="rounded-lg border p-4" key={item.id}>
                <h4 className="mb-2 text-sm font-medium">{item.sourceLabel}</h4>
                <p className="whitespace-pre-wrap break-words text-sm">
                  {item.excerpt.slice(0, item.matchStart)}
                  <mark>
                    {item.excerpt.slice(
                      item.matchStart,
                      item.matchStart + item.matchLength,
                    )}
                  </mark>
                  {item.excerpt.slice(item.matchStart + item.matchLength)}
                </p>
              </article>
            ))
          )}
        </div>
      )}
    </section>
  );
}

export function KnowledgeDocumentsPanel({
  knowledgeBaseId,
}: {
  knowledgeBaseId: string;
}) {
  const { t } = useI18n();
  const p = (key: string) => t(`publicAssistant.p0.${key}`);
  const e = (key: string) => t(`publicAssistant.embedding.${key}`);
  const client = useQueryClient();
  const fileInput = useRef<HTMLInputElement>(null);
  const trigger = useRef<HTMLElement | null>(null);
  const [active, setActive] = useState<AssistantKnowledgeDocument | null>(null);
  const [tab, setTab] = useState("original");
  const [uploadError, setUploadError] = useState<
    "uploadTooLarge" | "uploadFailed" | null
  >(null);
  const [copied, setCopied] = useState("");
  const [excerpt, setExcerpt] = useState("");
  const documents = useQuery({
    queryKey: ["assistant-knowledge-documents", knowledgeBaseId],
    queryFn: () => listKnowledgeDocuments(knowledgeBaseId),
    refetchInterval: (query) =>
      query.state.data?.some((item) => item.status === 1) ? 3000 : false,
  });
  const preview = useQuery({
    queryKey: [
      "assistant-knowledge-document-preview",
      knowledgeBaseId,
      active?.id,
    ],
    queryFn: () => getKnowledgeDocumentPreview(knowledgeBaseId, active!.id),
    enabled: Boolean(active),
  });
  const refresh = () => {
    void client.invalidateQueries({
      queryKey: ["assistant-knowledge-documents", knowledgeBaseId],
    });
    void client.invalidateQueries({
      queryKey: ["assistant-knowledge-document-preview", knowledgeBaseId],
    });
    void client.invalidateQueries({ queryKey: ["assistant-knowledge-bases"] });
    void client.invalidateQueries({
      queryKey: ["assistant-knowledge-embedding", knowledgeBaseId],
    });
  };
  const upload = useMutation({
    mutationFn: (file: File) => uploadKnowledgeDocument(knowledgeBaseId, file),
    onSuccess: refresh,
  });
  const process = useMutation({
    mutationFn: (id: string) => processKnowledgeDocument(knowledgeBaseId, id),
    onSuccess: refresh,
  });
  const status = (item: AssistantKnowledgeDocument) =>
    e(
      `document.${["ready", "failed", "processing", "needs_embedding_config", "pending_rebuild"].includes(item.indexStatus) ? item.indexStatus : "pending"}`,
    );
  const text = preview.data?.originalText ?? "";
  const location = excerpt ? text.indexOf(excerpt) : -1;
  return (
    <section className="space-y-4" data-testid="knowledge-base-document-list">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="max-w-xl text-sm text-[var(--text-subtle)]">
          {p("uploadHelp")}
        </p>
        <input
          ref={fileInput}
          type="file"
          className="hidden"
          accept=".txt,.md,.markdown,.json,.docx"
          data-testid="knowledge-document-file"
          onChange={(e) => {
            const file = e.target.files?.[0];
            e.currentTarget.value = "";
            if (!file) return;
            if (file.size > maxDocumentSizeBytes) {
              setUploadError("uploadTooLarge");
              return;
            }
            if (
              file.size === 0 ||
              !/\.(txt|md|markdown|json|docx)$/i.test(file.name)
            ) {
              setUploadError("uploadFailed");
              return;
            }
            setUploadError(null);
            upload.mutate(file);
          }}
        />
        <Button
          onClick={() => fileInput.current?.click()}
          disabled={upload.isPending}
          data-testid="knowledge-document-upload"
        >
          {upload.isPending
            ? p("uploading")
            : t("publicAssistant.knowledge.uploadDocument")}
        </Button>
      </div>
      {(uploadError || upload.isError) && (
        <p role="alert" className="text-[var(--text-danger)]">
          {p(uploadError ?? "uploadFailed")}
        </p>
      )}
      {process.isError && <p role="alert">{p("processFailed")}</p>}
      {documents.isLoading ? (
        <p role="status">{p("loading")}</p>
      ) : documents.isError ? (
        <div role="alert">
          {p("loadFailed")}{" "}
          <Button variant="outline" onClick={() => void documents.refetch()}>
            {p("retry")}
          </Button>
        </div>
      ) : !documents.data?.length ? (
        <p className="rounded-lg border border-dashed p-8 text-center">
          {t("publicAssistant.knowledge.noDocuments")}
        </p>
      ) : (
        <ul className="divide-y rounded-lg border">
          {documents.data.map((item) => (
            <li
              key={item.id}
              className="flex flex-wrap items-center justify-between gap-3 p-4"
              data-testid={`knowledge-document-${item.id}`}
            >
              <div className="min-w-0 flex-1">
                <h3 className="break-all font-medium">{item.fileName}</h3>
                <p className="mt-1 text-sm text-[var(--text-subtle)]">
                  <span data-testid={`knowledge-document-state-${item.id}`}>
                    {status(item)}
                  </span>{" "}
                  · {p("segments")} {item.chunkCount}
                </p>
                {(item.status === 3 || item.indexStatus === "failed") && (
                  <p className="mt-1 text-sm text-[var(--text-danger)]">
                    {e(item.lastErrorCode === "assistant_embedding_provider_failed" ? "providerFailureHelp" : "failureHelp")}
                  </p>
                )}
              </div>
              <div className="flex flex-wrap gap-2">
                <Button
                  size="sm"
                  variant="outline"
                  onClick={() => {
                    trigger.current = document.activeElement as HTMLElement;
                    setActive(item);
                    setTab("original");
                    setExcerpt("");
                    setCopied("");
                  }}
                  data-testid={`knowledge-document-preview-${item.id}`}
                >
                  {p("view")}
                </Button>
                <Button
                  size="sm"
                  variant="outline"
                  disabled={item.status === 1 || process.isPending}
                  onClick={() => process.mutate(item.id)}
                  data-testid={`knowledge-document-process-${item.id}`}
                >
                  {item.status === 1 ||
                  (process.isPending && process.variables === item.id)
                    ? p("processing")
                    : item.indexStatus === "failed"
                      ? e("retryProcess")
                      : t("publicAssistant.knowledge.processDocument")}
                </Button>
              </div>
            </li>
          ))}
        </ul>
      )}
      <Sheet
        open={Boolean(active)}
        onOpenChange={(open) => {
          if (!open) setActive(null);
        }}
      >
        <SheetContent
          className="flex w-full flex-col sm:max-w-3xl"
          data-testid="knowledge-document-preview"
          onCloseAutoFocus={(e) => {
            e.preventDefault();
            trigger.current?.focus();
          }}
        >
          <SheetHeader className="pr-8">
            <SheetTitle className="break-all">{active?.fileName}</SheetTitle>
            <SheetDescription>{p("previewHelp")}</SheetDescription>
          </SheetHeader>
          <Tabs
            value={tab}
            onValueChange={setTab}
            className="flex min-h-0 flex-1 flex-col"
          >
            <TabsList className="flex h-auto flex-wrap justify-start">
              {["original", "structure", "segments", "lookup"].map((key) => (
                <TabsTrigger
                  key={key}
                  value={key}
                  data-testid={`knowledge-document-tab-${key}`}
                >
                  {p(key)}
                </TabsTrigger>
              ))}
            </TabsList>
            {tab !== "lookup" &&
              (preview.isLoading ? (
                <p role="status">{p("loading")}</p>
              ) : preview.isError ? (
                <div role="alert">
                  {p("loadFailed")}{" "}
                  <Button
                    variant="outline"
                    onClick={() => void preview.refetch()}
                  >
                    {p("retry")}
                  </Button>
                </div>
              ) : null)}
            <div className="min-h-0 flex-1 overflow-y-auto py-3">
              <TabsContent value="original">
                <div
                  className="whitespace-pre-wrap break-words text-sm leading-7"
                  data-testid="knowledge-document-original"
                >
                  {location >= 0 ? (
                    <>
                      {text.slice(0, location)}
                      <mark
                        ref={(node) =>
                          node?.scrollIntoView?.({ block: "center" })
                        }
                      >
                        {text.slice(location, location + excerpt.length)}
                      </mark>
                      {text.slice(location + excerpt.length)}
                    </>
                  ) : (
                    text
                  )}
                </div>
              </TabsContent>
              <TabsContent
                value="structure"
                data-testid="knowledge-document-structure"
              >
                <p className="mb-4 text-sm text-[var(--text-subtle)]">
                  {p("structureHelp")}
                </p>
                {preview.data?.structure.map((block, index) => (
                  <p
                    key={index}
                    className={
                      block.kind === "heading"
                        ? "my-4 break-words font-semibold"
                        : "my-2 whitespace-pre-wrap break-words text-sm"
                    }
                  >
                    {block.text}
                  </p>
                ))}
              </TabsContent>
              <TabsContent
                value="segments"
                className="space-y-4"
                data-testid="knowledge-document-segments"
              >
                <p className="text-sm text-[var(--text-subtle)]">
                  {p("segmentHelp")}
                </p>
                {preview.data?.segments.length === 0 && (
                  <p>{p("noSegments")}</p>
                )}
                {preview.data?.segments.map((segment) => (
                  <article
                    key={segment.id}
                    className="space-y-3 rounded-lg border p-4"
                  >
                    <h4 className="break-words text-sm font-medium">
                      {segment.sourceLabel} ·{" "}
                      {p(segment.available ? "available" : "unavailable")}
                    </h4>
                    <p className="whitespace-pre-wrap break-words text-sm">
                      {segment.text}
                    </p>
                    <div className="flex gap-2">
                      <Button
                        size="sm"
                        variant="outline"
                        onClick={() =>
                          void navigator.clipboard
                            .writeText(segment.text)
                            .then(() => setCopied(segment.id))
                            .catch(() => setCopied("error"))
                        }
                      >
                        {p(copied === segment.id ? "copied" : "copy")}
                      </Button>
                      <Button
                        size="sm"
                        variant="outline"
                        onClick={() => {
                          setExcerpt(segment.text);
                          setTab("original");
                        }}
                      >
                        {p("locate")}
                      </Button>
                    </div>
                  </article>
                ))}
                {copied === "error" && <p role="alert">{p("copyFailed")}</p>}
              </TabsContent>
              <TabsContent value="lookup">
                {active && (
                  <KnowledgeContentLookup
                    knowledgeBaseId={knowledgeBaseId}
                    documentId={active.id}
                  />
                )}
              </TabsContent>
            </div>
          </Tabs>
        </SheetContent>
      </Sheet>
    </section>
  );
}
