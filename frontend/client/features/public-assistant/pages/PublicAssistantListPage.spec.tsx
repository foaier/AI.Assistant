// Spec: specs/136-public-assistant/results/S1-workbench-execution-card.md
// Also: specs/136-public-assistant/results/create-form-validation-2026-11-09.md
import React from 'react';
import '@testing-library/jest-dom/vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import PublicAssistantListPage from './PublicAssistantListPage';
import {
  createAssistant,
  listAssistants,
  type Assistant,
  type AssistantListItem,
} from '../services/publicAssistantService';

const locale = vi.hoisted(() => ({ value: 'zhCN' as 'zhCN' | 'enUS' }));
vi.mock('@/context/i18nContext', async () => {
  const { publicAssistantMessages } = await import('@/i18n/publicAssistantMessages');
  return {
    useI18n: () => ({
      locale: locale.value,
      t: (key: string) => (publicAssistantMessages[locale.value] as Record<string, string>)[key] ?? key,
    }),
  };
});

vi.mock('../services/publicAssistantService', () => ({
  DEFAULT_ASSISTANT_CONFIGURATION: {
    welcomeMessage: '你好，我是智能客服，有什么可以帮你？',
    suggestedQuestions: ['如何报名？', '活动什么时候开始？', '如何联系主办方？'],
    iconUrl: '',
    primaryColor: '#2563eb',
    capabilities: { knowledgeQa: true, webSearch: false, customQa: true },
    retrieval: { topK: 5, rerank: true },
  },
  listAssistants: vi.fn(),
  createAssistant: vi.fn(),
  deleteAssistant: vi.fn(),
  getAssistant: vi.fn(),
  saveAssistantDraft: vi.fn(),
}));

const mockedListAssistants = vi.mocked(listAssistants);
const mockedCreateAssistant = vi.mocked(createAssistant);

const configuration = {
  welcomeMessage: '欢迎咨询',
  suggestedQuestions: ['报名方式'],
  iconUrl: '',
  primaryColor: '#2563eb',
  capabilities: { knowledgeQa: true, webSearch: false, customQa: true },
  retrieval: { topK: 5, rerank: true },
};

function assistant(overrides: Partial<Assistant> = {}): Assistant {
  return {
    id: 'assistant-1',
    name: '默认客服',
    description: '默认描述',
    draftRevision: 1,
    configuration,
    ...overrides,
  };
}

function listItem(overrides: Partial<AssistantListItem> = {}): AssistantListItem {
  const source = assistant(overrides as Partial<Assistant>);
  return {
    id: source.id,
    name: source.name,
    description: source.description,
    draftRevision: source.draftRevision,
  };
}

