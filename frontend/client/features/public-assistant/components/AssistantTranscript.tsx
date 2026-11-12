import { useInfiniteQuery } from "@tanstack/react-query";
import { Button } from "@/components/ui/button";
import {
  Sheet,
  SheetContent,
  SheetHeader,
  SheetTitle,
  SheetDescription,
} from "@/components/ui/sheet";
import { useI18n } from "@/context/i18nContext";
import AssistantMessageMarkdown from "./AssistantMessageMarkdown";
import { AssistantAnswerDisclosure } from "./AssistantAnswerDisclosure";
import { getAssistantTranscript } from "../services/publicAssistantService";

export function AssistantTranscript({
  assistantId,
  conversationId,
  onClose,
}: {
  assistantId: string;
  conversationId: string | null;
  onClose: () => void;
}) {
  const { t } = useI18n();
  const p = (key: string) => t(`publicAssistant.experience.${key}`);
  const query = useInfiniteQuery({
    queryKey: ["assistant-transcript", assistantId, conversationId],
    queryFn: ({ pageParam }) =>
      getAssistantTranscript(assistantId, conversationId!, pageParam),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (page) =>
      page.hasMore ? (page.nextCursor ?? undefined) : undefined,
    enabled: Boolean(conversationId),
    retry: false,
  });
  return (
    <Sheet
      open={Boolean(conversationId)}
      onOpenChange={(open) => {
        if (!open) onClose();
      }}
    >
      <SheetContent
        className="flex w-full flex-col sm:max-w-2xl"
        data-testid="assistant-transcript"
      >
        <SheetHeader>
          <SheetTitle>{p("transcript")}</SheetTitle>
          <SheetDescription className="break-all">
            {conversationId}
          </SheetDescription>
        </SheetHeader>
        <div className="min-h-0 flex-1 space-y-4 overflow-y-auto py-4">
          {query.isPending && <p role="status">{p("loading")}</p>}
          {query.data?.pages
            .flatMap((page) => page.items)
            .map((message) => {
              let body: { text?: string; source?: string } = {};
              try {
                body = JSON.parse(message.contentJson);
              } catch {
                /* Malformed records retain their status. */
              }
              const text = body.text || p("emptyMessage");
              return (
                <article
                  key={message.id}
                  className={`max-w-[95%] rounded-xl border p-4 ${message.role === 1 ? "ml-auto bg-[var(--surface-neutral-muted)]" : "bg-[var(--surface-card)]"}`}
                  data-testid="assistant-transcript-message"
                >
                  <div className="mb-2 flex flex-wrap gap-2 text-xs text-[var(--text-subtle)]">
                    <span className="font-medium">
                      {p(message.role === 1 ? "visitor" : "assistant")}
                    </span>
                    <span>{p(`status.${message.status}`)}</span>
                    {body.source && <span>{p(`source.${body.source}`)}</span>}
                  </div>
                  {message.role === 1 ? (
                    <p className="whitespace-pre-wrap break-words text-sm leading-6">
                      {text}
                    </p>
                  ) : (
                    <AssistantAnswerDisclosure>
                      <div className="break-words text-sm leading-6">
                        <AssistantMessageMarkdown text={text} />
                      </div>
                    </AssistantAnswerDisclosure>
                  )}
                </article>
              );
            })}
          {query.isError && (
            <div role="alert" className="space-y-2 text-sm">
              <p>{p("readFailed")}</p>
              <Button
                variant="outline"
                onClick={() =>
                  void (query.hasNextPage
                    ? query.fetchNextPage()
                    : query.refetch())
                }
              >
                {p("retry")}
              </Button>
            </div>
          )}
          {query.data && query.data.pages[0].items.length === 0 && (
            <p className="text-sm text-[var(--text-subtle)]">
              {p("noMessages")}
            </p>
          )}
          {query.hasNextPage && (
            <Button
              variant="outline"
              disabled={query.isFetchingNextPage}
              data-testid="assistant-transcript-more"
              onClick={() => void query.fetchNextPage()}
            >
              {p(query.isFetchingNextPage ? "loading" : "loadMore")}
            </Button>
          )}
        </div>
      </SheetContent>
    </Sheet>
  );
}
