import { useEffect, useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from "@/components/ui/sheet";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { useI18n } from "@/context/i18nContext";
import {
  listKnowledgeQna,
  saveKnowledgeQna,
  testKnowledgeQna,
  type AssistantKnowledgeQna,
  deleteKnowledgeQna,
} from "../services/publicAssistantService";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog";

// Mirrors server exact-match normalization for immediate duplicate feedback; server remains authoritative.
export const normalizeQnaQuestion = (value: string) =>
  value.normalize("NFKC").trim().replace(/\s+/gu, " ").toUpperCase();

export function KnowledgeQnaPanel({
  knowledgeBaseId,
}: {
  knowledgeBaseId: string;
}) {
  const { t } = useI18n();
  const p = (key: string) => t(`publicAssistant.p0.${key}`);
  const client = useQueryClient();
  const [search, setSearch] = useState("");
  const [filter, setFilter] = useState("all");
  const [editor, setEditor] = useState<AssistantKnowledgeQna | null>(null);
  const initial = useRef("");
  const trigger = useRef<HTMLElement | null>(null);
  const [discard, setDiscard] = useState(false);
  const [validation, setValidation] = useState("");
  const [notice, setNotice] = useState("");
  const [testItem, setTestItem] = useState<AssistantKnowledgeQna | null>(null);
  const [testQuestion, setTestQuestion] = useState("");
  const [deleteTarget, setDeleteTarget] =
    useState<AssistantKnowledgeQna | null>(null);
  const query = useQuery({
    queryKey: ["assistant-knowledge-qna", knowledgeBaseId],
    queryFn: () => listKnowledgeQna(knowledgeBaseId),
  });
  const dirty = editor !== null && JSON.stringify(editor) !== initial.current;
  useEffect(() => {
    if (!dirty) return;
    const protect = (event: BeforeUnloadEvent) => {
      event.preventDefault();
      event.returnValue = "";
    };
    window.addEventListener("beforeunload", protect);
    return () => window.removeEventListener("beforeunload", protect);
  }, [dirty]);
  const newItem = (): AssistantKnowledgeQna => ({
    id: "",
    knowledgeBaseId,
    question: "",
    answer: "",
    enabled: true,
    showAsSuggestion: false,
    sortOrder: (query.data?.length ?? 0) + 1,
  });
  const beginEdit = (item: AssistantKnowledgeQna) => {
    trigger.current = document.activeElement as HTMLElement;
    initial.current = JSON.stringify(item);
    setEditor({ ...item });
    setDiscard(false);
    setValidation("");
    save.reset();
  };
  const save = useMutation({
    mutationFn: ({ item }: { item: AssistantKnowledgeQna; again: boolean }) =>
      saveKnowledgeQna(knowledgeBaseId, {
        id: item.id || undefined,
        question: item.question.trim(),
        answer: item.answer ?? "",
        enabled: item.enabled,
        showAsSuggestion: item.showAsSuggestion,
        sortOrder: item.sortOrder,
      }),
    onSuccess: (saved, { again }) => {
      client.setQueryData<AssistantKnowledgeQna[]>(
        ["assistant-knowledge-qna", knowledgeBaseId],
        (rows = []) => [...rows.filter((row) => row.id !== saved.id), saved],
      );
      void client.invalidateQueries({
        queryKey: ["assistant-knowledge-qna", knowledgeBaseId],
      });
      void client.invalidateQueries({
        queryKey: ["assistant-knowledge-bases"],
      });
      setNotice(p("saved"));
      if (again) {
        const item = newItem();
        initial.current = JSON.stringify(item);
        setEditor(item);
      } else setEditor(null);
    },
  });
  const submit = (again = false) => {
    if (!editor || save.isPending) return;
    if (!editor.question.trim()) {
      setValidation(p("required"));
      return;
    }
    if (
      query.data?.some(
        (item) =>
          item.id !== editor.id &&
          normalizeQnaQuestion(item.question) ===
            normalizeQnaQuestion(editor.question),
      )
    ) {
      setValidation(p("duplicate"));
      return;
    }
    setValidation("");
    save.mutate({ item: editor, again });
  };
  const test = useMutation({
    mutationFn: () =>
      testKnowledgeQna(knowledgeBaseId, testItem!.id, testQuestion.trim()),
  });
  const remove = useMutation({
    mutationFn: (item: AssistantKnowledgeQna) =>
      deleteKnowledgeQna(knowledgeBaseId, item.id),
    onSuccess: async () => {
      setDeleteTarget(null);
      await client.invalidateQueries({
        queryKey: ["assistant-knowledge-qna", knowledgeBaseId],
      });
      await client.invalidateQueries({
        queryKey: ["assistant-knowledge-bases"],
      });
    },
  });
  const rows = (query.data ?? [])
    .filter((item) =>
      `${item.question} ${item.answer ?? ""}`
        .toLocaleLowerCase()
        .includes(search.toLocaleLowerCase()),
    )
    .filter(
      (item) =>
        filter === "all" ||
        (filter === "enabled" && item.enabled) ||
        (filter === "disabled" && !item.enabled) ||
        (filter === "unanswered" && !item.answer?.trim()),
    );
  const change = (patch: Partial<AssistantKnowledgeQna>) => {
    setEditor((item) => item && { ...item, ...patch });
    setValidation("");
  };
  return (
    <section className="space-y-4" data-testid="knowledge-qna-list">
      <div className="flex flex-wrap items-end gap-3">
        <label className="min-w-0 flex-1 space-y-1 text-sm">
          {p("search")}
          <Input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            data-testid="knowledge-qna-search"
          />
        </label>
        <label className="space-y-1 text-sm">
          {p("enabled")}
          <select
            className="block h-10 rounded-md border bg-[var(--surface-overlay)] px-3"
            value={filter}
            onChange={(e) => setFilter(e.target.value)}
            data-testid="knowledge-qna-filter"
          >
            {["all", "enabled", "disabled", "unanswered"].map((key) => (
              <option key={key} value={key}>
                {p(key)}
              </option>
            ))}
          </select>
        </label>
        <Button
          onClick={() => beginEdit(newItem())}
          data-testid="knowledge-qna-add"
        >
          {p("addQna")}
        </Button>
      </div>
      <p className="text-sm text-[var(--text-subtle)]">{p("qnaHelp")}</p>
      {notice && <p role="status">{notice}</p>}
      {query.isLoading ? (
        <p role="status">{p("loading")}</p>
      ) : query.isError ? (
        <div role="alert">
          {p("loadFailed")}{" "}
          <Button variant="outline" onClick={() => void query.refetch()}>
            {p("retry")}
          </Button>
        </div>
      ) : rows.length === 0 ? (
        <p className="rounded-lg border border-dashed p-8 text-center text-sm">
          {p(query.data?.length ? "noMatch" : "noQna")}
        </p>
      ) : (
        <ul className="divide-y rounded-lg border">
          {rows.map((item) => (
            <li
              key={item.id}
              className="flex flex-wrap items-center justify-between gap-3 p-4"
              data-testid={`knowledge-qna-${item.id}`}
            >
              <div className="min-w-0 flex-1">
                <div className="break-words font-medium">{item.question}</div>
                <div className="mt-1 flex flex-wrap gap-3 text-xs text-[var(--text-subtle)]">
                  <span>
                    {p(
                      !item.enabled
                        ? "disabled"
                        : item.answer?.trim()
                          ? "direct"
                          : "generated",
                    )}
                  </span>
                  {item.showAsSuggestion && (
                    <span>
                      {p("recommendation")} · {item.sortOrder}
                    </span>
                  )}
                </div>
              </div>
              <div className="flex gap-2">
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => beginEdit(item)}
                  data-testid={`knowledge-qna-edit-${item.id}`}
                >
                  {p("edit")}
                </Button>
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => {
                    setTestItem(item);
                    setTestQuestion(item.question);
                    test.reset();
                  }}
                  data-testid={`knowledge-qna-test-${item.id}`}
                >
                  {p("test")}
                </Button>
                <Button
                  variant="ghost"
                  size="sm"
                  className="text-[var(--text-danger)]"
                  onClick={() => {
                    remove.reset();
                    setDeleteTarget(item);
                  }}
                  data-testid={`knowledge-qna-delete-${item.id}`}
                  aria-label={`${p("delete")} ${item.question}`}
                >
                  <Trash2 className="h-4 w-4" aria-hidden="true" />
                </Button>
              </div>
            </li>
          ))}
        </ul>
      )}
      <Sheet
        open={editor !== null}
        onOpenChange={(open) => {
          if (!open && !save.isPending) {
            if (dirty) setDiscard(true);
            else setEditor(null);
          }
        }}
      >
        <SheetContent
          className="flex w-full flex-col sm:max-w-xl"
          data-testid="knowledge-qna-editor"
          onCloseAutoFocus={(event) => {
            event.preventDefault();
            trigger.current?.focus();
          }}
        >
          <SheetHeader className="pr-8">
            <SheetTitle>{p(editor?.id ? "editQna" : "addQna")}</SheetTitle>
            <SheetDescription>{p("qnaHelp")}</SheetDescription>
          </SheetHeader>
          {editor && (
            <form
              className="flex min-h-0 flex-1 flex-col gap-5"
              onSubmit={(e) => {
                e.preventDefault();
                submit();
              }}
            >
              <fieldset
                disabled={save.isPending}
                className="min-h-0 flex-1 space-y-5 overflow-auto py-3"
              >
                <label className="block space-y-2 text-sm">
                  {p("question")}
                  <Input
                    maxLength={500}
                    value={editor.question}
                    onChange={(e) => change({ question: e.target.value })}
                    aria-invalid={Boolean(validation)}
                    data-testid="knowledge-qna-question"
                  />
                </label>
                <label className="block space-y-2 text-sm">
                  {p("answer")}
                  <Textarea
                    rows={8}
                    maxLength={10000}
                    value={editor.answer ?? ""}
                    onChange={(e) => change({ answer: e.target.value })}
                    data-testid="knowledge-qna-answer"
                  />
                </label>
                <label className="flex items-center gap-3 text-sm">
                  <input
                    type="checkbox"
                    checked={editor.showAsSuggestion}
                    onChange={(e) =>
                      change({ showAsSuggestion: e.target.checked })
                    }
                    data-testid="knowledge-qna-suggestion-toggle"
                  />
                  {p("recommendation")}
                </label>
                <label className="flex items-center gap-3 text-sm">
                  <input
                    type="checkbox"
                    checked={editor.enabled}
                    onChange={(e) => change({ enabled: e.target.checked })}
                    data-testid="knowledge-qna-enabled"
                  />
                  {p("enabled")}
                </label>
                {editor.showAsSuggestion && (
                  <label className="block space-y-2 text-sm">
                    {t("publicAssistant.knowledge.sortOrder")}
                    <Input
                      type="number"
                      min={0}
                      max={9999}
                      value={editor.sortOrder}
                      onChange={(e) =>
                        change({
                          sortOrder: Math.max(
                            0,
                            Math.min(9999, Number(e.target.value) || 0),
                          ),
                        })
                      }
                      data-testid="knowledge-qna-sort"
                    />
                  </label>
                )}
                {validation && (
                  <p role="alert" className="text-[var(--text-danger)]">
                    {validation}
                  </p>
                )}
                {save.isError && (
                  <p role="alert" className="text-[var(--text-danger)]">
                    {String(save.error).includes("duplicate")
                      ? p("duplicate")
                      : p("saveFailed")}
                  </p>
                )}
              </fieldset>
              {discard ? (
                <div role="alert" className="space-y-3 rounded border p-3">
                  <p>{p("discardHelp")}</p>
                  <Button
                    type="button"
                    variant="outline"
                    onClick={() => setDiscard(false)}
                  >
                    {p("keepEditing")}
                  </Button>{" "}
                  <Button
                    type="button"
                    onClick={() => {
                      setDiscard(false);
                      setEditor(null);
                    }}
                    data-testid="knowledge-qna-discard"
                  >
                    {p("discard")}
                  </Button>
                </div>
              ) : (
                <div className="flex flex-wrap justify-end gap-2 border-t pt-4">
                  <Button
                    type="button"
                    variant="outline"
                    disabled={save.isPending}
                    onClick={() => (dirty ? setDiscard(true) : setEditor(null))}
                  >
                    {p("cancel")}
                  </Button>
                  <Button
                    type="button"
                    variant="outline"
                    disabled={save.isPending}
                    onClick={() => submit(true)}
                    data-testid="knowledge-qna-save-next"
                  >
                    {p("saveNext")}
                  </Button>
                  <Button
                    type="submit"
                    disabled={save.isPending}
                    data-testid="knowledge-qna-save"
                  >
                    {save.isPending
                      ? t("publicAssistant.workbench.saving")
                      : t("publicAssistant.knowledge.save")}
                  </Button>
                </div>
              )}
            </form>
          )}
        </SheetContent>
      </Sheet>
      <Dialog
        open={testItem !== null}
        onOpenChange={(open) => {
          if (!open) {
            setTestItem(null);
            test.reset();
          }
        }}
      >
        <DialogContent data-testid="knowledge-qna-test">
          <DialogHeader>
            <DialogTitle>{p("qnaTest")}</DialogTitle>
            <DialogDescription>{p("testHelp")}</DialogDescription>
          </DialogHeader>
          <form
            className="space-y-3"
            onSubmit={(e) => {
              e.preventDefault();
              if (!test.isPending && testQuestion.trim()) test.mutate();
            }}
          >
            <label className="block space-y-2 text-sm">
              {p("testQuestion")}
              <Input
                value={testQuestion}
                maxLength={500}
                onChange={(e) => {
                  setTestQuestion(e.target.value);
                  test.reset();
                }}
                data-testid="knowledge-qna-test-question"
              />
            </label>
            <Button
              disabled={!testQuestion.trim() || test.isPending}
              data-testid="knowledge-qna-test-run"
            >
              {p(test.isPending ? "testing" : "test")}
            </Button>
          </form>
          {test.isError && <p role="alert">{p("testFailed")}</p>}
          {test.data && (
            <div
              role="status"
              className="space-y-2 rounded border p-4"
              data-testid="knowledge-qna-test-result"
            >
              <p>
                {p(
                  !testItem?.enabled
                    ? "testDisabled"
                    : !test.data.matched
                      ? "testNoMatch"
                      : test.data.answerMode === "direct"
                        ? "direct"
                        : "generated",
                )}
              </p>
              <p className="whitespace-pre-wrap break-words">
                {test.data.answer ?? p("noGenerated")}
              </p>
              <p className="text-sm">{p("noAi")}</p>
            </div>
          )}
        </DialogContent>
      </Dialog>
      <AlertDialog
        open={deleteTarget !== null}
        onOpenChange={(open) => {
          if (!open && !remove.isPending) setDeleteTarget(null);
        }}
      >
        <AlertDialogContent data-testid="knowledge-qna-delete-dialog">
          <AlertDialogHeader>
            <AlertDialogTitle>{p("deleteTitle")}</AlertDialogTitle>
            <AlertDialogDescription>
              {p("deleteDescription").replace(
                "{question}",
                deleteTarget?.question ?? "",
              )}
            </AlertDialogDescription>
          </AlertDialogHeader>
          {remove.isError && (
            <p role="alert" className="text-[var(--text-danger)]">
              {p("deleteFailed")}
            </p>
          )}
          <AlertDialogFooter>
            <AlertDialogCancel disabled={remove.isPending}>
              {p("cancel")}
            </AlertDialogCancel>
            <AlertDialogAction
              disabled={remove.isPending}
              onClick={(event) => {
                event.preventDefault();
                if (deleteTarget) remove.mutate(deleteTarget);
              }}
              data-testid="knowledge-qna-delete-confirm"
            >
              {remove.isPending ? p("deleting") : p("delete")}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </section>
  );
}
