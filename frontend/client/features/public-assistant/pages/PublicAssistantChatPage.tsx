import { useDateTime } from '@/shared/hooks/useDateTime';
import { useEffect, useRef, useState, type CSSProperties } from "react";
import { useParams } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import {
  ArrowRight,
  ChevronDown,
  ChevronUp,
  History,
  Plus,
  RotateCw,
  Send,
  Square,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Textarea } from "@/components/ui/textarea";
import { useI18n } from "@/context/i18nContext";
import {
  Sheet,
  SheetContent,
  SheetHeader,
  SheetTitle,
  SheetDescription,
} from "@/components/ui/sheet";
import {
  AssistantAvatar,
  AssistantQuickLinks,
} from "../components/AssistantIdentity";
import AssistantMessageMarkdown from "../components/AssistantMessageMarkdown";
import { AssistantAnswerDisclosure } from "../components/AssistantAnswerDisclosure";
import { splitAssistantWelcomeMessage } from "../services/assistantPresentation";
import {
  readAssistantHistory,
  writeAssistantHistory,
  type ChatHistoryEntry,
} from "../services/assistantHistory";
import {
  cancelChat,
  createChat,
  AssistantStreamTimeoutError,
  loadChat,
  readChat,
  sendChat,
  streamChat,
  type ChatEvent,
  type ChatSession,
  type ChatTurn,
} from "../services/assistantChatService";
import { resolveChatBackground } from "../services/assistantTheme";

type Bubble = {
  id: string;
  turnId?: string;
  role: number;
  text: string;
  source?: string;
};
type ActivePending = {
  id: string;
  text: string;
  turn?: ChatTurn;
  sequence: number;
};
const COLLAPSED_QUICK_LINK_COUNT = 6;

function getBrandTextColor(hex: string) {
  const luminance = [0.2126, 0.7152, 0.0722].reduce((sum, weight, index) => {
    const channel =
      Number.parseInt(hex.slice(1 + index * 2, 3 + index * 2), 16) / 255;
    const linear =
      channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4;
    return sum + linear * weight;
  }, 0);

  return luminance > 0.179 ? "#111827" : "#ffffff";
}

export default function PublicAssistantChatPage() {
  const { assistantKey = "" } = useParams();
  return <AssistantChat key={assistantKey} assistantKey={assistantKey} />;
}

