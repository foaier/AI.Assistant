import {
  useId,
  useLayoutEffect,
  useRef,
  useState,
  type ReactNode,
} from "react";
import { ChevronDown, ChevronUp } from "lucide-react";
import { Button } from "@/components/ui/button";
import { useI18n } from "@/context/i18nContext";

const COLLAPSED_ANSWER_HEIGHT = 240;

export function AssistantAnswerDisclosure({
  children,
  onExpandedChange,
}: {
  children: ReactNode;
  onExpandedChange?: (expanded: boolean) => void;
}) {
  const { t } = useI18n();
  const contentId = useId();
  const contentRef = useRef<HTMLDivElement>(null);
  const [isLong, setIsLong] = useState(false);
  const [isExpanded, setIsExpanded] = useState(false);

  useLayoutEffect(() => {
    const content = contentRef.current;
    if (!content) return;

    const measure = () =>
      setIsLong(content.scrollHeight > COLLAPSED_ANSWER_HEIGHT);

    measure();
    const observer = new ResizeObserver(measure);
    observer.observe(content);
    return () => observer.disconnect();
  }, []);

  useLayoutEffect(() => {
    contentRef.current?.toggleAttribute("inert", isLong && !isExpanded);
  }, [isLong, isExpanded]);

  return (
    <div className="min-w-0 max-w-full">
      <div
        id={contentId}
        ref={contentRef}
        className={`min-w-0 max-w-full overflow-x-hidden break-words ${
          isLong && !isExpanded
            ? "relative max-h-[240px] overflow-y-hidden"
            : ""
        }`}
        aria-hidden={isLong && !isExpanded}
      >
        {children}
        {isLong && !isExpanded && (
          <div
            className="pointer-events-none absolute inset-x-0 bottom-0 h-10 bg-gradient-to-b from-transparent to-[var(--surface-card)]"
            aria-hidden="true"
          />
        )}
      </div>
      {isLong && (
        <Button
          type="button"
          variant="ghost"
          className="h-11 min-h-11 w-full justify-start gap-1.5 rounded-md px-2 text-sm text-[var(--text-link)] transition-colors duration-200 hover:text-[var(--text-primary)] motion-reduce:transition-none"
          aria-expanded={isExpanded}
          aria-controls={contentId}
          onClick={() => {
            const expanded = !isExpanded;
            setIsExpanded(expanded);
            onExpandedChange?.(expanded);
          }}
          data-testid="assistant-answer-disclosure-toggle"
        >
          {isExpanded ? (
            <ChevronUp className="h-4 w-4" aria-hidden="true" />
          ) : (
            <ChevronDown className="h-4 w-4" aria-hidden="true" />
          )}
          {t(
            isExpanded
              ? "publicAssistant.answerDisclosure.collapse"
              : "publicAssistant.answerDisclosure.expand",
          )}
        </Button>
      )}
    </div>
  );
}
