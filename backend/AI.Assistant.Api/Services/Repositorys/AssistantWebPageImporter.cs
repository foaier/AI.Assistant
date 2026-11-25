#nullable enable
using System.Net;
using System.Net.Sockets;
using System.Text;
using AI.Assistant.Models.Knowledge;

namespace AI.Assistant.Services.Repositorys;

/// <summary>
/// 定义读取单个公开网页正文的导入契约。
/// </summary>
public interface IAssistantWebPageImporter
{
    /// <summary>
    /// 获取经过地址和响应校验的网页，并返回可展示的标题与规范化来源地址。
    /// </summary>
    Task<AssistantImportedWebPage> FetchAsync(string address, CancellationToken cancellationToken = default);
}

/// <summary>
/// 抓取单个公开网页，并限制协议、跳转、响应类型、响应大小和网络目标。
/// </summary>
public sealed class AssistantWebPageImporter : IAssistantWebPageImporter
{
    /// <summary>
    /// 限制网页导入允许读取的响应大小。
    /// </summary>
    private const int MaximumResponseBytes = 5 * 1024 * 1024;

    /// <summary>
    /// 限制网页导入允许跟随的跳转次数。
    /// </summary>
    private const int MaximumRedirects = 5;

    /// <summary>
    /// 限制单次网页导入请求的总耗时。
    /// </summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// 保存用于请求网页的 HTTP 客户端。
    /// </summary>
    private readonly HttpClient _httpClient;

    /// <summary>
    /// 保存用于读取网页标题和正文的文档解析器。
    /// </summary>
    private readonly IAssistantDocumentParser _documentParser;

    /// <summary>
    /// 接收网页请求客户端和文档解析器，建立网页导入所需的运行依赖。
    /// </summary>
    public AssistantWebPageImporter(HttpClient httpClient, IAssistantDocumentParser documentParser)
    {
        _httpClient = httpClient;
        _documentParser = documentParser;
    }

    /// <summary>
    /// 获取经过地址和响应校验的网页，并返回可展示的标题与规范化来源地址。
    /// </summary>
    public async Task<AssistantImportedWebPage> FetchAsync(
        string address,
        CancellationToken cancellationToken = default)
    {
        var current = ValidateAddress(address);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);

        for (var redirect = 0; redirect <= MaximumRedirects; redirect++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.UserAgent.ParseAdd("AI.Assistant-KnowledgeImporter/1.0");
            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new InvalidDataException("knowledge_web_page_timeout");
            }
            catch (HttpRequestException exception)
            {
                throw new InvalidDataException("knowledge_web_page_fetch_failed", exception);
            }

