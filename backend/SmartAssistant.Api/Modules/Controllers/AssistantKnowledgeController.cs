#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SmartAssistant.Extensions.Common;
using SmartAssistant.Authentication;
using SmartAssistant.Modules.Admin.Public.Contracts;
using SmartAssistant.Modules.Contracts;
using SmartAssistant.Platform;
using SmartAssistant.Platform.Tenant;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace SmartAssistant.Modules.Controllers;

/// <summary>
/// 提供知识库、问答、文档、分块和索引管理接口。
/// </summary>
[ApiController]

[Route("api/admin/knowledge-bases")]
public sealed class AssistantKnowledgeController : ControllerBase
{
    private readonly IAssistantKnowledgeService _service;
    private readonly ITenantContext _tenantContext;
    private readonly IAdminPermissionService _permissions;

    public AssistantKnowledgeController(IAssistantKnowledgeService service, ITenantContext tenantContext, IAdminPermissionService permissions)
    {
        _service = service; _tenantContext = tenantContext; _permissions = permissions;
    }

    /// <summary>
    /// 校验读取权限后列出组织内的知识库。
    /// </summary>
    [HttpGet]
    public async Task<BizResult<IReadOnlyList<AssistantKnowledgeBaseDto>>> ListAsync(CancellationToken cancellationToken)
    {
        var scope = RequireScope();
        var actor = User.GetUserId();
        await RequireReadAsync(actor, scope.OrgId);
        return BizResult.BusinessSuccess(await _service.ListKnowledgeBasesAsync(actor, scope.TenantId, scope.OrgId, cancellationToken));
    }

    /// <summary>
    /// 校验写入权限后创建知识库。
    /// </summary>
    [HttpPost]
    public async Task<BizResult<AssistantKnowledgeBaseDto>> CreateAsync([FromBody] CreateKnowledgeBaseRequest request, CancellationToken cancellationToken)
    {
        var scope = RequireScope();
        var actor = User.GetUserId();
        await _permissions.RequireOrgPermissionAsync(actor, scope.OrgId, AdminPermissionConstants.OrgSettingsWrite);
        return BizResult.BusinessSuccess(await _service.CreateKnowledgeBaseAsync(actor, scope.TenantId, scope.OrgId,
            new CreateAssistantKnowledgeBaseCommand(request.Name, request.Description), cancellationToken));
    }

    /// <summary>
    /// 校验写入权限后删除指定知识库。
    /// </summary>
    [HttpDelete("{knowledgeBaseId}")]
    public async Task<BizResult<object>> DeleteAsync(string knowledgeBaseId, CancellationToken cancellationToken)
    {
        var scope = RequireScope();
        var actor = User.GetUserId();
        await _permissions.RequireOrgPermissionAsync(actor, scope.OrgId, AdminPermissionConstants.OrgSettingsWrite);
        await _service.DeleteKnowledgeBaseAsync(actor, scope.TenantId, scope.OrgId, knowledgeBaseId, cancellationToken);
        return BizResult.BusinessSuccess<object>(new { deleted = true });
    }

    /// <summary>
    /// 返回知识库当前分块策略。
    /// </summary>
    [HttpGet("{knowledgeBaseId}/chunking-policy")]
    public async Task<BizResult<AssistantKnowledgeChunkingPolicyDto>> GetChunkingPolicyAsync(string knowledgeBaseId, CancellationToken cancellationToken)
    {
        var scope = RequireScope();
        var actor = User.GetUserId();
        await RequireReadAsync(actor, scope.OrgId);
        return BizResult.BusinessSuccess(await _service.GetChunkingPolicyAsync(actor, scope.TenantId, scope.OrgId, knowledgeBaseId, cancellationToken));
    }

