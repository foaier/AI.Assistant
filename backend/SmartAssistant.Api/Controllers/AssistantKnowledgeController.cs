#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SmartAssistant.Extensions.Common;
using SmartAssistant.Authentication;
using SmartAssistant.Modules.Contracts;
using SmartAssistant.Platform;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace SmartAssistant.Controllers;

/// <summary>
/// 提供知识库、问答、文档、分块和索引管理接口。
/// </summary>
[ApiController]

[Route("api/admin/knowledge-bases")]
public sealed class AssistantKnowledgeController : ControllerBase
{
    /// <summary>
    /// 保存助手知识服务依赖，供助手知识控制器执行相关操作。
    /// </summary>
    private readonly IAssistantKnowledgeService _service;

    /// <summary>
    /// 接收并保存助手知识控制器执行所需的依赖，建立后续操作使用的运行状态。
    /// </summary>
    public AssistantKnowledgeController(IAssistantKnowledgeService service)
    {
        _service = service;
    }

    /// <summary>
    /// 列出知识库及其摘要信息。
    /// </summary>
    [HttpGet]
    public async Task<BizResult<IReadOnlyList<AssistantKnowledgeBaseDto>>> ListAsync(CancellationToken cancellationToken)
    {
        var actor = User.GetUserId();
        return BizResult.BusinessSuccess(await _service.ListKnowledgeBasesAsync(actor, cancellationToken));
    }

    /// <summary>
    /// 校验写入权限后创建知识库。
    /// </summary>
    [HttpPost]
    public async Task<BizResult<AssistantKnowledgeBaseDto>> CreateAsync([FromBody] CreateKnowledgeBaseRequest request, CancellationToken cancellationToken)
    {
        var actor = User.GetUserId();
        return BizResult.BusinessSuccess(await _service.CreateKnowledgeBaseAsync(actor, new CreateAssistantKnowledgeBaseCommand(request.Name, request.Description), cancellationToken));
    }

    /// <summary>
    /// 校验写入权限后删除指定知识库。
    /// </summary>
    [HttpDelete("{knowledgeBaseId}")]
    public async Task<BizResult<object>> DeleteAsync(string knowledgeBaseId, CancellationToken cancellationToken)
    {
        var actor = User.GetUserId();
        await _service.DeleteKnowledgeBaseAsync(actor, knowledgeBaseId, cancellationToken);
        return BizResult.BusinessSuccess<object>(new { deleted = true });
    }

    /// <summary>
    /// 返回知识库当前分块策略。
    /// </summary>
    [HttpGet("{knowledgeBaseId}/chunking-policy")]
    public async Task<BizResult<AssistantKnowledgeChunkingPolicyDto>> GetChunkingPolicyAsync(string knowledgeBaseId, CancellationToken cancellationToken)
    {
        var actor = User.GetUserId();
        return BizResult.BusinessSuccess(await _service.GetChunkingPolicyAsync(actor, knowledgeBaseId, cancellationToken));
    }

    /// <summary>
    /// 校验写入权限后保存文档分块策略。
    /// </summary>
    [HttpPut("{knowledgeBaseId}/chunking-policy")]
    public async Task<BizResult<AssistantKnowledgeChunkingPolicyDto>> SaveChunkingPolicyAsync(
        string knowledgeBaseId, [FromBody] SaveChunkingPolicyRequest request, CancellationToken cancellationToken)
    {
        var actor = User.GetUserId();
        return BizResult.BusinessSuccess(await _service.SaveChunkingPolicyAsync(actor, knowledgeBaseId,
            new SaveAssistantKnowledgeChunkingPolicyCommand(request.Mode, request.MaxChunkSize, request.MinChunkSize, request.OverlapSize,
                request.MaxHeadingLevel, request.PreserveHeadingPath, request.MergeShortChunks, request.ReprocessDocuments), cancellationToken));
    }

    /// <summary>
    /// 返回知识库目标与生效嵌入配置。
    /// </summary>
    [HttpGet("{knowledgeBaseId}/embedding-profile")]
    public async Task<BizResult<AssistantKnowledgeEmbeddingProfileDto>> GetEmbeddingProfileAsync(
        string knowledgeBaseId, CancellationToken cancellationToken)
    {
        var actor = User.GetUserId();
        return BizResult.BusinessSuccess(await _service.GetEmbeddingProfileAsync(
            actor, knowledgeBaseId, cancellationToken));
    }

    /// <summary>
    /// 校验写入权限后更新嵌入模型选择。
    /// </summary>
    [HttpPut("{knowledgeBaseId}/embedding-profile")]
    public async Task<BizResult<AssistantKnowledgeEmbeddingProfileDto>> SaveEmbeddingProfileAsync(
        string knowledgeBaseId, [FromBody] SaveAssistantKnowledgeEmbeddingProfileCommand command,
        CancellationToken cancellationToken)
    {
        var actor = User.GetUserId();
        return BizResult.BusinessSuccess(await _service.SaveEmbeddingProfileAsync(
            actor, knowledgeBaseId, command, cancellationToken));
    }

