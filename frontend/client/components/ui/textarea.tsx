import * as React from "react";

import { cn } from "@/lib/utils";

export interface TextareaProps
  extends React.TextareaHTMLAttributes<HTMLTextAreaElement> {}

const Textarea = React.forwardRef<HTMLTextAreaElement, TextareaProps>(
  ({ className, ...props }, ref) => {
    return (
      <textarea
        className={cn(
          "flex min-h-[96px] w-full rounded-lg border border-[var(--border-control)] bg-[var(--surface-card)] px-3 py-2 text-sm text-[var(--text-secondary)] transition-colors placeholder:text-[var(--text-muted)] hover:border-[var(--border-hover)] hover:bg-[var(--surface-hover)] hover:text-[var(--text-primary)] focus-visible:border-[var(--brand-primary)] focus-visible:bg-[var(--surface-card)] focus-visible:text-[var(--text-primary)] focus-visible:outline-none focus-visible:[box-shadow:var(--focus-ring-shadow)] disabled:cursor-not-allowed disabled:border-[var(--border-disabled-token)] disabled:bg-[var(--surface-neutral-muted)] disabled:text-[var(--text-subtle)] disabled:opacity-100",
          className,
        )}
        ref={ref}
        {...props}
      />
    );
  },
);
Textarea.displayName = "Textarea";

export { Textarea };
