import * as React from "react";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { cn } from "@/lib/utils";

type TextControl = HTMLInputElement | HTMLTextAreaElement;
type CountedProps = {
  value: string;
  maxLength: number;
  containerClassName?: string;
  "data-testid"?: string;
};

function truncate(value: string, limit: number) {
  const result = value.slice(0, Math.max(0, limit));
  // Existing APIs count UTF-16 units; never leave half an emoji at the boundary.
  return /[\uD800-\uDBFF]$/.test(result) ? result.slice(0, -1) : result;
}

function limitEdit(previous: string, next: string, limit: number) {
  if (next.length <= limit) return truncate(next, limit);
  let start = 0;
  while (start < previous.length && previous[start] === next[start]) start++;
  let end = 0;
  while (
    end < previous.length - start &&
    end < next.length - start &&
    previous[previous.length - end - 1] === next[next.length - end - 1]
  )
    end++;
  const suffix = end ? next.slice(-end) : "";
  const available = limit - start - suffix.length;
  if (available < 0) return truncate(next, limit);
  return (
    next.slice(0, start) +
    truncate(next.slice(start, next.length - end), available) +
    suffix
  );
}

function useCountedField<T extends TextControl>({
  value,
  maxLength,
  onChange,
  onCompositionStart,
  onCompositionEnd,
  onPaste,
}: CountedProps & React.DOMAttributes<T>) {
  const id = React.useId();
  const composing = React.useRef(false);
  const [draft, setDraft] = React.useState<string | null>(null);
  const emit = (event: React.SyntheticEvent<T>, next: string) => {
    event.currentTarget.value = next;
    onChange?.(event as React.ChangeEvent<T>);
  };
  return {
    counterId: `${id}-count`,
    displayedValue: draft ?? value,
    handlers: {
      value: draft ?? value,
      maxLength: draft === null ? maxLength : undefined,
      onChange: (event: React.ChangeEvent<T>) => {
        if (composing.current) setDraft(event.currentTarget.value);
        else
          emit(event, limitEdit(value, event.currentTarget.value, maxLength));
      },
      onCompositionStart: (event: React.CompositionEvent<T>) => {
        composing.current = true;
        setDraft(event.currentTarget.value);
        onCompositionStart?.(event);
      },
      onCompositionEnd: (event: React.CompositionEvent<T>) => {
        composing.current = false;
        setDraft(null);
        emit(event, limitEdit(value, event.currentTarget.value, maxLength));
        onCompositionEnd?.(event);
      },
      onPaste: (event: React.ClipboardEvent<T>) => {
        onPaste?.(event);
        if (event.defaultPrevented || composing.current) return;
        const input = event.currentTarget;
        const start = input.selectionStart ?? value.length;
        const end = input.selectionEnd ?? start;
        const pasted = event.clipboardData
          .getData("text")
          .replace(
            input instanceof HTMLInputElement ? /[\r\n]/g : /\r\n?/g,
            input instanceof HTMLInputElement ? "" : "\n",
          );
        const available = maxLength - value.length + end - start;
        if (pasted.length <= available) return;
        event.preventDefault();
        const inserted = truncate(pasted, available);
        const next = value.slice(0, start) + inserted + value.slice(end);
        emit(event, truncate(next, maxLength));
        input.setSelectionRange(
          start + inserted.length,
          start + inserted.length,
        );
      },
    },
  };
}

function Counter({
  id,
  value,
  limit,
  testId,
  multiline,
}: {
  id: string;
  value: string;
  limit: number;
  testId?: string;
  multiline?: boolean;
}) {
  return (
    <span
      id={id}
      aria-hidden="true"
      data-testid={testId ? `${testId}-count` : undefined}
      className={cn(
        "pointer-events-none absolute bottom-2 right-3 whitespace-nowrap text-xs leading-4 tabular-nums text-[var(--text-secondary)]",
        multiline && "right-5",
        value.length >= limit && "font-medium",
        value.length > limit && "text-red-700 dark:text-red-300",
      )}
    >
      {value.length} / {limit}
    </span>
  );
}

export const AssistantCountedInput = React.forwardRef<
  HTMLInputElement,
  Omit<React.ComponentProps<typeof Input>, "value" | "maxLength"> & CountedProps
>(function AssistantCountedInput(
  { containerClassName, className, ...props },
  ref,
) {
  const { counterId, displayedValue, handlers } =
    useCountedField<HTMLInputElement>(props);
  return (
    <span className={cn("relative block w-full min-w-0", containerClassName)}>
      <Input
        {...props}
        {...handlers}
        ref={ref}
        className={cn(className, "pr-24")}
        aria-describedby={[props["aria-describedby"], counterId]
          .filter(Boolean)
          .join(" ")}
      />
      <Counter
        id={counterId}
        value={displayedValue}
        limit={props.maxLength}
        testId={props["data-testid"]}
      />
    </span>
  );
});

export const AssistantCountedTextarea = React.forwardRef<
  HTMLTextAreaElement,
  Omit<React.ComponentProps<typeof Textarea>, "value" | "maxLength"> &
    CountedProps
>(function AssistantCountedTextarea(
  { containerClassName, className, ...props },
  ref,
) {
  const { counterId, displayedValue, handlers } =
    useCountedField<HTMLTextAreaElement>(props);
  return (
    <span className={cn("relative block w-full min-w-0", containerClassName)}>
      <Textarea
        {...props}
        {...handlers}
        ref={ref}
        className={cn(className, "pb-7 pr-24")}
        aria-describedby={[props["aria-describedby"], counterId]
          .filter(Boolean)
          .join(" ")}
      />
      <Counter
        id={counterId}
        value={displayedValue}
        limit={props.maxLength}
        testId={props["data-testid"]}
        multiline
      />
    </span>
  );
});
