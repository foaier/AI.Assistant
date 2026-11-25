import { ExternalLink } from "lucide-react";
import { useI18n } from "@/context/i18nContext";
import { isSafeAssistantLink } from "../services/assistantPresentation";
import type { AssistantCitation } from "../services/assistantChatService";

/**
 * 在回答下方列出实际参与本次回答的知识片段和安全的网页来源。
 */
export function AssistantKnowledgeCitations({
  citations,
}: {
  citations?: AssistantCitation[];
}) {
  const { t } = useI18n();
  if (!citations?.length) return null;

  return (
    <section
      aria-label={t("publicAssistant.chat.sources")}
      className="mt-4 border-t border-[var(--border-subtle)] pt-3"
      data-testid="assistant-knowledge-citations"
    >
      <h3 className="mb-2 text-xs font-semibold text-[var(--text-secondary)]">
        {t("publicAssistant.chat.sources")}
      </h3>
      <ol className="space-y-2">
        {citations.map((citation) => {
          const sourceUrl = safeSourceUrl(citation.sourceUrl);
          return (
            <li
              className="rounded-md border border-[var(--border-subtle)] bg-[var(--surface-neutral-muted)]/50 px-3 py-2"
              key={citation.segmentId}
            >
              <div className="flex min-w-0 items-start justify-between gap-3">
                <p className="min-w-0 break-words text-xs font-medium text-[var(--text-primary)]">
                  {citation.documentName}
                  <span className="ml-2 whitespace-nowrap font-normal text-[var(--text-subtle)]">
                    {t("publicAssistant.chat.segment")} {citation.segmentIndex + 1}
                  </span>
                </p>
                {sourceUrl && (
                  <a
                    href={sourceUrl}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="inline-flex min-h-8 shrink-0 items-center gap-1 text-xs text-[var(--text-link)] underline-offset-2 hover:underline focus-visible:outline focus-visible:outline-2 focus-visible:outline-[var(--brand-primary)]"
                  >
                    <span>{t("publicAssistant.chat.openSource")}</span>
                    <ExternalLink className="h-3.5 w-3.5" aria-hidden="true" />
                  </a>
                )}
              </div>
              <p className="mt-1 whitespace-pre-wrap break-words text-xs leading-5 text-[var(--text-subtle)]">
                {citation.excerpt}
              </p>
            </li>
          );
        })}
      </ol>
    </section>
  );
}

/**
 * 仅允许可公开访问的 HTTP(S) 地址作为原网页链接。
 */
function safeSourceUrl(value?: string | null): string | undefined {
  if (!value || !isSafeAssistantLink(value)) return undefined;
  try {
    const url = new URL(value);
    return url.protocol === "http:" || url.protocol === "https:"
      ? value
      : undefined;
  } catch {
    return undefined;
  }
}
