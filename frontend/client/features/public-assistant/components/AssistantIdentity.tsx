import { useState } from "react";
import {
  Building2,
  BookOpenText,
  BriefcaseBusiness,
  CalendarDays,
  Car,
  Compass,
  CircleHelp,
  ClipboardCheck,
  Clock3,
  ExternalLink,
  Globe2,
  HeartHandshake,
  Hotel,
  Info,
  Mail,
  MapPin,
  MessageCircleQuestion,
  Package,
  Phone,
  ShieldCheck,
  Sparkles,
  Store,
  Ticket,
  TrainFront,
  Users,
} from "lucide-react";
import {
  isSafeAssistantIconUrl,
  isSafeAssistantLink,
} from "../services/assistantPresentation";

export const assistantQuickLinkIconNames = [
  "link",
  "map",
  "calendar",
  "help",
  "contact",
  "registration",
  "exhibitor",
  "products",
  "transport",
  "hotel",
  "building",
  "ticket",
  "users",
  "info",
  "guide",
  "time",
  "support",
  "email",
  "website",
  "venue",
  "business",
  "security",
  "compass",
  "rail",
] as const;

const assistantQuickLinkIcons = {
  link: ExternalLink,
  map: MapPin,
  calendar: CalendarDays,
  help: CircleHelp,
  contact: Phone,
  registration: ClipboardCheck,
  exhibitor: Store,
  products: Package,
  transport: Car,
  hotel: Hotel,
  building: Building2,
  ticket: Ticket,
  users: Users,
  info: Info,
  guide: BookOpenText,
  time: Clock3,
  support: HeartHandshake,
  email: Mail,
  website: Globe2,
  venue: Compass,
  business: BriefcaseBusiness,
  security: ShieldCheck,
  compass: Compass,
  rail: TrainFront,
};

const assistantQuickLinkTones: Record<string, string> = {
  link: "bg-slate-100 text-slate-700",
  map: "bg-emerald-100 text-emerald-700",
  calendar: "bg-sky-100 text-sky-700",
  help: "bg-violet-100 text-violet-700",
  contact: "bg-rose-100 text-rose-700",
  registration: "bg-blue-100 text-blue-700",
  exhibitor: "bg-amber-100 text-amber-800",
  products: "bg-fuchsia-100 text-fuchsia-700",
  transport: "bg-teal-100 text-teal-700",
  hotel: "bg-orange-100 text-orange-800",
  building: "bg-indigo-100 text-indigo-700",
  ticket: "bg-red-100 text-red-700",
  users: "bg-cyan-100 text-cyan-800",
  info: "bg-lime-100 text-lime-800",
  guide: "bg-indigo-100 text-indigo-700",
  time: "bg-sky-100 text-sky-700",
  support: "bg-pink-100 text-pink-700",
  email: "bg-cyan-100 text-cyan-800",
  website: "bg-teal-100 text-teal-700",
  venue: "bg-emerald-100 text-emerald-700",
  business: "bg-amber-100 text-amber-800",
  security: "bg-green-100 text-green-800",
  compass: "bg-blue-100 text-blue-700",
  rail: "bg-orange-100 text-orange-800",
};

export function AssistantQuickLinkIcon({
  name,
  imageUrl,
  className = "h-5 w-5",
}: {
  name: string;
  imageUrl?: string | null;
  className?: string;
}) {
  const [failedImageUrl, setFailedImageUrl] = useState<string | null>(null);
  const Icon =
    assistantQuickLinkIcons[name as keyof typeof assistantQuickLinkIcons] ??
    ExternalLink;
  if (
    imageUrl &&
    isSafeAssistantIconUrl(imageUrl) &&
    failedImageUrl !== imageUrl
  ) {
    return (
      <img
        src={imageUrl}
        alt=""
        aria-hidden="true"
        className={`object-contain ${className}`}
        data-testid="assistant-quick-link-image"
        onError={() => setFailedImageUrl(imageUrl)}
      />
    );
  }
  return (
    <Icon
      className={className}
      aria-hidden="true"
      data-testid="assistant-quick-link-icon"
      data-icon={name}
    />
  );
}

export function AssistantAvatar({
  url,
  className = "h-8 w-8",
}: {
  url?: string;
  className?: string;
}) {
  const [failed, setFailed] = useState<string>();
  return (
    <span
      className={`inline-flex shrink-0 items-center justify-center overflow-hidden rounded-lg bg-[var(--surface-card)] text-[var(--brand-primary)] ring-1 ring-[var(--border-control)] ${className}`}
      data-testid="assistant-avatar"
    >
      {url && failed !== url ? (
        <img
          src={url}
          alt=""
          width={48}
          height={48}
          className="h-full w-full object-cover"
          onError={() => setFailed(url)}
        />
      ) : (
        <span className="relative inline-flex h-6 w-6 items-center justify-center">
          <MessageCircleQuestion
            aria-hidden="true"
            className="h-5 w-5"
            strokeWidth={1.8}
          />
          <Sparkles
            aria-hidden="true"
            className="absolute -right-1 -top-1 h-3 w-3"
            strokeWidth={2}
          />
        </span>
      )}
    </span>
  );
}
export function AssistantQuickLinks({
  links,
  compact = false,
}: {
  compact?: boolean;
  links?: Array<{
    id: string;
    label: string;
    url: string;
    icon: string;
    iconUrl?: string | null;
  }>;
}) {
  return (
    <div
      className={
        compact
          ? "flex gap-2 overflow-x-auto py-1"
          : "grid grid-cols-3 items-start gap-2 sm:gap-3 md:grid-cols-6"
      }
      data-testid="assistant-quick-links"
    >
      {links
        ?.filter((x) => isSafeAssistantLink(x.url))
        .map((item) => {
          const iconTone =
            assistantQuickLinkTones[item.icon] ?? assistantQuickLinkTones.link;
          return (
            <a
              key={item.id}
              href={item.url}
              target="_blank"
              rel="noopener noreferrer"
              aria-label={item.label}
              title={item.label}
              data-testid="assistant-quick-link"
              className={`group relative flex min-w-0 items-center gap-3 border border-[var(--border-subtle)] bg-white/85 text-sm shadow-sm transition-[border-color,background-color,transform] duration-150 hover:border-[var(--brand-primary)]/40 hover:bg-white focus-visible:outline focus-visible:outline-2 focus-visible:outline-[var(--brand-primary)] motion-reduce:transition-none ${compact ? "min-h-11 max-w-[80vw] shrink-0 rounded-lg px-3 py-2" : "min-h-[84px] flex-col justify-center rounded-lg px-2 py-2.5 text-center hover:-translate-y-0.5 motion-reduce:hover:transform-none sm:min-h-[92px] sm:gap-2 sm:px-3 md:min-h-[104px]"}`}
            >
              <span
                className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-md sm:h-11 sm:w-11 ${iconTone}`}
              >
                <AssistantQuickLinkIcon
                  name={item.icon}
                  imageUrl={item.iconUrl}
                  className="h-5 w-5 sm:h-6 sm:w-6"
                />
              </span>
              <span
                className={`min-w-0 break-words line-clamp-2 ${compact ? "text-left" : "text-center"}`}
              >
                {item.label}
              </span>
              {compact && (
                <ExternalLink
                  className="ml-auto h-3.5 w-3.5 shrink-0 text-[var(--text-subtle)]"
                  aria-hidden="true"
                />
              )}
            </a>
          );
        })}
    </div>
  );
}
