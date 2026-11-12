import type { ReactNode } from 'react';

import { cn } from '@/lib/utils';

// DESIGN.md §4.14: 24px height, radius-sm(2px) Badge family. Grey is the
// resting tone; brand-soft marks an active filter; status tones follow the
// RFC-066 §3.2 status palette. Values mirror pricingConsoleStatPillClass /
// pricingConsoleFilteredPillClass until those call sites migrate here.
export type AdminStatPillTone = 'neutral' | 'brand' | 'success' | 'warning' | 'danger';

const toneClass: Record<AdminStatPillTone, string> = {
  neutral: 'bg-[var(--surface-neutral-muted)] font-medium text-[var(--text-subtle)]',
  brand: 'bg-[var(--surface-active)] font-semibold text-[var(--brand-primary)]',
  success: 'bg-[var(--status-success-bg)] font-medium text-[var(--status-success-text)]',
  warning: 'bg-[var(--status-warning-bg)] font-medium text-[var(--status-warning-text)]',
  danger: 'bg-[var(--status-danger-bg)] font-medium text-[var(--status-danger-text)]',
};

export interface AdminStatPillProps {
  label: ReactNode;
  value?: ReactNode;
  tone?: AdminStatPillTone;
  className?: string;
  'data-testid'?: string;
}

export function AdminStatPill({
  label,
  value,
  tone = 'neutral',
  className,
  'data-testid': dataTestId,
}: AdminStatPillProps) {
  return (
    <span
      data-testid={dataTestId}
      className={cn(
        'inline-flex h-6 items-center rounded-[2px] px-2 text-xs tracking-normal',
        toneClass[tone],
        className,
      )}
    >
      {label}
      {value !== undefined && value !== null ? <> {value}</> : null}
    </span>
  );
}
