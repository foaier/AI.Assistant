import { useEffect, useRef, useState, type ReactNode } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ArrowRight,
  Bot,
  Check,
  CircleCheck,
  Cpu,
  Globe2,
  KeyRound,
  Layers3,
  MessageSquare,
  Pencil,
  PlugZap,
  Plus,
  RefreshCw,
  ShieldCheck,
  SlidersHorizontal,
} from "lucide-react";
import { AdminPageHeading } from "@/components/admin/AdminPageHeading";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { useI18n } from "@/context/i18nContext";
import { ApiError } from "@/lib/ApiClient";
import {
  listModelServices,
  listAvailableModelServices,
  listBusinessPolicies,
  saveBusinessPolicy,
  setBusinessPolicyStatus,
  listPlatformModelServices,
  getPlatformModelServiceForEdit,
  savePlatformModelService,
  checkPlatformModelService,
  setPlatformModelServiceStatus,
  getPlatformAssistantWebSearchConfig,
  savePlatformAssistantWebSearchConfig,
  setPlatformAssistantWebSearchStatus,
  type ModelService,
  type ModelServiceSettings,
  type BusinessPolicy,
  type BusinessPolicySettings,
  type ModelConnectionCheck,
  type AssistantWebSearchConfig,
  type AssistantWebSearchSettings,
} from "../services/assistantPolicyService";

const selectStyle =
  "h-10 w-full rounded-md border border-[var(--border-control)] bg-[var(--surface-card)] px-3 text-sm";
const businessCodePattern = /^[a-z][a-z0-9_-]{0,79}$/;
function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <label className="block space-y-1.5 text-sm">
      <span>{label}</span>
      {children}
    </label>
  );
}