            using (response)
            {
                if (IsRedirect(response.StatusCode))
                {
                    if (redirect == MaximumRedirects || response.Headers.Location is null)
                        throw new InvalidDataException("knowledge_web_page_redirect_invalid");
                    current = ValidateAddress(new Uri(current, response.Headers.Location).AbsoluteUri);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                    throw new InvalidDataException("knowledge_web_page_fetch_failed");

                var mediaType = response.Content.Headers.ContentType?.MediaType;
                if (!string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(mediaType, "application/xhtml+xml", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("knowledge_web_page_content_type_invalid");

                if (response.Content.Headers.ContentLength is > MaximumResponseBytes)
                    throw new InvalidDataException("knowledge_web_page_size_invalid");

                var bytes = await ReadLimitedAsync(response.Content, timeout.Token);
                var html = DecodeHtml(bytes, response.Content.Headers.ContentType?.CharSet);
                var encoded = new MemoryStream(Encoding.UTF8.GetBytes(html), writable: false);
                await using (encoded)
                {
                    var parsed = await _documentParser.ParseAsync("text/html", "web-page.html", encoded, cancellationToken);
                    var title = string.IsNullOrWhiteSpace(parsed.Title) ? current.Host : parsed.Title;
                    return new AssistantImportedWebPage(current.AbsoluteUri, title, html);
                }
            }
        }

        throw new InvalidDataException("knowledge_web_page_redirect_invalid");
    }

    /// <summary>
    /// 创建仅连接到公网地址的 TCP 流，避免 DNS 名称解析到内网后访问本机资源。
    /// </summary>
    internal static async ValueTask<Stream> ConnectToPublicAddressAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        var addresses = IPAddress.TryParse(host, out var literal)
            ? new[] { literal }
            : await Dns.GetHostAddressesAsync(host, cancellationToken);
        if (addresses.Length == 0 || addresses.Any(address => !IsPublicAddress(address)))
            throw new InvalidDataException("knowledge_web_page_target_blocked");

        Exception? lastError = null;
        foreach (var address in addresses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true
            };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception exception) when (exception is SocketException or OperationCanceledException)
            {
                socket.Dispose();
                lastError = exception;
                if (exception is OperationCanceledException)
                    throw;
            }
        }

        throw new HttpRequestException("knowledge_web_page_connect_failed", lastError);
    }

    /// <summary>
    /// 校验网页地址只使用公开 HTTP(S) 主机，拒绝本机、内网和特殊用途地址。
    /// </summary>
    private static Uri ValidateAddress(string? address)
    {
        if (!Uri.TryCreate(address?.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || string.IsNullOrWhiteSpace(uri.Host)
            || uri.UserInfo.Length > 0
            || (uri.Scheme == "http" && uri.Port != 80)
            || (uri.Scheme == "https" && uri.Port != 443))
            throw new InvalidDataException("knowledge_web_page_url_invalid");

        var host = uri.DnsSafeHost.TrimEnd('.');
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".test", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".example", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase)
            || (IPAddress.TryParse(host, out var ipAddress) && !IsPublicAddress(ipAddress)))
            throw new InvalidDataException("knowledge_web_page_target_blocked");

        var builder = new UriBuilder(uri) { Fragment = string.Empty };
        return builder.Uri;
    }

    /// <summary>
    /// 判断地址是否属于可公开路由的 IPv4 或 IPv6 地址段。
    /// </summary>
    private static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            return IsPublicAddress(address.MapToIPv4());

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var first = bytes[0];
            var second = bytes[1];
            var third = bytes[2];
            return first != 0
                && first != 10
                && first != 127
                && !(first == 100 && second is >= 64 and <= 127)
                && !(first == 169 && second == 254)
                && !(first == 172 && second is >= 16 and <= 31)
                && !(first == 192 && second == 0 && third == 0)
                && !(first == 192 && second == 0 && third == 2)
                && !(first == 192 && second == 88 && third == 99)
                && !(first == 192 && second == 168)
                && !(first == 198 && second is 18 or 19)
                && !(first == 198 && second == 51 && third == 100)
                && !(first == 203 && second == 0 && third == 113)
                && first < 224;
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6
            || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.IPv6Loopback)
            || address.IsIPv6LinkLocal
            || address.IsIPv6SiteLocal
            || address.IsIPv6Multicast
            || (bytes[0] & 0xFE) == 0xFC
            || (bytes[0] & 0xE0) != 0x20)
            return false;

        return !(bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0D && bytes[3] == 0xB8);
    }

    /// <summary>
    /// 将 HTML 响应限制在固定字节数内读取，防止服务端返回超大页面。
    /// </summary>
    private static async Task<byte[]> ReadLimitedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var source = await content.ReadAsStreamAsync(cancellationToken);
        await using var target = new MemoryStream();
        var buffer = new byte[32 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
                break;
            if (target.Length + read > MaximumResponseBytes)
                throw new InvalidDataException("knowledge_web_page_size_invalid");
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return target.ToArray();
    }

    /// <summary>
    /// 按响应声明的编码读取 HTML，并在编码不可用时安全回退到 UTF-8。
    /// </summary>
    private static string DecodeHtml(byte[] bytes, string? charset)
    {
        if (!string.IsNullOrWhiteSpace(charset))
        {
            try
            {
                return Encoding.GetEncoding(charset.Trim().Trim('"', '\''))
                    .GetString(bytes).TrimStart('\uFEFF');
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
                /* 未知字符集按 UTF-8 解码，随后由 HTML 解析器做严格正文校验。 */
            }
        }

        return new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
    }

    /// <summary>
    /// 判断响应状态是否属于允许手动校验目标地址的 HTTP 跳转。
    /// </summary>
    private static bool IsRedirect(HttpStatusCode statusCode)
        => statusCode is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Redirect
            or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;
}
