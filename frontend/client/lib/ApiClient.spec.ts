import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiClient, ApiError } from './ApiClient';

describe('standalone API transport', () => {
  afterEach(() => vi.unstubAllGlobals());
  it('omits credentials and unwraps the backend business envelope', async () => {
    const request = vi.fn().mockResolvedValue(new Response(JSON.stringify({ isSuccess: true, returnObj: [{ id: 'assistant-1' }] })));
    vi.stubGlobal('fetch', request);
    expect(await new ApiClient().get('/assistants')).toEqual([{ id: 'assistant-1' }]);
    expect(request).toHaveBeenCalledWith('/api/admin/assistants', expect.objectContaining({ credentials: 'omit' }));
  });
  it('preserves revision conflicts as API errors', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ isSuccess: false, businessCode: 409, businessMessage: 'assistant_draft_revision_conflict' }), { status: 409 })));
    await expect(new ApiClient().put('/assistants/a/draft', { expectedDraftRevision: 1 })).rejects.toMatchObject({ status: 409, message: 'assistant_draft_revision_conflict' });
  });
  it('reports unauthorized errors in place without navigating to a login page', async () => {
    const originalUrl = window.location.href;
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ message: 'session_unavailable' }), { status: 401 })));
    await expect(new ApiClient().get('/assistants')).rejects.toMatchObject({ status: 401, message: 'session_unavailable' });
    expect(window.location.href).toBe(originalUrl);
  });
  it('does not turn a failed business envelope into successful data', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ isSuccess: false, businessCode: 400, businessMessage: 'assistant_provider_unavailable', returnObj: null }))));
    await expect(new ApiClient().post('/assistants', {})).rejects.toBeInstanceOf(ApiError);
  });
  it('lets the browser generate multipart boundaries', async () => {
    const request = vi.fn().mockResolvedValue(new Response(JSON.stringify({ isSuccess: true, returnObj: { assetId: 'file-1' } })));
    vi.stubGlobal('fetch', request);
    const form = new FormData();
    form.append('file', new File(['hello'], 'hello.txt'));
    await new ApiClient('/api').postFormData('/assets/upload', form);
    expect(request).toHaveBeenCalledWith('/api/assets/upload', expect.objectContaining({ body: form }));
    expect(new Headers(request.mock.calls[0][1].headers).has('Content-Type')).toBe(false);
  });
});