    /// <summary>
    /// 返回指定文档按分块策略切分的预览。
    /// </summary>
    [HttpPost("{knowledgeBaseId}/chunking-preview/{documentId}")]
    public async Task<BizResult<AssistantKnowledgeChunkPreviewDto>> PreviewChunkingAsync(
        string knowledgeBaseId, string documentId, [FromBody] SaveChunkingPolicyRequest request, CancellationToken cancellationToken)
    {
        var actor = User.GetUserId();
        return BizResult.BusinessSuccess(await _service.PreviewChunkingAsync(actor, knowledgeBaseId, documentId,
            new SaveAssistantKnowledgeChunkingPolicyCommand(request.Mode, request.MaxChunkSize, request.MinChunkSize, request.OverlapSize,
                request.MaxHeadingLevel, request.PreserveHeadingPath, request.MergeShortChunks, false), cancellationToken));
    }

    /// <summary>
    /// 返回知识库固定问答列表。
    /// </summary>
    [HttpGet("{knowledgeBaseId}/qna")]
    public async Task<BizResult<IReadOnlyList<AssistantKnowledgeQnaDto>>> ListQnaAsync(string knowledgeBaseId, CancellationToken cancellationToken)
    {
        var actor = User.GetUserId();
        return BizResult.BusinessSuccess(await _service.ListQnaAsync(actor, knowledgeBaseId, cancellationToken));
    }

    /// <summary>
    /// 校验写入权限后保存固定问答内容与设置。
    /// </summary>
    [HttpPut("{knowledgeBaseId}/qna")]
    public async Task<BizResult<AssistantKnowledgeQnaDto>> SaveQnaAsync(string knowledgeBaseId, [FromBody] SaveQnaRequest request, CancellationToken cancellationToken)
    {
        var actor = User.GetUserId();
        return BizResult.BusinessSuccess(await _service.SaveQnaAsync(actor, knowledgeBaseId,
            new SaveAssistantKnowledgeQnaCommand(request.Id, request.Question, request.Answer, request.Enabled, request.SortOrder, request.ShowAsSuggestion), cancellationToken));
    }

    /// <summary>
    /// 校验写入权限后删除指定固定问答。
    /// </summary>
    [HttpDelete("{knowledgeBaseId}/qna/{qnaId}")]
    public async Task<BizResult<object>> DeleteQnaAsync(string knowledgeBaseId, string qnaId, CancellationToken cancellationToken)
    {
        var actor = User.GetUserId();
        await _service.DeleteQnaAsync(actor, knowledgeBaseId, qnaId, cancellationToken);
        return BizResult.BusinessSuccess<object>(new { deleted = true });
    }

    /// <summary>
    /// 返回问题在知识库固定问答中的匹配结果。
    /// </summary>
    [HttpPost("{knowledgeBaseId}/qna/{qnaId}/test")]
    public async Task<BizResult<AssistantKnowledgeQnaTestResult>> TestQnaAsync(
        string knowledgeBaseId, string qnaId, [FromBody] TestQnaRequest request, CancellationToken cancellationToken)
    {
        var actor = User.GetUserId();
        return BizResult.BusinessSuccess(await _service.TestQnaAsync(actor, knowledgeBaseId, qnaId,
            new TestAssistantKnowledgeQnaCommand(request.Question), cancellationToken));
    }

    /// <summary>
    /// 返回知识库文档列表及处理状态。
    /// </summary>
    [HttpGet("{knowledgeBaseId}/documents")]
    public async Task<BizResult<IReadOnlyList<AssistantKnowledgeDocumentDto>>> ListDocumentsAsync(
        string knowledgeBaseId, CancellationToken cancellationToken)
    {
        var actor = User.GetUserId();
        return BizResult.BusinessSuccess(await _service.ListDocumentsAsync(actor, knowledgeBaseId, cancellationToken));
    }

    /// <summary>
    /// 读取 multipart 上传文件，并交由知识库服务保存私有文档。
    /// </summary>
    [HttpPost("{knowledgeBaseId}/documents")]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<BizResult<AssistantKnowledgeDocumentDto>> UploadDocumentAsync(
        string knowledgeBaseId, [FromForm] IFormFile file, CancellationToken cancellationToken)
    {
        var actor = User.GetUserId();
        if (file is null)
            throw new BizResultException(BusinessCodes.BusinessException, "knowledge_document_file_required");
        var upload = new AssistantKnowledgeDocumentUpload(file.FileName, file.ContentType, file.Length, file.OpenReadStream());
        await using (upload.Content)
        {
            return BizResult.BusinessSuccess(await _service.UploadDocumentAsync(actor, knowledgeBaseId, upload, cancellationToken));
        }
    }

    /// <summary>
    /// 校验写入权限后请求处理文档及其索引。
    /// </summary>
    [HttpPost("{knowledgeBaseId}/documents/{documentId}/process")]
    public async Task<BizResult<AssistantKnowledgeDocumentProcessResult>> ProcessDocumentAsync(
        string knowledgeBaseId, string documentId, CancellationToken cancellationToken)
    {
        var actor = User.GetUserId();
        return BizResult.BusinessSuccess(await _service.ProcessDocumentAsync(actor, knowledgeBaseId, documentId, cancellationToken));
    }

