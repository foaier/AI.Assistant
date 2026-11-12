import { useDateTime } from '@/shared/hooks/useDateTime';
import { useEffect, useState, type CSSProperties } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { zodResolver } from "@hookform/resolvers/zod";
import { useForm } from "react-hook-form";
import { z } from "zod";
import {
  AlertCircle,
  Bot,
  Check,
  ExternalLink,
  Save,
  Upload,
  X,
} from "lucide-react";
import { useParams } from "react-router-dom";

import { AdminPageHeading } from "@/components/admin/AdminPageHeading";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { useI18n } from "@/context/i18nContext";

import {
  DEFAULT_ASSISTANT_CONFIGURATION,
  getAssistant,
  createAssistantPreview,
  getAssistantAnalyticsSummary,
  listAssistantConversations,
  listKnowledgeBases,
  listKnowledgeQna,
  saveAssistantDraft,
  uploadAssistantIcon,
  type Assistant,
  type AssistantConfiguration,
} from "../services/publicAssistantService";
import {
  getAssistantWebSearchAvailability,
  listBusinessPolicies,
} from "../services/assistantPolicyService";
import { AssistantTestChat } from "../components/AssistantTestChat";
import {
  AssistantCountedInput,
  AssistantCountedTextarea,
} from "../components/AssistantCountedField";
import { AssistantPublicationPanel } from "../components/AssistantPublicationPanel";
import { AssistantPresentationEditor } from "../components/AssistantPresentationEditor";
import { AssistantThemeEditor } from "../components/AssistantThemeEditor";
import { AssistantTranscript } from "../components/AssistantTranscript";
import {
  AssistantAvatar,
  AssistantQuickLinks,
} from "../components/AssistantIdentity";
import {
  assistantQuestions,
  composeAssistantWelcomeMessage,
  isSafeAssistantLink,
  splitAssistantWelcomeMessage,
} from "../services/assistantPresentation";
import {
  assistantThemeReadable,
  primaryColorReadable,
  resolveChatBackground,
} from "../services/assistantTheme";

type WorkbenchTab = "settings" | "knowledge" | "capabilities" | "analytics";