export default function AssistantModelsPage({
  mode = "business",
}: {
  mode?: "business" | "platform";
}) {
  const { t } = useI18n();
  const m = (key: string) => t(`publicAssistant.models.${key}`);
  const saveErrorMessage = (error: unknown) => {
    if (!(error instanceof ApiError)) return m("saveFailed");
    const key: Record<string, string> = {
      assistant_provider_unavailable: "providerUnavailable",
      assistant_provider_not_found: "providerNotFound",
      assistant_provider_disabled: "providerDisabled",
      assistant_provider_revision_unavailable: "providerRevisionUnavailable",
      assistant_policy_model_not_supported: "modelNotSupported",
    };
    return m(key[error.message] ?? "saveFailed");
  };
  const isPlatform = mode === "platform";
  const cache = useQueryClient();
  const providers = useQuery({
    queryKey: ["assistant-model-services", mode],
    queryFn: isPlatform ? listPlatformModelServices : listModelServices,
  });
  const availableProviders = useQuery({
    queryKey: ["assistant-model-services-available"],
    queryFn: listAvailableModelServices,
    enabled: !isPlatform,
  });
  const policies = useQuery({
    queryKey: ["assistant-business-policies"],
    queryFn: listBusinessPolicies,
    enabled: !isPlatform,
  });
  const webSearchConfig = useQuery({
    queryKey: ["assistant-web-search"],
    queryFn: getPlatformAssistantWebSearchConfig,
    enabled: isPlatform,
  });
  const policyProviders = availableProviders.data ?? [];
  const [tab, setTab] = useState(isPlatform ? "providers" : "policies");
  const [editor, setEditor] = useState<"provider" | "policy" | null>(null);
  const editorTrigger = useRef<HTMLElement | null>(null);
  const providerEditRequest = useRef(0);
  const [provider, setProvider] = useState<ModelService>();
  const [providerEditLoading, setProviderEditLoading] = useState(false);
  const [providerEditError, setProviderEditError] = useState(false);
  const [policy, setPolicy] = useState<BusinessPolicy>();
  const [businessCodeTouched, setBusinessCodeTouched] = useState(false);
  const [serviceForm, setServiceForm] = useState<ModelServiceSettings>({
    name: "",
    baseUrl: "",
    secretReference: "",
    secretVersion: "v1",
    timeoutSeconds: 60,
    models: [],
  });
  const [policyForm, setPolicyForm] = useState<BusinessPolicySettings>({
    name: "",
    businessCode: "customer_service",
    providerId: "",
    providerRevision: 0,
    model: "",
    instructions: "",
    temperature: 0.7,
    maxOutputTokens: 2000,
    knowledgeEnabled: true,
    retrievalLimit: 5,
    requireEvidence: true,
    noAnswerMessage: "",
    webSearchEnabled: false,
    humanHandoffEnabled: false,
  });
  const [checks, setChecks] = useState<Record<string, ModelConnectionCheck>>(
    {},
  );
  const [notice, setNotice] = useState("");
  const [statusTarget, setStatusTarget] = useState<
    | { kind: "provider"; value: ModelService }
    | { kind: "policy"; value: BusinessPolicy }
    | { kind: "webSearch"; value: AssistantWebSearchConfig }
    | null
  >(null);
  const [webSearchForm, setWebSearchForm] =
    useState<AssistantWebSearchSettings>({
      endpoint: "https://open.feedcoopapi.com/agent_api/agent/chat/completion",
      botId: "7602814290642355738",
      serviceName: "ask_echo",
      apiKey: "",
      timeoutSeconds: 60,
    });
  const refresh = async () => {
    await Promise.all([
      cache.invalidateQueries({ queryKey: ["assistant-model-services"] }),
      cache.invalidateQueries({
        queryKey: ["assistant-model-services-available"],
      }),
      cache.invalidateQueries({ queryKey: ["assistant-business-policies"] }),
      cache.invalidateQueries({ queryKey: ["assistant-knowledge-embedding"] }),
      cache.invalidateQueries({ queryKey: ["assistant-web-search"] }),
      cache.invalidateQueries({
        queryKey: ["assistant-web-search-availability"],
      }),
    ]);
  };
  useEffect(() => {
    if (!webSearchConfig.data) return;
    setWebSearchForm({
      endpoint: webSearchConfig.data.endpoint,
      botId: webSearchConfig.data.botId,
      serviceName: webSearchConfig.data.serviceName,
      apiKey: "",
      timeoutSeconds: webSearchConfig.data.timeoutSeconds,
    });
  }, [webSearchConfig.data]);
  const save = useMutation({
    mutationFn: async () => {
      if (editor === "provider")
        await savePlatformModelService(serviceForm, provider);
      else await saveBusinessPolicy(policyForm, policy);
    },
    onSuccess: async () => {
      setEditor(null);
      setNotice(m("saved"));
      await refresh();
    },
  });
  const status = useMutation({
    mutationFn: async () => {
      if (statusTarget?.kind === "provider")
        await setPlatformModelServiceStatus(statusTarget.value);
      else if (statusTarget?.kind === "webSearch")
        await setPlatformAssistantWebSearchStatus(statusTarget.value);
      else if (statusTarget) await setBusinessPolicyStatus(statusTarget.value);
    },
    onSuccess: async () => {
      setStatusTarget(null);
      setNotice(m("saved"));
      await refresh();
    },
  });
  const check = useMutation({
    mutationFn: checkPlatformModelService,
    onSuccess: (result, id) =>
      setChecks((previous) => ({ ...previous, [id]: result })),
  });
  const webSearchSave = useMutation({
    mutationFn: () =>
      savePlatformAssistantWebSearchConfig(
        webSearchForm,
        webSearchConfig.data?.revision ?? 0,
      ),
    onSuccess: async () => {
      setNotice(m("saved"));
      setWebSearchForm((current) => ({ ...current, apiKey: "" }));
      await refresh();
    },
  });
  const loadProviderForEdit = async (
    value: ModelService,
    preserveDraft = false,
  ) => {
    const request = ++providerEditRequest.current;
    setProviderEditLoading(true);
    setProviderEditError(false);
    try {
      const details = await getPlatformModelServiceForEdit(value.id);
      if (request !== providerEditRequest.current) return;
      if (!details.settings)
        throw new Error("assistant_provider_settings_unavailable");
      setProvider(details);
      setServiceForm(
        preserveDraft
          ? (current) => ({
              ...structuredClone(details.settings!),
              ...current,
              secretReference:
                current.secretReference || details.settings!.secretReference,
            })
          : structuredClone(details.settings),
      );
    } catch {
      if (request === providerEditRequest.current) setProviderEditError(true);
    } finally {
      if (request === providerEditRequest.current)
        setProviderEditLoading(false);
    }
  };
  const openProvider = (trigger: HTMLElement, value?: ModelService) => {
    providerEditRequest.current++;
    editorTrigger.current = trigger;
    setProvider(value);
    save.reset();
    setProviderEditError(false);
    setServiceForm(
      value?.settings
        ? { ...structuredClone(value.settings), secretReference: "" }
        : {
            name: "",
            baseUrl: "",
            secretReference: "",
            secretVersion: "v1",
            timeoutSeconds: 60,
            models: [{ id: "", purpose: "chat", supportsTools: true }],
          },
    );
    setEditor("provider");
    if (value) void loadProviderForEdit(value);
    else setProviderEditLoading(false);
  };
  const openPolicy = (trigger: HTMLElement, value?: BusinessPolicy) => {
    providerEditRequest.current++;
    setProviderEditLoading(false);
    editorTrigger.current = trigger;
    setPolicy(value);
    save.reset();
    setBusinessCodeTouched(false);
    const available = policyProviders[0];
    setPolicyForm(
      value
        ? structuredClone(value.settings)
        : {
            name: "",
            businessCode: "customer_service",
            providerId: available?.id ?? "",
            providerRevision: available?.revision ?? 0,
            model:
              available?.models.find((model) => model.purpose === "chat")?.id ??
              "",
            instructions: m("defaultInstructions"),
            temperature: 0.7,
            maxOutputTokens: 2000,
            knowledgeEnabled: true,
            retrievalLimit: 5,
            requireEvidence: true,
            noAnswerMessage: m("defaultNoAnswer"),
            webSearchEnabled: false,
            humanHandoffEnabled: false,
          },
    );
    setEditor("policy");
  };
  const selectedProvider = policyProviders.find(
    (item) => item.id === policyForm.providerId,
  );
  const selectedProviderIsAvailable = policyProviders.some(
    (item) => item.id === policyForm.providerId,
  );
  const policyProviderError =
    policyForm.providerId &&
    !availableProviders.isLoading &&
    !availableProviders.isError &&
    !selectedProviderIsAvailable
      ? m("policyProviderUnavailable")
      : "";
  const businessCodeError =
    businessCodeTouched && !businessCodePattern.test(policyForm.businessCode)
      ? policyForm.businessCode
        ? m("businessInvalid")
        : m("businessRequired")
      : "";
  const formError = save.isError ? saveErrorMessage(save.error) : "";
  return (
    <div
      className="model-workspace mx-auto w-full max-w-7xl"
      data-testid="assistant-models-page"
    >
      <div className="model-page-intro">
        <div className="model-page-eyebrow">
          <span aria-hidden />
          <span>
            {m(isPlatform ? "connectionsEyebrow" : "policiesEyebrow")}
          </span>
        </div>
        <AdminPageHeading
          className="model-page-heading"
          title={m(isPlatform ? "platformTitle" : "policyTitle")}
          subtitle={m(isPlatform ? "platformDescription" : "policyDescription")}
          actions={
            tab === "policies" ? (
              <Button
                data-testid="assistant-policy-create"
                onClick={(event) => openPolicy(event.currentTarget)}
              >
                <Plus aria-hidden className="mr-2 h-4 w-4" />
                {m("newPolicy")}
              </Button>
            ) : tab === "providers" && isPlatform ? (
              <Button
                data-testid="assistant-provider-create"
                onClick={(event) => openProvider(event.currentTarget)}
              >
                <Plus aria-hidden className="mr-2 h-4 w-4" />
                {m("newProvider")}
              </Button>
            ) : undefined
          }
        />
      </div>
      {isPlatform && (
        <dl className="model-overview" aria-label={m("overview")}>
          {[
            {
              label: "connectedServices",
              value: providers.data?.length,
              icon: PlugZap,
              tone: "blue",
            },
            {
              label: "enabledServices",
              value: providers.data?.filter((item) => item.enabled).length,
              icon: CircleCheck,
              tone: "green",
            },
            {
              label: "configuredModels",
              value: providers.data?.reduce(
                (total, item) => total + item.models.length,
                0,
              ),
              icon: Layers3,
              tone: "violet",
            },
          ].map(({ label, value, icon: Icon, tone }) => (
            <div className="model-overview-item" key={label}>
              <span
                className={`model-overview-icon model-overview-icon--${tone}`}
              >
                <Icon size={20} strokeWidth={1.7} aria-hidden />
              </span>
              <div>
                <dt>{m(label)}</dt>
                <dd>
                  {providers.isError ? "—" : (value ?? "—")}
                  <span>{m("countUnit")}</span>
                </dd>
              </div>
            </div>
          ))}
        </dl>
      )}
      {notice && <p role="status">{notice}</p>}
      <Tabs value={tab} onValueChange={setTab} className="model-tabs">
        {isPlatform && (
          <TabsList variant="underline" className="model-tabs-list">
            {["providers", "webSearch"].map((name) => (
              <TabsTrigger
                variant="underline"
                key={name}
                value={name}
                data-testid={`assistant-models-tab-${name}`}
                className="model-tab-trigger"
              >
                {name === "providers" ? (
                  <Cpu size={17} aria-hidden />
                ) : (
                  <Globe2 size={17} aria-hidden />
                )}
                {m(name)}
              </TabsTrigger>
            ))}
          </TabsList>
        )}
        {(providers.isError ||
          (isPlatform
            ? false
            : availableProviders.isError || policies.isError)) && (
          <div role="alert" className="my-4 rounded-lg border p-4">
            {m("loadFailed")}{" "}
            <Button variant="outline" onClick={() => void refresh()}>
              {m("retry")}
            </Button>
          </div>
        )}
        {(providers.isLoading ||
          (!isPlatform &&
            (availableProviders.isLoading || policies.isLoading))) && (
          <div role="status" className="model-loading">
            <RefreshCw
              size={20}
              className="motion-safe:animate-spin"
              aria-hidden
            />
            {m("loading")}
          </div>
        )}
        <TabsContent value="policies" className="space-y-3">
          {!policies.isLoading &&
            !policies.isError &&
            policies.data?.length === 0 && (
              <div className="model-empty model-empty--policy">
                <span className="model-empty-simple-icon">
                  <SlidersHorizontal size={30} aria-hidden />
                </span>
                <h2>{m("policyEmptyTitle")}</h2>
                <p>{m("emptyPolicies")}</p>
                <Button onClick={(event) => openPolicy(event.currentTarget)}>
                  <Plus className="mr-2" aria-hidden />
                  {m("startPolicy")}
                </Button>
              </div>
            )}
          {policies.data?.map((item) => (
            <Card key={item.id} data-testid={`assistant-policy-${item.id}`}>
              <CardHeader className="flex flex-row flex-wrap items-start justify-between gap-3">
                <div className="min-w-0">
                  <CardTitle className="break-words">
                    {item.settings.name}
                  </CardTitle>
                  <p className="mt-2 text-sm text-[var(--text-subtle)]">
                    {item.settings.businessCode} · {item.settings.model} ·{" "}
                    {m(item.enabled ? "enabled" : "disabled")}
                  </p>
                </div>
                <div className="flex gap-2">
                  <Button
                    variant="outline"
                    aria-label={`${m("edit")} ${item.settings.name}`}
                    onClick={(event) => openPolicy(event.currentTarget, item)}
                  >
                    <Pencil aria-hidden className="mr-2 h-4 w-4" />
                    {m("edit")}
                  </Button>
                  <Button
                    variant="ghost"
                    onClick={() => {
                      status.reset();
                      setStatusTarget({ kind: "policy", value: item });
                    }}
                  >
                    {m(item.enabled ? "disable" : "enable")}
                  </Button>
                </div>
              </CardHeader>
              <CardContent>
                <p className="whitespace-pre-wrap break-words text-sm">
                  {item.settings.instructions}
                </p>
              </CardContent>
            </Card>
          ))}
        </TabsContent>
        <TabsContent
          value="providers"
          className="model-provider-panel space-y-4"
        >
          <div className="model-section-heading">
            <div>
              <h2>{m("yourConnections")}</h2>
              <p>{m("providerSectionHelp")}</p>
            </div>
            <span className="model-security-note">
              <ShieldCheck size={15} aria-hidden />
              {m("encryptedCredentials")}
            </span>
          </div>
          {!providers.isLoading &&
            !providers.isError &&
            providers.data?.length === 0 && (
              <div className="model-onboarding">
                <div className="model-empty">
                  <div className="model-empty-art" aria-hidden>
                    <span className="model-art-orbit" />
                    <span className="model-art-satellite model-art-satellite--left">
                      <MessageSquare size={23} strokeWidth={1.5} />
                    </span>
                    <span className="model-art-core">
                      <Cpu size={38} strokeWidth={1.4} />
                      <span>
                        <Plus size={13} strokeWidth={2.5} />
                      </span>
                    </span>
                    <span className="model-art-satellite model-art-satellite--right">
                      <Layers3 size={23} strokeWidth={1.5} />
                    </span>
                    <span className="model-art-dot model-art-dot--one" />
                    <span className="model-art-dot model-art-dot--two" />
                  </div>
                  <h2>{m("providerEmptyTitle")}</h2>
                  <p>{m("providerEmptyDescription")}</p>
                  <Button
                    className="model-empty-action"
                    onClick={(event) => openProvider(event.currentTarget)}
                  >
                    {m("startConnecting")}
                    <ArrowRight className="ml-2" aria-hidden />
                  </Button>
                  <div className="model-capabilities">
                    <span>
                      <Check size={14} aria-hidden />
                      {m("openAiCompatible")}
                    </span>
                    <span>
                      <Check size={14} aria-hidden />
                      {m("chatAndEmbedding")}
                    </span>
                  </div>
                </div>
                <ol className="model-setup-steps" aria-label={m("setupSteps")}>
                  {[
                    {
                      title: "stepConnect",
                      description: "stepConnectHelp",
                      icon: KeyRound,
                    },
                    {
                      title: "stepConfigure",
                      description: "stepConfigureHelp",
                      icon: SlidersHorizontal,
                    },
                    {
                      title: "stepCreate",
                      description: "stepCreateHelp",
                      icon: Bot,
                    },
                  ].map(({ title, description, icon: Icon }, index) => (
                    <li key={title}>
                      <span className="model-step-number">0{index + 1}</span>
                      <div>
                        <h3>
                          <Icon size={15} aria-hidden />
                          {m(title)}
                        </h3>
                        <p>{m(description)}</p>
                      </div>
                    </li>
                  ))}
                </ol>
              </div>
            )}
          {providers.data?.map((item) => (
            <Card
              key={item.id}
              className="model-provider-card"
              data-testid={`assistant-provider-${item.id}`}
            >
              <CardHeader className="flex flex-row flex-wrap justify-between gap-3">
                <div className="model-provider-identity">
                  <span className="model-provider-icon">
                    <Cpu size={22} aria-hidden />
                  </span>
                  <div className="min-w-0">
                    <CardTitle className="break-words">{item.name}</CardTitle>
                    <p
                      className={`model-provider-status ${item.enabled ? "is-enabled" : ""}`}
                    >
                      <span aria-hidden />
                      {m(item.enabled ? "enabled" : "disabled")}
                    </p>
                  </div>
                </div>
                {isPlatform && (
                  <div className="flex flex-wrap gap-2">
                    <Button
                      variant="outline"
                      onClick={(event) =>
                        openProvider(event.currentTarget, item)
                      }
                      aria-label={`${m("edit")} ${item.name}`}
                    >
                      <Pencil aria-hidden className="mr-2 h-4 w-4" />
                      {m("edit")}
                    </Button>
                    <Button
                      variant="outline"
                      disabled={!item.enabled || check.isPending}
                      data-testid={`assistant-provider-check-${item.id}`}
                      onClick={() => check.mutate(item.id)}
                    >
                      <RefreshCw
                        aria-hidden
                        className={`mr-2 h-4 w-4 ${check.isPending && check.variables === item.id ? "animate-spin" : ""}`}
                      />
                      {m("check")}
                    </Button>
                    <Button
                      variant="ghost"
                      onClick={() => {
                        status.reset();
                        setStatusTarget({ kind: "provider", value: item });
                      }}
                    >
                      {m(item.enabled ? "disable" : "enable")}
                    </Button>
                  </div>
                )}
              </CardHeader>
              <CardContent className="space-y-3">
                <div className="flex flex-wrap gap-2">
                  {item.models.map((model) => (
                    <span
                      className="model-tag"
                      key={`${model.purpose}:${model.id}`}
                    >
                      {model.id} · {m(model.purpose)}
                    </span>
                  ))}
                </div>
                {checks[item.id] && (
                  <p
                    role={checks[item.id].success ? "status" : "alert"}
                    data-testid={`assistant-provider-check-result-${item.id}`}
                  >
                    {checks[item.id].message}
                  </p>
                )}
                {check.isError && check.variables === item.id && (
                  <p role="alert">{m("checkFailed")}</p>
                )}
              </CardContent>
            </Card>
          ))}
        </TabsContent>
        {isPlatform && (
          <TabsContent value="webSearch" className="space-y-3">
            {webSearchConfig.isError && (
              <div role="alert" className="rounded-md border p-4">
                {m("loadFailed")}{" "}
                <Button
                  variant="outline"
                  onClick={() => void webSearchConfig.refetch()}
                >
                  {m("retry")}
                </Button>
              </div>
            )}
            {webSearchConfig.isLoading ? (
              <p role="status" className="py-8">
                {m("loading")}
              </p>
            ) : (
              webSearchConfig.data && (
                <Card
                  className="model-web-search-card"
                  data-testid="assistant-web-search-config"
                >
                  <CardHeader className="flex flex-row flex-wrap items-start justify-between gap-3">
                    <div>
                      <CardTitle>{m("webSearchTitle")}</CardTitle>
                      <p className="mt-2 text-sm text-[var(--text-subtle)]">
                        {m("webSearchHelp")}
                      </p>
                    </div>
                    {webSearchConfig.data.revision > 0 && (
                      <Button
                        variant="outline"
                        onClick={() => {
                          status.reset();
                          setStatusTarget({
                            kind: "webSearch",
                            value: webSearchConfig.data!,
                          });
                        }}
                        data-testid="assistant-web-search-status"
                      >
                        {m(webSearchConfig.data.enabled ? "disable" : "enable")}
                      </Button>
                    )}
                  </CardHeader>
                  <CardContent>
                    <form
                      className="grid gap-4 sm:grid-cols-2"
                      onSubmit={(event) => {
                        event.preventDefault();
                        if (!webSearchSave.isPending) webSearchSave.mutate();
                      }}
                    >
                      <Field label={m("endpoint")}>
                        <Input
                          type="url"
                          readOnly
                          required
                          value={webSearchForm.endpoint}
                          data-testid="assistant-web-search-endpoint"
                        />
                      </Field>
                      <Field label={m("webSearchBotId")}>
                        <Input
                          required
                          maxLength={128}
                          value={webSearchForm.botId}
                          onChange={(event) =>
                            setWebSearchForm({
                              ...webSearchForm,
                              botId: event.target.value,
                            })
                          }
                          data-testid="assistant-web-search-bot-id"
                        />
                      </Field>
                      <Field label={m("webSearchServiceName")}>
                        <Input
                          required
                          maxLength={64}
                          value={webSearchForm.serviceName}
                          onChange={(event) =>
                            setWebSearchForm({
                              ...webSearchForm,
                              serviceName: event.target.value,
                            })
                          }
                          data-testid="assistant-web-search-service-name"
                        />
                      </Field>
                      <Field label={m("timeout")}>
                        <Input
                          required
                          type="number"
                          min={5}
                          max={120}
                          value={webSearchForm.timeoutSeconds}
                          onChange={(event) =>
                            setWebSearchForm({
                              ...webSearchForm,
                              timeoutSeconds: Number(event.target.value),
                            })
                          }
                          data-testid="assistant-web-search-timeout"
                        />
                      </Field>
                      <div className="sm:col-span-2">
                        <Field label={m("webSearchCredential")}>
                          <Input
                            required={
                              !webSearchConfig.data.credentialConfigured
                            }
                            type="password"
                            autoComplete="new-password"
                            placeholder={m(
                              webSearchConfig.data.credentialConfigured
                                ? "webSearchCredentialKeep"
                                : "webSearchCredentialPlaceholder",
                            )}
                            value={webSearchForm.apiKey}
                            onChange={(event) =>
                              setWebSearchForm({
                                ...webSearchForm,
                                apiKey: event.target.value,
                              })
                            }
                            data-testid="assistant-web-search-credential"
                          />
                        </Field>
                        <p
                          className="mt-1.5 flex flex-wrap items-center gap-2 text-xs text-[var(--text-subtle)]"
                          data-testid="assistant-web-search-credential-status"
                        >
                          <span>
                            {m(
                              webSearchConfig.data.credentialConfigured
                                ? "webSearchCredentialConfigured"
                                : "webSearchCredentialMissing",
                            )}
                          </span>
                          {webSearchConfig.data.credentialConfigured && (
                            <code data-testid="assistant-web-search-credential-mask">
                              {webSearchConfig.data.credentialMasked}
                            </code>
                          )}
                        </p>
                      </div>
                      {webSearchSave.isError && (
                        <p
                          role="alert"
                          className="sm:col-span-2 text-sm text-[var(--text-danger)]"
                        >
                          {m("saveFailed")}
                        </p>
                      )}
                      <div className="flex justify-end sm:col-span-2">
                        <Button
                          type="submit"
                          disabled={
                            webSearchSave.isPending ||
                            !webSearchForm.botId.trim() ||
                            !webSearchForm.serviceName.trim() ||
                            (!webSearchConfig.data.credentialConfigured &&
                              !webSearchForm.apiKey.trim())
                          }
                          data-testid="assistant-web-search-save"
                        >
                          {m(webSearchSave.isPending ? "saving" : "save")}
                        </Button>
                      </div>
                    </form>
                  </CardContent>
                </Card>
              )
            )}
          </TabsContent>
        )}
      </Tabs>
      <Dialog
        open={editor !== null}
        onOpenChange={(open) => {
          if (!open && !save.isPending) {
            providerEditRequest.current++;
            setProviderEditLoading(false);
            setEditor(null);
          }
        }}
      >
        <DialogContent
          className="model-editor max-h-[90dvh] overflow-y-auto sm:max-w-2xl"
          closeButtonClassName="h-11 w-11"
          data-testid="assistant-models-editor"
          onCloseAutoFocus={(event) => {
            if (editorTrigger.current?.isConnected) {
              event.preventDefault();
              editorTrigger.current.focus();
            }
          }}
        >
          <DialogHeader>
            <DialogTitle>
              {m(
                editor === "provider"
                  ? provider
                    ? "editProvider"
                    : "newProvider"
                  : policy
                    ? "editPolicy"
                    : "newPolicy",
              )}
            </DialogTitle>
            <DialogDescription>
              {m(editor === "provider" ? "providerAdminHelp" : "policyHelp")}
            </DialogDescription>
          </DialogHeader>
          <form
            className="space-y-4"
            onSubmit={(event) => {
              event.preventDefault();
              if (
                editor === "provider" &&
                provider &&
                (providerEditLoading ||
                  (providerEditError && !serviceForm.secretReference))
              )
                return;
              if (editor === "policy" && policyProviderError) return;
              if (
                editor === "policy" &&
                !businessCodePattern.test(policyForm.businessCode)
              ) {
                setBusinessCodeTouched(true);
                return;
              }
              if (!save.isPending) save.mutate();
            }}
          >
            {editor === "provider" ? (
              <fieldset
                disabled={providerEditLoading}
                className="min-w-0 space-y-4 border-0 p-0"
              >
                <legend className="sr-only">
                  {m(provider ? "editProvider" : "newProvider")}
                </legend>
                <Field label={m("name")}>
                  <Input
                    required
                    maxLength={120}
                    value={serviceForm.name}
                    onChange={(e) =>
                      setServiceForm({ ...serviceForm, name: e.target.value })
                    }
                    data-testid="assistant-provider-name"
                  />
                </Field>
                <Field label={m("endpoint")}>
                  <Input
                    required
                    type="url"
                    value={serviceForm.baseUrl}
                    onChange={(e) =>
                      setServiceForm({
                        ...serviceForm,
                        baseUrl: e.target.value,
                      })
                    }
                    data-testid="assistant-provider-endpoint"
                  />
                </Field>
                <div className="grid gap-4 sm:grid-cols-2">
                  <Field label={m("credential")}>
                    <Input
                      required={!provider}
                      type="password"
                      autoComplete="new-password"
                      placeholder={m(
                        provider
                          ? "credentialKeepPlaceholder"
                          : "credentialPlaceholder",
                      )}
                      value={serviceForm.secretReference}
                      onChange={(e) =>
                        setServiceForm({
                          ...serviceForm,
                          secretReference: e.target.value,
                        })
                      }
                      data-testid="assistant-provider-credential"
                    />
                  </Field>
                  {provider && providerEditLoading && (
                    <p
                      className="-mt-2 text-xs text-[var(--text-subtle)]"
                      role="status"
                    >
                      {m("credentialLoading")}
                    </p>
                  )}
                  {provider && providerEditError && (
                    <p
                      className="-mt-2 text-xs text-[var(--text-danger)]"
                      role="alert"
                      data-testid="assistant-provider-credential-error"
                    >
                      {m("credentialLoadFailed")}{" "}
                      <Button
                        type="button"
                        variant="outline"
                        onClick={() => void loadProviderForEdit(provider, true)}
                      >
                        {m("credentialRetry")}
                      </Button>
                    </p>
                  )}
                  {provider && !providerEditLoading && !providerEditError && (
                    <p
                      className="-mt-2 text-xs text-[var(--text-subtle)]"
                      data-testid="assistant-provider-credential-status"
                    >
                      {m("credentialConfiguredHelp")}
                    </p>
                  )}
                </div>
                <Field label={m("timeout")}>
                  <Input
                    required
                    type="number"
                    min={5}
                    max={180}
                    value={serviceForm.timeoutSeconds}
                    onChange={(e) =>
                      setServiceForm({
                        ...serviceForm,
                        timeoutSeconds: Number(e.target.value),
                      })
                    }
                  />
                </Field>
                {serviceForm.models.map((model, index) => (
                  <fieldset
                    key={index}
                    className="space-y-3 rounded-md border p-3"
                  >
                    <legend className="px-1 text-sm">
                      {m("model")} {index + 1}
                    </legend>
                    <Field label={m("modelName")}>
                      <Input
                        required
                        maxLength={120}
                        value={model.id}
                        data-testid={`assistant-provider-model-${index}`}
                        onChange={(e) =>
                          setServiceForm({
                            ...serviceForm,
                            models: serviceForm.models.map((entry, i) =>
                              i === index
                                ? { ...entry, id: e.target.value }
                                : entry,
                            ),
                          })
                        }
                      />
                    </Field>
                    <Field label={m("purpose")}>
                      <select
                        className={selectStyle}
                        value={model.purpose}
                        data-testid={`assistant-provider-purpose-${index}`}
                        onChange={(e) =>
                          setServiceForm({
                            ...serviceForm,
                            models: serviceForm.models.map((entry, i) =>
                              i === index
                                ? {
                                    ...entry,
                                    purpose: e.target
                                      .value as typeof model.purpose,
                                    dimension:
                                      e.target.value === "embedding"
                                        ? entry.dimension
                                        : undefined,
                                    supportsTools:
                                      e.target.value === "chat" &&
                                      entry.supportsTools,
                                  }
                                : entry,
                            ),
                          })
                        }
                      >
                        {["chat", "embedding", "rerank"].map((purpose) => (
                          <option key={purpose} value={purpose}>
                            {m(purpose)}
                          </option>
                        ))}
                      </select>
                    </Field>
                    {model.purpose === "embedding" && (
                      <>
                        <Field label={m("dimension")}>
                          <Input
                            required
                            type="number"
                            min={1}
                            max={32768}
                            step={1}
                            value={model.dimension ?? ""}
                            aria-describedby={`embedding-dimension-help-${index}`}
                            data-testid={`assistant-provider-dimension-${index}`}
                            onChange={(e) =>
                              setServiceForm({
                                ...serviceForm,
                                models: serviceForm.models.map((entry, i) =>
                                  i === index
                                    ? {
                                        ...entry,
                                        dimension: e.target.value
                                          ? Number(e.target.value)
                                          : undefined,
                                      }
                                    : entry,
                                ),
                              })
                            }
                          />
                        </Field>
                        <p
                          id={`embedding-dimension-help-${index}`}
                          className="text-xs text-[var(--text-subtle)]"
                        >
                          {m("dimensionHelp")}
                        </p>
                      </>
                    )}
                    {model.purpose === "chat" && (
                      <label className="flex items-center gap-2 text-sm">
                        <input
                          type="checkbox"
                          checked={model.supportsTools}
                          onChange={(e) =>
                            setServiceForm({
                              ...serviceForm,
                              models: serviceForm.models.map((entry, i) =>
                                i === index
                                  ? {
                                      ...entry,
                                      supportsTools: e.target.checked,
                                    }
                                  : entry,
                              ),
                            })
                          }
                        />
                        {m("tools")}
                      </label>
                    )}
                    {serviceForm.models.length > 1 && (
                      <Button
                        type="button"
                        variant="ghost"
                        onClick={() =>
                          setServiceForm({
                            ...serviceForm,
                            models: serviceForm.models.filter(
                              (_, i) => i !== index,
                            ),
                          })
                        }
                      >
                        {m("removeModel")}
                      </Button>
                    )}
                  </fieldset>
                ))}
                <Button
                  type="button"
                  variant="outline"
                  onClick={() =>
                    setServiceForm({
                      ...serviceForm,
                      models: [
                        ...serviceForm.models,
                        { id: "", purpose: "chat", supportsTools: false },
                      ],
                    })
                  }
                >
                  {m("addModel")}
                </Button>
              </fieldset>
            ) : (
              <>
                <Field label={m("name")}>
                  <Input
                    required
                    maxLength={120}
                    value={policyForm.name}
                    onChange={(e) =>
                      setPolicyForm({ ...policyForm, name: e.target.value })
                    }
                    data-testid="assistant-policy-name"
                  />
                </Field>
                <Field label={m("business")}>
                  <Input
                    required
                    pattern="[a-z][a-z0-9_-]{0,79}"
                    maxLength={80}
                    value={policyForm.businessCode}
                    aria-invalid={Boolean(businessCodeError)}
                    aria-describedby={
                      businessCodeError
                        ? "assistant-policy-business-help assistant-policy-business-error"
                        : "assistant-policy-business-help"
                    }
                    onBlur={() => setBusinessCodeTouched(true)}
                    onChange={(e) => {
                      setBusinessCodeTouched(true);
                      setPolicyForm({
                        ...policyForm,
                        businessCode: e.target.value,
                      });
                    }}
                    data-testid="assistant-policy-business"
                  />
                </Field>
                <p
                  id="assistant-policy-business-help"
                  className="text-xs text-[var(--text-subtle)]"
                >
                  {m("businessHelp")}
                </p>
                {businessCodeError && (
                  <p
                    id="assistant-policy-business-error"
                    role="alert"
                    className="text-xs text-[var(--text-danger)]"
                  >
                    {businessCodeError}
                  </p>
                )}
                <Field label={m("provider")}>
                  <select
                    required
                    className={selectStyle}
                    aria-invalid={Boolean(policyProviderError)}
                    aria-describedby={
                      policyProviderError
                        ? "assistant-policy-provider-error"
                        : undefined
                    }
                    value={policyForm.providerId}
                    data-testid="assistant-policy-provider"
                    onChange={(e) => {
                      const item = policyProviders.find(
                        (p) => p.id === e.target.value,
                      );
                      setPolicyForm({
                        ...policyForm,
                        providerId: e.target.value,
                        providerRevision: item?.revision ?? 0,
                        model:
                          item?.models.find((model) => model.purpose === "chat")
                            ?.id ?? "",
                      });
                    }}
                  >
                    <option value="">{m("chooseProvider")}</option>
                    {policyForm.providerId && !selectedProviderIsAvailable && (
                      <option value={policyForm.providerId} disabled>
                        {providers.data?.find(
                          (item) => item.id === policyForm.providerId,
                        )?.name ?? policyForm.providerId}{" "}
                        · {m("unavailable")}
                      </option>
                    )}
                    {policyProviders.map((p) => (
                      <option key={p.id} value={p.id}>
                        {p.name}
                      </option>
                    ))}
                  </select>
                </Field>
                {policyProviderError && (
                  <p
                    id="assistant-policy-provider-error"
                    role="alert"
                    className="text-sm text-[var(--text-danger)]"
                  >
                    {policyProviderError}
                  </p>
                )}
                {availableProviders.isError && (
                  <p role="alert" className="text-sm text-[var(--text-danger)]">
                    {m("loadFailed")}{" "}
                    <Button
                      type="button"
                      variant="outline"
                      onClick={() => void availableProviders.refetch()}
                    >
                      {m("retry")}
                    </Button>
                  </p>
                )}
                {!policyProviderError &&
                  !availableProviders.isLoading &&
                  !availableProviders.isError &&
                  policyProviders.length === 0 && (
                    <p
                      role="status"
                      className="text-sm text-[var(--text-subtle)]"
                    >
                      {m("noEligibleProviders")}
                    </p>
                  )}
                <Field label={m("model")}>
                  <select
                    required
                    className={selectStyle}
                    disabled={!selectedProviderIsAvailable}
                    value={policyForm.model}
                    data-testid="assistant-policy-model"
                    onChange={(e) =>
                      setPolicyForm({
                        ...policyForm,
                        model: e.target.value,
                        providerRevision: selectedProvider?.revision ?? 0,
                      })
                    }
                  >
                    <option value="">{m("chooseModel")}</option>
                    {selectedProvider?.models
                      .filter((model) => model.purpose === "chat")
                      .map((model) => (
                        <option key={model.id} value={model.id}>
                          {model.id}
                        </option>
                      ))}
                  </select>
                </Field>
                {selectedProviderIsAvailable &&
                  selectedProvider &&
                  selectedProvider.revision !== policyForm.providerRevision && (
                    <div className="rounded-md border p-3 text-sm">
                      <p>{m("newVersion")}</p>
                      <Button
                        type="button"
                        variant="outline"
                        onClick={() =>
                          setPolicyForm({
                            ...policyForm,
                            providerRevision: selectedProvider.revision,
                          })
                        }
                      >
                        {m("useLatest")}
                      </Button>
                    </div>
                  )}
                <Field label={m("instructions")}>
                  <Textarea
                    required
                    rows={4}
                    maxLength={8000}
                    value={policyForm.instructions}
                    onChange={(e) =>
                      setPolicyForm({
                        ...policyForm,
                        instructions: e.target.value,
                      })
                    }
                    data-testid="assistant-policy-instructions"
                  />
                </Field>
                <label className="flex items-center gap-2 text-sm">
                  <input
                    type="checkbox"
                    checked={policyForm.knowledgeEnabled}
                    onChange={(e) =>
                      setPolicyForm({
                        ...policyForm,
                        knowledgeEnabled: e.target.checked,
                      })
                    }
                  />
                  {m("knowledge")}
                </label>
                <label className="flex items-center gap-2 text-sm">
                  <input
                    type="checkbox"
                    checked={policyForm.requireEvidence}
                    onChange={(e) =>
                      setPolicyForm({
                        ...policyForm,
                        requireEvidence: e.target.checked,
                      })
                    }
                  />
                  {m("evidence")}
                </label>
                <Field label={m("noAnswer")}>
                  <Textarea
                    required
                    maxLength={1000}
                    value={policyForm.noAnswerMessage}
                    onChange={(e) =>
                      setPolicyForm({
                        ...policyForm,
                        noAnswerMessage: e.target.value,
                      })
                    }
                  />
                </Field>
                <details className="rounded-md border p-3">
                  <summary className="cursor-pointer text-sm">
                    {m("more")}
                  </summary>
                  <div className="mt-3 grid gap-4 sm:grid-cols-2">
                    <Field label={m("style")}>
                      <select
                        data-testid="assistant-policy-style"
                        className={selectStyle}
                        value={policyForm.temperature}
                        onChange={(e) =>
                          setPolicyForm({
                            ...policyForm,
                            temperature: Number(e.target.value),
                          })
                        }
                      >
                        {[0.2, 0.7, 1].map((value, i) => (
                          <option key={value} value={value}>
                            {m(["precise", "balanced", "creative"][i])}
                          </option>
                        ))}
                        {![0.2, 0.7, 1].includes(policyForm.temperature) && (
                          <option value={policyForm.temperature}>
                            {m("custom")}
                          </option>
                        )}
                      </select>
                    </Field>
                    <Field label={m("length")}>
                      <select
                        data-testid="assistant-policy-length"
                        className={selectStyle}
                        value={policyForm.maxOutputTokens}
                        onChange={(e) =>
                          setPolicyForm({
                            ...policyForm,
                            maxOutputTokens: Number(e.target.value),
                          })
                        }
                      >
                        {[1000, 2000, 4000].map((value, i) => (
                          <option key={value} value={value}>
                            {m(["short", "standard", "detailed"][i])}
                          </option>
                        ))}
                        {![1000, 2000, 4000].includes(
                          policyForm.maxOutputTokens,
                        ) && (
                          <option value={policyForm.maxOutputTokens}>
                            {m("custom")}
                          </option>
                        )}
                      </select>
                    </Field>
                  </div>
                </details>
              </>
            )}
            {formError && (
              <p role="alert" className="text-sm text-[var(--text-danger)]">
                {formError}
              </p>
            )}
            <div className="flex justify-end gap-2">
              <Button
                type="button"
                variant="outline"
                disabled={save.isPending}
                onClick={() => {
                  providerEditRequest.current++;
                  setProviderEditLoading(false);
                  setEditor(null);
                }}
              >
                {m("cancel")}
              </Button>
              <Button
                type="submit"
                disabled={
                  save.isPending ||
                  (editor === "policy" && !selectedProviderIsAvailable) ||
                  (editor === "provider" &&
                    Boolean(provider) &&
                    (providerEditLoading ||
                      (providerEditError && !serviceForm.secretReference)))
                }
                data-testid="assistant-models-save"
              >
                {m(save.isPending ? "saving" : "save")}
              </Button>
            </div>
          </form>
        </DialogContent>
      </Dialog>
      <Dialog
        open={statusTarget !== null}
        onOpenChange={(open) => {
          if (!open && !status.isPending) setStatusTarget(null);
        }}
      >
        <DialogContent
          className="model-editor"
          closeButtonClassName="h-11 w-11"
        >
          <DialogHeader>
            <DialogTitle>
              {m(statusTarget?.value.enabled ? "disable" : "enable")}
            </DialogTitle>
            <DialogDescription>
              {m(statusTarget?.value.enabled ? "disableHelp" : "enableHelp")}
            </DialogDescription>
          </DialogHeader>
          {status.isError && (
            <p role="alert">{saveErrorMessage(status.error)}</p>
          )}
          <div className="flex justify-end gap-2">
            <Button
              variant="outline"
              disabled={status.isPending}
              onClick={() => setStatusTarget(null)}
            >
              {m("cancel")}
            </Button>
            <Button disabled={status.isPending} onClick={() => status.mutate()}>
              {m("confirm")}
            </Button>
          </div>
        </DialogContent>
      </Dialog>
    </div>
  );
}
