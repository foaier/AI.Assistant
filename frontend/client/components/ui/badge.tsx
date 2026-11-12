import * as React from "react";
import { cva, type VariantProps } from "class-variance-authority";

import { cn } from "@/lib/utils";

const badgeVariants = cva(
  "inline-flex h-[22px] items-center rounded-[2px] border px-2 text-xs font-medium leading-none transition-colors focus:outline-none focus:ring-2 focus:ring-ring focus:ring-offset-2",
  {
    variants: {
      variant: {
        default:
          "border-0 bg-[var(--status-info-light)] text-[var(--status-info-strong)]",
        secondary:
          "border-0 bg-[var(--status-neutral-light)] text-[var(--status-neutral-strong)]",
        destructive:
          "border-0 bg-[var(--status-danger-light)] text-[var(--status-danger-strong)]",
        outline: "border-[var(--border-control)] bg-transparent text-[var(--text-primary)]",
        success:
          "border-0 bg-[var(--status-success-light)] text-[var(--status-success-strong)]",
        warning:
          "border-0 bg-[var(--status-warning-light)] text-[var(--status-warning-strong)]",
        info:
          "border-0 bg-[var(--status-info-light)] text-[var(--status-info-strong)]",
      },
    },
    defaultVariants: {
      variant: "default",
    },
  },
);

export interface BadgeProps
  extends React.HTMLAttributes<HTMLDivElement>,
    VariantProps<typeof badgeVariants> {}

function Badge({ className, variant, ...props }: BadgeProps) {
  return (
    <div className={cn(badgeVariants({ variant }), className)} {...props} />
  );
}

export { Badge, badgeVariants };