function renderPage() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/admin/assistants']}>
        <Routes>
          <Route path="/admin/assistants" element={<PublicAssistantListPage />} />
          <Route
            path="/admin/assistants/:assistantId"
            element={<div data-testid="assistant-created-route">created route</div>}
          />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('PublicAssistantListPage', () => {
  beforeEach(() => {
    locale.value = 'zhCN';
    mockedListAssistants.mockResolvedValue([listItem()]);
    mockedCreateAssistant.mockResolvedValue(assistant({ id: 'created-1', name: '新客服' }));
  });

  afterEach(() => {
    cleanup();
    vi.clearAllMocks();
  });

  it('renders assistant cards from the canonical list query', async () => {
    renderPage();

    expect(await screen.findByTestId('public-assistant-list')).toBeInTheDocument();
    expect(await screen.findByText('默认客服')).toBeInTheDocument();
    expect(screen.getByText('默认描述')).toBeInTheDocument();
    expect(mockedListAssistants).toHaveBeenCalledTimes(1);
  });

  it('creates an assistant and navigates to the single-assistant workbench', async () => {
    renderPage();

    await screen.findByText('默认客服');
    fireEvent.click(screen.getByTestId('public-assistant-create'));
    fireEvent.change(screen.getByTestId('public-assistant-create-name'), {
      target: { value: '新客服' },
    });
    fireEvent.change(screen.getByTestId('public-assistant-create-description'), {
      target: { value: '新的客服说明' },
    });
    fireEvent.click(screen.getByRole('button', { name: '创建' }));

    await waitFor(() => {
      expect(mockedCreateAssistant).toHaveBeenCalledWith(
        expect.objectContaining({
          name: '新客服',
          description: '新的客服说明',
          configuration: expect.objectContaining({
            capabilities: expect.objectContaining({ knowledgeQa: true }),
          }),
        }),
      );
    });
    expect(await screen.findByTestId('assistant-created-route')).toBeInTheDocument();
  });

  async function openCreate() {
    renderPage();
    await screen.findByText('默认客服');
    fireEvent.click(screen.getByTestId('public-assistant-create'));
    return screen.getByTestId('public-assistant-create-dialog');
  }

  it('shows an actionable required error linked to the name field', async () => {
    await openCreate();
    const name = screen.getByTestId('public-assistant-create-name');
    fireEvent.change(name, { target: { value: '   ' } });
    fireEvent.click(screen.getByRole('button', { name: '创建' }));
    expect(await screen.findByText('请输入助手名称')).toBeInTheDocument();
    expect(name).toHaveAttribute('aria-invalid', 'true');
    expect(name).toHaveAccessibleDescription(/请输入助手名称/);
    expect(name).toHaveFocus();
    expect(mockedCreateAssistant).not.toHaveBeenCalled();
  });

  it.each(['名'.repeat(121), '🤖'.repeat(61)])('rejects an oversized name before sending a request (%#)', async (value) => {
    const dialog = await openCreate();
    const name = screen.getByTestId('public-assistant-create-name');
    fireEvent.change(name, { target: { value } });
    expect(await within(dialog).findByText('名称不能超过 120 字，请缩短后重试')).toBeInTheDocument();
    expect(within(dialog).getByText(`${value.length} / 120`)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: '创建' }));
    await waitFor(() => expect(name).toHaveFocus());
    expect(name).toHaveValue(value);
    expect(name).toHaveAccessibleDescription(/名称不能超过 120 字/);
    expect(mockedCreateAssistant).not.toHaveBeenCalled();
    expect(screen.queryByTestId('public-assistant-list-error')).not.toBeInTheDocument();
  });

  it('accepts trimmed names and descriptions at the existing length limits', async () => {
    await openCreate();
    fireEvent.change(screen.getByTestId('public-assistant-create-name'), { target: { value: ` ${'名'.repeat(120)} ` } });
    fireEvent.change(screen.getByTestId('public-assistant-create-description'), { target: { value: ` ${'描'.repeat(500)} ` } });
    fireEvent.click(screen.getByRole('button', { name: '创建' }));
    expect(await screen.findByTestId('assistant-created-route')).toBeInTheDocument();
    expect(mockedCreateAssistant).toHaveBeenCalledWith(expect.objectContaining({ name: '名'.repeat(120), description: '描'.repeat(500) }));
  });

  it('shows the description limit error and clears it after correction', async () => {
    await openCreate();
    fireEvent.change(screen.getByTestId('public-assistant-create-name'), { target: { value: '新客服' } });
    const description = screen.getByTestId('public-assistant-create-description');
    fireEvent.change(description, { target: { value: '描'.repeat(501) } });
    fireEvent.click(screen.getByRole('button', { name: '创建' }));
    expect(await screen.findByText('描述不能超过 500 字，请缩短后重试')).toBeInTheDocument();
    expect(description).toHaveAccessibleDescription(/描述不能超过 500 字/);
    expect(mockedCreateAssistant).not.toHaveBeenCalled();
    fireEvent.change(description, { target: { value: '修改后的说明' } });
    await waitFor(() => expect(screen.queryByText('描述不能超过 500 字，请缩短后重试')).not.toBeInTheDocument());
    fireEvent.click(screen.getByRole('button', { name: '创建' }));
    expect(await screen.findByTestId('assistant-created-route')).toBeInTheDocument();
  });

  it.each([
    ['Name too long', '名称不能超过 120 字，请缩短后重试'],
    ['Name required', '请输入助手名称'],
  ])('maps backend validation to the name field: %s', async (serverError, message) => {
    mockedCreateAssistant.mockRejectedValueOnce(new Error(serverError));
    const dialog = await openCreate();
    const name = screen.getByTestId('public-assistant-create-name');
    fireEvent.change(name, { target: { value: '新客服' } });
    fireEvent.click(screen.getByRole('button', { name: '创建' }));
    expect(await within(dialog).findByText(message)).toBeInTheDocument();
    expect(name).toHaveAccessibleDescription(new RegExp(message));
    expect(name).toHaveFocus();
    expect(screen.queryByText(serverError)).not.toBeInTheDocument();
    expect(screen.queryByTestId('public-assistant-list-error')).not.toBeInTheDocument();
  });

  it('keeps a creation failure inside the dialog, preserves input and allows retry', async () => {
    mockedCreateAssistant.mockRejectedValueOnce(new Error('Network request failed'));
    const dialog = await openCreate();
    const name = screen.getByTestId('public-assistant-create-name');
    fireEvent.change(name, { target: { value: '新客服' } });
    fireEvent.click(screen.getByRole('button', { name: '创建' }));
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('创建失败，请稍后重试。已填写的内容已保留。');
    expect(name).toHaveValue('新客服');
    expect(screen.queryByTestId('public-assistant-list-error')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: '创建' }));
    expect(await screen.findByTestId('assistant-created-route')).toBeInTheDocument();
    expect(mockedCreateAssistant).toHaveBeenCalledTimes(2);
  });

  it('clears values and errors when cancelling and reopening', async () => {
    mockedCreateAssistant.mockRejectedValueOnce(new Error('Network request failed'));
    const dialog = await openCreate();
    fireEvent.change(screen.getByTestId('public-assistant-create-name'), { target: { value: '新客服' } });
    fireEvent.click(screen.getByRole('button', { name: '创建' }));
    await within(dialog).findByRole('alert');
    fireEvent.click(screen.getByRole('button', { name: '取消' }));
    fireEvent.click(screen.getByTestId('public-assistant-create'));
    expect(screen.getByTestId('public-assistant-create-name')).toHaveValue('');
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('prevents duplicate creation and dismissal while a request is pending', async () => {
    let complete!: (value: Assistant) => void;
    mockedCreateAssistant.mockImplementationOnce(() => new Promise((resolve) => { complete = resolve; }));
    const dialog = await openCreate();
    fireEvent.change(screen.getByTestId('public-assistant-create-name'), { target: { value: '新客服' } });
    fireEvent.click(screen.getByRole('button', { name: '创建' }));
    const pending = await screen.findByRole('button', { name: '创建中…' });
    expect(pending).toBeDisabled();
    expect(screen.getByRole('button', { name: '取消' })).toBeDisabled();
    fireEvent.click(pending);
    fireEvent.keyDown(dialog, { key: 'Escape', code: 'Escape' });
    expect(screen.getByTestId('public-assistant-create-dialog')).toBeInTheDocument();
    expect(mockedCreateAssistant).toHaveBeenCalledTimes(1);
    await act(async () => complete(assistant({ id: 'created-1' })));
    expect(await screen.findByTestId('assistant-created-route')).toBeInTheDocument();
  });

  it('localizes validation for English users', async () => {
    locale.value = 'enUS';
    await openCreate();
    fireEvent.change(screen.getByTestId('public-assistant-create-name'), { target: { value: 'n'.repeat(121) } });
    expect(await screen.findByText('Name must be 120 characters or fewer. Please shorten it.')).toBeInTheDocument();
    expect(mockedCreateAssistant).not.toHaveBeenCalled();
  });
});