    /// <summary>
    /// 返回知识文档的正文、结构与片段预览。
    /// </summary>
    [HttpGet("{knowledgeBaseId}/documents/{documentId}/preview")]
    public async Task<BizResult<AssistantKnowledgeDocumentPreviewDto>> PreviewDocumentAsync(
        string knowledgeBaseId, string documentId, CancellationToken cancellationToken)
    {
        var actor = User.GetUserId();
        return BizResult.BusinessSuccess(await _service.GetDocumentPreviewAsync(actor, knowledgeBaseId, documentId, cancellationToken));
    }

    /// <summary>
    /// 返回指定问题的知识片段检索结果。
    /// </summary>
    [HttpPost("{knowledgeBaseId}/retrieval-test")]
    public async Task<BizResult<AssistantKnowledgeRetrievalTestResult>> TestRetrievalAsync(
        string knowledgeBaseId, [FromBody] TestRetrievalRequest request, CancellationToken cancellationToken)
    {
        var actor = User.GetUserId();
        return BizResult.BusinessSuccess(await _service.TestRetrievalAsync(actor, knowledgeBaseId,
            new TestAssistantKnowledgeRetrievalCommand(request.Question), cancellationToken));
    }

    /// <summary>
    /// 返回知识库索引任务列表。
    /// </summary>
    [HttpGet("{knowledgeBaseId}/index-jobs")]
    public async Task<BizResult<IReadOnlyList<AssistantKnowledgeIndexJobDto>>> ListIndexJobsAsync(
        string knowledgeBaseId, CancellationToken cancellationToken)
    {
        var actor = User.GetUserId();
        return BizResult.BusinessSuccess(await _service.ListIndexJobsAsync(actor, knowledgeBaseId, cancellationToken));
    }
}

/// <summary>
/// 创建知识库的 HTTP 请求体。
/// </summary>
public sealed class CreateKnowledgeBaseRequest
{
    /// <summary>
    /// 记录此资源的展示名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>
    /// 记录此资源的可选说明。
    /// </summary>
    public string? Description { get; set; }
}

/// <summary>
/// 修改知识库分块策略的 HTTP 请求体。
/// </summary>
public sealed class SaveChunkingPolicyRequest
{
    /// <summary>
    /// 保存请求中的模式字段。
    /// </summary>
    public AssistantKnowledgeChunkingMode Mode { get; set; } = AssistantKnowledgeChunkingMode.Hybrid;
    /// <summary>
    /// 限制单个文本片段的最大字符数。
    /// </summary>
    public int MaxChunkSize { get; set; } = 1200;
    /// <summary>
    /// 指定短片段合并所依据的最小字符数。
    /// </summary>
    public int MinChunkSize { get; set; } = 200;
    /// <summary>
    /// 指定相邻文本片段保留的重叠字符数。
    /// </summary>
    public int OverlapSize { get; set; } = 160;
    /// <summary>
    /// 限制用于文档分段的 Markdown 标题层级。
    /// </summary>
    public int MaxHeadingLevel { get; set; } = 3;
    /// <summary>
    /// 控制片段正文是否保留所属标题路径。
    /// </summary>
    public bool PreserveHeadingPath { get; set; } = true;
    /// <summary>
    /// 控制是否合并长度不足的相邻片段。
    /// </summary>
    public bool MergeShortChunks { get; set; } = true;
    /// <summary>
    /// 控制修改分块策略后是否重新处理已有文档。
    /// </summary>
    public bool ReprocessDocuments { get; set; }
}

/// <summary>
/// 保存固定问答内容和展示设置的 HTTP 请求体。
/// </summary>
public sealed class SaveQnaRequest
{
    /// <summary>
    /// 记录当前数据的唯一标识。
    /// </summary>
    public string? Id { get; set; }
    /// <summary>
    /// 保存请求中的问题字段。
    /// </summary>
    public string Question { get; set; } = string.Empty;
    /// <summary>
    /// 保存请求中的回答字段。
    /// </summary>
    public string? Answer { get; set; }
    /// <summary>
    /// 标记对应能力或配置是否启用。
    /// </summary>
    public bool Enabled { get; set; } = true;
    /// <summary>
    /// 记录问答项在列表中的显示顺序。
    /// </summary>
    public int SortOrder { get; set; }
    /// <summary>
    /// 标记该问答是否可以作为建议问题展示。
    /// </summary>
    public bool ShowAsSuggestion { get; set; }
}

/// <summary>
/// 验证固定问答匹配的问题请求体。
/// </summary>
public sealed class TestQnaRequest
{
    /// <summary>
    /// 保存请求中的问题字段。
    /// </summary>
    public string Question { get; set; } = string.Empty;
}

/// <summary>
/// 验证知识检索效果的问题请求体。
/// </summary>
public sealed class TestRetrievalRequest
{
    /// <summary>
    /// 保存请求中的问题字段。
    /// </summary>
    public string Question { get; set; } = string.Empty;
}