    /// <summary>
    /// 校验写入权限后保存文档分块策略。
    /// </summary>
    [HttpPut("{knowledgeBaseId}/chunking-policy")]
    public async Task<BizResult<AssistantKnowledgeChunkingPolicyDto>> SaveChunkingPolicyAsync(
        string knowledgeBaseId, [FromBody] SaveChunkingPolicyRequest request, CancellationToken cancellationToken)
    {
        var scope = RequireScope();
        var actor = User.GetUserId();
        await _permissions.RequireOrgPermissionAsync(actor, scope.OrgId, AdminPermissionConstants.OrgSettingsWrite);
        return BizResult.BusinessSuccess(await _service.SaveChunkingPolicyAsync(actor, scope.TenantId, scope.OrgId, knowledgeBaseId,
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
        var scope = RequireScope();
        var actor = User.GetUserId();
        await RequireReadAsync(actor, scope.OrgId);
        return BizResult.BusinessSuccess(await _service.GetEmbeddingProfileAsync(
            actor, scope.TenantId, scope.OrgId, knowledgeBaseId, cancellationToken));
    }

    /// <summary>
    /// 校验写入权限后更新嵌入模型选择。
    /// </summary>
    [HttpPut("{knowledgeBaseId}/embedding-profile")]
    public async Task<BizResult<AssistantKnowledgeEmbeddingProfileDto>> SaveEmbeddingProfileAsync(
        string knowledgeBaseId, [FromBody] SaveAssistantKnowledgeEmbeddingProfileCommand command,
        CancellationToken cancellationToken)
    {
        var scope = RequireScope();
        var actor = User.GetUserId();
        await _permissions.RequireOrgPermissionAsync(actor, scope.OrgId, AdminPermissionConstants.OrgSettingsWrite);
        return BizResult.BusinessSuccess(await _service.SaveEmbeddingProfileAsync(
            actor, scope.TenantId, scope.OrgId, knowledgeBaseId, command, cancellationToken));
    }

    /// <summary>
    /// 返回指定文档按分块策略切分的预览。
    /// </summary>
    [HttpPost("{knowledgeBaseId}/chunking-preview/{documentId}")]
    public async Task<BizResult<AssistantKnowledgeChunkPreviewDto>> PreviewChunkingAsync(
        string knowledgeBaseId, string documentId, [FromBody] SaveChunkingPolicyRequest request, CancellationToken cancellationToken)
    {
        var scope = RequireScope();
        var actor = User.GetUserId();
        await RequireReadAsync(actor, scope.OrgId);
        return BizResult.BusinessSuccess(await _service.PreviewChunkingAsync(actor, scope.TenantId, scope.OrgId, knowledgeBaseId, documentId,
            new SaveAssistantKnowledgeChunkingPolicyCommand(request.Mode, request.MaxChunkSize, request.MinChunkSize, request.OverlapSize,
                request.MaxHeadingLevel, request.PreserveHeadingPath, request.MergeShortChunks, false), cancellationToken));
    }

    /// <summary>
    /// 返回知识库固定问答列表。
    /// </summary>
    [HttpGet("{knowledgeBaseId}/qna")]
    public async Task<BizResult<IReadOnlyList<AssistantKnowledgeQnaDto>>> ListQnaAsync(string knowledgeBaseId, CancellationToken cancellationToken)
    {
        var scope = RequireScope();
        var actor = User.GetUserId();
        await RequireReadAsync(actor, scope.OrgId);
        return BizResult.BusinessSuccess(await _service.ListQnaAsync(actor, scope.TenantId, scope.OrgId, knowledgeBaseId, cancellationToken));
    }

    /// <summary>
    /// 校验写入权限后保存固定问答内容与设置。
    /// </summary>
    [HttpPut("{knowledgeBaseId}/qna")]
    public async Task<BizResult<AssistantKnowledgeQnaDto>> SaveQnaAsync(string knowledgeBaseId, [FromBody] SaveQnaRequest request, CancellationToken cancellationToken)
    {
        var scope = RequireScope();
        var actor = User.GetUserId();
        await _permissions.RequireOrgPermissionAsync(actor, scope.OrgId, AdminPermissionConstants.OrgSettingsWrite);
        return BizResult.BusinessSuccess(await _service.SaveQnaAsync(actor, scope.TenantId, scope.OrgId, knowledgeBaseId,
            new SaveAssistantKnowledgeQnaCommand(request.Id, request.Question, request.Answer, request.Enabled, request.SortOrder, request.ShowAsSuggestion), cancellationToken));
    }

    /// <summary>
    /// 校验写入权限后删除指定固定问答。
    /// </summary>
    [HttpDelete("{knowledgeBaseId}/qna/{qnaId}")]
    public async Task<BizResult<object>> DeleteQnaAsync(string knowledgeBaseId, string qnaId, CancellationToken cancellationToken)
    {
        var scope = RequireScope();
        var actor = User.GetUserId();
        await _permissions.RequireOrgPermissionAsync(actor, scope.OrgId, AdminPermissionConstants.OrgSettingsWrite);
        await _service.DeleteQnaAsync(actor, scope.TenantId, scope.OrgId, knowledgeBaseId, qnaId, cancellationToken);
        return BizResult.BusinessSuccess<object>(new { deleted = true });
    }

    /// <summary>
    /// 返回问题在知识库固定问答中的匹配结果。
    /// </summary>
    [HttpPost("{knowledgeBaseId}/qna/{qnaId}/test")]
    public async Task<BizResult<AssistantKnowledgeQnaTestResult>> TestQnaAsync(
        string knowledgeBaseId, string qnaId, [FromBody] TestQnaRequest request, CancellationToken cancellationToken)
    {
        var scope = RequireScope();
        var actor = User.GetUserId();
        await RequireReadAsync(actor, scope.OrgId);
        return BizResult.BusinessSuccess(await _service.TestQnaAsync(actor, scope.TenantId, scope.OrgId, knowledgeBaseId, qnaId,
            new TestAssistantKnowledgeQnaCommand(request.Question), cancellationToken));
    }

    /// <summary>
    /// 返回知识库文档列表及处理状态。
    /// </summary>
    [HttpGet("{knowledgeBaseId}/documents")]
    public async Task<BizResult<IReadOnlyList<AssistantKnowledgeDocumentDto>>> ListDocumentsAsync(
        string knowledgeBaseId, CancellationToken cancellationToken)
    {
        var scope = RequireScope();
        var actor = User.GetUserId();
        await RequireReadAsync(actor, scope.OrgId);
        return BizResult.BusinessSuccess(await _service.ListDocumentsAsync(actor, scope.TenantId, scope.OrgId, knowledgeBaseId, cancellationToken));
    }

    /// <summary>
    /// 读取 multipart 上传文件，并交由知识库服务保存私有文档。
    /// </summary>
    [HttpPost("{knowledgeBaseId}/documents")]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<BizResult<AssistantKnowledgeDocumentDto>> UploadDocumentAsync(
        string knowledgeBaseId, [FromForm] IFormFile file, CancellationToken cancellationToken)
    {
        var scope = RequireScope();
        var actor = User.GetUserId();
        await _permissions.RequireOrgPermissionAsync(actor, scope.OrgId, AdminPermissionConstants.OrgSettingsWrite);
        if (file is null)
            throw new BizResultException(BusinessCodes.BusinessException, "knowledge_document_file_required");
        var upload = new AssistantKnowledgeDocumentUpload(file.FileName, file.ContentType, file.Length, file.OpenReadStream());
        await using (upload.Content)
        {
            return BizResult.BusinessSuccess(await _service.UploadDocumentAsync(actor, scope.TenantId, scope.OrgId, knowledgeBaseId, upload, cancellationToken));
        }
    }

    /// <summary>
    /// 校验写入权限后请求处理文档及其索引。
    /// </summary>
    [HttpPost("{knowledgeBaseId}/documents/{documentId}/process")]
    public async Task<BizResult<AssistantKnowledgeDocumentProcessResult>> ProcessDocumentAsync(
        string knowledgeBaseId, string documentId, CancellationToken cancellationToken)
    {
        var scope = RequireScope();
        var actor = User.GetUserId();
        await _permissions.RequireOrgPermissionAsync(actor, scope.OrgId, AdminPermissionConstants.OrgSettingsWrite);
        return BizResult.BusinessSuccess(await _service.ProcessDocumentAsync(actor, scope.TenantId, scope.OrgId, knowledgeBaseId, documentId, cancellationToken));
    }

    /// <summary>
    /// 返回知识文档的正文、结构与片段预览。
    /// </summary>
    [HttpGet("{knowledgeBaseId}/documents/{documentId}/preview")]
    public async Task<BizResult<AssistantKnowledgeDocumentPreviewDto>> PreviewDocumentAsync(
        string knowledgeBaseId, string documentId, CancellationToken cancellationToken)
    {
        var scope = RequireScope();
        var actor = User.GetUserId();
        await RequireReadAsync(actor, scope.OrgId);
        return BizResult.BusinessSuccess(await _service.GetDocumentPreviewAsync(actor, scope.TenantId, scope.OrgId, knowledgeBaseId, documentId, cancellationToken));
    }

    /// <summary>
    /// 返回指定问题的知识片段检索结果。
    /// </summary>
    [HttpPost("{knowledgeBaseId}/retrieval-test")]
    public async Task<BizResult<AssistantKnowledgeRetrievalTestResult>> TestRetrievalAsync(
        string knowledgeBaseId, [FromBody] TestRetrievalRequest request, CancellationToken cancellationToken)
    {
        var scope = RequireScope();
        var actor = User.GetUserId();
        await RequireReadAsync(actor, scope.OrgId);
        return BizResult.BusinessSuccess(await _service.TestRetrievalAsync(actor, scope.TenantId, scope.OrgId, knowledgeBaseId,
            new TestAssistantKnowledgeRetrievalCommand(request.Question), cancellationToken));
    }

    /// <summary>
    /// 返回知识库索引任务列表。
    /// </summary>
    [HttpGet("{knowledgeBaseId}/index-jobs")]
    public async Task<BizResult<IReadOnlyList<AssistantKnowledgeIndexJobDto>>> ListIndexJobsAsync(
        string knowledgeBaseId, CancellationToken cancellationToken)
    {
        var scope = RequireScope();
        var actor = User.GetUserId();
        await RequireReadAsync(actor, scope.OrgId);
        return BizResult.BusinessSuccess(await _service.ListIndexJobsAsync(actor, scope.TenantId, scope.OrgId, knowledgeBaseId, cancellationToken));
    }

    private (string TenantId, string OrgId) RequireScope()
    {
        if (string.IsNullOrWhiteSpace(_tenantContext.TenantId) || string.IsNullOrWhiteSpace(_tenantContext.OrgId))
            throw new BizResultException(BusinessCodes.Forbidden, "knowledge_scope_required");
        return (_tenantContext.TenantId, _tenantContext.OrgId);
    }

    private async Task RequireReadAsync(string actor, string orgId)
    {
        if (!await _permissions.HasOrgPermissionAsync(actor, orgId, AdminPermissionConstants.OrgRead)
            && !await _permissions.HasOrgPermissionAsync(actor, orgId, AdminPermissionConstants.OrgSettingsWrite))
            throw new BizResultException(BusinessCodes.Forbidden, "knowledge_read_permission_required");
    }
}

/// <summary>
/// 创建知识库的 HTTP 请求体。
/// </summary>
public sealed class CreateKnowledgeBaseRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

/// <summary>
/// 修改知识库分块策略的 HTTP 请求体。
/// </summary>
public sealed class SaveChunkingPolicyRequest
{
    public AssistantKnowledgeChunkingMode Mode { get; set; } = AssistantKnowledgeChunkingMode.Hybrid;
    public int MaxChunkSize { get; set; } = 1200;
    public int MinChunkSize { get; set; } = 200;
    public int OverlapSize { get; set; } = 160;
    public int MaxHeadingLevel { get; set; } = 3;
    public bool PreserveHeadingPath { get; set; } = true;
    public bool MergeShortChunks { get; set; } = true;
    public bool ReprocessDocuments { get; set; }
}

/// <summary>
/// 保存固定问答内容和展示设置的 HTTP 请求体。
/// </summary>
public sealed class SaveQnaRequest
{
    public string? Id { get; set; }
    public string Question { get; set; } = string.Empty;
    public string? Answer { get; set; }
    public bool Enabled { get; set; } = true;
    public int SortOrder { get; set; }
    public bool ShowAsSuggestion { get; set; }
}

/// <summary>
/// 验证固定问答匹配的问题请求体。
/// </summary>
public sealed class TestQnaRequest
{
    public string Question { get; set; } = string.Empty;
}

/// <summary>
/// 验证知识检索效果的问题请求体。
/// </summary>
public sealed class TestRetrievalRequest
{
    public string Question { get; set; } = string.Empty;
}
