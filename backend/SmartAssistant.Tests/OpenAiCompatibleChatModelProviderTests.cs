// Spec: specs/136-public-assistant/requests/REQ-20260925-004-public-assistant-s2-chat-runtime-admission.md,
// S2-C provider streaming, tool-call assembly and normalized error contract.
#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SmartAssistant.Modules.Infrastructure.AI;
using SmartAssistant.Modules.Contracts;
using Xunit;

namespace SmartAssistant.Tests;

/// <summary>
/// 验证打开兼容聊天模型模型供应商行为和边界条件的自动化测试集合。
/// </summary>
public sealed class OpenAiCompatibleChatModelProviderTests
{
    /// <summary>
    /// 验证保留配置的 API 根路径，不重复追加版本路径。
    /// </summary>
    [Theory]
    [InlineData("https://provider.example", "https://provider.example/v1/chat/completions")]
    [InlineData("https://provider.example/v1/", "https://provider.example/v1/chat/completions")]
    [InlineData("https://dashscope.aliyuncs.com/compatible-mode/v1", "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions")]
    public async Task Configured_api_root_is_preserved_without_duplicate_version(string root, string expected)
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "data: [DONE]\n\n");
        var provider = new OpenAiCompatibleChatModelProvider(new HttpClient(handler),
            new FakeCredentialResolver(root), new OpenAiCompatibleProviderOptions(TimeSpan.FromSeconds(5)));
        await ReadAllAsync(provider.StreamAsync(new AssistantProviderRequest("profile", "model", Array.Empty<AssistantProviderMessage>())));
        Assert.Equal(expected, handler.LastRequestUri?.ToString());
    }

    /// <summary>
    /// 验证流响应会统一解析文本、工具调用、令牌用量和结束状态。
    /// </summary>
    [Fact]
    public async Task Streaming_response_normalizes_text_tool_call_usage_and_completion()
    {
        var handler = new RecordingHandler(
            HttpStatusCode.OK,
            """
            data: {"id":"req-1","choices":[{"delta":{"content":"hello"},"finish_reason":null}]}

            data: {"id":"req-1","choices":[{"delta":{"tool_calls":[{"index":0,"id":"call-1","function":{"name":"public.event.overview.read","arguments":"{\"event"}}]},"finish_reason":null}]}

            data: {"id":"req-1","choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"Id\":\"event-1\"}"}}]},"finish_reason":null}]}

            data: {"id":"req-1","choices":[],"usage":{"prompt_tokens":11,"completion_tokens":7,"total_tokens":18}}

            data: {"id":"req-1","choices":[{"delta":{},"finish_reason":"stop"}]}

            data: [DONE]

            """);
        var provider = CreateProvider(handler);

        var chunks = await ReadAllAsync(provider.StreamAsync(
            new AssistantProviderRequest(
                "profile-1",
                "model-1",
                new[]
                {
                    new AssistantProviderMessage(AssistantMessageRole.User, "What is next?")
                },
                Tools: new[]
                {
                    new AssistantToolDefinition(
                        "event-tool",
                        "v1",
                        "public.event.overview.read",
                        "Read public event overview.",
                        JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone())
                })));

        Assert.Contains(chunks, chunk =>
            chunk.Kind == AssistantProviderChunkKind.TextDelta
            && chunk.TextDelta == "hello");
        Assert.Equal(
            2,
            chunks.FindAll(chunk => chunk.Kind == AssistantProviderChunkKind.ToolCallDelta).Count);
        Assert.Contains(chunks, chunk =>
            chunk.ToolCall?.ToolCallId == "call-1"
            && chunk.ToolCall?.Name == "public.event.overview.read");
        Assert.Contains(chunks, chunk =>
            chunk.ToolCall?.ArgumentsDelta == "Id\":\"event-1\"}");
        Assert.Contains(chunks, chunk =>
            chunk.Kind == AssistantProviderChunkKind.Usage
            && chunk.Usage?.TotalTokens == 18);
        Assert.Single(chunks.FindAll(chunk => chunk.Kind == AssistantProviderChunkKind.Completed));
        Assert.Equal("https://provider.example/v1/chat/completions", handler.LastRequestUri?.ToString());
        Assert.Equal("Bearer provider-secret", handler.LastAuthorization);

        using var requestDocument = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.True(requestDocument.RootElement.GetProperty("stream").GetBoolean());
        Assert.True(requestDocument.RootElement.GetProperty("stream_options").GetProperty("include_usage").GetBoolean());
        Assert.Equal(
            "public.event.overview.read",
            requestDocument.RootElement
                .GetProperty("tools")[0]
                .GetProperty("function")
                .GetProperty("name")
                .GetString());
    }

    /// <summary>
    /// 验证 HTTP 错误被规范化，且不返回供应商密钥。
    /// </summary>
    [Fact]
    public async Task Http_error_is_normalized_without_returning_provider_secret()
    {
        var handler = new RecordingHandler(
            HttpStatusCode.TooManyRequests,
            """{"error":{"code":"rate_limit","message":"slow down"}}""");
        var provider = CreateProvider(handler);

        var chunks = await ReadAllAsync(provider.StreamAsync(
            new AssistantProviderRequest(
                "profile-1",
                "model-1",
                new[] { new AssistantProviderMessage(AssistantMessageRole.User, "hello") })));

        var error = Assert.Single(chunks);
        Assert.Equal(AssistantProviderChunkKind.Error, error.Kind);
        Assert.Equal("provider_http_429", error.Error?.Code);
        Assert.True(error.Error?.Retryable);
        Assert.DoesNotContain("provider-secret", error.Error?.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证无效 SSE 负载会产生统一错误并停止流读取。
    /// </summary>
    [Fact]
    public async Task Malformed_sse_payload_is_normalized_and_stream_stops()
    {
        var handler = new RecordingHandler(
            HttpStatusCode.OK,
            """
            data: {not-json}

            data: {"choices":[{"delta":{"content":"must-not-leak"}}]}

            """);
        var provider = CreateProvider(handler);

        var chunks = await ReadAllAsync(provider.StreamAsync(
            new AssistantProviderRequest(
                "profile-1",
                "model-1",
                new[] { new AssistantProviderMessage(AssistantMessageRole.User, "hello") })));

        var error = Assert.Single(chunks);
        Assert.Equal(AssistantProviderChunkKind.Error, error.Kind);
        Assert.Equal("provider_malformed_stream", error.Error?.Code);
    }

    /// <summary>
    /// 验证收到结束原因后，仅等待可选用量窗口便关闭未结束的响应流。
    /// </summary>
    [Fact]
    public async Task Finish_reason_ends_an_open_stream_after_the_optional_usage_window()
    {
        var handler = new OpenEndedStreamHandler(
            "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}\r\n\r\n");
        var provider = CreateProvider(handler);
        using var cancellation = new CancellationTokenSource();
        var readTask = ReadAllAsync(provider.StreamAsync(
            new AssistantProviderRequest("profile-1", "model-1", Array.Empty<AssistantProviderMessage>()),
            cancellation.Token));

        List<AssistantProviderChunk> chunks;
        try
        {
            chunks = await readTask.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            cancellation.Cancel();
            if (!readTask.IsCompleted)
                await readTask;
        }

        Assert.Single(chunks.FindAll(chunk => chunk.Kind == AssistantProviderChunkKind.Completed));
        Assert.DoesNotContain(chunks, chunk => chunk.Kind == AssistantProviderChunkKind.Error);
    }

    /// <summary>
    /// 验证非 HTTPS 供应商地址在发送网络请求之前被拒绝。
    /// </summary>
    [Fact]
    public async Task Non_https_provider_url_is_rejected_before_network_call()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "data: [DONE]\n\n");
        var provider = new OpenAiCompatibleChatModelProvider(
                new HttpClient(handler),
                new FakeCredentialResolver("http://provider.example"),
                new OpenAiCompatibleProviderOptions(TimeSpan.FromSeconds(5)));
        var exception = await Assert.ThrowsAsync<SmartAssistant.Platform.BizResultException>(() =>
            ReadAllAsync(provider.StreamAsync(new AssistantProviderRequest("profile", "model", Array.Empty<AssistantProviderMessage>()))));

        Assert.Equal("assistant_provider_base_url_must_be_https", exception.BusinessMessage);
        Assert.False(handler.WasCalled);
    }

    /// <summary>
    /// 验证创建模型供应商相关行为、预期结果及错误边界。
    /// </summary>
    private static OpenAiCompatibleChatModelProvider CreateProvider(HttpMessageHandler handler)
        => new(
            new HttpClient(handler),
            new FakeCredentialResolver(),
            new OpenAiCompatibleProviderOptions(
                TimeSpan.FromSeconds(5)));

    /// <summary>
    /// 验证读取相关行为、预期结果及错误边界。
    /// </summary>
    private static async Task<List<AssistantProviderChunk>> ReadAllAsync(
        IAsyncEnumerable<AssistantProviderChunk> chunks)
    {
        var result = new List<AssistantProviderChunk>();
        await foreach (var chunk in chunks)
            result.Add(chunk);
        return result;
    }

    /// <summary>
    /// 测试用解析器，为凭据或配置提供确定的固定结果。
    /// </summary>
    private sealed class FakeCredentialResolver : IAssistantProviderCredentialResolver
    {
        /// <summary>
        /// 保存测试凭据解析器返回的供应商地址。
        /// </summary>
        private readonly string _url;
        /// <summary>
        /// 初始化凭据解析器测试依赖或隔离测试环境。
        /// </summary>
        public FakeCredentialResolver(string url = "https://provider.example") => _url = url;
        /// <summary>
        /// 验证解析相关行为、预期结果及错误边界。
        /// </summary>
        public ValueTask<OpenAiCompatibleProviderCredential> ResolveAsync(
            AssistantProviderAccess access,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new OpenAiCompatibleProviderCredential(
                new Uri(_url),
                "provider-secret"));
    }

    /// <summary>
    /// 测试用请求处理器，用于验证 HTTP 请求和响应边界。
    /// </summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        /// <summary>
        /// 保存状态代码依赖，供处理器执行相关操作。
        /// </summary>
        private readonly HttpStatusCode _statusCode;
        /// <summary>
        /// 保存处理器在处理正文时所需的数据或状态。
        /// </summary>
        private readonly string _body;

        /// <summary>
        /// 初始化处理器测试依赖或隔离测试环境。
        /// </summary>
        public RecordingHandler(HttpStatusCode statusCode, string body)
        {
            _statusCode = statusCode;
            _body = body;
        }

        /// <summary>
        /// 标记测试中的处理器是否收到请求。
        /// </summary>
        public bool WasCalled { get; private set; }
        /// <summary>
        /// 表示处理器的最后请求URI信息。
        /// </summary>
        public Uri? LastRequestUri { get; private set; }
        /// <summary>
        /// 表示处理器的最后授权信息。
        /// </summary>
        public string? LastAuthorization { get; private set; }
        /// <summary>
        /// 表示处理器的最后请求正文信息。
        /// </summary>
        public string? LastRequestBody { get; private set; }

        /// <summary>
        /// 验证发送相关行为、预期结果及错误边界。
        /// </summary>
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            LastRequestUri = request.RequestUri;
            LastAuthorization = request.Headers.Authorization?.ToString();
            LastRequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(
                    _body,
                    Encoding.UTF8,
                    _statusCode == HttpStatusCode.OK ? "text/event-stream" : "application/json")
            };
        }
    }

    /// <summary>
    /// 测试用请求处理器，用于验证 HTTP 请求和响应边界。
    /// </summary>
    private sealed class OpenEndedStreamHandler : HttpMessageHandler
    {
        /// <summary>
        /// 保存打开流式输出处理器在处理前缀时所需的数据或状态。
        /// </summary>
        private readonly string _prefix;

        /// <summary>
        /// 初始化打开流式输出处理器测试依赖或隔离测试环境。
        /// </summary>
        public OpenEndedStreamHandler(string prefix) => _prefix = prefix;

        /// <summary>
        /// 验证发送相关行为、预期结果及错误边界。
        /// </summary>
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new OpenEndedSseContent(_prefix)
            });
    }

    /// <summary>
    /// 测试用流内容，实现可控的读取、阻塞和取消行为。
    /// </summary>
    private sealed class OpenEndedSseContent : HttpContent
    {
        /// <summary>
        /// 保存打开SSE内容在处理前缀时所需的数据或状态。
        /// </summary>
        private readonly byte[] _prefix;

        /// <summary>
        /// 初始化打开SSE内容测试依赖或隔离测试环境。
        /// </summary>
        public OpenEndedSseContent(string prefix)
        {
            _prefix = Encoding.UTF8.GetBytes(prefix);
            Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
        }

        /// <summary>
        /// 验证序列化流式输出相关行为、预期结果及错误边界。
        /// </summary>
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
            => stream.WriteAsync(_prefix, 0, _prefix.Length);

        /// <summary>
        /// 验证尝试计算长度相关行为、预期结果及错误边界。
        /// </summary>
        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        /// <summary>
        /// 验证创建内容读取流式输出相关行为、预期结果及错误边界。
        /// </summary>
        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
            => Task.FromResult<Stream>(new PrefixThenOpenStream(_prefix));
    }

    /// <summary>
    /// 仅供打开兼容聊天模型模型供应商测试使用的测试辅助类型。
    /// </summary>
    private sealed class PrefixThenOpenStream : Stream
    {
        /// <summary>
        /// 保存前缀则打开流式输出在处理前缀时所需的数据或状态。
        /// </summary>
        private readonly byte[] _prefix;
        /// <summary>
        /// 保存前缀则打开流式输出在处理位置时所需的数据或状态。
        /// </summary>
        private int _position;

        /// <summary>
        /// 初始化前缀则打开流式输出测试依赖或隔离测试环境。
        /// </summary>
        public PrefixThenOpenStream(byte[] prefix) => _prefix = prefix;

        /// <summary>
        /// 表示前缀则打开流式输出的是否可读取信息。
        /// </summary>
        public override bool CanRead => true;
        /// <summary>
        /// 表示前缀则打开流式输出的是否可信息。
        /// </summary>
        public override bool CanSeek => false;
        /// <summary>
        /// 表示前缀则打开流式输出的是否可写入信息。
        /// </summary>
        public override bool CanWrite => false;
        /// <summary>
        /// 表示前缀则打开流式输出的长度信息。
        /// </summary>
        public override long Length => throw new NotSupportedException();
        /// <summary>
        /// 记录当前内容流的读取位置。
        /// </summary>
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        /// <summary>
        /// 将当前正文缓冲区整理为文档分段，并清空缓冲区以继续读取后续内容。
        /// </summary>
        public override void Flush() { }
        /// <summary>
        /// 验证前缀则打开流式输出相关行为、预期结果及错误边界。
        /// </summary>
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        /// <summary>
        /// 验证长度相关行为、预期结果及错误边界。
        /// </summary>
        public override void SetLength(long value) => throw new NotSupportedException();
        /// <summary>
        /// 验证写入相关行为、预期结果及错误边界。
        /// </summary>
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        /// <summary>
        /// 读取 JSON 配置并转换为指定设置类型；格式无效时报告业务校验错误。
        /// </summary>
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        /// <summary>
        /// 验证读取相关行为、预期结果及错误边界。
        /// </summary>
        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_position < _prefix.Length)
            {
                var count = Math.Min(buffer.Length, _prefix.Length - _position);
                _prefix.AsMemory(_position, count).CopyTo(buffer);
                _position += count;
                return count;
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        /// <summary>
        /// 验证读取相关行为、预期结果及错误边界。
        /// </summary>
        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }
}