// Match existing UTF-16 limits; loaded invalid values still use form validation.
const NAME_LIMIT = 120;
const DESCRIPTION_LIMIT = 500;
const FIELD_ERROR_CLASS = "text-xs leading-5 text-red-700 dark:text-red-300";
const assistantFormSchema = (t: (key: string) => string) =>
  z
    .object({
      name: z
        .string()
        .trim()
        .min(1, t("publicAssistant.create.nameRequired"))
        .max(NAME_LIMIT, t("publicAssistant.create.nameTooLong")),
      description: z
        .string()
        .trim()
        .max(DESCRIPTION_LIMIT, t("publicAssistant.create.descriptionTooLong")),
      configuration: z.object({
        welcomeMessage: z.string().trim().min(1).max(2000),
        welcomeTitle: z.string().trim().min(1).max(2000),
        welcomeDescription: z.string().trim().max(2000),
        suggestedQuestions: z.array(z.string().trim().min(1).max(200)).max(20),
        quickLinks: z
          .array(
            z.object({
              id: z.string().min(1).max(100),
              label: z.string().trim().min(1).max(60),
              url: z.string().trim().refine(isSafeAssistantLink),
              icon: z.string(),
              iconUrl: z.string().max(2000).nullable().optional(),
              iconAssetId: z.string().max(120).nullable().optional(),
              enabled: z.boolean(),
            }),
          )
          .max(12),
        quickLinksEnabled: z.boolean(),
        historyEnabled: z.boolean(),
        iconUrl: z.string().max(1000),
        iconAssetId: z.string().nullable().optional(),
        knowledgeBaseId: z.string().nullable().optional(),
        businessPolicyId: z.string().nullable().optional(),
        primaryColor: z.string().regex(/^#[0-9a-fA-F]{6}$/),
        chatBackgroundColor: z
          .string()
          .regex(/^#[0-9a-fA-F]{6}$/)
          .nullable()
          .optional(),
        capabilities: z.object({
          knowledgeQa: z.boolean(),
          webSearch: z.boolean(),
          customQa: z.boolean(),
        }),
        retrieval: z.object({
          topK: z.number().int().min(1).max(20),
          rerank: z.boolean(),
        }),
      }),
    })
    .superRefine(({ configuration }, context) => {
      if (
        composeAssistantWelcomeMessage(
          configuration.welcomeTitle,
          configuration.welcomeDescription,
        ).length > 2000
      ) {
        context.addIssue({
          code: "custom",
          path: ["configuration", "welcomeDescription"],
          message: "welcome_message_too_long",
        });
      }
      if (
        !assistantThemeReadable(
          configuration.primaryColor,
          configuration.chatBackgroundColor,
        )
      ) {
        context.addIssue({
          code: "custom",
          path: [
            "configuration",
            configuration.chatBackgroundColor
              ? "chatBackgroundColor"
              : "primaryColor",
          ],
          message: "assistant_theme_contrast_invalid",
        });
      }
    });

type AssistantFormValues = z.infer<ReturnType<typeof assistantFormSchema>>;
const focusableSettingsFields = [
  "name",
  "description",
  "configuration.welcomeTitle",
  "configuration.welcomeDescription",
] as const;
type FocusableSettingsField = (typeof focusableSettingsFields)[number];

export function themeReadable(color: string): boolean {
  return primaryColorReadable(color);
}

function cloneConfiguration(
  configuration: AssistantConfiguration,
): AssistantConfiguration {
  return JSON.parse(JSON.stringify(configuration)) as AssistantConfiguration;
}

function toFormValues(assistant: Assistant): AssistantFormValues {
  const welcomeCopy = splitAssistantWelcomeMessage(
    assistant.configuration.welcomeMessage,
  );
  return {
    name: assistant.name,
    description: assistant.description ?? "",
    configuration: {
      ...cloneConfiguration(assistant.configuration),
      chatBackgroundColor: assistant.configuration.chatBackgroundColor ?? null,
      welcomeTitle: welcomeCopy.title,
      welcomeDescription: welcomeCopy.description,
      quickLinks: assistant.configuration.quickLinks ?? [],
      quickLinksEnabled: assistant.configuration.quickLinksEnabled !== false,
      historyEnabled: assistant.configuration.historyEnabled !== false,
    },
  };
}

export default function PublicAssistantWorkbenchPage() {
  const { formatDateTimeDisplay } = useDateTime();
  const { t, locale } = useI18n();
  const p = (key: string) => t(`publicAssistant.p0.${key}`);
  const { assistantId = "" } = useParams<{ assistantId: string }>();
  const queryClient = useQueryClient();
  const [activeTab, setActiveTab] = useState<WorkbenchTab>("settings");
  const [transcriptId, setTranscriptId] = useState<string | null>(null);
  const [conversationPagination, setConversationPagination] = useState({
    assistantId,
    page: 1,
  });
  const conversationPage =
    conversationPagination.assistantId === assistantId
      ? conversationPagination.page
      : 1;
  useEffect(() => {
    setConversationPagination({ assistantId, page: 1 });
    setTranscriptId(null);
  }, [assistantId]);
  const [notice, setNotice] = useState<string | null>(null);
  const [mobilePanel, setMobilePanel] = useState("settings");
  const [expandedQuestions, setExpandedQuestions] = useState(false);
  const [realChat, setRealChat] = useState(false);
  const [validationError, setValidationError] = useState(false);
  const [pendingFocus, setPendingFocus] =
    useState<FocusableSettingsField | null>(null);
  const [previewInput, setPreviewInput] = useState("");
  const [previewMessages, setPreviewMessages] = useState<
    Array<{ role: "user" | "assistant"; text: string }>
  >([]);

  const assistantQuery = useQuery({
    queryKey: ["public-assistant", assistantId],
    queryFn: () => getAssistant(assistantId),
    enabled: Boolean(assistantId),
  });

  const knowledgeBasesQuery = useQuery({
    queryKey: ["assistant-knowledge-bases"],
    queryFn: listKnowledgeBases,
  });
  const policiesQuery = useQuery({
    queryKey: ["assistant-business-policies"],
    queryFn: listBusinessPolicies,
  });
  const webSearchAvailabilityQuery = useQuery({
    queryKey: ["assistant-web-search-availability"],
    queryFn: getAssistantWebSearchAvailability,
    retry: false,
  });

  const analyticsSummaryQuery = useQuery({
    queryKey: ["assistant-analytics-summary", assistantId],
    queryFn: () => getAssistantAnalyticsSummary(assistantId),
    enabled: Boolean(assistantId) && activeTab === "analytics",
  });
  const conversationsQuery = useQuery({
    queryKey: [
      "assistant-analytics-conversations",
      assistantId,
      conversationPage,
    ],
    queryFn: () => listAssistantConversations(assistantId, conversationPage),
    enabled: Boolean(assistantId) && activeTab === "analytics",
    retry: false,
  });

  const form = useForm<AssistantFormValues>({
    resolver: zodResolver(assistantFormSchema(t)),
    mode: "onChange",
    shouldFocusError: false,
    defaultValues: {
      name: "",
      description: "",
      configuration: {
        ...cloneConfiguration(DEFAULT_ASSISTANT_CONFIGURATION),
        welcomeTitle: splitAssistantWelcomeMessage(
          DEFAULT_ASSISTANT_CONFIGURATION.welcomeMessage,
        ).title,
        welcomeDescription: splitAssistantWelcomeMessage(
          DEFAULT_ASSISTANT_CONFIGURATION.welcomeMessage,
        ).description,
        quickLinks: [],
        quickLinksEnabled: true,
        historyEnabled: true,
      },
    },
  });

  useEffect(() => {
    if (!form.formState.isDirty) return;
    setNotice(null);
    const protect = (event: BeforeUnloadEvent) => {
      event.preventDefault();
      event.returnValue = "";
    };
    window.addEventListener("beforeunload", protect);
    return () => window.removeEventListener("beforeunload", protect);
  }, [form.formState.isDirty]);

  useEffect(() => {
    if (!pendingFocus) return;
    // Wait for Radix tab content to mount before focusing a previously hidden input.
    const frame = requestAnimationFrame(() => {
      form.setFocus(pendingFocus);
      document.activeElement?.scrollIntoView?.({ block: "center" });
      setPendingFocus(null);
    });
    return () => cancelAnimationFrame(frame);
  }, [pendingFocus, activeTab, mobilePanel, form]);

  useEffect(() => {
    const assistant = assistantQuery.data;
    if (assistant && !form.formState.isDirty) {
      form.reset(toFormValues(assistant));
    }
  }, [assistantQuery.data, form]);

  const saveMutation = useMutation({
    mutationFn: () => {
      const assistant = assistantQuery.data;
      if (!assistant) throw new Error("assistant_not_loaded");
      const values = form.getValues() as AssistantFormValues;
      const configuration = values.configuration as AssistantConfiguration & {
        welcomeTitle: string;
        welcomeDescription: string;
      };
      return saveAssistantDraft(assistant.id, {
        name: values.name ?? assistant.name,
        description: values.description || undefined,
        configuration: {
          welcomeMessage: composeAssistantWelcomeMessage(
            configuration.welcomeTitle,
            configuration.welcomeDescription,
          ),
          suggestedQuestions: configuration?.suggestedQuestions ?? [],
          quickLinks: configuration.quickLinks,
          quickLinksEnabled: configuration.quickLinksEnabled,
          historyEnabled: configuration.historyEnabled,
          iconUrl: configuration?.iconUrl ?? "",
          iconAssetId: configuration?.iconAssetId ?? null,
          knowledgeBaseId: configuration?.knowledgeBaseId ?? null,
          businessPolicyId: configuration?.businessPolicyId ?? null,
          primaryColor:
            configuration?.primaryColor ??
            DEFAULT_ASSISTANT_CONFIGURATION.primaryColor,
          chatBackgroundColor: configuration?.chatBackgroundColor ?? null,
          capabilities: {
            knowledgeQa: configuration?.capabilities?.knowledgeQa ?? false,
            webSearch: configuration?.capabilities?.webSearch ?? false,
            customQa: configuration?.capabilities?.customQa ?? false,
          },
          retrieval: {
            topK:
              configuration?.retrieval?.topK ??
              DEFAULT_ASSISTANT_CONFIGURATION.retrieval.topK,
            rerank: configuration?.retrieval?.rerank ?? false,
          },
        },
        expectedDraftRevision: assistant.draftRevision,
      });
    },
    onSuccess: (saved) => {
      queryClient.setQueryData(["public-assistant", saved.id], saved);
      form.reset(toFormValues(saved));
      setValidationError(false);
      setNotice(t("publicAssistant.workbench.saved"));
    },
  });

  const iconUploadMutation = useMutation({
    mutationFn: uploadAssistantIcon,
    onSuccess: ({ assetId, publicUrl }) => {
      form.setValue("configuration.iconAssetId", assetId, {
        shouldDirty: true,
      });
      form.setValue("configuration.iconUrl", publicUrl, { shouldDirty: true });
      setNotice(t("publicAssistant.settings.iconUploaded"));
    },
  });

  const quickLinkIconUploadMutation = useMutation({
    mutationFn: ({ file }: { linkId: string; file: File }) =>
      uploadAssistantIcon(file),
    onSuccess: ({ assetId, publicUrl }, { linkId }) => {
      const configuration = form.getValues("configuration");
      form.setValue(
        "configuration",
        {
          ...configuration,
          quickLinks: (configuration.quickLinks ?? []).map((link) =>
            link.id === linkId
              ? { ...link, iconAssetId: assetId, iconUrl: publicUrl }
              : link,
          ),
        } as AssistantFormValues["configuration"],
        { shouldDirty: true },
      );
      setNotice(t("publicAssistant.experience.iconUploaded"));
    },
  });

  const assistant = assistantQuery.data;
  const values = form.watch() as {
    name: string;
    description: string;
    configuration: AssistantConfiguration & {
      welcomeTitle: string;
      welcomeDescription: string;
      quickLinks: NonNullable<AssistantConfiguration["quickLinks"]>;
    };
  };
  const webSearchReady =
    webSearchAvailabilityQuery.data?.configured === true &&
    webSearchAvailabilityQuery.data.enabled === true;
  const welcomeCopy = {
    title: values.configuration.welcomeTitle,
    description: values.configuration.welcomeDescription,
  };
  const welcomeTitleLimit = Math.max(
    0,
    2000 - (welcomeCopy.description.length ? welcomeCopy.description.length + 1 : 0),
  );
  const welcomeDescriptionLimit = Math.max(0, 1999 - welcomeCopy.title.length);
  const knowledgeQnaQuery = useQuery({
    queryKey: ["assistant-knowledge-qna", values.configuration.knowledgeBaseId],
    queryFn: () => listKnowledgeQna(values.configuration.knowledgeBaseId ?? ""),
    enabled: Boolean(values.configuration.knowledgeBaseId),
  });
  const previewQuestions = assistantQuestions(
    values.configuration.suggestedQuestions,
    values.configuration.capabilities.customQa
      ? (knowledgeQnaQuery.data ?? [])
          .filter((item) => item.enabled && item.showAsSuggestion)
          .sort(
            (a, b) =>
              a.sortOrder - b.sortOrder || a.id.localeCompare(b.id, "en"),
          )
          .map((item) => item.question)
      : [],
  );
  const selectedPolicy = policiesQuery.data?.find(
    (policy) => policy.id === values.configuration.businessPolicyId,
  );

  const sendPreview = (question: string) => {
    const trimmed = question.trim();
    if (!trimmed) return;
    setPreviewMessages((messages) => [
      ...messages,
      { role: "user", text: trimmed },
      { role: "assistant", text: p("exampleReply") },
    ]);
    setPreviewInput("");
  };

  const revealValidationErrors = () => {
    setNotice(null);
    saveMutation.reset();
    setValidationError(true);
    const firstInvalid = focusableSettingsFields.find(
      (field) => form.getFieldState(field).invalid,
    );
    if (firstInvalid) {
      setActiveTab("settings");
      setMobilePanel("settings");
    }
    setPendingFocus(firstInvalid ?? null);
  };

  const validateSettings = async () => {
    const valid = await form.trigger();
    if (!valid) revealValidationErrors();
    else setValidationError(false);
    return valid;
  };

  const submit = form.handleSubmit(() => {
    if (
      !assistantThemeReadable(
        values.configuration.primaryColor,
        values.configuration.chatBackgroundColor,
      )
    ) {
      setValidationError(true);
      return;
    }
    setValidationError(false);
    setNotice(null);
    saveMutation.mutate();
  }, revealValidationErrors);

  if (assistantQuery.isLoading) {
    return (
      <div className="p-6 text-sm text-[var(--text-subtle)]">
        {t("publicAssistant.list.loading")}
      </div>
    );
  }

  if (!assistant) {
    return (
      <div className="p-6">
        <div
          className="rounded-md border border-[var(--border-danger)] px-3 py-2 text-sm text-[var(--text-danger)]"
          role="alert"
        >
          {assistantQuery.error instanceof Error
            ? assistantQuery.error.message
            : t("publicAssistant.list.loadFailed")}
        </div>
      </div>
    );
  }

  return (
    <div
      className="mx-auto min-h-full w-full max-w-[1680px] space-y-5 bg-[var(--surface-page)] p-5"
      data-testid="assistant-workbench"
    >
      <AdminPageHeading
        to="/admin/assistants"
        backLabel={t("publicAssistant.workbench.back")}
        title={
          <span
            className="block truncate"
            title={values.name || assistant.name}
          >
            {values.name || assistant.name}
          </span>
        }
        subtitle={
          <span className="line-clamp-2 break-words [overflow-wrap:anywhere]">
            {values.description || t("publicAssistant.description")}
          </span>
        }
        className="[&>div:first-child]:lg:flex-1"
        actions={
          <Button
            type="button"
            onClick={() => void submit()}
            disabled={saveMutation.isPending}
            data-testid="assistant-save-draft"
          >
            {saveMutation.isPending ? (
              <Save className="mr-2 h-4 w-4 animate-pulse" aria-hidden="true" />
            ) : (
              <Save className="mr-2 h-4 w-4" aria-hidden="true" />
            )}
            {saveMutation.isPending
              ? t("publicAssistant.workbench.saving")
              : t("publicAssistant.workbench.save")}
          </Button>
        }
      />

      <p
        role="status"
        className="text-sm text-[var(--text-subtle)]"
        data-testid="assistant-preview-draft-status"
      >
        {p(form.formState.isDirty ? "dirty" : "draft")}
      </p>
      <AssistantPublicationPanel
        assistantId={assistant.id}
        validate={validateSettings}
        disabled={saveMutation.isPending}
        save={async () => {
          if (!(await validateSettings())) {
            throw new Error("assistant_settings_invalid");
          }
          return form.formState.isDirty
            ? saveMutation.mutateAsync()
            : assistant;
        }}
      />
      {validationError && !form.formState.isValid && (
        <p
          role="alert"
          className="flex items-start gap-2 rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700 dark:border-red-900 dark:bg-red-950/30 dark:text-red-300"
          data-testid="assistant-validation-error"
        >
          <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
          {form.formState.errors.name || form.formState.errors.description
            ? t("publicAssistant.settings.validationSummary")
            : p("settingsInvalid")}
        </p>
      )}

      {notice ? (
        <div
          className="flex items-center gap-2 rounded-md border border-[var(--border-success)] bg-[var(--surface-success-muted)] px-3 py-2 text-sm text-[var(--text-success)]"
          role="status"
        >
          <Check className="h-4 w-4" aria-hidden="true" />
          {notice}
        </div>
      ) : null}
      {saveMutation.error ? (
        <div
          className="rounded-md border border-[var(--border-danger)] px-3 py-2 text-sm text-[var(--text-danger)]"
          role="alert"
        >
          {saveMutation.error instanceof Error
            ? saveMutation.error.message
            : t("publicAssistant.workbench.saveFailed")}
        </div>
      ) : null}

      <Tabs
        value={activeTab}
        onValueChange={(value) => setActiveTab(value as WorkbenchTab)}
        className="space-y-5"
      >
        <TabsList
          variant="underline"
          className="w-full max-w-full justify-start overflow-x-auto border-b border-[var(--border-subtle)]"
        >
          <TabsTrigger
            variant="underline"
            value="settings"
            data-testid="assistant-tab-settings"
          >
            {t("publicAssistant.tabs.settings")}
          </TabsTrigger>
          <TabsTrigger
            variant="underline"
            value="knowledge"
            data-testid="assistant-tab-knowledge"
          >
            {t("publicAssistant.tabs.knowledge")}
          </TabsTrigger>
          <TabsTrigger
            variant="underline"
            value="capabilities"
            data-testid="assistant-tab-capabilities"
          >
            {t("publicAssistant.tabs.capabilities")}
          </TabsTrigger>
          <TabsTrigger
            variant="underline"
            value="analytics"
            data-testid="assistant-tab-analytics"
          >
            {t("publicAssistant.tabs.analytics")}
          </TabsTrigger>
        </TabsList>

        <TabsContent value="settings">
          <div
            className="mb-4 flex gap-2 xl:hidden"
            aria-label={p("configure")}
          >
            <Button
              variant={mobilePanel === "settings" ? "default" : "outline"}
              onClick={() => setMobilePanel("settings")}
              data-testid="assistant-mobile-settings"
            >
              {p("configure")}
            </Button>
            <Button
              variant={mobilePanel === "preview" ? "default" : "outline"}
              onClick={() => setMobilePanel("preview")}
              data-testid="assistant-mobile-preview"
            >
              {p("appearance")}
            </Button>
          </div>
          <div className="grid gap-5 xl:grid-cols-[minmax(0,1fr)_minmax(420px,0.8fr)]">
            <Card
              className={`min-w-0 shadow-none ${mobilePanel === "settings" ? "" : "hidden xl:block"}`}
            >
              <CardHeader>
                <CardTitle>{t("publicAssistant.tabs.settings")}</CardTitle>
              </CardHeader>
              <CardContent>
                <form
                  id="assistant-settings-form"
                  onSubmit={(event) => {
                    event.preventDefault();
                    void submit();
                  }}
                  className="space-y-5"
                  data-testid="assistant-settings-form"
                >
                  <div className="space-y-1.5 text-sm">
                    <label
                      htmlFor="assistant-settings-name"
                      className="font-medium"
                    >
                      {t("publicAssistant.settings.name")}
                    </label>
                    <AssistantCountedInput
                      {...form.register("name")}
                      value={values.name}
                      maxLength={NAME_LIMIT}
                      id="assistant-settings-name"
                      aria-required="true"
                      aria-invalid={Boolean(form.formState.errors.name)}
                      aria-describedby={`assistant-settings-name-help${form.formState.errors.name ? " assistant-settings-name-error" : ""}`}
                      className="h-11 aria-[invalid=true]:border-red-500 aria-[invalid=true]:focus-visible:ring-red-500"
                      data-testid="assistant-settings-name"
                    />
                    <div
                      id="assistant-settings-name-help"
                      className="flex items-start justify-between gap-3 text-xs leading-5 text-[var(--text-secondary)]"
                    >
                      <span>{t("publicAssistant.create.nameHelp")}</span>
                    </div>
                    {form.formState.errors.name && (
                      <p
                        id="assistant-settings-name-error"
                        role="alert"
                        className={FIELD_ERROR_CLASS}
                      >
                        {form.formState.errors.name.message}
                      </p>
                    )}
                  </div>
                  <label className="block space-y-1.5 text-sm">
                    <span>{t("publicAssistant.settings.policy")}</span>
                    <select
                      className="h-10 w-full rounded-md border border-[var(--border-subtle)] bg-transparent px-3 text-sm"
                      value={values.configuration.businessPolicyId ?? ""}
                      onChange={(event) =>
                        form.setValue(
                          "configuration.businessPolicyId",
                          event.target.value || null,
                          { shouldDirty: true },
                        )
                      }
                      data-testid="assistant-settings-business-policy"
                    >
                      <option value="">
                        {t("publicAssistant.settings.noPolicy")}
                      </option>
                      {policiesQuery.data
                        ?.filter((policy) => policy.enabled)
                        .map((policy) => (
                          <option key={policy.id} value={policy.id}>
                            {policy.settings.name} ·{" "}
                            {policy.settings.businessCode}
                          </option>
                        ))}
                    </select>
                    <p className="text-xs text-[var(--text-subtle)]">
                      {t("publicAssistant.settings.policyHint")}
                    </p>
                  </label>
                  <div
                    className="space-y-2 rounded-lg border border-[var(--border-subtle)] bg-[var(--surface-neutral-muted)] p-4 text-sm"
                    data-testid="assistant-system-prompt"
                  >
                    <p className="font-medium">
                      {t("publicAssistant.experience.systemPrompt")}
                    </p>
                    <p className="text-xs text-[var(--text-subtle)]">
                      {t("publicAssistant.experience.systemPromptHelp")}
                    </p>
                    {selectedPolicy && (
                      <p className="max-h-48 overflow-y-auto whitespace-pre-wrap break-words">
                        {selectedPolicy.settings.instructions}
                      </p>
                    )}
                    <a
                      href="/admin/assistant-models"
                      className="inline-flex items-center gap-2 text-[var(--brand-primary)] underline underline-offset-4"
                      data-testid="assistant-edit-policy"
                    >
                      {t("publicAssistant.experience.editPolicy")}
                      <ExternalLink className="h-3 w-3" aria-hidden />
                    </a>
                  </div>
                  <div className="space-y-1.5 text-sm">
                    <label
                      htmlFor="assistant-settings-description"
                      className="font-medium"
                    >
                      {t("publicAssistant.settings.description")}
                    </label>
                    <AssistantCountedTextarea
                      {...form.register("description")}
                      value={values.description}
                      maxLength={DESCRIPTION_LIMIT}
                      id="assistant-settings-description"
                      aria-invalid={Boolean(form.formState.errors.description)}
                      aria-describedby={`assistant-settings-description-help${form.formState.errors.description ? " assistant-settings-description-error" : ""}`}
                      className="min-h-24 resize-y aria-[invalid=true]:border-red-500 aria-[invalid=true]:focus-visible:ring-red-500"
                      rows={3}
                      data-testid="assistant-settings-description"
                    />
                    <div
                      id="assistant-settings-description-help"
                      className="flex items-start justify-between gap-3 text-xs leading-5 text-[var(--text-secondary)]"
                    >
                      <span>{t("publicAssistant.create.descriptionHelp")}</span>
                    </div>
                    {form.formState.errors.description && (
                      <p
                        id="assistant-settings-description-error"
                        role="alert"
                        className={FIELD_ERROR_CLASS}
                      >
                        {form.formState.errors.description.message}
                      </p>
                    )}
                  </div>
                  <fieldset className="space-y-3 rounded-lg border border-[var(--border-subtle)] p-3">
                    <legend className="px-1 text-sm font-medium">
                      {t("publicAssistant.settings.welcome")}
                    </legend>
                    <label className="block space-y-1.5 text-sm">
                      <span>{t("publicAssistant.settings.welcomeTitle")}</span>
                      <AssistantCountedInput
                        {...form.register("configuration.welcomeTitle")}
                        value={welcomeCopy.title}
                        maxLength={welcomeTitleLimit}
                        data-testid="assistant-settings-welcome-title"
                      />
                    </label>
                    <label className="block space-y-1.5 text-sm">
                      <span>
                        {t("publicAssistant.settings.welcomeDescription")}
                      </span>
                      <AssistantCountedTextarea
                        {...form.register("configuration.welcomeDescription")}
                        value={welcomeCopy.description}
                        maxLength={welcomeDescriptionLimit}
                        rows={2}
                        data-testid="assistant-settings-welcome-description"
                      />
                    </label>
                    <span className="block text-xs leading-5 text-[var(--text-subtle)]">
                      {t("publicAssistant.settings.welcomeHelp")}
                    </span>
                  </fieldset>
                  <div className="block space-y-1.5 text-sm">
                    <span>{t("publicAssistant.settings.icon")}</span>
                    <div className="flex items-center gap-2">
                      <AssistantAvatar
                        url={values.configuration.iconUrl}
                        className="h-10 w-10"
                      />
                      <label className="relative inline-flex cursor-pointer items-center rounded-md border px-3 py-2 text-sm focus-within:ring-2 focus-within:ring-[var(--brand-primary)] focus-within:ring-offset-2">
                        <Upload className="mr-2 h-4 w-4" />
                        {iconUploadMutation.isPending
                          ? t("publicAssistant.settings.iconUploading")
                          : t("publicAssistant.settings.uploadIcon")}
                        <input
                          type="file"
                          accept="image/png,image/jpeg,image/webp"
                          className="absolute inset-0 h-full w-full cursor-pointer opacity-0 disabled:cursor-not-allowed"
                          disabled={iconUploadMutation.isPending}
                          onChange={(event) => {
                            const file = event.target.files?.[0];
                            if (file) iconUploadMutation.mutate(file);
                            event.currentTarget.value = "";
                          }}
                          data-testid="assistant-settings-icon-upload"
                        />
                      </label>
                      {values.configuration.iconUrl ? (
                        <Button
                          type="button"
                          variant="ghost"
                          size="icon"
                          onClick={() => {
                            form.setValue("configuration.iconAssetId", null, {
                              shouldDirty: true,
                            });
                            form.setValue("configuration.iconUrl", "", {
                              shouldDirty: true,
                            });
                          }}
                          aria-label={t("publicAssistant.settings.removeIcon")}
                        >
                          <X className="h-4 w-4" />
                        </Button>
                      ) : null}
                    </div>
                    {iconUploadMutation.isError && (
                      <p role="alert">{p("iconFailed")}</p>
                    )}
                  </div>
                  <AssistantPresentationEditor
                    value={values.configuration}
                    onUploadLinkIcon={(linkId, file) =>
                      quickLinkIconUploadMutation.mutate({ linkId, file })
                    }
                    uploadingLinkIconId={
                      quickLinkIconUploadMutation.isPending
                        ? quickLinkIconUploadMutation.variables?.linkId
                        : undefined
                    }
                    uploadErrorLinkIconId={
                      quickLinkIconUploadMutation.isError
                        ? quickLinkIconUploadMutation.variables?.linkId
                        : undefined
                    }
                    onChange={(patch) => {
                      form.setValue(
                        "configuration",
                        {
                          ...form.getValues("configuration"),
                          ...patch,
                        } as AssistantFormValues["configuration"],
                        { shouldDirty: true },
                      );
                    }}
                  />
                  <AssistantThemeEditor
                    primaryColor={values.configuration.primaryColor}
                    chatBackgroundColor={
                      values.configuration.chatBackgroundColor
                    }
                    onChange={(patch) =>
                      form.setValue(
                        "configuration",
                        { ...form.getValues("configuration"), ...patch },
                        { shouldDirty: true },
                      )
                    }
                  />
                  <label className="block space-y-1.5 text-sm">
                    <span>{t("publicAssistant.settings.knowledgeBase")}</span>
                    <select
                      className="h-10 w-full rounded-md border border-[var(--border-subtle)] bg-transparent px-3 text-sm"
                      value={values.configuration.knowledgeBaseId ?? ""}
                      onChange={(event) =>
                        form.setValue(
                          "configuration.knowledgeBaseId",
                          event.target.value || null,
                          { shouldDirty: true },
                        )
                      }
                      data-testid="assistant-settings-knowledge-base"
                    >
                      <option value="">
                        {t("publicAssistant.settings.noKnowledgeBase")}
                      </option>
                      {knowledgeBasesQuery.data?.map((base) => (
                        <option key={base.id} value={base.id}>
                          {base.name}
                        </option>
                      ))}
                    </select>
                    <p className="text-xs text-[var(--text-subtle)]">
                      {t("publicAssistant.settings.qnaHint")}
                    </p>
                  </label>
                </form>
              </CardContent>
            </Card>

            <Card
              className={`min-w-0 overflow-hidden shadow-none ${mobilePanel === "preview" ? "" : "hidden xl:block"}`}
              data-testid="assistant-preview-panel"
            >
              <CardHeader className="flex flex-row items-center justify-between">
                <CardTitle>{p(realChat ? "realChat" : "appearance")}</CardTitle>
                <Button
                  onClick={() => setRealChat(!realChat)}
                  variant="outline"
                  data-testid="assistant-preview-mode-chat"
                >
                  {p(realChat ? "appearance" : "realChat")}
                </Button>
              </CardHeader>
              <CardContent className="p-0">
                {realChat ? (
                  <AssistantTestChat
                    key={assistant.id}
                    assistantId={assistant.id}
                    dirty={form.formState.isDirty}
                    create={async () => {
                      if (
                        !(await form.trigger()) ||
                        !assistantThemeReadable(
                          form.getValues("configuration.primaryColor"),
                          form.getValues("configuration.chatBackgroundColor"),
                        )
                      ) {
                        setValidationError(true);
                        throw new Error("assistant_settings_invalid");
                      }
                      const saved = form.formState.isDirty
                        ? await saveMutation.mutateAsync()
                        : assistant;
                      return createAssistantPreview(
                        saved.id,
                        saved.draftRevision,
                      );
                    }}
                  />
                ) : (
                  <>
                    <p className="px-5 pb-4 text-xs text-[var(--text-subtle)]">
                      {p("realChatPending")}
                    </p>
                    <div
                      className="mx-auto flex min-h-[540px] max-w-[440px] flex-col border-x border-[var(--border-subtle)] bg-[var(--surface-neutral-muted)]"
                      style={
                        {
                          "--brand-primary": values.configuration.primaryColor,
                          background: resolveChatBackground(
                            values.configuration.chatBackgroundColor,
                          ),
                        } as CSSProperties
                      }
                      data-testid="assistant-preview-shell"
                    >
                      <div
                        className="flex items-center gap-3 px-5 py-4 text-white"
                        style={{
                          backgroundColor: values.configuration.primaryColor,
                        }}
                      >
                        <AssistantAvatar
                          url={values.configuration.iconUrl}
                          className="h-9 w-9 shrink-0"
                        />
                        <div
                          className="min-w-0 flex-1 truncate font-semibold"
                          title={values.name || assistant.name}
                          data-testid="assistant-preview-name"
                        >
                          {values.name || assistant.name}
                        </div>
                      </div>
                      <div
                        className="space-y-4 p-5"
                        data-testid="assistant-preview-mode"
                      >
                        <div
                          className="max-w-[86%] rounded-lg rounded-tl-sm bg-white px-4 py-3 text-sm leading-6 text-[var(--text-primary)] shadow-sm"
                          data-testid="assistant-preview-welcome"
                        >
                          <p
                            className="font-semibold"
                            data-testid="assistant-preview-welcome-title"
                          >
                            {welcomeCopy.title}
                          </p>
                          {welcomeCopy.description && (
                            <p
                              className="whitespace-pre-wrap text-[var(--text-secondary)]"
                              data-testid="assistant-preview-welcome-description"
                            >
                              {welcomeCopy.description}
                            </p>
                          )}
                        </div>
                        {values.configuration.quickLinksEnabled && (
                          <AssistantQuickLinks
                            compact
                            links={values.configuration.quickLinks.filter(
                              (link) => link.enabled,
                            )}
                          />
                        )}
                        <div className="flex flex-wrap gap-2">
                          {previewQuestions
                            .slice(0, expandedQuestions ? undefined : 4)
                            .map((question, index) => (
                              <button
                                key={`${question}-${index}`}
                                type="button"
                                onClick={() => sendPreview(question)}
                                className="rounded-full border border-[var(--border-subtle)] bg-white px-3 py-2 text-left text-xs text-[var(--text-secondary)] focus-visible:outline focus-visible:outline-2"
                                data-testid={`assistant-preview-entry-${index}`}
                              >
                                {question}
                              </button>
                            ))}
                          {previewQuestions.length > 4 && (
                            <Button
                              size="sm"
                              variant="ghost"
                              onClick={() =>
                                setExpandedQuestions(!expandedQuestions)
                              }
                            >
                              {p(expandedQuestions ? "fewer" : "more")}
                            </Button>
                          )}
                          {!previewQuestions.length && (
                            <p className="text-xs text-[var(--text-subtle)]">
                              {p("noSuggestions")}
                            </p>
                          )}
                        </div>
                        <div
                          className="max-h-72 space-y-3 overflow-y-auto"
                          aria-live="polite"
                        >
                          {previewMessages.map((message, index) => (
                            <div
                              key={index}
                              className={`max-w-[88%] break-words rounded-2xl px-3 py-2 text-sm ${message.role === "user" ? "ml-auto text-white" : "bg-white text-[var(--text-primary)]"}`}
                              style={
                                message.role === "user"
                                  ? {
                                      backgroundColor:
                                        values.configuration.primaryColor,
                                    }
                                  : undefined
                              }
                            >
                              <span className="mb-1 block text-xs">
                                {p("example")}
                              </span>
                              {message.text}
                            </div>
                          ))}
                        </div>
                      </div>
                      <div className="mt-auto border-t border-[var(--border-subtle)] bg-white p-4">
                        <form
                          onSubmit={(e) => {
                            e.preventDefault();
                            sendPreview(previewInput);
                          }}
                          className="flex items-center gap-2 rounded-xl border border-[var(--border-subtle)] px-3 py-2 text-sm"
                        >
                          <AssistantCountedInput
                            aria-label={t("publicAssistant.preview.input")}
                            value={previewInput}
                            maxLength={500}
                            onChange={(event) =>
                              setPreviewInput(event.target.value)
                            }
                            placeholder={t("publicAssistant.preview.input")}
                            containerClassName="flex-1"
                            data-testid="assistant-preview-input"
                          />
                          <Button
                            type="submit"
                            size="sm"
                            disabled={!previewInput.trim()}
                            variant="ghost"
                            data-testid="assistant-preview-send"
                          >
                            {p("sendExample")}
                          </Button>
                        </form>
                      </div>
                    </div>
                  </>
                )}
              </CardContent>
            </Card>
          </div>
        </TabsContent>

        <TabsContent value="knowledge">
          <Card
            className="shadow-none"
            data-testid="assistant-knowledge-preview"
          >
            <CardHeader className="flex flex-row items-center justify-between">
              <div>
                <CardTitle>{t("publicAssistant.tabs.knowledge")}</CardTitle>
                <p className="mt-1 text-sm text-[var(--text-subtle)]">
                  {t("publicAssistant.settings.qnaHint")}
                </p>
              </div>
              <Button
                type="button"
                variant="outline"
                onClick={() => {
                  window.location.href = "/admin/knowledge-bases";
                }}
                data-testid="assistant-open-knowledge-bases"
              >
                {t("publicAssistant.knowledge.openManager")}
              </Button>
            </CardHeader>
            <CardContent className="space-y-3">
              {!values.configuration.knowledgeBaseId ? (
                <div className="rounded-md border border-dashed p-8 text-center text-sm text-[var(--text-subtle)]">
                  {t("publicAssistant.settings.noKnowledgeBase")}
                </div>
              ) : knowledgeQnaQuery.isLoading ? (
                <div className="p-8 text-center text-sm text-[var(--text-subtle)]">
                  {t("publicAssistant.list.loading")}
                </div>
              ) : knowledgeQnaQuery.data?.length ? (
                knowledgeQnaQuery.data.map((item) => (
                  <div
                    key={item.id}
                    className="rounded-lg border border-[var(--border-subtle)] p-4"
                    data-testid={`assistant-knowledge-qna-${item.id}`}
                  >
                    <div className="flex items-start justify-between gap-3">
                      <div className="font-medium">{item.question}</div>
                      <span
                        className={`rounded-full px-2 py-0.5 text-xs ${item.enabled ? "bg-[var(--surface-success-muted)] text-[var(--text-success)]" : "bg-[var(--surface-neutral-muted)] text-[var(--text-subtle)]"}`}
                      >
                        {item.enabled
                          ? t("publicAssistant.knowledge.enabled")
                          : t("publicAssistant.knowledge.disabled")}
                      </span>
                    </div>
                    <div className="mt-2 whitespace-pre-wrap text-sm text-[var(--text-secondary)]">
                      {item.answer ||
                        t("publicAssistant.knowledge.answerOptional")}
                    </div>
                  </div>
                ))
              ) : (
                <div className="rounded-md border border-dashed p-8 text-center text-sm text-[var(--text-subtle)]">
                  {t("publicAssistant.knowledge.empty")}
                </div>
              )}
            </CardContent>
          </Card>
        </TabsContent>

        <TabsContent value="capabilities">
          <Card className="shadow-none">
            <CardContent className="grid w-full max-w-xl gap-4 p-8 text-left sm:grid-cols-2">
              {(
                [
                  ["knowledgeQa", t("publicAssistant.capability.knowledgeQa")],
                  ["webSearch", t("publicAssistant.capability.webSearch")],
                  ["customQa", t("publicAssistant.capability.customQa")],
                ] as const
              ).map(([key, label]) => (
                <label
                  key={key}
                  className="flex items-center gap-3 rounded-lg border border-[var(--border-subtle)] p-4"
                >
                  {key === "webSearch" ? (
                    <input
                      type="checkbox"
                      checked={values.configuration.capabilities.webSearch}
                      disabled={
                        !webSearchReady &&
                        !values.configuration.capabilities.webSearch
                      }
                      onChange={(event) =>
                        form.setValue(
                          "configuration.capabilities.webSearch",
                          event.target.checked,
                          { shouldDirty: true },
                        )
                      }
                      data-testid="assistant-capability-web-search"
                      aria-describedby="assistant-web-search-readiness"
                    />
                  ) : (
                    <input
                      type="checkbox"
                      {...form.register(`configuration.capabilities.${key}`)}
                    />
                  )}
                  <span>{label}</span>
                  {key === "webSearch" && !webSearchReady && (
                    <span
                      id="assistant-web-search-readiness"
                      className="text-xs text-[var(--text-subtle)]"
                      data-testid="assistant-web-search-readiness"
                    >
                      {webSearchAvailabilityQuery.isLoading
                        ? t("publicAssistant.models.searchAvailabilityLoading")
                        : t("publicAssistant.models.searchUnavailable")}
                    </span>
                  )}
                </label>
              ))}
              <label className="flex items-center gap-3 rounded-lg border border-[var(--border-subtle)] p-4">
                <input
                  type="checkbox"
                  {...form.register("configuration.retrieval.rerank")}
                />
                <span>{t("publicAssistant.capability.rerank")}</span>
              </label>
              <label className="space-y-1.5 rounded-lg border border-[var(--border-subtle)] p-4 text-sm">
                <span>{t("publicAssistant.capability.topK")}</span>
                <Input
                  type="number"
                  min={1}
                  max={20}
                  {...form.register("configuration.retrieval.topK", {
                    valueAsNumber: true,
                  })}
                />
              </label>
            </CardContent>
          </Card>
        </TabsContent>

        <TabsContent value="analytics">
          <div className="space-y-5">
            <div className="grid gap-4 sm:grid-cols-3 lg:grid-cols-6">
              {[
                [
                  "conversationCount",
                  t("publicAssistant.analytics.conversations"),
                ],
                ["turnCount", t("publicAssistant.analytics.turns")],
                [
                  "completedTurnCount",
                  t("publicAssistant.analytics.completed"),
                ],
                ["failedTurnCount", t("publicAssistant.analytics.failed")],
                ["runCount", t("publicAssistant.analytics.runs")],
                ["toolCallCount", t("publicAssistant.analytics.tools")],
              ].map(([key, label]) => (
                <Card key={key} className="shadow-none">
                  <CardContent className="p-4">
                    <div className="text-xs text-[var(--text-subtle)]">
                      {label}
                    </div>
                    <div
                      className="mt-2 text-2xl font-semibold"
                      aria-describedby={
                        key === "toolCallCount"
                          ? "assistant-analytics-tools-help"
                          : undefined
                      }
                    >
                      {analyticsSummaryQuery.data?.[
                        key as keyof NonNullable<
                          typeof analyticsSummaryQuery.data
                        >
                      ] ?? "—"}
                    </div>
                  </CardContent>
                </Card>
              ))}
            </div>
            <p
              id="assistant-analytics-tools-help"
              role="note"
              className="text-sm leading-6 text-[var(--text-secondary)]"
            >
              {t("publicAssistant.analytics.toolsHelp")}
            </p>
            <Card className="shadow-none">
              <CardHeader>
                <CardTitle>
                  {t("publicAssistant.analytics.recentConversations")}
                </CardTitle>
              </CardHeader>
              <CardContent
                className="space-y-2"
                aria-busy={conversationsQuery.isFetching}
              >
                {conversationsQuery.isPending ? (
                  <p role="status" className="py-8 text-center text-sm">
                    {p("loading")}
                  </p>
                ) : conversationsQuery.isError ? (
                  <div
                    role="alert"
                    className="space-y-2 py-4 text-sm"
                    data-testid="assistant-conversations-error"
                  >
                    <p>{p("loadFailed")}</p>
                    <Button
                      variant="outline"
                      data-testid="assistant-conversations-retry"
                      disabled={conversationsQuery.isFetching}
                      onClick={() => void conversationsQuery.refetch()}
                    >
                      {p("retry")}
                    </Button>
                  </div>
                ) : conversationsQuery.data?.items.length ? (
                  conversationsQuery.data.items.map((conversation) => (
                    <div
                      key={conversation.conversationId}
                      className="flex flex-wrap items-center justify-between rounded-md border border-[var(--border-subtle)] px-3 py-2 text-sm"
                    >
                      <span className="min-w-0 break-all">
                        {conversation.conversationId}
                      </span>
                      <span className="text-[var(--text-subtle)]">
                        {conversation.lastTurnNo}{" "}
                        {t("publicAssistant.analytics.turnsShort")} ·{" "}
                        {formatDateTimeDisplay(
                          conversation.lastActivityAt,
                          locale,
                        )}
                      </span>
                      <Button
                        variant="outline"
                        size="sm"
                        data-testid={`assistant-open-transcript-${conversation.conversationId}`}
                        onClick={() =>
                          setTranscriptId(conversation.conversationId)
                        }
                      >
                        {t("publicAssistant.experience.viewMessages")}
                      </Button>
                    </div>
                  ))
                ) : (
                  <div className="py-8 text-center text-sm text-[var(--text-subtle)]">
                    {t("publicAssistant.analytics.empty")}
                  </div>
                )}
                <div
                  className="flex flex-wrap items-center justify-between gap-3 pt-4"
                  data-testid="assistant-conversations-pagination"
                >
                  <p
                    className="text-sm text-[var(--text-subtle)]"
                    aria-live="polite"
                  >
                    {conversationsQuery.data &&
                      t("publicAssistant.analytics.pageInfo", {
                        page: conversationPage,
                        pages: Math.max(
                          1,
                          Math.ceil(
                            conversationsQuery.data.totalCount /
                              conversationsQuery.data.pageSize,
                          ),
                        ),
                        total: conversationsQuery.data.totalCount,
                      })}
                  </p>
                  <div className="flex gap-2">
                    <Button
                      variant="outline"
                      size="sm"
                      data-testid="assistant-conversations-previous"
                      disabled={
                        conversationPage <= 1 || conversationsQuery.isFetching
                      }
                      onClick={() =>
                        setConversationPagination({
                          assistantId,
                          page: conversationPage - 1,
                        })
                      }
                    >
                      {t("publicAssistant.analytics.previousPage")}
                    </Button>
                    <Button
                      variant="outline"
                      size="sm"
                      data-testid="assistant-conversations-next"
                      disabled={
                        conversationsQuery.isFetching ||
                        conversationsQuery.isError ||
                        !conversationsQuery.data ||
                        conversationPage * conversationsQuery.data.pageSize >=
                          conversationsQuery.data.totalCount
                      }
                      onClick={() =>
                        setConversationPagination({
                          assistantId,
                          page: conversationPage + 1,
                        })
                      }
                    >
                      {t("publicAssistant.analytics.nextPage")}
                    </Button>
                  </div>
                </div>
              </CardContent>
            </Card>
          </div>
        </TabsContent>
      </Tabs>
      <AssistantTranscript
        assistantId={assistant.id}
        conversationId={transcriptId}
        onClose={() => setTranscriptId(null)}
      />
    </div>
  );
}
