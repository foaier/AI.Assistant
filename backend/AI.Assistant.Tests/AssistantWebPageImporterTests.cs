#nullable enable
using System.Net;
using System.Text;
using AI.Assistant.Services.Repositorys;
using Xunit;

namespace AI.Assistant.Tests;

/// <summary>
/// 验证单页网页导入的地址校验、跳转限制和页面元数据读取。
/// </summary>
public sealed class AssistantWebPageImporterTests
{
    /// <summary>
    /// 验证普通 HTTPS 页面可读取标题，并保留最终页面地址和 HTML 正文。
    /// </summary>
    [Fact]
    public async Task Fetches_public_html_and_extracts_its_title()
    {
        const string html = "<html><head><title>使用指南</title></head><body><main>正文内容</main></body></html>";
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(html, Encoding.UTF8, "text/html")
        });
        var importer = CreateImporter(handler);

        var result = await importer.FetchAsync("https://docs.example.com/guide", CancellationToken.None);

        Assert.Equal("https://docs.example.com/guide", result.SourceUrl);
        Assert.Equal("使用指南", result.Title);
        Assert.Equal(html, result.Html);
        Assert.Equal(1, handler.RequestCount);
    }

    /// <summary>
    /// 验证非 HTTP 协议、localhost 和私有 IP 在发起网络请求前被拒绝。
    /// </summary>
    [Theory]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("http://localhost/admin")]
    [InlineData("http://127.0.0.1:5088/api/health")]
    [InlineData("http://192.168.1.20/")]
    [InlineData("http://docs.example.com:443/guide")]
    [InlineData("https://docs.example.com:80/guide")]
    [InlineData("https://docs.example.com:8443/guide")]
    public async Task Rejects_non_public_addresses_before_network_access(string address)
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var importer = CreateImporter(handler);

        await Assert.ThrowsAsync<InvalidDataException>(() => importer.FetchAsync(address, CancellationToken.None));

        Assert.Equal(0, handler.RequestCount);
    }

    /// <summary>
    /// 验证公开页面不能通过 HTTP 跳转访问环回地址。
    /// </summary>
    [Fact]
    public async Task Rejects_redirects_to_private_addresses()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Redirect)
        {
            Headers = { Location = new Uri("http://127.0.0.1/internal") }
        });
        var importer = CreateImporter(handler);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            importer.FetchAsync("https://docs.example.com/redirect", CancellationToken.None));

        Assert.Equal(1, handler.RequestCount);
    }

    /// <summary>
    /// 验证网页导入拒绝非 HTML 响应，避免将任意下载文件作为页面解析。
    /// </summary>
    [Fact]
    public async Task Rejects_non_html_responses()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("binary", Encoding.UTF8, "application/octet-stream")
        });
        var importer = CreateImporter(handler);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            importer.FetchAsync("https://docs.example.com/file", CancellationToken.None));

        Assert.Equal("knowledge_web_page_content_type_invalid", error.Message);
    }

    /// <summary>
    /// 验证大小写不同的合法 HTML 媒体类型仍可作为网页内容读取。
    /// </summary>
    [Fact]
    public async Task Accepts_case_insensitive_html_content_types()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "<html><head><title>大小写测试</title></head><body><main>网页正文</main></body></html>",
                Encoding.UTF8,
                "Text/HTML")
        });
        var importer = CreateImporter(handler);

        var result = await importer.FetchAsync("https://docs.example.com/guide", CancellationToken.None);

        Assert.Equal("大小写测试", result.Title);
    }

    private static AssistantWebPageImporter CreateImporter(StubHttpMessageHandler handler)
        => new(new HttpClient(handler), new AssistantDocumentParser());

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestCount++;
            return Task.FromResult(responseFactory(request));
        }
    }
}
