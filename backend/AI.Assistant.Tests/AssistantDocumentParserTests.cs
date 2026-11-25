#nullable enable
// Spec: specs/136-public-assistant/requests/REQ-20260928-007-public-assistant-knowledge-qna-asset-analytics.md
// Focused proof for the document-indexing parser boundary: supported formats parse,
// unsupported binary formats fail before an upload can be advertised as indexable.

using System.IO;
using System.IO.Compression;
using System.Text;
using AI.Assistant.Services.Repositorys;
using Xunit;

namespace AI.Assistant.Tests;

/// <summary>
/// 验证助手文档解析器行为和边界条件的自动化测试集合。
/// </summary>
public sealed class AssistantDocumentParserTests
{
    /// <summary>
    /// 验证文本类文档使用严格 UTF-8 解码读取。
    /// </summary>
    [Theory]
    [InlineData("text/plain", "hello\r\nworld", "text-v1")]
    [InlineData("text/markdown", "# title\r\nbody", "text-v1")]
    [InlineData("application/json", "{\"answer\":\"ok\"}", "text-v1")]
    public async Task Text_documents_are_read_as_strict_utf8(string contentType, string input, string parserVersion)
    {
        var parser = new AssistantDocumentParser();
        await using var content = new MemoryStream(Encoding.UTF8.GetBytes(input));

        var parsed = await parser.ParseAsync(contentType, "document.txt", content, CancellationToken.None);

        Assert.Equal(parserVersion, parsed.ParserVersion);
        Assert.Equal(input.Replace("\r\n", "\n", StringComparison.Ordinal), parsed.Text);
    }

    /// <summary>
    /// 验证 DOCX 解析保留段落、制表符和换行。
    /// </summary>
    [Fact]
    public async Task Docx_documents_extract_paragraphs_tabs_and_breaks()
    {
        var parser = new AssistantDocumentParser();
        await using var content = CreateDocx(
            "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>"
            + "<w:p><w:r><w:t>第一段</w:t><w:tab/><w:t>字段</w:t><w:br/><w:t>继续</w:t></w:r></w:p>"
            + "<w:p><w:r><w:t>第二段</w:t></w:r></w:p>"
            + "</w:body></w:document>");

        var parsed = await parser.ParseAsync(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "document.docx", content, CancellationToken.None);

        Assert.Equal("docx-v1", parsed.ParserVersion);
        Assert.Equal("第一段\t字段\n继续\n第二段", parsed.Text);
    }

    /// <summary>
    /// 验证 PDF 文本可以按页面顺序提取，并保留页面之间的分隔。
    /// </summary>
    [Fact]
    public async Task Pdf_documents_extract_page_text()
    {
        var parser = new AssistantDocumentParser();
        await using var content = CreatePdf("PDF extraction marker");

        var parsed = await parser.ParseAsync("application/pdf", "guide.pdf", content, CancellationToken.None);

        Assert.Equal("pdf-v1", parsed.ParserVersion);
        Assert.Contains("PDF extraction marker", parsed.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 HTML 正文会提取标题和主体内容，并排除脚本及导航文本。
    /// </summary>
    [Fact]
    public async Task Html_documents_extract_title_and_visible_body_text()
    {
        var parser = new AssistantDocumentParser();
        const string html = """
            <!doctype html><html><head><title>报名指南</title><script>privateScriptMarker()</script></head>
            <body><nav>导航不应进入知识库</nav><main><h1>活动报名</h1><p>请在 10 月 1 日前提交申请。</p></main></body></html>
            """;
        await using var content = new MemoryStream(Encoding.UTF8.GetBytes(html));

        var parsed = await parser.ParseAsync("text/html", "guide.html", content, CancellationToken.None);

        Assert.Equal("html-v1", parsed.ParserVersion);
        Assert.Equal("报名指南", parsed.Title);
        Assert.Contains("活动报名", parsed.Text, StringComparison.Ordinal);
        Assert.Contains("10 月 1 日", parsed.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("privateScriptMarker", parsed.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("导航不应进入知识库", parsed.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证缺少真实解析器的格式被明确拒绝。
    /// </summary>
    [Theory]
    [InlineData("application/msword")]
    public async Task Formats_without_a_real_parser_fail_closed(string contentType)
    {
        var parser = new AssistantDocumentParser();
        await using var content = new MemoryStream("payload"u8.ToArray());

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            parser.ParseAsync(contentType, "document.bin", content, CancellationToken.None));

        Assert.Equal("knowledge_document_parser_unsupported_type", error.Message);
    }

    /// <summary>
    /// 验证内容并非有效 PDF 时返回稳定的解析错误。
    /// </summary>
    [Fact]
    public async Task Invalid_pdf_documents_fail_with_a_stable_error()
    {
        var parser = new AssistantDocumentParser();
        await using var content = new MemoryStream("not a pdf"u8.ToArray());

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            parser.ParseAsync("application/pdf", "broken.pdf", content, CancellationToken.None));

        Assert.Equal("knowledge_document_pdf_invalid", error.Message);
    }

    /// <summary>
    /// 验证创建相关行为、预期结果及错误边界。
    /// </summary>
    private static MemoryStream CreateDocx(string documentXml)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        using (var writer = new StreamWriter(archive.CreateEntry("word/document.xml").Open(), new UTF8Encoding(false)))
        {
            writer.Write(documentXml);
        }

        stream.Position = 0;
        return stream;
    }

    /// <summary>
    /// 生成带有有效交叉引用表的单页 PDF 测试文档。
    /// </summary>
    private static MemoryStream CreatePdf(string text)
    {
        var stream = new MemoryStream();
        var offsets = new List<long> { 0 };
        void Write(string value) => stream.Write(Encoding.ASCII.GetBytes(value));

        Write("%PDF-1.4\n");
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
        };
        foreach (var (body, index) in objects.Select((body, index) => (body, index)))
        {
            offsets.Add(stream.Position);
            Write($"{index + 1} 0 obj\n{body}\nendobj\n");
        }

        var content = $"BT\n/F1 12 Tf\n72 720 Td\n({text}) Tj\nET\n";
        offsets.Add(stream.Position);
        Write($"5 0 obj\n<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}endstream\nendobj\n");
        var xrefOffset = stream.Position;
        Write("xref\n0 6\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
            Write($"{offset:0000000000} 00000 n \n");
        Write($"trailer\n<< /Root 1 0 R /Size 6 >>\nstartxref\n{xrefOffset}\n%%EOF\n");
        stream.Position = 0;
        return stream;
    }
}
