import * as React from "react";

import { cn } from "@/lib/utils";

const Input = React.forwardRef<HTMLInputElement, React.ComponentProps<"input">>(
  ({ className, type, ...props }, ref) => {
    return (
      <input
        type={type}
        className={cn(
          "flex h-9 w-full rounded-lg border border-[var(--border-control)] bg-[var(--surface-card)] px-3 py-0 text-sm text-[var(--text-secondary)] transition-colors file:border-0 file:bg-transparent file:text-sm file:font-medium file:text-[var(--text-primary)] placeholder:text-[var(--text-muted)] hover:border-[var(--border-hover)] hover:bg-[var(--surface-hover)] hover:text-[var(--text-primary)] focus-visible:border-[var(--brand-primary)] focus-visible:bg-[var(--surface-card)] focus-visible:text-[var(--text-primary)] focus-visible:outline-none focus-visible:[box-shadow:var(--focus-ring-shadow)] disabled:cursor-not-allowed disabled:border-[var(--border-disabled-token)] disabled:bg-[var(--surface-neutral-muted)] disabled:text-[var(--text-subtle)] disabled:opacity-100",
          className,
        )}
        ref={ref}
        {...props}
      />
    );
  },
);
Input.displayName = "Input";

export { Input };
