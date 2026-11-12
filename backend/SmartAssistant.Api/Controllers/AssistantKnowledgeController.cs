#nullable enable
using Microsoft.AspNetCore.Mvc;
using SmartAssistant.Models.Common;
using SmartAssistant.Models.Knowledge;
using SmartAssistant.Platform;
using SmartAssistant.Services.Contracts;

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
        return BizResult.BusinessSuccess(await _service.ListKnowledgeBasesAsync(cancellationToken));
    }

    /// <summary>
    /// 创建知识库。
    /// </summary>
    [HttpPost]
    public async Task<BizResult<AssistantKnowledgeBaseDto>> CreateAsync([FromBody] CreateKnowledgeBaseRequest request, CancellationToken cancellationToken)
    {
        return BizResult.BusinessSuccess(await _service.CreateKnowledgeBaseAsync(new CreateAssistantKnowledgeBaseCommand(request.Name, request.Description), cancellationToken));
    }

    /// <summary>
    /// 删除指定知识库。
    /// </summary>
    [HttpDelete("{knowledgeBaseId}")]
    public async Task<BizResult<object>> DeleteAsync(string knowledgeBaseId, CancellationToken cancellationToken)
    {
        await _service.DeleteKnowledgeBaseAsync(knowledgeBaseId, cancellationToken);
        return BizResult.BusinessSuccess<object>(new { deleted = true });
    }

    /// <summary>
    /// 返回知识库当前分块策略。
    /// </summary>
    [HttpGet("{knowledgeBaseId}/chunking-policy")]
    public async Task<BizResult<AssistantKnowledgeChunkingPolicyDto>> GetChunkingPolicyAsync(string knowledgeBaseId, CancellationToken cancellationToken)
    {
        return BizResult.BusinessSuccess(await _service.GetChunkingPolicyAsync(knowledgeBaseId, cancellationToken));
    }

    /// <summary>
    /// 保存文档分块策略。
    /// </summary>
    [HttpPut("{knowledgeBaseId}/chunking-policy")]
    public async Task<BizResult<AssistantKnowledgeChunkingPolicyDto>> SaveChunkingPolicyAsync(
        string knowledgeBaseId, [FromBody] SaveChunkingPolicyRequest request, CancellationToken cancellationToken)
    {
        return BizResult.BusinessSuccess(await _service.SaveChunkingPolicyAsync(knowledgeBaseId,
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
        return BizResult.BusinessSuccess(await _service.GetEmbeddingProfileAsync(
            knowledgeBaseId, cancellationToken));
    }

    /// <summary>
    /// 更新嵌入模型选择。
    /// </summary>
    [HttpPut("{knowledgeBaseId}/embedding-profile")]
    public async Task<BizResult<AssistantKnowledgeEmbeddingProfileDto>> SaveEmbeddingProfileAsync(
        string knowledgeBaseId, [FromBody] SaveAssistantKnowledgeEmbeddingProfileCommand command,
        CancellationToken cancellationToken)
    {
        return BizResult.BusinessSuccess(await _service.SaveEmbeddingProfileAsync(
            knowledgeBaseId, command, cancellationToken));
    }

    /// <summary>
    /// 返回指定文档按分块策略切分的预览。
    /// </summary>
    [HttpPost("{knowledgeBaseId}/chunking-preview/{documentId}")]
    public async Task<BizResult<AssistantKnowledgeChunkPreviewDto>> PreviewChunkingAsync(
        string knowledgeBaseId, string documentId, [FromBody] SaveChunkingPolicyRequest request, CancellationToken cancellationToken)
    {
        return BizResult.BusinessSuccess(await _service.PreviewChunkingAsync(knowledgeBaseId, documentId,
            new SaveAssistantKnowledgeChunkingPolicyCommand(request.Mode, request.MaxChunkSize, request.MinChunkSize, request.OverlapSize,
                request.MaxHeadingLevel, request.PreserveHeadingPath, request.MergeShortChunks, false), cancellationToken));
    }

    /// <summary>
    /// 返回知识库固定问答列表。
    /// </summary>
    [HttpGet("{knowledgeBaseId}/qna")]
    public async Task<BizResult<IReadOnlyList<AssistantKnowledgeQnaDto>>> ListQnaAsync(string knowledgeBaseId, CancellationToken cancellationToken)
    {
        return BizResult.BusinessSuccess(await _service.ListQnaAsync(knowledgeBaseId, cancellationToken));
    }

    /// <summary>
    /// 保存固定问答内容与设置。
    /// </summary>
    [HttpPut("{knowledgeBaseId}/qna")]
    public async Task<BizResult<AssistantKnowledgeQnaDto>> SaveQnaAsync(string knowledgeBaseId, [FromBody] SaveQnaRequest request, CancellationToken cancellationToken)
    {
        return BizResult.BusinessSuccess(await _service.SaveQnaAsync(knowledgeBaseId,
            new SaveAssistantKnowledgeQnaCommand(request.Id, request.Question, request.Answer, request.Enabled, request.SortOrder, request.ShowAsSuggestion), cancellationToken));
    }

    /// <summary>
    /// 删除指定固定问答。
    /// </summary>
    [HttpDelete("{knowledgeBaseId}/qna/{qnaId}")]
    public async Task<BizResult<object>> DeleteQnaAsync(string knowledgeBaseId, string qnaId, CancellationToken cancellationToken)
    {
        await _service.DeleteQnaAsync(knowledgeBaseId, qnaId, cancellationToken);
        return BizResult.BusinessSuccess<object>(new { deleted = true });
    }

    /// <summary>
    /// 返回问题在知识库固定问答中的匹配结果。
    /// </summary>
    [HttpPost("{knowledgeBaseId}/qna/{qnaId}/test")]
    public async Task<BizResult<AssistantKnowledgeQnaTestResult>> TestQnaAsync(
        string knowledgeBaseId, string qnaId, [FromBody] TestQnaRequest request, CancellationToken cancellationToken)
    {
        return BizResult.BusinessSuccess(await _service.TestQnaAsync(knowledgeBaseId, qnaId,
            new TestAssistantKnowledgeQnaCommand(request.Question), cancellationToken));
    }

    /// <summary>
    /// 返回知识库文档列表及处理状态。
    /// </summary>
    [HttpGet("{knowledgeBaseId}/documents")]
    public async Task<BizResult<IReadOnlyList<AssistantKnowledgeDocumentDto>>> ListDocumentsAsync(
        string knowledgeBaseId, CancellationToken cancellationToken)
    {
        return BizResult.BusinessSuccess(await _service.ListDocumentsAsync(knowledgeBaseId, cancellationToken));
    }

    /// <summary>
    /// 读取 multipart 上传文件，并交由知识库服务保存私有文档。
    /// </summary>
    [HttpPost("{knowledgeBaseId}/documents")]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<BizResult<AssistantKnowledgeDocumentDto>> UploadDocumentAsync(
        string knowledgeBaseId, [FromForm] IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null)
            throw new BizResultException(BusinessCodes.BusinessException, "knowledge_document_file_required");
        var upload = new AssistantKnowledgeDocumentUpload(file.FileName, file.ContentType, file.Length, file.OpenReadStream());
        await using (upload.Content)
        {
            return BizResult.BusinessSuccess(await _service.UploadDocumentAsync(knowledgeBaseId, upload, cancellationToken));
        }
    }

    /// <summary>
    /// 请求处理文档及其索引。
    /// </summary>
    [HttpPost("{knowledgeBaseId}/documents/{documentId}/process")]
    public async Task<BizResult<AssistantKnowledgeDocumentProcessResult>> ProcessDocumentAsync(
        string knowledgeBaseId, string documentId, CancellationToken cancellationToken)
    {
        return BizResult.BusinessSuccess(await _service.ProcessDocumentAsync(knowledgeBaseId, documentId, cancellationToken));
    }

    /// <summary>
    /// 返回知识文档的正文、结构与片段预览。
    /// </summary>
    [HttpGet("{knowledgeBaseId}/documents/{documentId}/preview")]
    public async Task<BizResult<AssistantKnowledgeDocumentPreviewDto>> PreviewDocumentAsync(
        string knowledgeBaseId, string documentId, CancellationToken cancellationToken)
    {
        return BizResult.BusinessSuccess(await _service.GetDocumentPreviewAsync(knowledgeBaseId, documentId, cancellationToken));
    }

    /// <summary>
    /// 返回指定问题的知识片段检索结果。
    /// </summary>
    [HttpPost("{knowledgeBaseId}/retrieval-test")]
    public async Task<BizResult<AssistantKnowledgeRetrievalTestResult>> TestRetrievalAsync(
        string knowledgeBaseId, [FromBody] TestRetrievalRequest request, CancellationToken cancellationToken)
    {
        return BizResult.BusinessSuccess(await _service.TestRetrievalAsync(knowledgeBaseId,
            new TestAssistantKnowledgeRetrievalCommand(request.Question), cancellationToken));
    }

    /// <summary>
    /// 返回知识库索引任务列表。
    /// </summary>
    [HttpGet("{knowledgeBaseId}/index-jobs")]
    public async Task<BizResult<IReadOnlyList<AssistantKnowledgeIndexJobDto>>> ListIndexJobsAsync(
        string knowledgeBaseId, CancellationToken cancellationToken)
    {
        return BizResult.BusinessSuccess(await _service.ListIndexJobsAsync(knowledgeBaseId, cancellationToken));
    }
}
