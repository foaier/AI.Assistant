import { lazy, Suspense } from "react";
import {
  ArrowUpRight,
  BookOpen,
  Bot,
  ChevronRight,
  Github,
  Languages,
  Layers3,
  PlugZap,
  SlidersHorizontal,
  Sparkles,
} from "lucide-react";
import {
  Link,
  Navigate,
  NavLink,
  Outlet,
  Route,
  Routes,
  useLocation,
} from "react-router-dom";
import { useI18n } from "./context/i18nContext";
import { Button } from "./components/ui/button";

const List = lazy(
  () => import("./features/public-assistant/pages/PublicAssistantListPage"),
);
const Workbench = lazy(
  () =>
    import("./features/public-assistant/pages/PublicAssistantWorkbenchPage"),
);
const Knowledge = lazy(
  () =>
    import("./features/public-assistant/pages/PublicAssistantKnowledgePage"),
);
const Models = lazy(
  () => import("./features/public-assistant/pages/AssistantModelsPage"),
);
const SystemModels = lazy(
  () => import("./features/public-assistant/pages/SystemAssistantModelsPage"),
);
const Chat = lazy(
  () => import("./features/public-assistant/pages/PublicAssistantChatPage"),
);

/**
 * 在页面代码加载期间提供可被辅助技术识别的状态提示。
 */
function Loading() {
  const { locale } = useI18n();
  return (
    <div
      className="flex min-h-[40vh] w-full items-center justify-center text-sm text-muted-foreground"
      role="status"
    >
      {locale === "enUS" ? "Loading…" : "加载中…"}
    </div>
  );
}

/**
 * 统一管理工作台导航与页面层级，移动端采用紧凑的四列导航。
 */
function AdminShell() {
  const { locale, setLocale } = useI18n();
  const { pathname } = useLocation();
  const english = locale === "enUS";
  const links = [
    {
      to: "/admin/assistants",
      icon: Bot,
      label: english ? "Assistants" : "智能助手",
    },
    {
      to: "/admin/knowledge-bases",
      icon: BookOpen,
      label: english ? "Knowledge" : "知识库",
    },
    {
      to: "/admin/assistant-models",
      icon: SlidersHorizontal,
      label: english ? "Model policies" : "模型策略",
    },
    {
      to: "/admin/system/assistant-models",
      icon: PlugZap,
      label: english ? "Model services" : "模型服务",
    },
  ];
  const currentPage = links.find(
    ({ to }) => pathname === to || pathname.startsWith(`${to}/`),
  );
  return (
    <div className="assistant-shell">
      <a className="assistant-skip-link" href="#workspace-content">
        {english ? "Skip to content" : "跳转到主要内容"}
      </a>
      <aside className="assistant-sidebar">
        <Link to="/admin/assistants" className="assistant-brand">
          <span className="assistant-brand-mark">
            <Layers3 size={23} strokeWidth={1.8} aria-hidden />
          </span>
          <span>
            <span className="assistant-brand-name">AI.Assistant</span>
            <span className="assistant-brand-caption">
              {english ? "A workspace for your ideas" : "让想法，成为智能助手"}
            </span>
          </span>
        </Link>
        <nav
          aria-label={english ? "Main navigation" : "主导航"}
          className="assistant-nav"
        >
          {links.map(({ to, icon: Icon, label }, index) => (
            <div key={to} className="assistant-nav-entry">
              {(index === 0 || index === 2) && (
                <p className="assistant-nav-label">
                  {index === 0
                    ? english
                      ? "WORKSPACE"
                      : "工作空间"
                    : english
                      ? "CONFIGURATION"
                      : "配置中心"}
                </p>
              )}
              <NavLink to={to}>
                <Icon size={19} strokeWidth={1.7} aria-hidden />
                <span>{label}</span>
                <ChevronRight
                  className="assistant-nav-arrow"
                  size={15}
                  aria-hidden
                />
              </NavLink>
            </div>
          ))}
        </nav>
        <div className="assistant-sidebar-bottom">
          <div className="assistant-sidebar-guide">
            <Sparkles size={20} aria-hidden />
            <h2>{english ? "Start with a connection" : "从连接模型开始"}</h2>
            <p>
              {english
                ? "Bring your model, shape its answers, and build your assistant."
                : "接入模型、配置策略，打造属于你的智能助手。"}
            </p>
            <Link to="/admin/system/assistant-models">
              {english ? "Configure models" : "配置模型服务"}
              <ArrowUpRight size={15} aria-hidden />
            </Link>
          </div>
          <span className="assistant-sidebar-signature">
            <Layers3 size={14} aria-hidden />
            {english ? "Your AI. Your possibilities." : "你的 AI，更多可能。"}
          </span>
        </div>
      </aside>
      <div className="assistant-main">
        <header className="assistant-header">
          <div className="assistant-breadcrumb">
            <span>{english ? "Workspace" : "工作台"}</span>
            <ChevronRight size={14} aria-hidden />
            <span aria-current="page">{currentPage?.label}</span>
          </div>
          <div className="assistant-header-actions">
            <a
              className="assistant-source-link"
              href="https://github.com/foaier/AI.Assistant"
              target="_blank"
              rel="noreferrer"
              aria-label={
                english
                  ? "GitHub repository (opens a new tab)"
                  : "GitHub 源码仓库（在新标签页打开）"
              }
            >
              <Github size={18} aria-hidden />
            </a>
            <span className="assistant-header-divider" aria-hidden />
            <Button
              variant="ghost"
              className="assistant-language"
              onClick={() => setLocale(english ? "zhCN" : "enUS")}
            >
              <Languages aria-hidden />
              {english ? "中文" : "English"}
            </Button>
          </div>
        </header>
        <main
          id="workspace-content"
          tabIndex={-1}
          className="assistant-content"
        >
          <div className="assistant-page-card">
            <Suspense fallback={<Loading />}>
              <Outlet />
            </Suspense>
          </div>
        </main>
      </div>
    </div>
  );
}

/**
 * 按需加载公开对话与管理页面，并为工作台提供统一的导航外壳。
 */
export default function App() {
  return (
    <Suspense fallback={<Loading />}>
      <Routes>
        <Route path="/assistants/:assistantKey" element={<Chat />} />
        <Route element={<AdminShell />}>
          <Route path="/admin/assistants" element={<List />} />
          <Route
            path="/admin/assistants/:assistantId"
            element={<Workbench />}
          />
          <Route path="/admin/knowledge-bases" element={<Knowledge />} />
          <Route path="/admin/assistant-models" element={<Models />} />
          <Route
            path="/admin/system/assistant-models"
            element={<SystemModels />}
          />
        </Route>
        <Route path="/" element={<Navigate to="/admin/assistants" replace />} />
        <Route
          path="*"
          element={
            <main className="m-auto p-8 text-center">
              <h1 className="text-2xl font-semibold">页面不存在</h1>
              <NavLink className="mt-4 inline-block text-blue-600" to="/">
                返回工作台
              </NavLink>
            </main>
          }
        />
      </Routes>
    </Suspense>
  );
}
