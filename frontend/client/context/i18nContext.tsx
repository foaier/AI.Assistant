import { createContext, useContext, useMemo, useState, type ReactNode } from 'react';
import { publicAssistantMessages } from '@/i18n/publicAssistantMessages';

type Locale = keyof typeof publicAssistantMessages;
const LocaleContext = createContext<{ locale: Locale; setLocale: (locale: Locale) => void }>({ locale: 'zhCN', setLocale: () => {} });

export function I18nProvider({ children }: { children: ReactNode }) {
  const [locale, updateLocale] = useState<Locale>(() => localStorage.getItem('assistant.locale') === 'enUS' ? 'enUS' : 'zhCN');
  const value = useMemo(() => ({ locale, setLocale: (next: Locale) => { localStorage.setItem('assistant.locale', next); updateLocale(next); } }), [locale]);
  return <LocaleContext.Provider value={value}>{children}</LocaleContext.Provider>;
}

export function useI18n() {
  const state = useContext(LocaleContext);
  return useMemo(() => ({ ...state, t: (key: string, values?: Record<string, string | number>) => {
    const message = (publicAssistantMessages[state.locale] as Record<string, string>)[key] ?? (publicAssistantMessages.enUS as Record<string, string>)[key] ?? key;
    return message.replace(/\{(\w+)\}/g, (match, name: string) => String(values?.[name] ?? match));
  } }), [state]);
}
