import { test, expect, type Page } from '@playwright/test';
import { DEFAULT_ASSISTANT_CONFIGURATION } from '../client/features/public-assistant/services/publicAssistantService';

async function openWorkspace(page: Page) {
  await page.goto('/');
  await expect(page).toHaveURL(/\/admin\/assistants$/);
  await expect(page.getByTestId('public-assistant-list')).toBeVisible();
  await expect(page.getByTestId('public-assistant-list-loading')).toBeHidden();
  await expect(page.getByLabel('管理员密码')).toBeHidden();
  await expect(page.getByRole('button', { name: /^(登录|退出|Sign in|Sign out)$/ })).toHaveCount(0);
  expect(await page.context().cookies()).toEqual([]);
  const response = await page.request.get('/api/admin/assistants');
  expect(response.status()).toBe(200);
}

test('standalone administration uses real backend storage on desktop and mobile', async ({ page }, info) => {
  const name = `独立助手验收-${info.project.name}-${Date.now()}`;
  const knowledgeName = `验收知识-${info.project.name}-${Date.now()}`;
  const failures: string[] = [];
  const authRequests: string[] = [];
  page.on('pageerror', error => failures.push(error.message));
  page.on('request', request => { if (new URL(request.url()).pathname.startsWith('/api/auth/')) authRequests.push(request.url()); });
  await openWorkspace(page);

  await page.getByTestId('public-assistant-create').click();
  await page.getByTestId('public-assistant-create-name').fill(name);
  await page.getByTestId('public-assistant-create-description').fill('独立前后端真实链路验证');
  await page.getByTestId('public-assistant-create-dialog').getByRole('button', { name: '创建', exact: true }).click();
  await expect(page).toHaveURL(/\/admin\/assistants\/[^/]+$/);
  const assistantPath = new URL(page.url()).pathname;
  const assistantId = assistantPath.split('/').at(-1)!;
  await expect(page.getByTestId('assistant-workbench')).toBeVisible();
  await expect(page.getByTestId('assistant-settings-name')).toHaveValue(name);
  await page.getByTestId('assistant-settings-name').fill(`${name}已保存`);
  const saved = page.waitForResponse(response => response.request().method() === 'PUT' && response.url().endsWith(`/assistants/${assistantId}/draft`));
  await page.getByTestId('assistant-save-draft').click();
  expect((await saved).ok()).toBe(true);
  await page.reload();
  await expect(page.getByTestId('assistant-settings-name')).toHaveValue(`${name}已保存`);

  await page.getByRole('link', { name: '知识库', exact: true }).click();
  await expect(page.getByTestId('assistant-knowledge-page')).toBeVisible();
  await page.getByTestId('knowledge-base-name').fill(knowledgeName);
  const createdKnowledge = page.waitForResponse(response => response.request().method() === 'POST' && response.url().endsWith('/knowledge-bases'));
  await page.getByTestId('knowledge-base-create').click();
  const knowledgeResponse = await createdKnowledge;
  expect(knowledgeResponse.ok()).toBe(true);
  const knowledgeId = (await knowledgeResponse.json()).returnObj.id as string;
  await expect(page.getByTestId(`knowledge-base-${knowledgeId}`)).toContainText(knowledgeName);
  await page.getByTestId('knowledge-qna-add').click();
  await page.getByTestId('knowledge-qna-question').fill('助手在哪里运行？');
  await page.getByTestId('knowledge-qna-answer').fill('助手运行在独立项目。');
  await page.getByTestId('knowledge-qna-save').click();
  await expect(page.getByTestId('knowledge-qna-editor')).toBeHidden();
  await expect(page.getByTestId('knowledge-qna-list')).toContainText('助手在哪里运行？');

  await page.getByRole('link', { name: '模型策略', exact: true }).click();
  await expect(page.getByRole('heading', { name: '助手业务策略', exact: true })).toBeVisible();
  await page.getByRole('link', { name: '模型服务', exact: true }).click();
  await expect(page.getByTestId('assistant-provider-create')).toBeVisible();

  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1)).toBe(true);
  await page.screenshot({ path: info.outputPath('standalone-model-services.png'), fullPage: true });

  await page.context().clearCookies();
  const accessible = await page.request.get('/api/admin/assistants');
  expect(accessible.status()).toBe(200);
  await expect(page.getByRole('button', { name: /^(登录|退出|Sign in|Sign out)$/ })).toHaveCount(0);
  expect(authRequests).toEqual([]);
  expect(failures).toEqual([]);
});

