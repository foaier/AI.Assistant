import type { ReactNode } from 'react';
import { ChevronLeft } from 'lucide-react';
import { Link } from 'react-router-dom';

import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';

import { AdminStatPill, type AdminStatPillProps } from './AdminStatPill';

export type AdminPageHeadingStat = AdminStatPillProps;

interface AdminPageHeadingProps {
  title: ReactNode;
  subtitle?: ReactNode;
  /** @deprecated Pass onBack/to + backLabel so the §4.14 built-in back renders. */
  back?: ReactNode;
  /** Semantic back: navigate programmatically. Mutually exclusive with to. */
  onBack?: () => void;
  /** Semantic back: link target path. Mutually exclusive with onBack. */
  to?: string;
  /** Accessible label + tooltip for the built-in back control. */
  backLabel?: string;
  /** Recommended: AdminStatPill object array. Legacy ReactNode still renders. */
  stats?: AdminPageHeadingStat[] | ReactNode;
  actions?: ReactNode;
  className?: string;
}

export function AdminPageHeading({
  title,
  subtitle,
  back,
  onBack,
  to,
  backLabel,
  stats,
  actions,
  className,
}: AdminPageHeadingProps) {
  const builtInBack = to || onBack ? (
    <Button
      asChild={Boolean(to)}
      variant="secondary"
      size="icon"
      aria-label={backLabel ?? '返回'}
      title={backLabel}
      onClick={onBack}
    >
      {to ? <Link to={to}><ChevronLeft aria-hidden /></Link> : <ChevronLeft aria-hidden />}
    </Button>
  ) : back;

  const statsNode = Array.isArray(stats)
    ? stats.map((stat, index) => (
      <AdminStatPill key={index} {...stat} />
    ))
    : stats;

  return (
    <div className={cn('flex min-w-0 flex-col gap-3 lg:flex-row lg:items-start lg:justify-between', className)}>
      <div className="flex min-w-0 flex-col gap-3 sm:flex-row sm:items-start">
        {builtInBack ? <div className="shrink-0">{builtInBack}</div> : null}
        <div className="min-w-0">
          <h1 className="text-[28px] font-bold leading-[1.2] tracking-normal text-[var(--text-primary)]">
            {title}
          </h1>
          {subtitle ? (
            <p className="mt-1 max-w-3xl text-sm leading-[1.4] text-[var(--text-subtle)]">
              {subtitle}
            </p>
          ) : null}
          {statsNode ? <div className="mt-2 flex flex-wrap items-center gap-2">{statsNode}</div> : null}
        </div>
      </div>
      {actions ? (
        <div className="flex shrink-0 flex-wrap items-center gap-2 lg:justify-end">
          {actions}
        </div>
      ) : null}
    </div>
  );
}
