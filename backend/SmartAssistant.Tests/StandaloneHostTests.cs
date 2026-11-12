using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace SmartAssistant.Tests;

// Exercise the real host, controllers, SQLite migrations and repositories. All
// files belong to a unique temporary test directory; the donor tree is unused.
/// <summary>
/// 验证独立项目行为和边界条件的自动化测试集合。
/// </summary>
public sealed class StandaloneHostTests
{
    /// <summary>
    /// 验证管理操作和文件上传无需登录或身份 Cookie。
    /// </summary>
    [Fact]
    public async Task Management_and_uploads_work_without_login_or_authentication_cookies()
    {
        var directory = TestHost.NewDirectory();
        using var host = new TestHost(directory);
        using var client = host.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = false });
        using var listed = await client.GetAsync("/api/admin/assistants");
        Assert.Empty((await Data(listed)).EnumerateArray());
        Assert.False(listed.Headers.Contains("Set-Cookie"));

        var assistant = await Data(await client.PostAsJsonAsync("/api/admin/assistants", new { name = "无需登录助手", configuration = new { } }));
        Assert.False(assistant.TryGetProperty("updatedBy", out _));
        Assert.False(assistant.TryGetProperty("updatedAt", out _));
        var id = assistant.GetProperty("id").GetString();
        Assert.Equal(id, (await Data(await client.GetAsync($"/api/admin/assistants/{id}"))).GetProperty("id").GetString());

        using var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent("assistant-image"), "category");
        var content = new ByteArrayContent(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jGxkAAAAASUVORK5CYII="));
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        multipart.Add(content, "file", "icon.png");
        using var uploaded = await client.PostAsync("/api/assets/upload", multipart);
        var asset = await Data(uploaded);
        Assert.Equal("image/png", asset.GetProperty("contentType").GetString());
        Assert.False(uploaded.Headers.Contains("Set-Cookie"));
        await Data(await client.DeleteAsync($"/api/admin/assistants/{id}"));
        Assert.Empty((await Data(await client.GetAsync("/api/admin/assistants"))).EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/auth/login", new { password = "unused" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.False(File.Exists(Path.Combine(directory, "admin-password.txt")));
    }

    /// <summary>
    /// 验证草稿的幂等、修订和物理删除，并确认重启后数据仍存在。
    /// </summary>
    [Fact]
    public async Task Drafts_preserve_idempotency_revisions_and_restart_persistence_with_physical_deletion()
    {
        var directory = TestHost.NewDirectory();
        string id;
        using (var host = new TestHost(directory))
        using (var client = host.CreateClient())
        {
            client.DefaultRequestHeaders.Add("Idempotency-Key", "standalone-create-once");
            var payload = new { name = "独立助手", description = "持久化证明", configuration = new { welcomeMessage = "欢迎" } };
            var created = await Data(await client.PostAsJsonAsync("/api/admin/assistants", payload));
            id = created.GetProperty("id").GetString()!;
            var replay = await Data(await client.PostAsJsonAsync("/api/admin/assistants", payload));
            Assert.Equal(id, replay.GetProperty("id").GetString());
            Assert.Single((await Data(await client.GetAsync("/api/admin/assistants"))).EnumerateArray());
            client.DefaultRequestHeaders.Remove("Idempotency-Key");

            var update = new { name = "已保存助手", description = "持久化证明", expectedDraftRevision = 1, configuration = new { welcomeMessage = "保存后欢迎" } };
            var saved = await Data(await client.PutAsJsonAsync($"/api/admin/assistants/{id}/draft", update));
            Assert.Equal(2, saved.GetProperty("draftRevision").GetInt64());
            var stale = await client.PutAsJsonAsync($"/api/admin/assistants/{id}/draft", update);
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            var readback = await Data(await client.GetAsync($"/api/admin/assistants/{id}"));
            Assert.Equal("已保存助手", readback.GetProperty("name").GetString());
            Assert.Equal("保存后欢迎", readback.GetProperty("configuration").GetProperty("welcomeMessage").GetString());
        }

        using (var restarted = new TestHost(directory))
        using (var client = restarted.CreateClient())
        {
            var readback = await Data(await client.GetAsync($"/api/admin/assistants/{id}"));
            Assert.Equal(2, readback.GetProperty("draftRevision").GetInt64());
            await Data(await client.DeleteAsync($"/api/admin/assistants/{id}"));
            Assert.Empty((await Data(await client.GetAsync("/api/admin/assistants"))).EnumerateArray());
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/admin/assistants/{id}")).StatusCode);
        }
    }

    /// <summary>
    /// 验证发布冻结公开配置，撤回后公开访问失效。
    /// </summary>
    [Fact]
    public async Task Publishing_freezes_the_public_configuration_and_revoking_removes_access()
    {
        using var host = new TestHost();
        using var client = host.CreateClient();
        var provider = await Data(await client.PostAsJsonAsync("/api/admin/system/assistant-models/providers", new
        {
            expectedRevision = 0,
            settings = new { name = "离线协议模型", baseUrl = "https://models.example.test/v1", secretReference = "test-token-material", secretVersion = "v1", models = new[] { new { id = "chat-test", purpose = "chat", supportsTools = false } } }
        }));
        var policy = await Data(await client.PostAsJsonAsync("/api/admin/assistant-models/policies", new
        {
            expectedRevision = 0,
            settings = new { name = "独立客服", businessCode = "standalone_service", providerId = provider.GetProperty("id").GetString(), providerRevision = provider.GetProperty("revision").GetInt64(), model = "chat-test", instructions = "使用中文回答。", knowledgeEnabled = false }
        }));
        var configuration = new { businessPolicyId = policy.GetProperty("id").GetString(), welcomeMessage = "发布时欢迎", anonymousChatEnabled = true };
        var assistant = await Data(await client.PostAsJsonAsync("/api/admin/assistants", new { name = "公开助手", configuration }));
        var id = assistant.GetProperty("id").GetString();
        var preview = await Data(await client.PostAsJsonAsync($"/api/admin/assistants/{id}/previews", new { expectedDraftRevision = 1 }));
        var previewId = preview.GetProperty("id").GetString();
        Assert.True(preview.GetProperty("expiresAt").GetDateTime() > DateTime.UtcNow);
        var previewMessages = await Data(await client.GetAsync($"/api/admin/assistants/{id}/previews/{previewId}/messages"));
        Assert.Empty(previewMessages.GetProperty("items").EnumerateArray());
        var otherAssistant = await Data(await client.PostAsJsonAsync("/api/admin/assistants", new { name = "其他助手", configuration }));
        var otherId = otherAssistant.GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/admin/assistants/{otherId}/previews/{previewId}/messages")).StatusCode);
        var publication = await Data(await client.PostAsJsonAsync($"/api/admin/assistants/{id}/publish", new { expectedDraftRevision = 1, expectedPointerRevision = 0 }));
        Assert.Equal(1, publication.GetProperty("pointerRevision").GetInt64());

        using var anonymous = host.CreateClient();
        var bootstrap = await Data(await anonymous.GetAsync($"/api/public/assistants/{id}/bootstrap"));
        Assert.Equal("公开助手", bootstrap.GetProperty("displayName").GetString());
        Assert.Equal("发布时欢迎", bootstrap.GetProperty("welcomeMessage").GetString());
        await Data(await client.PutAsJsonAsync($"/api/admin/assistants/{id}/draft", new { name = "新草稿名称", expectedDraftRevision = 1, configuration = new { businessPolicyId = policy.GetProperty("id").GetString(), welcomeMessage = "未发布欢迎" } }));
        bootstrap = await Data(await anonymous.GetAsync($"/api/public/assistants/{id}/bootstrap"));
        Assert.Equal("发布时欢迎", bootstrap.GetProperty("welcomeMessage").GetString());

        var revoked = await Data(await client.PostAsJsonAsync($"/api/admin/assistants/{id}/revoke", new { expectedPointerRevision = 1 }));
        Assert.Equal(2, revoked.GetProperty("pointerRevision").GetInt64());
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/public/assistants/{id}/bootstrap")).StatusCode);
    }

    /// <summary>
    /// 验证知识问答和文档上传使用独立项目的存储。
    /// </summary>
    [Fact]
    public async Task Knowledge_questions_and_document_uploads_use_the_independent_storage()
    {
        using var host = new TestHost();
        using var client = host.CreateClient();
        var knowledge = await Data(await client.PostAsJsonAsync("/api/admin/knowledge-bases", new { name = "独立知识库", description = "本地文档" }));
        var id = knowledge.GetProperty("id").GetString();
        var question = await Data(await client.PutAsJsonAsync($"/api/admin/knowledge-bases/{id}/qna", new { question = "几点开始？", answer = "上午九点。", enabled = true, sortOrder = 1, showAsSuggestion = true }));
        Assert.Equal("上午九点。", question.GetProperty("answer").GetString());
        Assert.Single((await Data(await client.GetAsync($"/api/admin/knowledge-bases/{id}/qna"))).EnumerateArray());

        using var multipart = new MultipartFormDataContent();
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes("# 活动说明\n\n上午九点开始。"));
        content.Headers.ContentType = new MediaTypeHeaderValue("text/markdown");
        multipart.Add(content, "file", "guide.md");
        var document = await Data(await client.PostAsync($"/api/admin/knowledge-bases/{id}/documents", multipart));
        Assert.Equal("guide.md", document.GetProperty("fileName").GetString());
        var assetId = document.GetProperty("assetId").GetString();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/assets/{assetId}/content")).StatusCode);
        Assert.Single((await Data(await client.GetAsync($"/api/admin/knowledge-bases/{id}/documents"))).EnumerateArray());
        var listed = (await Data(await client.GetAsync("/api/admin/knowledge-bases"))).EnumerateArray().Single();
        Assert.Equal(1, listed.GetProperty("qnaCount").GetInt32());
        Assert.Equal(1, listed.GetProperty("documentCount").GetInt32());
    }

    /// <summary>
    /// 验证公开聊天持久化完整回答、流式返回内容并生成统计数据。
    /// </summary>
    [Fact]
    public async Task Public_chat_persists_a_completed_model_answer_streams_it_and_reports_analytics()
    {
        // Only the upstream HTTP transport is controlled. The actual provider
        // adapter parses this OpenAI SSE protocol; all app/DB/transport paths are real.
        using var providerFixture = new ChatProviderFixture();
        using var host = new TestHost(upstream: providerFixture);
        using var admin = host.CreateClient();
        var provider = await Data(await admin.PostAsJsonAsync("/api/admin/system/assistant-models/providers", new
        {
            expectedRevision = 0,
            settings = new { name = "受控上游协议", baseUrl = "https://fixture.example.test/v1", secretReference = "config://AssistantSecrets:FixtureChat", secretVersion = "v1", models = new[] { new { id = "fixture-chat", purpose = "chat", supportsTools = false } } }
        }));
        var policy = await Data(await admin.PostAsJsonAsync("/api/admin/assistant-models/policies", new
        {
            expectedRevision = 0,
            settings = new { name = "真实链路策略", businessCode = "chat_fixture", providerId = provider.GetProperty("id").GetString(), providerRevision = provider.GetProperty("revision").GetInt64(), model = "fixture-chat", instructions = "Answer the question.", knowledgeEnabled = false }
        }));
        var assistant = await Data(await admin.PostAsJsonAsync("/api/admin/assistants", new
        {
            name = "完整链路助手",
            configuration = new { businessPolicyId = policy.GetProperty("id").GetString(), welcomeMessage = "欢迎提问", anonymousChatEnabled = true }
        }));
        var id = assistant.GetProperty("id").GetString();
        await Data(await admin.PostAsJsonAsync($"/api/admin/assistants/{id}/publish", new { expectedDraftRevision = 1, expectedPointerRevision = 0 }));

        using var publicClient = host.CreateClient();
        var bootstrap = await Data(await publicClient.GetAsync($"/api/public/assistants/{id}/bootstrap"));
        Assert.Equal("完整链路助手", bootstrap.GetProperty("displayName").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await publicClient.PostAsJsonAsync($"/api/public/assistants/{id}/conversations", new { locale = "zh-cn" })).StatusCode);
        var session = await Data(await publicClient.PostAsJsonAsync($"/api/public/assistants/{id}/sessions", new { subjectHash = "fixture-browser", locale = "zh-cn" }));
        publicClient.DefaultRequestHeaders.Add("X-Assistant-Session-Id", session.GetProperty("session").GetProperty("id").GetString());
        publicClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-public-session-token");
        Assert.Equal(HttpStatusCode.Forbidden, (await publicClient.PostAsJsonAsync($"/api/public/assistants/{id}/conversations", new { locale = "zh-cn" })).StatusCode);
        publicClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("bearerToken").GetString());
        var conversation = await Data(await publicClient.PostAsJsonAsync($"/api/public/assistants/{id}/conversations", new { locale = "zh-cn" }));
        var conversationId = conversation.GetProperty("conversationId").GetString();
        var turn = await Data(await publicClient.PostAsJsonAsync($"/api/public/assistants/{id}/conversations/{conversationId}/turns", new { clientMessageId = "fixture-message-1", messageType = "text", text = ChatProviderFixture.Question, locale = "zh-cn" }));
        var runId = turn.GetProperty("runId").GetString();
        var ticket = Uri.EscapeDataString(turn.GetProperty("streamTicket").GetString()!);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var stream = await publicClient.GetAsync($"/api/public/assistants/{id}/runs/{runId}/events?conversationId={conversationId}&ticket={ticket}", HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        stream.EnsureSuccessStatusCode();
        Assert.Equal("text/event-stream", stream.Content.Headers.ContentType?.MediaType);
        var frames = await stream.Content.ReadAsStringAsync(deadline.Token);
        Assert.Contains("event: run.accepted", frames);
        Assert.Contains("event: message.delta", frames);
        Assert.Contains("event: message.completed", frames);
        Assert.Contains("event: run.completed", frames);
        Assert.Contains("data: [DONE]", frames);
        Assert.DoesNotContain("event: run.failed", frames);
        var envelopes = frames.Split('\n').Where(line => line.StartsWith("data: {")).Select(line => JsonDocument.Parse(line[6..]).RootElement.Clone()).ToArray();
        var completed = Assert.Single(envelopes, envelope => envelope.GetProperty("type").GetString() == "message.completed");
        Assert.Equal(ChatProviderFixture.Answer, completed.GetProperty("payload").GetProperty("text").GetString());
        Assert.Equal(1, providerFixture.Calls);

        var messages = await Data(await publicClient.GetAsync($"/api/public/assistants/{id}/conversations/{conversationId}/messages"));
        var items = messages.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(2, items.Length);
        var reply = Assert.Single(items, item => item.GetProperty("role").GetInt32() == 2);
        Assert.Equal(3, reply.GetProperty("status").GetInt32());
        Assert.Equal(3, reply.GetProperty("turnStatus").GetInt32());
        using var saved = JsonDocument.Parse(reply.GetProperty("contentJson").GetString()!);
        Assert.Equal(ChatProviderFixture.Answer, saved.RootElement.GetProperty("text").GetString());
        Assert.Equal("model", saved.RootElement.GetProperty("source").GetString());
        var conversations = await Data(await admin.GetAsync($"/api/admin/assistants/{id}/analytics/conversations"));
        Assert.Equal(1, conversations.GetProperty("totalCount").GetInt32());
        Assert.Equal(conversationId, Assert.Single(conversations.GetProperty("items").EnumerateArray()).GetProperty("conversationId").GetString());
        var summary = await Data(await admin.GetAsync($"/api/admin/assistants/{id}/analytics/summary"));
        Assert.Equal(1, summary.GetProperty("conversationCount").GetInt32());
        Assert.Equal(1, summary.GetProperty("completedTurnCount").GetInt32());
        Assert.Equal(1, summary.GetProperty("modelAnswerCount").GetInt32());
        Assert.Equal(0, summary.GetProperty("failedTurnCount").GetInt32());

        await Data(await admin.PostAsJsonAsync($"/api/admin/assistants/{id}/revoke", new { expectedPointerRevision = 1 }));
        Assert.Equal(HttpStatusCode.NotFound, (await publicClient.GetAsync($"/api/public/assistants/{id}/conversations/{conversationId}/messages")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await publicClient.PostAsJsonAsync($"/api/public/assistants/{id}/conversations", new { locale = "zh-cn" })).StatusCode);
    }

    /// <summary>
    /// 确认 HTTP 响应成功并拆取统一业务响应中的返回对象。
    /// </summary>
    private static async Task<JsonElement> Data(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"HTTP {(int)response.StatusCode}: {json}");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.True(root.GetProperty("isSuccess").GetBoolean());
        Assert.Equal(0, root.GetProperty("businessCode").GetInt32());
        return root.GetProperty("returnObj").Clone();
    }

    /// <summary>
    /// 仅供独立项目测试使用的测试辅助类型。
    /// </summary>
    private sealed class TestHost(string? directory = null, HttpMessageHandler? upstream = null) : WebApplicationFactory<Program>
    {
        /// <summary>
        /// 保存测试在处理目录时所需的数据或状态。
        /// </summary>
        private readonly string _directory = directory ?? NewDirectory();
        /// <summary>
        /// 在测试输出目录下创建隔离的临时数据路径。
        /// </summary>
        public static string NewDirectory() => Path.Combine(AppContext.BaseDirectory, "TestResults", "test-data", Guid.NewGuid().ToString("N"));
        /// <summary>
        /// 验证网络相关行为、预期结果及错误边界。
        /// </summary>
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Test");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Assistant:DataDirectory"] = _directory,
                ["AssistantSecrets:FixtureChat"] = ChatProviderFixture.TestToken
            }));
            if (upstream is not null)
                builder.ConfigureServices(services => services.AddHttpClient("assistant-provider").ConfigurePrimaryHttpMessageHandler(() => upstream));
        }
    }

    /// <summary>
    /// 为独立项目测试相关测试提供隔离数据、受控依赖或固定 HTTP 响应。
    /// </summary>
    private sealed class ChatProviderFixture : HttpMessageHandler
    {
        /// <summary>
        /// 定义聊天模型供应商测试夹具使用的测试令牌配置值。
        /// </summary>
        public const string TestToken = "fixture-only-no-real-provider-key";
        /// <summary>
        /// 定义聊天模型供应商测试夹具使用的问题配置值。
        /// </summary>
        public const string Question = "What does the standalone assistant verify?";
        /// <summary>
        /// 定义聊天模型供应商测试夹具使用的回答配置值。
        /// </summary>
        public const string Answer = "The standalone assistant persists and streams this verified answer.";
        /// <summary>
        /// 表示聊天模型供应商测试夹具的调用信息。
        /// </summary>
        public int Calls { get; private set; }
        /// <summary>
        /// 验证发送相关行为、预期结果及错误边界。
        /// </summary>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://fixture.example.test/v1/chat/completions", request.RequestUri?.ToString());
            Assert.Equal(TestToken, request.Headers.Authorization?.Parameter);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Assert.Equal("fixture-chat", body.RootElement.GetProperty("model").GetString());
            Assert.True(body.RootElement.GetProperty("stream").GetBoolean());
            Assert.Contains(body.RootElement.GetProperty("messages").EnumerateArray(), message => message.GetProperty("role").GetString() == "user" && message.GetProperty("content").GetString() == Question);
            Calls++;
            var chunk = JsonSerializer.Serialize(new { id = "fixture-completion", choices = new[] { new { index = 0, delta = new { content = Answer }, finish_reason = (string?)null } } });
            var done = JsonSerializer.Serialize(new { id = "fixture-completion", choices = new[] { new { index = 0, delta = new { }, finish_reason = "stop" } }, usage = new { prompt_tokens = 12, completion_tokens = 10, total_tokens = 22 } });
            return new(HttpStatusCode.OK) { Content = new StringContent($"data: {chunk}\n\ndata: {done}\n\ndata: [DONE]\n\n", Encoding.UTF8, "text/event-stream") };
        }
    }
}
