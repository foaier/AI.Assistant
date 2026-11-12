#nullable enable
// Spec: specs/136-public-assistant/requests/REQ-20260928-007-public-assistant-knowledge-qna-asset-analytics.md
// Focused proof for the document-indexing parser boundary: supported formats parse,
// unsupported binary formats fail before an upload can be advertised as indexable.

using System.IO;
using System.IO.Compression;
using System.Text;
using SmartAssistant.Services.Infrastructure;
using Xunit;

namespace SmartAssistant.Tests;

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
    /// 验证缺少真实解析器的格式被明确拒绝。
    /// </summary>
    [Theory]
    [InlineData("application/pdf")]
    [InlineData("application/msword")]
    [InlineData("text/html")]
    public async Task Formats_without_a_real_parser_fail_closed(string contentType)
    {
        var parser = new AssistantDocumentParser();
        await using var content = new MemoryStream("payload"u8.ToArray());

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            parser.ParseAsync(contentType, "document.bin", content, CancellationToken.None));

        Assert.Equal("knowledge_document_parser_unsupported_type", error.Message);
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
}
