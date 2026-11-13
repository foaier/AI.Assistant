#nullable enable
// Spec: REQ-20261009-011; results/embedding-batch-limit-20261110.md.
// The HTTP fixture represents the external supplier, including its 20-input limit.
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AI.Assistant.Services.Repositorys.AI;
using AI.Assistant.Services.Repositorys;
using AI.Assistant.Services.Contracts;
using Xunit;
using AI.Assistant.Models.AI;
using AI.Assistant.Models.Knowledge;

namespace AI.Assistant.Tests;

/// <summary>
/// 验证打开兼容嵌入模型供应商行为和边界条件的自动化测试集合。
/// </summary>
public sealed class OpenAiCompatibleEmbeddingProviderTests
{
    /// <summary>
    /// 定义打开兼容嵌入模型供应商测试使用的配置配置值。
    /// </summary>
    private static readonly AssistantKnowledgeEmbeddingExecutionProfile Profile = new("provider", 3, "embedding", 3, 2, 1);

    /// <summary>
    /// 验证嵌入输入按供应商上限分批，返回向量保持文档原顺序。
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    [InlineData(21)]
    [InlineData(24)]
    [InlineData(40)]
    [InlineData(41)]
    public async Task Inputs_respect_supplier_limit_and_vectors_keep_document_order(int count)
    {
        using var fixture = new SupplierFixture();
        var resolver = new CredentialResolver();
        var provider = new OpenAiCompatibleEmbeddingProvider(fixture, resolver);
        var inputs = Enumerable.Range(0, count).Select(i => $"document chunk {i}").ToArray();

        var result = await provider.EmbedAsync(inputs, Profile, CancellationToken.None);

        Assert.Equal(Profile.ProviderProfileRef, result.ProfileId);
        Assert.Equal(3, result.Dimension);
        Assert.Equal(count, result.Vectors.Count);
        Assert.Equal(inputs, fixture.Batches.SelectMany(batch => batch));
        Assert.Equal((count + 19) / 20, fixture.Batches.Count);
        Assert.All(fixture.Batches, batch => Assert.InRange(batch.Length, 1, 20));
        for (var i = 0; i < count; i++) Assert.Equal(VectorFor(inputs[i]), result.Vectors[i]);
        Assert.Equal(new AssistantProviderAccess("provider@3", "embedding", "embedding", 3), Assert.Single(resolver.Accesses));
    }

    /// <summary>
    /// 验证包含二十四个章节的 Markdown 能完成解析、分块和嵌入。
    /// </summary>
    [Fact]
    public async Task Markdown_with_24_sections_is_parsed_chunked_and_embedded()
    {
        var markdown = string.Join("\n", Enumerable.Range(1, 24).Select(i => $"## Section {i}\n{new string('文', 250)}"));
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(markdown));
        var parsed = await new AssistantDocumentParser().ParseAsync("text/markdown", "guide.md", stream, CancellationToken.None);
        var chunks = AssistantKnowledgeChunker.Split(parsed.Text, AssistantKnowledgeChunkingPolicy.Default);
        Assert.Equal(24, chunks.Count);
        using var fixture = new SupplierFixture();
        var provider = new OpenAiCompatibleEmbeddingProvider(fixture, new CredentialResolver());

        var result = await provider.EmbedAsync(chunks, Profile, CancellationToken.None);

