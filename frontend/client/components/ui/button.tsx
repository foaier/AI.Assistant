import * as React from "react";
import { Slot } from "@radix-ui/react-slot";
import { cva, type VariantProps } from "class-variance-authority";

import { cn } from "@/lib/utils";

const buttonVariants = cva(
  "inline-flex items-center justify-center whitespace-nowrap rounded-lg text-sm font-medium leading-none ring-offset-background transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 disabled:pointer-events-none disabled:border-[var(--border-disabled-token)] disabled:bg-[var(--border-disabled-token)] disabled:text-[var(--text-subtle)] disabled:opacity-100 [&_svg]:pointer-events-none [&_svg]:size-4 [&_svg]:shrink-0",
  {
    variants: {
      variant: {
        default: "border border-transparent bg-[var(--button-primary-bg)] text-[var(--button-primary-foreground)] hover:bg-[var(--button-primary-hover-bg)] active:bg-[var(--button-primary-pressed-bg)]",
        destructive:
          "border border-transparent bg-[var(--status-danger-text)] text-[var(--brand-primary-foreground)] hover:bg-[color-mix(in_srgb,var(--status-danger-text)_90%,transparent)] active:bg-[var(--status-danger-text)]",
        outline:
          "border border-[var(--border-control)] bg-[var(--surface-card)] text-[var(--text-primary)] hover:border-[var(--border-hover)] hover:bg-[var(--surface-hover)] active:border-[var(--brand-primary-pressed)] active:text-[var(--brand-primary-pressed)]",
        secondary:
          "border border-[var(--border-control)] bg-[var(--surface-card)] text-[var(--text-primary)] hover:border-[var(--border-hover)] hover:bg-[var(--surface-hover)] active:border-[var(--border-hover)]",
        ghost: "border border-transparent text-[var(--text-primary)] hover:bg-[var(--surface-hover)] active:bg-[var(--surface-neutral-muted)]",
        link: "h-auto min-w-0 rounded-none border border-transparent px-0 text-[var(--brand-primary)] underline-offset-4 hover:text-[var(--brand-primary-hover)] hover:no-underline active:text-[var(--brand-primary-pressed)]",
        roxPrimary: "rox-btn rox-btn--primary gap-2",
        roxBrandOutline: "rox-btn rox-btn--brand-outline gap-2",
        roxSecondary: "rox-btn rox-btn--secondary gap-2",
        roxSubtle: "rox-btn rox-btn--subtle gap-2",
        roxText: "rox-btn rox-btn--text gap-2",
      },
      size: {
        default: "h-9 min-w-20 gap-2 px-3 py-0",
        sm: "h-8 min-w-[52px] gap-1 px-2 py-0 text-xs [&_svg]:size-3.5",
        lg: "h-10 min-w-24 gap-2 px-4 py-0",
        icon: "h-9 w-9 min-w-0 gap-0 p-0",
        roxSm: "rox-btn--size-sm text-xs",
        roxDefault: "rox-btn--size-default",
        roxLg: "rox-btn--size-lg font-semibold",
        roxIconSm: "rox-btn--size-icon-sm",
        roxIcon: "rox-btn--size-icon-default",
        roxIconLg: "rox-btn--size-icon-lg",
      },
    },
    defaultVariants: {
      variant: "default",
      size: "default",
    },
  },
);

export interface ButtonProps
  extends React.ButtonHTMLAttributes<HTMLButtonElement>,
    VariantProps<typeof buttonVariants> {
  asChild?: boolean;
}

const Button = React.forwardRef<HTMLButtonElement, ButtonProps>(
  ({ className, variant, size, asChild = false, ...props }, ref) => {
    const Comp = asChild ? Slot : "button";
    return (
      <Comp
        className={cn(buttonVariants({ variant, size, className }))}
        ref={ref}
        {...props}
      />
    );
  },
);
Button.displayName = "Button";

export { Button, buttonVariants };
