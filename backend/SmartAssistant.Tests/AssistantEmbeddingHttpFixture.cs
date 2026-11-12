// Spec: REQ-20261009-011. Controlled third-party protocol fixture; no real supplier credentials.
#nullable enable
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SmartAssistant.Tests;

/// <summary>
/// 为助手嵌入测试夹具相关测试提供隔离数据、受控依赖或固定 HTTP 响应。
/// </summary>
internal sealed class AssistantEmbeddingHttpFixture : HttpMessageHandler, IHttpClientFactory
{
    /// <summary>
    /// 表示助手嵌入测试夹具的调用信息。
    /// </summary>
    public int Calls { get; private set; }
    /// <summary>
    /// 表示助手嵌入测试夹具的响应信息。
    /// </summary>
    public Func<JsonElement, object>? Response { get; set; }
    /// <summary>
    /// 表示助手嵌入测试夹具的响应信息。
    /// </summary>
    public Action? BeforeResponse { get; set; }
    /// <summary>
    /// 表示助手嵌入测试夹具的最后请求信息。
    /// </summary>
    public JsonElement LastRequest { get; private set; }
    /// <summary>
    /// 验证创建客户端相关行为、预期结果及错误边界。
    /// </summary>
    public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

    /// <summary>
    /// 验证发送相关行为、预期结果及错误边界。
    /// </summary>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri?.Host != "fixture.test" || !request.RequestUri.AbsolutePath.EndsWith("/embeddings"))
            throw new InvalidOperationException("Unexpected external request.");
        if (request.Headers.Authorization?.Parameter != "test-key-material")
            throw new InvalidOperationException("Fixture credential was not resolved.");
        Calls++;
        using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
        LastRequest = json.RootElement.Clone();
        BeforeResponse?.Invoke();
        var dimension = LastRequest.GetProperty("dimensions").GetInt32();
        var body = Response?.Invoke(LastRequest) ?? new
        {
            data = LastRequest.GetProperty("input").EnumerateArray().Select((input, index) =>
                new { index, embedding = Vector(input.GetString()!, dimension) }).Reverse().ToArray()
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
    }

    /// <summary>
    /// 验证向量相关行为、预期结果及错误边界。
    /// </summary>
    private static float[] Vector(string input, int dimension)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Enumerable.Range(0, dimension).Select(i => (digest[i % digest.Length] - 127.5f) / 127.5f).ToArray();
    }
}