        Assert.Equal(new[] { 20, 4 }, fixture.Batches.Select(batch => batch.Length));
        Assert.Equal(chunks.Count, result.Vectors.Count);
        for (var i = 0; i < chunks.Count; i++) Assert.Equal(VectorFor(chunks[i]), result.Vectors[i]);
    }

    /// <summary>
    /// 验证后续批次失败会中止处理，不返回部分结果或继续发送请求。
    /// </summary>
    [Theory]
    [InlineData("http", "assistant_embedding_provider_failed")]
    [InlineData("timeout", "assistant_embedding_provider_failed")]
    [InlineData("dimension", "assistant_embedding_dimension_mismatch")]
    [InlineData("missing", "assistant_embedding_dimension_mismatch")]
    [InlineData("duplicate", "assistant_embedding_dimension_mismatch")]
    [InlineData("shape", "assistant_embedding_response_invalid")]
    public async Task Later_batch_failure_aborts_without_partial_result_or_further_requests(string failure, string code)
    {
        using var fixture = new SupplierFixture(failure);
        var provider = new OpenAiCompatibleEmbeddingProvider(fixture, new CredentialResolver());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.EmbedAsync(
            Enumerable.Range(0, 41).Select(i => $"chunk {i}").ToArray(), Profile, CancellationToken.None));

        Assert.Equal(code, error.Message);
        Assert.Equal(2, fixture.Batches.Count);
        Assert.DoesNotContain("supplier-private-detail", error.ToString());
    }

    /// <summary>
    /// 验证调用方取消后不再发送剩余批次，也不将取消误报为供应商失败。
    /// </summary>
    [Fact]
    public async Task Caller_cancellation_does_not_send_remaining_batches_or_become_provider_failure()
    {
        using var cancellation = new CancellationTokenSource();
        using var fixture = new SupplierFixture(afterResponse: cancellation.Cancel);
        var provider = new OpenAiCompatibleEmbeddingProvider(fixture, new CredentialResolver());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.EmbedAsync(
            Enumerable.Repeat("chunk", 41).ToArray(), Profile, cancellation.Token));

        Assert.Single(fixture.Batches);
    }

    /// <summary>
    /// 验证空输入在解析凭据及发送请求之前被拒绝。
    /// </summary>
    [Fact]
    public async Task Empty_input_is_rejected_before_resolving_credentials_or_sending_requests()
    {
        using var fixture = new SupplierFixture();
        var resolver = new CredentialResolver();
        var provider = new OpenAiCompatibleEmbeddingProvider(fixture, resolver);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.EmbedAsync(
            Array.Empty<string>(), Profile, CancellationToken.None));
        Assert.Equal("assistant_embedding_request_invalid", error.Message);
        Assert.Empty(fixture.Batches);
        Assert.Empty(resolver.Accesses);
    }

    /// <summary>
    /// 验证向量相关行为、预期结果及错误边界。
    /// </summary>
    private static float[] VectorFor(string input)
        => SHA256.HashData(Encoding.UTF8.GetBytes(input)).Take(3).Select(value => (float)value + 1).ToArray();

    /// <summary>
    /// 测试用解析器，为凭据或配置提供确定的固定结果。
    /// </summary>
    private sealed class CredentialResolver : IAssistantProviderCredentialResolver
    {
        /// <summary>
        /// 记录测试中凭据解析器的访问次数。
        /// </summary>
        public List<AssistantProviderAccess> Accesses { get; } = new();
        /// <summary>
        /// 验证解析相关行为、预期结果及错误边界。
        /// </summary>
        public ValueTask<OpenAiCompatibleProviderCredential> ResolveAsync(AssistantProviderAccess access, CancellationToken cancellationToken = default)
        {
            Accesses.Add(access);
            return ValueTask.FromResult(new OpenAiCompatibleProviderCredential(new Uri("https://provider.example/compatible-mode/v1"), "test-secret", "test-org"));
        }
    }

    /// <summary>
    /// 为打开兼容嵌入模型供应商测试相关测试提供隔离数据、受控依赖或固定 HTTP 响应。
    /// </summary>
    private sealed class SupplierFixture(string? failure = null, Action? afterResponse = null) : HttpMessageHandler, IHttpClientFactory
    {
        /// <summary>
        /// 表示供应商测试夹具的批次信息。
        /// </summary>
        public List<string[]> Batches { get; } = new();
        /// <summary>
        /// 验证创建客户端相关行为、预期结果及错误边界。
        /// </summary>
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

        /// <summary>
        /// 验证发送相关行为、预期结果及错误边界。
        /// </summary>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://provider.example/compatible-mode/v1/embeddings", request.RequestUri!.ToString());
            Assert.Equal("Bearer test-secret", request.Headers.Authorization!.ToString());
            Assert.Equal("test-org", Assert.Single(request.Headers.GetValues("OpenAI-Organization")));
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Assert.Equal("embedding", body.RootElement.GetProperty("model").GetString());
            Assert.Equal(3, body.RootElement.GetProperty("dimensions").GetInt32());
            var inputs = body.RootElement.GetProperty("input").EnumerateArray().Select(x => x.GetString()!).ToArray();
            Batches.Add(inputs);
            if (inputs.Length > 20 || (Batches.Count == 2 && failure == "http"))
                return new(HttpStatusCode.BadRequest) { Content = new StringContent("supplier-private-detail") };
            if (Batches.Count == 2 && failure == "timeout") throw new OperationCanceledException();
            if (Batches.Count == 2 && failure == "shape")
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { error = "supplier-private-detail" }) };
            var data = inputs.Select((input, index) => new
            {
                index = Batches.Count == 2 && failure == "duplicate" ? 0 : index,
                embedding = Batches.Count == 2 && failure == "dimension" ? new float[] { 1 } : VectorFor(input)
            }).Reverse().ToArray();
            if (Batches.Count == 2 && failure == "missing") data = data.Skip(1).ToArray();
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { data }) };
            afterResponse?.Invoke();
            return response;
        }
    }
}
