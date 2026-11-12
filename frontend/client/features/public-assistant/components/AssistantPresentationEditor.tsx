import { useState } from "react";
import { ArrowDown, ArrowUp, Plus, Trash2 } from "lucide-react";
import { ChevronDown, Upload, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui/collapsible";
import { AssistantCountedInput } from "./AssistantCountedField";
import { useI18n } from "@/context/i18nContext";
import {
  ASSET_IMAGE_ALLOWED_TYPES,
  ASSET_IMAGE_MAX_BYTES,
} from "@/services/assetService";
import {
  AssistantQuickLinkIcon,
  assistantQuickLinkIconNames,
} from "./AssistantIdentity";
import type { AssistantConfiguration } from "../services/publicAssistantService";

export function AssistantPresentationEditor({
  value,
  onChange,
  onUploadLinkIcon,
  uploadingLinkIconId,
  uploadErrorLinkIconId,
}: {
  value: AssistantConfiguration;
  onChange: (patch: Partial<AssistantConfiguration>) => void;
  onUploadLinkIcon: (linkId: string, file: File) => void;
  uploadingLinkIconId?: string;
  uploadErrorLinkIconId?: string;
}) {
  const { t } = useI18n();
  const p = (key: string) => t(`publicAssistant.experience.${key}`);
  const links = value.quickLinks ?? [];
  const questions = value.suggestedQuestions;
  const [navigationOpen, setNavigationOpen] = useState(false);
  const [uploadValidationError, setUploadValidationError] = useState<{
    linkId: string;
    message: string;
  } | null>(null);
  const handleLinkIconUpload = (linkId: string, file?: File) => {
    if (!file) return;
    if (!ASSET_IMAGE_ALLOWED_TYPES.includes(file.type)) {
      setUploadValidationError({ linkId, message: p("iconTypeError") });
      return;
    }
    if (file.size > ASSET_IMAGE_MAX_BYTES) {
      setUploadValidationError({ linkId, message: p("iconSizeError") });
      return;
    }
    setUploadValidationError(null);
    onUploadLinkIcon(linkId, file);
  };
  const move = (index: number, direction: number) => {
    const next = [...links];
    [next[index], next[index + direction]] = [
      next[index + direction],
      next[index],
    ];
    onChange({ quickLinks: next });
  };
  return (
    <div className="space-y-5" data-testid="assistant-presentation-editor">
      <fieldset className="space-y-3">
        <legend className="text-sm font-medium">{p("suggestions")}</legend>
        <p className="text-xs text-[var(--text-subtle)]">
          {p("suggestionsHelp")}
        </p>
        {questions.map((question, index) => (
          <div key={index} className="flex gap-2">
            <AssistantCountedInput
              containerClassName="flex-1"
              aria-label={`${p("question")} ${index + 1}`}
              maxLength={200}
              value={question}
              data-testid={`assistant-question-${index}`}
              onChange={(e) =>
                onChange({
                  suggestedQuestions: questions.map((v, i) =>
                    i === index ? e.target.value : v,
                  ),
                })
              }
            />
            <Button
              type="button"
              variant="ghost"
              size="icon"
              aria-label={`${p("removeQuestion")} ${index + 1}`}
              onClick={() =>
                onChange({
                  suggestedQuestions: questions.filter((_, i) => i !== index),
                })
              }
            >
              <Trash2 aria-hidden className="h-4 w-4" />
            </Button>
          </div>
        ))}
        <Button
          type="button"
          variant="outline"
          size="sm"
          disabled={questions.length >= 20}
          data-testid="assistant-add-question"
          onClick={() => onChange({ suggestedQuestions: [...questions, ""] })}
        >
          <Plus aria-hidden className="mr-2 h-4 w-4" />
          {p("addQuestion")}
        </Button>
      </fieldset>
      <section
        className="space-y-3"
        role="group"
        aria-labelledby="assistant-navigation-heading"
        data-testid="assistant-navigation-section"
      >
        <Collapsible open={navigationOpen} onOpenChange={setNavigationOpen}>
          <div className="flex items-center justify-between gap-3">
            <div className="min-w-0">
              <h3
                id="assistant-navigation-heading"
                className="text-sm font-medium"
              >
                {p("navigation")}
              </h3>
              <p className="text-xs text-[var(--text-subtle)]">
                {p("navigationCount").replace("{count}", String(links.length))}
                {" · "}
                {p(
                  value.quickLinksEnabled === false
                    ? "navigationDisabled"
                    : "navigationEnabled",
                )}
              </p>
            </div>
            <CollapsibleTrigger asChild>
              <Button
                type="button"
                variant="outline"
                size="icon"
                aria-label={
                  navigationOpen
                    ? p("collapseNavigation")
                    : p("expandNavigation")
                }
                data-testid="assistant-navigation-collapse"
              >
                <ChevronDown
                  aria-hidden
                  className={`h-4 w-4 transition-transform motion-reduce:transition-none ${navigationOpen ? "rotate-180" : ""}`}
                />
              </Button>
            </CollapsibleTrigger>
          </div>
          <label className="flex items-center gap-2 text-sm">
            <input
              type="checkbox"
              checked={value.quickLinksEnabled !== false}
              data-testid="assistant-navigation-enabled"
              onChange={(e) =>
                onChange({ quickLinksEnabled: e.target.checked })
              }
            />
            {p("showNavigation")}
          </label>
          <CollapsibleContent
            className="mt-3 space-y-3"
            data-testid="assistant-navigation-items"
          >
            <p className="text-xs text-[var(--text-subtle)]">
              {p("navigationHelp")}
            </p>
            {links.map((link, index) => (
              <div
                key={link.id}
                className="space-y-3 rounded-lg border border-[var(--border-subtle)] p-3"
                data-testid={`assistant-navigation-${index}`}
              >
                <div className="grid gap-3 sm:grid-cols-2">
                  <label className="space-y-1 text-sm">
                    <span>{p("linkLabel")}</span>
                    <AssistantCountedInput
                      maxLength={60}
                      value={link.label}
                      data-testid={`assistant-navigation-label-${index}`}
                      onChange={(e) =>
                        onChange({
                          quickLinks: links.map((v) =>
                            v.id === link.id
                              ? { ...v, label: e.target.value }
                              : v,
                          ),
                        })
                      }
                    />
                  </label>
                  <label className="space-y-1 text-sm">
                    <span>{p("linkUrl")}</span>
                    <AssistantCountedInput
                      maxLength={2000}
                      value={link.url}
                      placeholder="https://"
                      data-testid={`assistant-navigation-url-${index}`}
                      onChange={(e) =>
                        onChange({
                          quickLinks: links.map((v) =>
                            v.id === link.id
                              ? { ...v, url: e.target.value }
                              : v,
                          ),
                        })
                      }
                    />
                  </label>
                </div>
                <div className="flex flex-wrap items-center gap-2">
                  <div className="relative min-w-[180px]">
                    <AssistantQuickLinkIcon
                      name={link.icon}
                      imageUrl={link.iconUrl}
                      className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-[var(--brand-primary)]"
                    />
                    <select
                      className="h-10 w-full rounded-lg border border-[var(--border-control)] bg-[var(--surface-card)] pl-10 pr-3 text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-[var(--brand-primary)]"
                      aria-label={`${p("icon")} ${index + 1}`}
                      value={link.icon}
                      onChange={(e) =>
                        onChange({
                          quickLinks: links.map((v) =>
                            v.id === link.id
                              ? { ...v, icon: e.target.value }
                              : v,
                          ),
                        })
                      }
                    >
                      {assistantQuickLinkIconNames.map((icon) => (
                        <option key={icon} value={icon}>
                          {p(`icon.${icon}`)}
                        </option>
                      ))}
                    </select>
                  </div>
                  <label
                    className={`relative inline-flex h-9 cursor-pointer items-center gap-2 rounded-md border border-[var(--border-control)] px-3 text-sm hover:bg-[var(--surface-hover)] focus-within:ring-2 focus-within:ring-[var(--brand-primary)] focus-within:ring-offset-2 ${uploadingLinkIconId ? "pointer-events-none opacity-60" : ""}`}
                  >
                    <Upload aria-hidden className="h-4 w-4" />
                    {uploadingLinkIconId === link.id
                      ? p("iconUploading")
                      : link.iconUrl
                        ? p("replaceCustomIcon")
                        : p("uploadCustomIcon")}
                    <input
                      type="file"
                      accept={ASSET_IMAGE_ALLOWED_TYPES.join(",")}
                      className="absolute inset-0 h-full w-full cursor-pointer opacity-0 disabled:cursor-not-allowed"
                      disabled={Boolean(uploadingLinkIconId)}
                      aria-label={`${p("uploadCustomIcon")} ${index + 1}`}
                      data-testid={`assistant-navigation-icon-upload-${index}`}
                      onChange={(event) => {
                        handleLinkIconUpload(
                          link.id,
                          event.currentTarget.files?.[0],
                        );
                        event.currentTarget.value = "";
                      }}
                    />
                  </label>
                  {link.iconUrl && (
                    <Button
                      type="button"
                      variant="ghost"
                      size="icon"
                      aria-label={`${p("removeCustomIcon")} ${index + 1}`}
                      data-testid={`assistant-navigation-icon-remove-${index}`}
                      onClick={() =>
                        onChange({
                          quickLinks: links.map((v) =>
                            v.id === link.id
                              ? { ...v, iconUrl: null, iconAssetId: null }
                              : v,
                          ),
                        })
                      }
                    >
                      <X aria-hidden className="h-4 w-4" />
                    </Button>
                  )}
                  <label className="mr-auto flex items-center gap-2 text-sm">
                    <input
                      type="checkbox"
                      checked={link.enabled}
                      data-testid={`assistant-navigation-visible-${index}`}
                      onChange={(e) =>
                        onChange({
                          quickLinks: links.map((v) =>
                            v.id === link.id
                              ? { ...v, enabled: e.target.checked }
                              : v,
                          ),
                        })
                      }
                    />
                    {p("visible")}
                  </label>
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    disabled={index === 0}
                    aria-label={`${p("moveUp")} ${index + 1}`}
                    onClick={() => move(index, -1)}
                  >
                    <ArrowUp aria-hidden className="h-4 w-4" />
                  </Button>
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    disabled={index === links.length - 1}
                    aria-label={`${p("moveDown")} ${index + 1}`}
                    onClick={() => move(index, 1)}
                  >
                    <ArrowDown aria-hidden className="h-4 w-4" />
                  </Button>
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    aria-label={`${p("removeLink")} ${index + 1}`}
                    onClick={() =>
                      onChange({
                        quickLinks: links.filter((v) => v.id !== link.id),
                      })
                    }
                  >
                    <Trash2 aria-hidden className="h-4 w-4" />
                  </Button>
                </div>
                {(uploadValidationError?.linkId === link.id ||
                  uploadErrorLinkIconId === link.id) && (
                  <p
                    role="alert"
                    className="text-xs text-[var(--status-danger-text)]"
                  >
                    {uploadValidationError?.linkId === link.id
                      ? uploadValidationError.message
                      : p("iconUploadFailed")}
                  </p>
                )}
              </div>
            ))}
            <Button
              type="button"
              variant="outline"
              size="sm"
              disabled={links.length >= 12}
              data-testid="assistant-add-navigation"
              onClick={() =>
                onChange({
                  quickLinks: [
                    ...links,
                    {
                      id: crypto.randomUUID(),
                      label: "",
                      url: "",
                      icon: "link",
                      enabled: true,
                    },
                  ],
                })
              }
            >
              <Plus aria-hidden className="mr-2 h-4 w-4" />
              {p("addLink")}
            </Button>
          </CollapsibleContent>
        </Collapsible>
      </section>
      <label className="flex items-start gap-2 text-sm">
        <input
          type="checkbox"
          className="mt-1"
          checked={value.historyEnabled !== false}
          data-testid="assistant-history-enabled"
          onChange={(e) => onChange({ historyEnabled: e.target.checked })}
        />
        <span>
          {p("history")}
          <span className="mt-1 block text-xs text-[var(--text-subtle)]">
            {p("historyHelp")}
          </span>
        </span>
      </label>
    </div>
  );
}