function AssistantChat({ assistantKey }: { assistantKey: string }) {
  const { formatDateTimeDisplay } = useDateTime();
  const { t, locale } = useI18n();
  const label = (key: string) => t(`publicAssistant.chat.${key}`);
  const bootstrap = useQuery({
    queryKey: ["assistant-public", assistantKey],
    queryFn: () => loadChat(assistantKey),
    retry: false,
  });
  const [session, setSession] = useState<ChatSession>();
  const [messages, setMessages] = useState<Bubble[]>([]);
  const [text, setText] = useState("");
  const [busy, setBusy] = useState(false);
  const [stopping, setStopping] = useState(false);
  const stopLock = useRef(false);
  const stopOperation = useRef<Promise<void>>();
  const [error, setError] = useState("");
  const [pending, setPending] = useState<ActivePending>();
  const [history, setHistory] = useState<ChatHistoryEntry[]>([]);
  const historyRef = useRef<ChatHistoryEntry[]>([]);
  const [historyOpen, setHistoryOpen] = useState(false);
  const [restoring, setRestoring] = useState(false);
  const [showAllQuickLinks, setShowAllQuickLinks] = useState(false);
  const [showAllQuestions, setShowAllQuestions] = useState(false);
  const [questionOffset, setQuestionOffset] = useState(0);
  const historyEnabled = bootstrap.data?.historyEnabled !== false;
  const saveHistory = (entries: ChatHistoryEntry[]) => {
    historyRef.current = entries;
    setHistory(entries);
    writeAssistantHistory(assistantKey, entries);
  };
  const remember = (
    active: ChatSession,
    title: string,
    request?: ActivePending,
    newActivity = false,
  ) => {
    if (!historyEnabled) return;
    const existing = historyRef.current.find(
      (e) => e.session.conversationId === active.conversationId,
    );
    if (existing && !newActivity) {
      // Reconnection and run status changes are not new visitor messages.
      saveHistory(
        historyRef.current.map((entry) =>
          entry.session.conversationId === active.conversationId
            ? {
                ...entry,
                session: active,
                pending: request ? { id: request.id } : undefined,
              }
            : entry,
        ),
      );
      return;
    }
    saveHistory(
      [
        {
          session: active,
          title: existing?.title || title.slice(0, 60),
          updatedAt: new Date().toISOString(),
          ...(request ? { pending: { id: request.id } } : {}),
        },
        ...historyRef.current.filter(
          (e) => e.session.conversationId !== active.conversationId,
        ),
      ].slice(0, 30),
    );
  };
  const lock = useRef(false);
  const stream = useRef<AbortController>();
  const messageViewport = useRef<HTMLElement>(null);
  const followingLatest = useRef(true);
  const forcedWs =
    new URLSearchParams(location.search).get("transport") === "ws";
  useEffect(() => {
    return () => {
      stream.current?.abort();
    };
  }, []);
  useEffect(() => {
    // Deltas grow an existing bubble without changing the message count.
    // Let pending scroll events record manual reading before following layout.
    const frame = requestAnimationFrame(() => {
      const viewport = messageViewport.current;
      if (viewport && followingLatest.current)
        viewport.scrollTop = viewport.scrollHeight;
    });
    return () => cancelAnimationFrame(frame);
  }, [messages, busy]);
  useEffect(() => {
    const viewport = messageViewport.current;
    if (!viewport) return;
    const observer = new ResizeObserver(() => {
      if (followingLatest.current) viewport.scrollTop = viewport.scrollHeight;
    });
    observer.observe(viewport);
    return () => observer.disconnect();
  }, [bootstrap.data?.anonymousChatEnabled]);
  const sync = async (value: ChatSession) => {
    const result = await readChat(assistantKey, value);
    const bubbles = result.items
      .map((item) => {
        let body: { text?: string; source?: string } = {};
        try {
          body = JSON.parse(item.contentJson);
        } catch {
          /* Retain visible message status without exposing internals. */
        }
        return {
          id: item.id,
          turnId: item.turnId,
          role: item.role,
          text: body.text ?? "",
          source: body.source,
          clientMessageId: item.clientMessageId,
          status: item.status,
        };
      })
      .filter((item) => (item.role === 1 || item.role === 2) && item.text);
    setMessages(bubbles);
    return { items: result.items, bubbles };
  };
  const restore = async (entry: ChatHistoryEntry) => {
    if (lock.current) return;
    followingLatest.current = true;
    lock.current = true;
    setRestoring(true);
    setError("");
    try {
      const result = await sync(entry.session);
      setSession(entry.session);
      const pendingUser =
        entry.pending &&
        result.items.find(
          (item) =>
            item.role === 1 && item.clientMessageId === entry.pending?.id,
        );
      const pendingAnswer =
        pendingUser &&
        result.items.find(
          (item) =>
            item.turnId === pendingUser.turnId &&
            item.role === 2 &&
            [3, 4].includes(item.status),
        );
      if (
        pendingUser &&
        (pendingUser.turnStatus != null
          ? pendingUser.turnStatus < 3
          : !pendingAnswer)
      ) {
        let body: { text?: string } = {};
        try {
          body = JSON.parse(pendingUser.contentJson);
        } catch {
          /* Ignore malformed message content. */
        }
        if (body.text) {
          const turn = await sendChat(
            assistantKey,
            entry.session,
            entry.pending!.id,
            body.text,
          );
          const active: ActivePending = {
            id: entry.pending!.id,
            text: body.text,
            turn,
            sequence: 0,
          };
          setPending(active);
          remember(entry.session, entry.title, active);
          setHistoryOpen(false);
          queueMicrotask(
            () => void send(body.text!, true, entry.session, active),
          );
          return;
        }
      }
      setPending(undefined);
      if (entry.pending) {
        saveHistory(
          historyRef.current.map((item) =>
            item.session.conversationId === entry.session.conversationId
              ? { ...item, pending: undefined }
              : item,
          ),
        );
      }
      setHistoryOpen(false);
    } catch {
      setSession(undefined);
      setMessages([]);
      setPending(undefined);
      setError(label("historyUnavailable"));
    } finally {
      lock.current = false;
      setRestoring(false);
    }
  };
  useEffect(() => {
    if (!bootstrap.data) return;
    stream.current?.abort();
    setSession(undefined);
    setMessages([]);
    setPending(undefined);
    setError("");
    const entries = historyEnabled
      ? readAssistantHistory(assistantKey, bootstrap.data.publishedVersionId)
      : [];
    saveHistory(entries);
    if (entries[0]) void restore(entries[0]);
    // Restore only when the assistant/publication changes, never on token deltas.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [assistantKey, bootstrap.data?.publishedVersionId, historyEnabled]);
  const send = async (
    question: string,
    retry = false,
    sessionOverride?: ChatSession,
    pendingOverride?: ActivePending,
  ) => {
    if (lock.current || !question.trim()) return;
    followingLatest.current = true;
    lock.current = true;
    stopOperation.current = undefined;
    setBusy(true);
    setError("");
    let stage: "submit" | "stream" | "read" = "submit";
    let activeSession = session;
    let current =
      pendingOverride ??
      (retry && pending
        ? { ...pending, sequence: 0 }
        : {
            id: crypto.randomUUID(),
            text: question.trim(),
            sequence: 0,
            turn: undefined as ChatTurn | undefined,
          });
    setPending(current);
    // Show the submitted question immediately, including during session creation.
    // On retry replay the same turn from sequence zero without duplicating text.
    setMessages((previous) => [
      ...previous.filter(
        (item) =>
          item.id !== current.id &&
          (!current.turn || item.turnId !== current.turn.turnId),
      ),
      {
        id: current.id,
        turnId: current.turn?.turnId,
        role: 1,
        text: current.text,
      },
    ]);
    try {
      const active =
        sessionOverride ?? session ?? (await createChat(assistantKey));
      activeSession = active;
      if (!session) setSession(active);
      remember(active, current.text, current, !retry && !pendingOverride);
      const turn =
        pendingOverride?.turn ??
        (await sendChat(assistantKey, active, current.id, current.text));
      current = { ...current, turn };
      remember(active, current.text, current);
      setPending(current);
      setText("");
      setMessages((previous) => [
        ...previous.filter(
          (item) => item.turnId !== turn.turnId && item.id !== current.id,
        ),
        {
          id: current.id,
          turnId: turn.turnId,
          role: 1,
          text: current.text,
        },
      ]);
      let finalError = "";
      let runCompleted = false;
      stage = "stream";
      const controller = new AbortController();
      stream.current = controller;
      const onEvent = (event: ChatEvent) => {
        if (controller.signal.aborted) return;
        // Keep the event callback recoverable. A replay gap is a transport/data
        // condition, not a render exception; the next sync will reconcile the
        // persisted turn state and the user will get a retryable error.
        if (event.type === "replay.gap") {
          finalError = label("connectionFailed");
          return;
        }
        if (event.sequence <= current.sequence) return;
        current = { ...current, sequence: event.sequence };
        setPending(current);
        if (
          event.type === "message.delta" ||
          event.type === "message.completed"
        ) {
          setMessages((previous) => {
            const existing = previous.find(
              (item) =>
                (item.turnId === turn.turnId && item.role === 2) ||
                item.id === turn.runId ||
                item.id === `stream-${turn.runId}`,
            );
            const value = {
              id: `stream-${turn.runId}`,
              turnId: turn.turnId,
              role: 2,
              text:
                event.type === "message.completed"
                  ? (event.payload.text ?? "")
                  : (existing?.text ?? "") + (event.payload.text ?? ""),
              source: event.payload.source,
            };
            return [
              ...previous.filter(
                (item) =>
                  !(item.turnId === turn.turnId && item.role === 2) &&
                  item.id !== turn.runId &&
                  item.id !== `stream-${turn.runId}`,
              ),
              value,
            ];
          });
        }
        if (event.type === "run.failed" || event.type === "run.expired")
          finalError = label("answerFailed");
        if (event.type === "run.completed") runCompleted = true;
      };
      await streamChat(
        assistantKey,
        active,
        turn,
        forcedWs ? "ws" : "sse",
        current.sequence,
        onEvent,
        controller.signal,
      );
      // The terminal event can arrive before the cancel HTTP response. Let the
      // stop operation finish its readback before this send can reconcile state.
      await stopOperation.current;
      if (controller.signal.aborted) return;
      if (runCompleted) {
        remember(active, current.text);
        setPending(undefined);
        setError(finalError);
        return;
      }
      stage = "read";
      await sync(active);
      remember(active, current.text);
      setPending(undefined);
      setError(finalError);
    } catch (failure) {
      await stopOperation.current;
      if (stream.current?.signal.aborted) return;
      if (failure instanceof AssistantStreamTimeoutError) {
        if (activeSession && current.turn) {
          await cancelChat(
            assistantKey,
            activeSession,
            current.turn.runId,
          ).catch(() => undefined);
          await sync(activeSession).catch(() => undefined);
          remember(activeSession, current.text);
          current = {
            id: crypto.randomUUID(),
            text: current.text,
            sequence: 0,
          };
          setPending(current);
        }
        setError(label("answerTimeout"));
      } else if (
        !(failure instanceof DOMException && failure.name === "AbortError")
      ) {
        setError(
          label(
            stage === "submit"
              ? "sendFailed"
              : stage === "read"
                ? "readFailed"
                : "connectionFailed",
          ),
        );
      }
    } finally {
      stream.current = undefined;
      if (!stopLock.current) {
        lock.current = false;
        setBusy(false);
      }
    }
  };
  const stop = () => {
    if (!session || !pending?.turn || stopLock.current) return;
    const runId = pending.turn.runId;
    stopLock.current = true;
    setStopping(true);
    setError("");
    stopOperation.current = (async () => {
      try {
        await cancelChat(assistantKey, session, runId);
        stream.current?.abort();
        remember(session, pending.text);
        setPending(undefined);
        try {
          await sync(session);
        } catch {
          // Cancellation already succeeded. Keep partial text and do not offer
          // to resubmit a cancelled turn just because history could not refresh.
          setError(label("readFailed"));
        }
      } catch {
        setError(label("stopFailed"));
      } finally {
        stopLock.current = false;
        setStopping(false);
        if (!stream.current) {
          lock.current = false;
          setBusy(false);
        }
      }
    })();
  };
  const reset = () => {
    followingLatest.current = true;
    stream.current?.abort();
    setSession(undefined);
    setPending(undefined);
    setMessages([]);
    setError("");
    setText("");
    setShowAllQuickLinks(false);
    setShowAllQuestions(false);
    setQuestionOffset(0);
  };
  if (bootstrap.isLoading)
    return (
      <p role="status" className="p-8">
        {label("loading")}
      </p>
    );
  if (bootstrap.isError || !bootstrap.data?.anonymousChatEnabled)
    return (
      <main className="mx-auto max-w-lg space-y-4 p-8">
        <h1>{label("unavailable")}</h1>
        <Button onClick={() => void bootstrap.refetch()}>
          {label("retry")}
        </Button>
      </main>
    );
  const info = bootstrap.data;
  const welcomeCopy = splitAssistantWelcomeMessage(info.welcomeMessage);
  const color = /^#[0-9a-f]{6}$/i.test(info.primaryColor)
    ? info.primaryColor
    : "#2563eb";
  const brandTextColor = getBrandTextColor(color);
  const quickLinks = info.quickLinks ?? [];
  const visibleQuickLinks = showAllQuickLinks
    ? quickLinks
    : quickLinks.slice(0, COLLAPSED_QUICK_LINK_COUNT);
  const questions = info.suggestedQuestions ?? [];
  const visibleCount = showAllQuestions ? questions.length : 4;
  const visibleQuestions = Array.from(
    { length: Math.min(visibleCount, questions.length) },
    (_, index) => questions[(questionOffset + index) % questions.length],
  );
  const composerQuestions = Array.from(
    { length: Math.min(4, questions.length) },
    (_, index) => questions[(questionOffset + index) % questions.length],
  );
  return (
    <main
      className="flex h-[100dvh] min-h-0 w-full flex-col overflow-hidden text-[var(--text-primary)]"
      style={
        {
          "--brand-primary": color,
          "--brand-soft": `color-mix(in srgb, ${color} 12%, white)`,
          background: resolveChatBackground(info.chatBackgroundColor),
        } as CSSProperties
      }
      data-testid="assistant-public-chat"
    >
      <header className="relative z-10 shrink-0 border-b border-white/70 bg-white/90 backdrop-blur-sm">
        <div className="mx-auto flex w-full max-w-4xl items-center gap-3 px-4 py-3 sm:px-8 sm:py-4">
          <AssistantAvatar
            url={info.homeIconUrl}
            className="h-11 w-11 rounded-lg shadow-sm"
          />
          <h1 className="min-w-0 flex-1 truncate text-base font-semibold text-[var(--text-primary)]">
            {info.displayName}
          </h1>
          {historyEnabled && (
            <Button
              variant="ghost"
              size="icon"
              aria-label={label("history")}
              title={label("history")}
              className="h-11 w-11"
              disabled={busy || restoring}
              onClick={() => setHistoryOpen(true)}
              data-testid="assistant-public-history"
            >
              <History aria-hidden />
            </Button>
          )}
          {messages.length === 0 && (
            <Button
              variant="ghost"
              size="sm"
              aria-label={label("newChat")}
              title={label("newChat")}
              className="h-11 w-11 shrink-0 px-0 sm:h-11 sm:w-auto sm:px-3"
              disabled={busy || restoring}
              onClick={reset}
              data-testid="assistant-public-reset"
            >
              <Plus aria-hidden />
              <span className="hidden sm:inline">{label("newChat")}</span>
            </Button>
          )}
        </div>
      </header>

      <section
        ref={messageViewport}
        onScroll={(event) => {
          const viewport = event.currentTarget;
          followingLatest.current =
            viewport.scrollHeight -
              viewport.clientHeight -
              viewport.scrollTop <=
            32;
        }}
        className="min-h-0 flex-1 overflow-y-auto overscroll-contain"
        aria-label={label("messages")}
        role="log"
        aria-live="polite"
        aria-busy={busy}
      >
        <div className="mx-auto flex min-h-full w-full max-w-4xl flex-col gap-4 px-4 py-3 sm:gap-6 sm:px-8 sm:py-8">
          {!messages.length && (
            <div className="mx-auto flex w-full max-w-2xl flex-col gap-4 py-1 sm:gap-5 sm:py-2">
              <div>
                <div
                  className="min-w-0 space-y-1 rounded-lg border border-l-[3px] px-3 py-2.5"
                  style={{
                    backgroundColor: `color-mix(in srgb, ${color} 6%, white)`,
                    borderColor: `color-mix(in srgb, ${color} 24%, white)`,
                    borderLeftColor: color,
                  }}
                >
                  <p className="truncate text-sm font-semibold text-[var(--text-secondary)]">
                    {info.displayName}
                  </p>
                  <h2
                    className="whitespace-pre-wrap break-words text-lg font-semibold leading-6 text-pretty"
                    style={{ color }}
                    data-testid="assistant-welcome-title"
                  >
                    {welcomeCopy.title}
                  </h2>
                  {welcomeCopy.description && (
                    <p
                      className="whitespace-pre-wrap break-words text-sm leading-5 text-[var(--text-secondary)]"
                      data-testid="assistant-welcome-description"
                    >
                      {welcomeCopy.description}
                    </p>
                  )}
                </div>
              </div>
              {!!info.quickLinks?.length && (
                <nav
                  aria-label={label("quickLinks")}
                  className="rounded-lg border border-white/80 bg-white/75 p-3 shadow-[0_12px_32px_rgba(30,64,110,0.07)] backdrop-blur-sm sm:p-4"
                >
                  <div id="assistant-quick-links">
                    <AssistantQuickLinks links={visibleQuickLinks} />
                  </div>
                  {quickLinks.length > COLLAPSED_QUICK_LINK_COUNT && (
                    <Button
                      type="button"
                      variant="ghost"
                      size="sm"
                      className="mt-2 min-h-11 px-2 text-[var(--brand-primary)]"
                      aria-expanded={showAllQuickLinks}
                      aria-controls="assistant-quick-links"
                      onClick={() => setShowAllQuickLinks((value) => !value)}
                      data-testid="assistant-public-quick-links-toggle"
                    >
                      {showAllQuickLinks ? (
                        <ChevronUp
                          className="mr-2 h-4 w-4"
                          aria-hidden="true"
                        />
                      ) : (
                        <ChevronDown
                          className="mr-2 h-4 w-4"
                          aria-hidden="true"
                        />
                      )}
                      {label(
                        showAllQuickLinks ? "showFewerLinks" : "showMoreLinks",
                      )}
                    </Button>
                  )}
                </nav>
              )}
              {!!info.suggestedQuestions?.length && (
                <section
                  aria-labelledby="assistant-suggested-questions"
                  className="space-y-3 rounded-lg border border-white/75 bg-white/65 p-3 shadow-[0_12px_32px_rgba(30,64,110,0.05)] sm:p-4"
                  data-testid="assistant-suggestions"
                >
                  <div className="flex items-center justify-between gap-3">
                    <h3
                      id="assistant-suggested-questions"
                      className="text-sm font-semibold text-[var(--text-secondary)]"
                    >
                      {label("suggestions")}
                    </h3>
                    {questions.length > 1 && (
                      <Button
                        type="button"
                        variant="ghost"
                        size="sm"
                        className="min-h-10 shrink-0 gap-1.5 px-2 text-[var(--text-muted)] hover:text-[var(--brand-primary)]"
                        aria-label={label("changeQuestions")}
                        onClick={() =>
                          setQuestionOffset(
                            (value) =>
                              (value + Math.min(4, questions.length)) %
                              questions.length,
                          )
                        }
                        data-testid="assistant-public-change-questions"
                      >
                        <RotateCw aria-hidden="true" className="h-4 w-4" />
                        {label("changeQuestions")}
                      </Button>
                    )}
                  </div>
                  <div className="space-y-2">
                    {visibleQuestions.map((question) => (
                      <Button
                        variant="outline"
                        className="h-auto min-h-11 w-full justify-between gap-3 whitespace-normal rounded-md border-white bg-white/95 px-4 py-2.5 text-left text-sm font-normal leading-5 shadow-[0_2px_8px_rgba(31,41,55,0.04)] transition-[border-color,background-color,box-shadow] hover:border-[var(--brand-primary)]/35 hover:bg-white hover:shadow-sm"
                        key={question}
                        disabled={busy}
                        onClick={() => void send(question)}
                        data-testid="assistant-public-suggestion"
                      >
                        <span className="min-w-0 break-words">{question}</span>
                        <ArrowRight
                          className="h-4 w-4 shrink-0 text-[var(--text-subtle)]"
                          aria-hidden="true"
                        />
                      </Button>
                    ))}
                  </div>
                  {questions.length > 4 && (
                    <Button
                      type="button"
                      variant="ghost"
                      size="sm"
                      className="min-h-10 px-2 text-[var(--brand-primary)]"
                      aria-expanded={showAllQuestions}
                      onClick={() => setShowAllQuestions((value) => !value)}
                      data-testid="assistant-public-suggestion-toggle"
                    >
                      {showAllQuestions ? (
                        <ChevronUp
                          className="mr-2 h-4 w-4"
                          aria-hidden="true"
                        />
                      ) : (
                        <ChevronDown
                          className="mr-2 h-4 w-4"
                          aria-hidden="true"
                        />
                      )}
                      {label(showAllQuestions ? "showFewer" : "showMore")}
                    </Button>
                  )}
                </section>
              )}
            </div>
          )}

          {messages.map((message) => {
            const isQuestion = message.role === 1;
            return (
              <div
                key={message.id}
                className={`flex gap-3 ${isQuestion ? "items-end justify-end" : "items-start justify-start"}`}
              >
                {!isQuestion && (
                  <AssistantAvatar url={info.homeIconUrl} className="h-8 w-8" />
                )}
                <div
                  data-testid={
                    isQuestion
                      ? "assistant-public-question"
                      : "assistant-public-answer"
                  }
                  className={`max-w-[min(90%,42rem)] break-words px-4 py-3 text-sm leading-6 shadow-sm sm:px-5 ${isQuestion ? "whitespace-pre-wrap rounded-lg rounded-br-sm text-white" : "rounded-lg rounded-tl-sm border border-[var(--border-control)] bg-[var(--surface-card)] text-[var(--text-primary)]"}`}
                  style={
                    isQuestion
                      ? { backgroundColor: color, color: brandTextColor }
                      : undefined
                  }
                >
                  {isQuestion ? (
                    message.text
                  ) : (
                    <AssistantAnswerDisclosure
                      onExpandedChange={(expanded) => {
                        if (expanded) followingLatest.current = false;
                      }}
                    >
                      <AssistantMessageMarkdown text={message.text} />
                    </AssistantAnswerDisclosure>
                  )}
                </div>
              </div>
            );
          })}
          {busy && (
            <div
              className="flex items-start gap-3 text-sm text-[var(--text-muted)]"
              role="status"
            >
              <AssistantAvatar url={info.homeIconUrl} className="h-8 w-8" />
              <span
                className="flex items-center gap-1 rounded-lg rounded-tl-sm border border-white bg-white/90 px-4 py-3 shadow-sm"
                aria-hidden="true"
              >
                <span className="h-1.5 w-1.5 animate-pulse rounded-full bg-current motion-reduce:animate-none" />
                <span className="h-1.5 w-1.5 animate-pulse rounded-full bg-current [animation-delay:150ms] motion-reduce:animate-none" />
                <span className="h-1.5 w-1.5 animate-pulse rounded-full bg-current [animation-delay:300ms] motion-reduce:animate-none" />
              </span>
              <span className="self-center">{label("answering")}</span>
            </div>
          )}
          <div />
        </div>
      </section>

      <footer className="shrink-0 border-t border-white/70 bg-white/85 px-3 pt-3 pb-[calc(0.75rem+env(safe-area-inset-bottom))] backdrop-blur-sm sm:px-6 sm:pt-4">
        <div className="mx-auto w-full max-w-3xl">
          {error && (
            <div
              role="alert"
              className="mb-2 flex items-center gap-2 rounded-lg border border-[var(--status-danger-border)] bg-[var(--status-danger-bg)] px-3 py-2 text-sm text-[var(--status-danger-text)]"
            >
              <span className="min-w-0 flex-1">{error}</span>
              {pending && (
                <Button
                  disabled={busy}
                  variant="ghost"
                  size="sm"
                  className="shrink-0 text-[var(--status-danger-text)]"
                  onClick={() => void send(pending.text, true)}
                  data-testid="assistant-public-retry"
                >
                  {label("retry")}
                </Button>
              )}
            </div>
          )}
          {messages.length > 0 && (
            <section
              aria-label={label("suggestions")}
              className="mb-2 space-y-1"
              data-testid="assistant-public-composer-actions"
            >
              <div className="flex min-h-11 items-center justify-between gap-2">
                <span className="min-w-0 truncate px-1 text-xs font-medium text-[var(--text-muted)]">
                  {label("suggestions")}
                </span>
                <div className="flex shrink-0 items-center gap-1">
                  {questions.length > 4 && (
                    <Button
                      type="button"
                      variant="ghost"
                      size="sm"
                      className="min-h-11 shrink-0 gap-1 px-2 text-[var(--text-muted)]"
                      aria-label={label("changeQuestions")}
                      onClick={() =>
                        setQuestionOffset(
                          (value) =>
                            (value + Math.min(4, questions.length)) %
                            questions.length,
                        )
                      }
                      data-testid="assistant-public-composer-change-questions"
                    >
                      <RotateCw aria-hidden="true" className="h-4 w-4" />
                      {label("changeQuestions")}
                    </Button>
                  )}
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    className="min-h-11 shrink-0 gap-1.5 px-3"
                    aria-label={label("newChat")}
                    disabled={busy || restoring}
                    onClick={reset}
                    data-testid="assistant-public-reset-composer"
                  >
                    <Plus aria-hidden className="h-4 w-4" />
                    {label("newChat")}
                  </Button>
                </div>
              </div>
              {!!composerQuestions.length && (
                <div
                  role="group"
                  aria-label={label("suggestions")}
                  className="-mx-1 grid max-h-[min(30dvh,16rem)] grid-cols-1 gap-2 overflow-y-auto overscroll-y-contain px-1 py-1"
                  data-testid="assistant-public-composer-question-strip"
                >
                  {composerQuestions.map((question, index) => (
                    <Button
                      key={`${question}-${index}`}
                      type="button"
                      variant="outline"
                      className="h-auto min-h-11 w-full min-w-0 scroll-my-1 justify-start whitespace-normal rounded-md border-white/90 bg-white/90 px-3 py-2 text-left text-sm font-normal leading-5 shadow-sm hover:border-[var(--brand-primary)]/35 hover:bg-white"
                      disabled={busy || restoring}
                      onClick={() => void send(question)}
                      data-testid="assistant-public-composer-suggestion"
                    >
                      <span className="min-w-0 [overflow-wrap:anywhere]">
                        {question}
                      </span>
                    </Button>
                  ))}
                </div>
              )}
            </section>
          )}
          <form
            className="flex items-end gap-2 rounded-lg border border-[var(--border-control)] bg-white p-2 pl-4 shadow-[0_8px_28px_rgba(31,41,55,0.10)] transition-[border-color,box-shadow] focus-within:border-[var(--brand-primary)]/50 focus-within:ring-2 focus-within:ring-[var(--brand-primary)]/15"
            onSubmit={(event) => {
              event.preventDefault();
              void send(text);
            }}
          >
            <Textarea
              aria-label={label("input")}
              name="message"
              autoComplete="off"
              placeholder={`${label("input")}…`}
              value={text}
              onChange={(event) => setText(event.target.value)}
              maxLength={4000}
              rows={2}
              disabled={busy || restoring}
              data-testid="assistant-public-input"
              className="max-h-32 min-h-12 min-w-0 flex-1 resize-none overflow-y-auto border-0 bg-transparent px-0 py-2 text-base leading-6 shadow-none placeholder:text-[var(--text-subtle)] hover:bg-transparent focus-visible:border-0 focus-visible:bg-transparent focus-visible:shadow-none focus-visible:ring-0"
              onKeyDown={(event) => {
                if (
                  event.key === "Enter" &&
                  !event.shiftKey &&
                  !event.nativeEvent.isComposing
                ) {
                  event.preventDefault();
                  void send(text);
                }
              }}
            />
            {busy ? (
              <Button
                type="button"
                size="icon"
                className="h-11 w-11 min-w-0 shrink-0 rounded-md"
                aria-label={label("stop")}
                title={label("stop")}
                data-testid="assistant-public-stop"
                onClick={() => void stop()}
                disabled={!pending?.turn || stopping}
              >
                <Square aria-hidden className="fill-current" />
              </Button>
            ) : (
              <Button
                type="submit"
                size="icon"
                className="h-11 w-11 min-w-0 shrink-0 rounded-md"
                style={{ backgroundColor: color, color: brandTextColor }}
                aria-label={label("send")}
                title={label("send")}
                disabled={restoring || !text.trim()}
                data-testid="assistant-public-send"
              >
                <Send aria-hidden />
              </Button>
            )}
          </form>
          <p className="pt-2 text-center text-xs text-[var(--text-subtle)]">
            {label("disclaimer")}
          </p>
        </div>
      </footer>
      <Sheet open={historyOpen} onOpenChange={setHistoryOpen}>
        <SheetContent
          className="flex w-full flex-col sm:max-w-md"
          data-testid="assistant-history-panel"
        >
          <SheetHeader>
            <SheetTitle>{label("history")}</SheetTitle>
            <SheetDescription>{label("historyDescription")}</SheetDescription>
          </SheetHeader>
          <div className="min-h-0 flex-1 space-y-2 overflow-y-auto py-4">
            {restoring && <p role="status">{label("restoring")}</p>}
            {!history.length && (
              <p className="py-8 text-center text-sm text-[var(--text-subtle)]">
                {label("historyEmpty")}
              </p>
            )}
            {history.map((entry) => (
              <button
                type="button"
                key={entry.session.conversationId}
                disabled={restoring || busy}
                onClick={() => void restore(entry)}
                className={`w-full rounded-lg border px-3 py-3 text-left focus-visible:outline focus-visible:outline-2 focus-visible:outline-[var(--brand-primary)] ${entry.session.conversationId === session?.conversationId ? "border-[var(--brand-primary)] bg-[var(--surface-neutral-muted)]" : "border-[var(--border-subtle)]"}`}
                data-testid="assistant-history-entry"
              >
                <span className="block truncate text-sm font-medium">
                  {entry.title || label("untitled")}
                </span>
                <time
                  className="mt-1 block text-xs text-[var(--text-subtle)]"
                  dateTime={entry.updatedAt}
                >
                  {formatDateTimeDisplay(entry.updatedAt, locale)}
                </time>
              </button>
            ))}
          </div>
        </SheetContent>
      </Sheet>
    </main>
  );
}
