export class ApiError extends Error {
  constructor(public status: number, message: string, public data?: unknown) {
    super(message);
    this.name = 'ApiError';
  }
}

/** Transport for the standalone application's directly accessible management API. */
export class ApiClient {
  constructor(private readonly baseUrl = '/api/admin') {}

  private async request<T>(path: string, method: string, body?: unknown): Promise<T> {
    const multipart = body instanceof FormData;
    const response = await fetch(this.baseUrl + path, {
      method,
      credentials: 'omit',
      headers: multipart ? undefined : { 'Content-Type': 'application/json' },
      body: body === undefined ? undefined : multipart ? body : JSON.stringify(body),
    });
    if (response.status === 204) return undefined as T;
    let result;
    try { result = await response.json(); }
    catch { throw new ApiError(response.status, response.ok ? 'Invalid JSON response from server' : `HTTP ${response.status}`); }
    if (!response.ok || result?.isSuccess === false) {
      throw new ApiError(result?.businessCode ?? response.status, result?.businessMessage || result?.sysMessage || result?.message || `HTTP ${response.status}`, result);
    }
    return result && typeof result.isSuccess === 'boolean' ? result.returnObj as T : result as T;
  }

  get<T>(path: string): Promise<T> { return this.request(path, 'GET'); }
  post<T>(path: string, body?: unknown): Promise<T> { return this.request(path, 'POST', body); }
  put<T>(path: string, body?: unknown): Promise<T> { return this.request(path, 'PUT', body); }
  delete<T>(path: string): Promise<T> { return this.request(path, 'DELETE'); }
  postFormData<T>(path: string, body: FormData): Promise<T> { return this.request(path, 'POST', body); }
}

export const apiClient = new ApiClient();
