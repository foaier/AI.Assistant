import * as React from "react";
import * as TabsPrimitive from "@radix-ui/react-tabs";

import { cn } from "@/lib/utils";

const Tabs = TabsPrimitive.Root;

type TabsVariant = "segmented" | "underline";

interface TabsListProps extends React.ComponentPropsWithoutRef<typeof TabsPrimitive.List> {
  variant?: TabsVariant;
}

interface TabsTriggerProps extends React.ComponentPropsWithoutRef<typeof TabsPrimitive.Trigger> {
  variant?: TabsVariant;
}

const tabsListClasses: Record<TabsVariant, string> = {
  segmented:
    "inline-flex h-9 items-center justify-center rounded-lg bg-[var(--surface-neutral-muted)] p-[3px] text-[var(--text-subtle)] shadow-none",
  underline:
    "inline-flex h-14 items-center justify-start gap-10 rounded-none border-0 bg-transparent p-0 text-[var(--text-subtle)] shadow-none",
};

const tabsTriggerClasses: Record<TabsVariant, string> = {
  segmented:
    "inline-flex h-[30px] items-center justify-center whitespace-nowrap rounded px-3 py-0 text-sm font-medium leading-none ring-offset-background transition-colors hover:bg-[color-mix(in_srgb,var(--surface-card)_80%,transparent)] hover:text-[var(--text-primary)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 disabled:pointer-events-none disabled:opacity-50 data-[state=active]:bg-[var(--surface-card)] data-[state=active]:font-semibold data-[state=active]:text-[var(--brand-primary)] data-[state=active]:shadow-none",
  underline:
    "inline-flex h-14 items-center justify-center whitespace-nowrap rounded-none border-b-2 border-transparent bg-transparent px-0 py-0 text-[16px] font-semibold leading-[1.4] text-[var(--text-subtle)] ring-offset-background transition-colors hover:bg-transparent hover:text-[var(--text-secondary)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-0 disabled:pointer-events-none disabled:opacity-50 data-[state=active]:border-[var(--brand-primary)] data-[state=active]:bg-transparent data-[state=active]:text-[var(--brand-primary)] data-[state=active]:shadow-none",
};

const TabsList = React.forwardRef<
  React.ElementRef<typeof TabsPrimitive.List>,
  TabsListProps
>(({ className, variant = "segmented", ...props }, ref) => (
  <TabsPrimitive.List
    ref={ref}
    className={cn(tabsListClasses[variant], className)}
    {...props}
  />
));
TabsList.displayName = TabsPrimitive.List.displayName;

const TabsTrigger = React.forwardRef<
  React.ElementRef<typeof TabsPrimitive.Trigger>,
  TabsTriggerProps
>(({ className, variant = "segmented", ...props }, ref) => (
  <TabsPrimitive.Trigger
    ref={ref}
    className={cn(tabsTriggerClasses[variant], className)}
    {...props}
  />
));
TabsTrigger.displayName = TabsPrimitive.Trigger.displayName;

const TabsContent = React.forwardRef<
  React.ElementRef<typeof TabsPrimitive.Content>,
  React.ComponentPropsWithoutRef<typeof TabsPrimitive.Content>
>(({ className, ...props }, ref) => (
  <TabsPrimitive.Content
    ref={ref}
    className={cn(
      "mt-3 ring-offset-background focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2",
      className,
    )}
    {...props}
  />
));
TabsContent.displayName = TabsPrimitive.Content.displayName;

export { Tabs, TabsList, TabsTrigger, TabsContent, tabsListClasses, tabsTriggerClasses };
