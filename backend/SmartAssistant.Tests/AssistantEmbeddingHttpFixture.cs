// Spec: REQ-20261009-011. Controlled third-party protocol fixture; no real supplier credentials.
#nullable enable
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SmartAssistant.Tests;

internal sealed class AssistantEmbeddingHttpFixture : HttpMessageHandler, IHttpClientFactory
{
    public int Calls { get; private set; }
    public Func<JsonElement, object>? Response { get; set; }
    public Action? BeforeResponse { get; set; }
    public JsonElement LastRequest { get; private set; }
    public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

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

    private static float[] Vector(string input, int dimension)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Enumerable.Range(0, dimension).Select(i => (digest[i % digest.Length] - 127.5f) / 127.5f).ToArray();
    }
}