test('unknown public assistant remains public and displays an unavailable state', async ({ page }) => {
  await page.goto('/assistants/nonexistent-independent-assistant');
  await expect(page.getByRole('heading')).toHaveText('这个智能客服暂时不可用。');
  await expect(page).toHaveURL(/\/assistants\/nonexistent-independent-assistant$/);
  await expect(page.getByLabel('管理员密码')).toBeHidden();
});

test('published fixed answers stream through real anonymous sessions, survive reload, and stop after revoke', async ({ page }, info) => {
  await openWorkspace(page);
  const stamp = `${info.project.name}-${Date.now()}`;
  const api = page.request;
  async function post(url: string, data: unknown) {
    const response = await api.post(url, { data });
    expect(response.ok(), `POST ${url}: ${await response.text()}`).toBe(true);
    return (await response.json()).returnObj;
  }
  // A publication binds a configured model policy. Exact fixed answers bypass the
  // provider entirely; this reserved loopback endpoint is deliberately unreachable.
  // This test verifies fixed Q&A and transport, and makes no LLM quality claim.
  const provider = await post('/api/admin/system/assistant-models/providers', {
    expectedRevision: 0,
    settings: { name: `固定问答验收模型配置-${stamp}`, baseUrl: 'https://127.0.0.1:1/v1', secretReference: 'browser-fixed-answer-test-only', secretVersion: 'v1', timeoutSeconds: 10, models: [{ id: 'fixed-answer-test', purpose: 'chat', supportsTools: false }] },
  });
  const policy = await post('/api/admin/assistant-models/policies', {
    expectedRevision: 0,
    settings: { name: `固定问答验收策略-${stamp}`, businessCode: `fixed_qa_${info.project.name}_${Date.now()}`, providerId: provider.id, providerRevision: provider.revision, model: 'fixed-answer-test', instructions: '固定问答浏览器验收。', temperature: 0, maxOutputTokens: 128, knowledgeEnabled: false, retrievalLimit: 1, requireEvidence: false, noAnswerMessage: '无固定回答', webSearchEnabled: false, humanHandoffEnabled: false },
  });
  const knowledge = await post('/api/admin/knowledge-bases', { name: `公开问答验收知识-${stamp}` });
  const question = '这个助手已经独立出来了吗？';
  const answer = '已经独立，前后端与会话数据都属于 AI.Assistant 项目。';
  const qnaResponse = await api.put(`/api/admin/knowledge-bases/${knowledge.id}/qna`, { data: { question, answer, enabled: true, sortOrder: 0, showAsSuggestion: true } });
  expect(qnaResponse.ok()).toBe(true);
  const assistant = await post('/api/admin/assistants', {
    name: `固定问答验收助手-${stamp}`,
    configuration: { ...DEFAULT_ASSISTANT_CONFIGURATION, welcomeMessage: '欢迎使用独立 AI.Assistant', suggestedQuestions: [question], historyEnabled: true, businessPolicyId: policy.id, knowledgeBaseId: knowledge.id, capabilities: { knowledgeQa: false, webSearch: false, customQa: true } },
  });
  const publication = await post(`/api/admin/assistants/${assistant.id}/publish`, { expectedDraftRevision: assistant.draftRevision, expectedPointerRevision: 0 });
  const stateResponse = await api.get(`/api/admin/assistants/${assistant.id}/publication`);
  const state = (await stateResponse.json()).returnObj;
  const assistantKey = state.assistantKey;
  await page.goto(`/assistants/${encodeURIComponent(assistantKey)}`);
  await expect(page.getByTestId('assistant-public-chat')).toBeVisible();
  await page.getByTestId('assistant-public-input').fill(question);
  await page.getByTestId('assistant-public-send').click();
  await expect(page.getByTestId('assistant-public-answer').last()).toContainText(answer);
  await expect(page.getByTestId('assistant-public-send')).toBeVisible();
  await page.reload();
  await expect(page.getByTestId('assistant-public-answer').last()).toContainText(answer);
  await expect(page.getByTestId('assistant-public-question').last()).toContainText(question);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1)).toBe(true);
  await page.screenshot({ path: info.outputPath('standalone-public-fixed-answer.png'), fullPage: true });

  await post(`/api/admin/assistants/${assistant.id}/revoke`, { expectedPointerRevision: state.pointerRevision ?? publication.pointerRevision });
  await page.reload();
  await expect(page.getByRole('heading')).toHaveText('这个智能客服暂时不可用。');
  await expect(page.getByTestId('assistant-public-input')).toBeHidden();
});
