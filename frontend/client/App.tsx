import { lazy, Suspense } from 'react';
import { BookOpen, Bot, SlidersHorizontal, PlugZap } from 'lucide-react';
import { Navigate, NavLink, Outlet, Route, Routes } from 'react-router-dom';
import { useI18n } from './context/i18nContext';
import { Button } from './components/ui/button';

const List = lazy(() => import('./features/public-assistant/pages/PublicAssistantListPage'));
const Workbench = lazy(() => import('./features/public-assistant/pages/PublicAssistantWorkbenchPage'));
const Knowledge = lazy(() => import('./features/public-assistant/pages/PublicAssistantKnowledgePage'));
const Models = lazy(() => import('./features/public-assistant/pages/AssistantModelsPage'));
const SystemModels = lazy(() => import('./features/public-assistant/pages/SystemAssistantModelsPage'));
const Chat = lazy(() => import('./features/public-assistant/pages/PublicAssistantChatPage'));

function Loading() { return <div className="flex min-h-[40vh] w-full items-center justify-center text-sm text-muted-foreground" role="status">加载中…</div>; }

function AdminShell() {
  const { locale, setLocale } = useI18n();
  const english = locale === 'enUS';
  const links = [
    { to: '/admin/assistants', icon: Bot, label: english ? 'Assistants' : '智能助手' },
    { to: '/admin/knowledge-bases', icon: BookOpen, label: english ? 'Knowledge' : '知识库' },
    { to: '/admin/assistant-models', icon: SlidersHorizontal, label: english ? 'Model policies' : '模型策略' },
    { to: '/admin/system/assistant-models', icon: PlugZap, label: english ? 'Model services' : '模型服务' },
  ];
  return <div className="assistant-shell">
    <aside className="assistant-sidebar">
      <div className="assistant-brand"><span className="rounded-xl bg-blue-600 p-2.5 text-white"><Bot size={24} /></span><div><div className="text-lg font-semibold">AI.Assistant</div><p className="mt-0.5 text-xs text-muted-foreground">{english ? 'Your intelligent workspace' : '智能助手工作台'}</p></div></div>
      <nav aria-label={english ? 'Main navigation' : '主导航'} className="assistant-nav">{links.map(({ to, icon: Icon, label }) => <NavLink key={to} to={to}><Icon size={18} />{label}</NavLink>)}</nav>
    </aside>
    <div className="assistant-main">
      <header className="assistant-header"><span className="text-sm text-muted-foreground">{english ? 'AI.Assistant workspace' : 'AI.Assistant 工作台'}</span><Button variant="ghost" size="sm" onClick={() => setLocale(english ? 'zhCN' : 'enUS')}>{english ? '中文' : 'English'}</Button></header>
      <main className="assistant-content"><Outlet /></main>
    </div>
  </div>;
}

export default function App() {
  return <Suspense fallback={<Loading />}><Routes>
    <Route path="/assistants/:assistantKey" element={<Chat />} />
    <Route element={<AdminShell />}>
      <Route path="/admin/assistants" element={<List />} />
      <Route path="/admin/assistants/:assistantId" element={<Workbench />} />
      <Route path="/admin/knowledge-bases" element={<Knowledge />} />
      <Route path="/admin/assistant-models" element={<Models />} />
      <Route path="/admin/system/assistant-models" element={<SystemModels />} />
    </Route>
    <Route path="/" element={<Navigate to="/admin/assistants" replace />} />
    <Route path="*" element={<main className="m-auto p-8 text-center"><h1 className="text-2xl font-semibold">页面不存在</h1><NavLink className="mt-4 inline-block text-blue-600" to="/">返回工作台</NavLink></main>} />
  </Routes></Suspense>;
}
