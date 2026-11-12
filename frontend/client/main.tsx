import React from 'react';
import ReactDOM from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { BrowserRouter } from 'react-router-dom';
import { I18nProvider } from './context/i18nContext';
import App from './App';
import './global.css';

const client = new QueryClient({ defaultOptions: { queries: { retry: 1, refetchOnWindowFocus: false } } });
ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode><QueryClientProvider client={client}><I18nProvider><BrowserRouter><App /></BrowserRouter></I18nProvider></QueryClientProvider></React.StrictMode>,
);
