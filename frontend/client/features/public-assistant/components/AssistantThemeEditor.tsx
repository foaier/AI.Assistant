import { Check } from "lucide-react";
import { useI18n } from "@/context/i18nContext";
import type { AssistantConfiguration } from "../services/publicAssistantService";
import {
  assistantThemePresets,
  assistantThemeReadable,
} from "../services/assistantTheme";

export function AssistantThemeEditor({
  primaryColor,
  chatBackgroundColor,
  onChange,
}: {
  primaryColor: string;
  chatBackgroundColor?: string | null;
  onChange: (
    patch: Partial<
      Pick<AssistantConfiguration, "primaryColor" | "chatBackgroundColor">
    >,
  ) => void;
}) {
  const { t } = useI18n();
  const selectedPreset = assistantThemePresets.find(
    (preset) =>
      preset.primaryColor.toLowerCase() === primaryColor.toLowerCase() &&
      preset.chatBackgroundColor.toLowerCase() ===
        chatBackgroundColor?.toLowerCase(),
  );

  return (
    <fieldset className="space-y-3" data-testid="assistant-theme-editor">
      <legend className="text-sm font-medium">
        {t("publicAssistant.settings.themePresets")}
      </legend>
      <div
        className="grid grid-cols-1 gap-2 sm:grid-cols-2"
        role="group"
        aria-label={t("publicAssistant.settings.themePresets")}
        data-testid="assistant-settings-theme-palette"
      >
        {assistantThemePresets.map((preset) => {
          const selected = selectedPreset?.id === preset.id;
          return (
            <button
              key={preset.id}
              type="button"
              aria-pressed={selected}
              aria-label={t(`publicAssistant.settings.themePreset.${preset.id}`)}
              className={`flex min-h-11 items-center gap-3 rounded-md border px-3 py-2 text-left text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--brand-primary)] ${selected ? "border-[var(--brand-primary)] bg-[var(--surface-neutral-muted)]" : "border-[var(--border-control)] bg-[var(--surface-card)] hover:bg-[var(--surface-neutral-muted)]"}`}
              onClick={() =>
                onChange({
                  primaryColor: preset.primaryColor,
                  chatBackgroundColor: preset.chatBackgroundColor,
                })
              }
              data-testid={`assistant-theme-preset-${preset.id}`}
            >
              <span className="flex shrink-0 gap-1" aria-hidden="true">
                <span
                  className="h-5 w-5 rounded-sm border border-black/10"
                  style={{ backgroundColor: preset.primaryColor }}
                />
                <span
                  className="h-5 w-5 rounded-sm border border-black/10"
                  style={{ backgroundColor: preset.chatBackgroundColor }}
                />
              </span>
              <span className="min-w-0 flex-1 break-words">
                {t(`publicAssistant.settings.themePreset.${preset.id}`)}
              </span>
              {selected && <Check aria-hidden="true" className="h-4 w-4 shrink-0" />}
            </button>
          );
        })}
      </div>

      <div className="grid gap-3 sm:grid-cols-2">
        <label className="flex min-h-11 items-center gap-3 rounded-md border border-[var(--border-subtle)] px-3 py-2 text-sm">
          <input
            type="color"
            value={primaryColor}
            aria-label={t("publicAssistant.settings.color")}
            className="h-8 w-10 cursor-pointer rounded border border-[var(--border-control)] bg-transparent p-0.5"
            onChange={(event) => onChange({ primaryColor: event.target.value })}
            data-testid="assistant-theme-primary-color"
          />
          <span className="min-w-0 flex-1 break-words">
            {t("publicAssistant.settings.color")}
          </span>
        </label>
        <label className="flex min-h-11 items-center gap-3 rounded-md border border-[var(--border-subtle)] px-3 py-2 text-sm">
          <input
            type="color"
            value={chatBackgroundColor ?? "#f8fbff"}
            aria-label={t("publicAssistant.settings.chatBackground")}
            className="h-8 w-10 cursor-pointer rounded border border-[var(--border-control)] bg-transparent p-0.5"
            onChange={(event) =>
              onChange({ chatBackgroundColor: event.target.value })
            }
            data-testid="assistant-theme-chat-background-color"
          />
          <span className="min-w-0 flex-1 break-words">
            {t("publicAssistant.settings.chatBackground")}
          </span>
        </label>
      </div>
      {!chatBackgroundColor && (
        <p className="text-xs text-[var(--text-subtle)]">
          {t("publicAssistant.settings.chatBackgroundHelp")}
        </p>
      )}
      {!assistantThemeReadable(primaryColor, chatBackgroundColor) && (
        <p
          role="alert"
          className="text-sm text-[var(--text-danger)]"
          data-testid="assistant-theme-contrast-error"
        >
          {t("publicAssistant.settings.themeContrast")}
        </p>
      )}
    </fieldset>
  );
}
