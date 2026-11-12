import * as React from "react";
import * as DialogPrimitive from "@radix-ui/react-dialog";
import { X } from "lucide-react";

import { cn } from "@/lib/utils";

const Dialog = DialogPrimitive.Root;

const DialogTrigger = DialogPrimitive.Trigger;

const DialogPortal = DialogPrimitive.Portal;

const DialogClose = DialogPrimitive.Close;

const DialogOverlay = React.forwardRef<
  React.ElementRef<typeof DialogPrimitive.Overlay>,
  React.ComponentPropsWithoutRef<typeof DialogPrimitive.Overlay>
>(({ className, ...props }, ref) => (
  <DialogPrimitive.Overlay
    ref={ref}
    className={cn(
      "fixed inset-0 z-50 bg-black/45 data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0",
      className,
    )}
    {...props}
  />
));
DialogOverlay.displayName = DialogPrimitive.Overlay.displayName;

interface DialogContentProps
  extends React.ComponentPropsWithoutRef<typeof DialogPrimitive.Content> {
  closeButtonTestId?: string;
  closeButtonClassName?: string;
  fixedLayout?: boolean;
}

const DialogLayoutContext = React.createContext(false);

const DialogContent = React.forwardRef<
  React.ElementRef<typeof DialogPrimitive.Content>,
  DialogContentProps
>(({ className, children, closeButtonTestId, closeButtonClassName, fixedLayout = false, ...props }, ref) => (
  <DialogPortal>
    <DialogOverlay />
    <DialogPrimitive.Content
      ref={ref}
      className={cn(
        "fixed left-[50%] top-[50%] z-50 grid max-h-[calc(100vh-80px)] w-[calc(100vw-40px)] max-w-[640px] translate-x-[-50%] translate-y-[-50%] gap-4 overflow-auto rounded-lg border border-[var(--border-subtle)] bg-[var(--surface-overlay)] p-6 text-[var(--text-primary)] [box-shadow:var(--shadow-layer-md)] duration-200 data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-95 data-[state=closed]:slide-out-to-left-1/2 data-[state=closed]:slide-out-to-top-[48%] data-[state=open]:slide-in-from-left-1/2 data-[state=open]:slide-in-from-top-[48%]",
        fixedLayout && "flex h-[calc(100vh-80px)] max-h-[calc(100vh-80px)] flex-col gap-0 overflow-hidden p-0",
        className,
      )}
      {...props}
    >
      <DialogLayoutContext.Provider value={fixedLayout}>
        {children}
        <DialogPrimitive.Close
          className={cn(
            "absolute right-4 top-4 z-20 flex h-8 w-8 items-center justify-center rounded-lg text-[var(--text-muted)] opacity-80 ring-offset-background transition-colors hover:bg-[var(--surface-hover)] hover:text-[var(--text-primary)] hover:opacity-100 focus:outline-none focus:ring-2 focus:ring-ring focus:ring-offset-2 disabled:pointer-events-none data-[state=open]:bg-[var(--surface-hover)] data-[state=open]:text-[var(--text-muted)]",
            closeButtonClassName,
          )}
          data-testid={closeButtonTestId}
        >
          <X className="h-4 w-4" />
          <span className="sr-only">Close</span>
        </DialogPrimitive.Close>
      </DialogLayoutContext.Provider>
    </DialogPrimitive.Content>
  </DialogPortal>
));
DialogContent.displayName = DialogPrimitive.Content.displayName;

const DialogHeader = ({
  className,
  ...props
}: React.HTMLAttributes<HTMLDivElement>) => {
  const fixedLayout = React.useContext(DialogLayoutContext);

  return (
    <div
      className={cn(
        "sticky top-0 z-10 flex flex-col space-y-1.5 bg-[var(--surface-overlay)] text-center sm:text-left",
        fixedLayout && "shrink-0 border-b border-[var(--border-subtle)] px-6 py-5 pr-14",
        className,
      )}
      {...props}
    />
  );
};
DialogHeader.displayName = "DialogHeader";

const DialogFooter = ({
  className,
  ...props
}: React.HTMLAttributes<HTMLDivElement>) => {
  const fixedLayout = React.useContext(DialogLayoutContext);

  return (
    <div
      className={cn(
        "sticky bottom-0 z-10 flex flex-col-reverse bg-[var(--surface-overlay)] sm:flex-row sm:justify-end sm:space-x-2",
        fixedLayout && "shrink-0 border-t border-[var(--border-subtle)] px-6 py-4",
        className,
      )}
      {...props}
    />
  );
};
DialogFooter.displayName = "DialogFooter";

const DialogBody = ({
  className,
  ...props
}: React.HTMLAttributes<HTMLDivElement>) => (
  <div className={cn("min-h-0 flex-1 overflow-y-auto px-6 py-5", className)} {...props} />
);
DialogBody.displayName = "DialogBody";

const DialogTitle = React.forwardRef<
  React.ElementRef<typeof DialogPrimitive.Title>,
  React.ComponentPropsWithoutRef<typeof DialogPrimitive.Title>
>(({ className, ...props }, ref) => (
  <DialogPrimitive.Title
    ref={ref}
    className={cn(
      "text-lg font-medium leading-tight tracking-normal",
      className,
    )}
    {...props}
  />
));
DialogTitle.displayName = DialogPrimitive.Title.displayName;

const DialogDescription = React.forwardRef<
  React.ElementRef<typeof DialogPrimitive.Description>,
  React.ComponentPropsWithoutRef<typeof DialogPrimitive.Description>
>(({ className, ...props }, ref) => (
  <DialogPrimitive.Description
    ref={ref}
    className={cn("text-sm text-[var(--text-subtle)]", className)}
    {...props}
  />
));
DialogDescription.displayName = DialogPrimitive.Description.displayName;

export {
  Dialog,
  DialogPortal,
  DialogOverlay,
  DialogClose,
  DialogTrigger,
  DialogContent,
  DialogHeader,
  DialogBody,
  DialogFooter,
  DialogTitle,
  DialogDescription,
};
