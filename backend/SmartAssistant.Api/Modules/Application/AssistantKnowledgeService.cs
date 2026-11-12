#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SmartAssistant.Extensions.Common;
using SmartAssistant.Extensions.DAL;
using SmartAssistant.Modules.Infrastructure;
using SmartAssistant.Modules.Contracts;
using SmartAssistant.Platform;
using SmartAssistant.Platform.Assets;
using Grpc.Core;
using Microsoft.AspNetCore.Http;

namespace SmartAssistant.Modules.Application;

/// <summary>
/// 管理知识库、固定问答、文档解析、分块和索引任务。
/// 索引处理保留文档版本与嵌入代次，供发布快照选择已完成的可用数据。
/// </summary>
public sealed class AssistantKnowledgeService : IAssistantKnowledgeService
{
    private readonly IAssistantKnowledgeRepository _repository;
    private readonly IAssistantKnowledgeEmbeddingProfileRepository _embeddingProfiles;
    private readonly IAssistantKnowledgeDocumentRepository _documentRepository;
    private readonly IAssistantPolicyService _policies;
    private readonly IDbContext _dbContext;
    private readonly AssetApplicationService _assetService;
    private readonly IAssistantDocumentParser _documentParser;
    private readonly IAssistantEmbeddingProvider _embeddingProvider;
    private readonly IAssistantVectorIndexStore _vectorIndexStore;

    public AssistantKnowledgeService(
        IAssistantKnowledgeRepository repository,
        IAssistantKnowledgeEmbeddingProfileRepository embeddingProfiles,
        IAssistantKnowledgeDocumentRepository documentRepository,
        IAssistantPolicyService policies,
        IDbContext dbContext,
        AssetApplicationService assetService,
        IAssistantDocumentParser documentParser,
        IAssistantEmbeddingProvider embeddingProvider,
        IAssistantVectorIndexStore vectorIndexStore)
    {
        _repository = repository;
        _embeddingProfiles = embeddingProfiles;
        _documentRepository = documentRepository;
        _policies = policies;
        _dbContext = dbContext;
        _assetService = assetService;
        _documentParser = documentParser;
        _embeddingProvider = embeddingProvider;
        _vectorIndexStore = vectorIndexStore;
    }

    /// <summary>
    /// 列出当前租户和组织范围内的知识库及问答、文档数量。
    /// </summary>
    public async Task<IReadOnlyList<AssistantKnowledgeBaseDto>> ListKnowledgeBasesAsync(
        string actorUserId, string tenantId, string orgId, CancellationToken cancellationToken = default)
    {
        var rows = await _repository.ListBasesAsync(tenantId, orgId, cancellationToken);
        var result = new List<AssistantKnowledgeBaseDto>(rows.Count);
        foreach (var row in rows)
        {
            var qna = await _repository.ListQnaAsync(tenantId, orgId, row.Id, cancellationToken);
            var documents = await _documentRepository.ListDocumentsAsync(tenantId, orgId, row.Id, cancellationToken);
            result.Add(new AssistantKnowledgeBaseDto(row.Id, row.Name, row.Description, qna.Count, documents.Count, row.UpdatedAt));
        }
        return result;
    }

    /// <summary>
    /// 校验名称与范围后创建知识库。
    /// </summary>
    public async Task<AssistantKnowledgeBaseDto> CreateKnowledgeBaseAsync(
        string actorUserId, string tenantId, string orgId, CreateAssistantKnowledgeBaseCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
            throw new BizResultException(BusinessCodes.BusinessException, "knowledge_base_name_required");
        var now = DateTime.UtcNow;
        var row = new AssistantKnowledgeRepository.AssistantKnowledgeBaseRow
        {
            Id = Guid.NewGuid().ToString("N"), TenantId = tenantId, OrgId = orgId,
            Name = command.Name.Trim(), Description = string.IsNullOrWhiteSpace(command.Description) ? null : command.Description.Trim(),
            CreatedAt = now, UpdatedAt = now, CreatedBy = actorUserId, UpdatedBy = actorUserId
        };
        await _repository.AddBaseAsync(row, cancellationToken);
        await _dbContext.CommitAsync(cancellationToken);
        return new AssistantKnowledgeBaseDto(row.Id, row.Name, row.Description, 0, 0, row.UpdatedAt);
    }

    /// <summary>
    /// 检查草稿和有效发布的知识库引用后执行删除，避免破坏已绑定的助手。
    /// </summary>
    public async Task DeleteKnowledgeBaseAsync(
        string actorUserId, string tenantId, string orgId, string knowledgeBaseId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
            throw new BizResultException(BusinessCodes.Forbidden, "knowledge_scope_required");
        _ = await RequireBaseAsync(tenantId, orgId, knowledgeBaseId, cancellationToken);
        if (await _repository.HasAssistantReferenceAsync(tenantId, orgId, knowledgeBaseId, cancellationToken))
            throw new BizResultException(BusinessCodes.Conflict, "knowledge_base_in_use_remove_assistant_binding");
        await _repository.DeleteBaseAsync(tenantId, orgId, knowledgeBaseId, actorUserId, DateTime.UtcNow, cancellationToken);
        await _dbContext.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// 读取知识库当前分块策略与是否存在待重处理文档的信息。
    /// </summary>
    public async Task<AssistantKnowledgeChunkingPolicyDto> GetChunkingPolicyAsync(
        string actorUserId, string tenantId, string orgId, string knowledgeBaseId, CancellationToken cancellationToken = default)
    {
        _ = actorUserId;
        _ = await RequireBaseAsync(tenantId, orgId, knowledgeBaseId, cancellationToken);
        var row = await _repository.GetChunkingPolicyAsync(tenantId, orgId, knowledgeBaseId, cancellationToken);
        var policy = row is null ? AssistantKnowledgeChunkingPolicy.Default : FromRow(row);
        var pending = await HasPendingDocumentsAsync(tenantId, orgId, knowledgeBaseId, policy, cancellationToken);
        return ToDto(policy, pending);
    }

    /// <summary>
    /// 读取目标与生效嵌入配置、文档完成进度和可选模型。
    /// </summary>
    public async Task<AssistantKnowledgeEmbeddingProfileDto> GetEmbeddingProfileAsync(
        string actorUserId, string tenantId, string orgId, string knowledgeBaseId, CancellationToken cancellationToken = default)
    {
        _ = actorUserId;
        _ = await RequireBaseAsync(tenantId, orgId, knowledgeBaseId, cancellationToken);
        var row = await _embeddingProfiles.GetAsync(tenantId, orgId, knowledgeBaseId, cancellationToken);
        var documents = await _documentRepository.ListDocumentsAsync(tenantId, orgId, knowledgeBaseId, cancellationToken);
        var jobs = row is null
            ? Array.Empty<AssistantKnowledgeDocumentRepository.AssistantKnowledgeIndexJobRow>()
            : (await _documentRepository.ListIndexJobsAsync(tenantId, orgId, knowledgeBaseId, cancellationToken)).ToArray();
        var readyCount = 0;
        if (row is not null)
        {
            foreach (var document in documents.Where(value => !value.IsDeleted))
            {
                var version = await _documentRepository.GetVersionAsync(tenantId, orgId, document.Id, document.CurrentVersionNo, cancellationToken);
                if (version is not null && HasSuccessfulJob(jobs, version.Id, row, row.TargetGeneration)) readyCount++;
            }
        }
        var targetAvailable = row is not null && await IsEmbeddingAvailableAsync(
            tenantId, orgId, ToExecutionProfile(row, row.TargetGeneration), cancellationToken);
        var activeAvailable = row?.ActiveGeneration > 0 && await IsEmbeddingAvailableAsync(
            tenantId, orgId, ToActiveExecutionProfile(row), cancellationToken);
        return ToEmbeddingProfileDto(row, documents.Count, readyCount,
            await ListAvailableEmbeddingModelsAsync(tenantId, orgId, cancellationToken), jobs,
            targetAvailable, activeAvailable);
    }

    private async Task<bool> IsEmbeddingAvailableAsync(string tenantId, string orgId,
        AssistantKnowledgeEmbeddingExecutionProfile profile, CancellationToken ct)
    {
        try
        {
            await _policies.EnsureEmbeddingAvailableAsync(profile, ct);
            return true;
        }
        catch (BizResultException ex) when (ex.Message == "knowledge_embedding_model_unavailable")
        {
            return false;
        }
    }

    /// <summary>
    /// 校验嵌入模型与预期修订号后保存目标配置，并维护相应索引代次。
    /// </summary>
    public async Task<AssistantKnowledgeEmbeddingProfileDto> SaveEmbeddingProfileAsync(
        string actorUserId, string tenantId, string orgId, string knowledgeBaseId,
        SaveAssistantKnowledgeEmbeddingProfileCommand command, CancellationToken cancellationToken = default)
    {
        _ = await RequireBaseAsync(tenantId, orgId, knowledgeBaseId, cancellationToken);
        if (command.ExpectedRevision < 0 || command.ExpectedRevision == long.MaxValue
            || command.ProviderRevision < 1 || command.Dimension is < 1 or > 32768
            || string.IsNullOrWhiteSpace(command.Model))
            throw new BizResultException(BusinessCodes.BusinessException, "knowledge_embedding_profile_invalid");

        var options = await ListAvailableEmbeddingModelsAsync(tenantId, orgId, cancellationToken);
        var option = options.FirstOrDefault(value => value.ProviderId == command.ProviderId
            && value.ProviderRevision == command.ProviderRevision && value.Model == command.Model
            && value.Dimension == command.Dimension);
        if (option is null)
            throw new BizResultException(BusinessCodes.Forbidden, "knowledge_embedding_model_unavailable");

        var current = await _embeddingProfiles.GetAsync(tenantId, orgId, knowledgeBaseId, cancellationToken);
        if (current is null ? command.ExpectedRevision != 0 : current.Revision != command.ExpectedRevision)
            throw new BizResultException(BusinessCodes.Conflict, "knowledge_embedding_profile_revision_conflict");
        if (current is not null && current.ProviderId == command.ProviderId && current.ProviderRevision == command.ProviderRevision
            && current.Model == command.Model && current.Dimension == command.Dimension)
            return await GetEmbeddingProfileAsync(actorUserId, tenantId, orgId, knowledgeBaseId, cancellationToken);

        var documents = await _documentRepository.ListDocumentsAsync(tenantId, orgId, knowledgeBaseId, cancellationToken);
        var now = DateTime.UtcNow;
        var row = current ?? new AssistantKnowledgeEmbeddingProfileRepository.AssistantKnowledgeEmbeddingProfileRow
        {
            Id = Guid.NewGuid().ToString("N"), TenantId = tenantId, OrgId = orgId, KnowledgeBaseId = knowledgeBaseId,
            CreatedAt = now, CreatedBy = actorUserId
        };
        row.ProviderId = command.ProviderId;
        row.ProviderRevision = command.ProviderRevision;
        row.Model = command.Model.Trim();
        row.Dimension = command.Dimension;
        row.TargetGeneration = current is null ? 1 : checked(current.TargetGeneration + 1);
        row.Revision = command.ExpectedRevision + 1;
        row.Status = documents.Count == 0 ? "ready" : "pending_rebuild";
        row.LastErrorCode = null;
        row.UpdatedAt = now;
        row.UpdatedBy = actorUserId;
        if (documents.Count == 0)
        {
            row.ActiveProviderId = row.ProviderId;
            row.ActiveProviderRevision = row.ProviderRevision;
            row.ActiveModel = row.Model;
            row.ActiveDimension = row.Dimension;
            row.ActiveProfileRevision = row.Revision;
            row.ActiveGeneration = row.TargetGeneration;
        }

        if (current is null) await _embeddingProfiles.AddAsync(row, cancellationToken);
        else await _embeddingProfiles.SaveAsync(row, command.ExpectedRevision, cancellationToken);
        await _dbContext.CommitAsync(cancellationToken);
        return await GetEmbeddingProfileAsync(actorUserId, tenantId, orgId, knowledgeBaseId, cancellationToken);
    }

    /// <summary>
    /// 校验并保存分块参数，按请求决定是否为现有文档启动重新处理。
    /// </summary>
    public async Task<AssistantKnowledgeChunkingPolicyDto> SaveChunkingPolicyAsync(
        string actorUserId, string tenantId, string orgId, string knowledgeBaseId,
        SaveAssistantKnowledgeChunkingPolicyCommand command, CancellationToken cancellationToken = default)
    {
        _ = await RequireBaseAsync(tenantId, orgId, knowledgeBaseId, cancellationToken);
        var current = await _repository.GetChunkingPolicyAsync(tenantId, orgId, knowledgeBaseId, cancellationToken);
        var previous = current is null ? AssistantKnowledgeChunkingPolicy.Default : FromRow(current);
        var policy = ToPolicy(command, previous.Revision + 1);
        ValidatePolicy(policy);
        var strategyChanged = policy.StrategyKey != previous.StrategyKey;
        if (current is not null && policy.StrategyKey == previous.StrategyKey)
            policy = previous; // An unchanged save is not a new processing strategy.
        var now = DateTime.UtcNow;
        var row = current ?? new AssistantKnowledgeRepository.AssistantKnowledgeChunkingPolicyRow
        {
            Id = Guid.NewGuid().ToString("N"), TenantId = tenantId, OrgId = orgId, KnowledgeBaseId = knowledgeBaseId,
            CreatedAt = now, CreatedBy = actorUserId
        };
        Apply(row, policy, actorUserId, now);
        if (current is null) await _repository.AddChunkingPolicyAsync(row, cancellationToken);
        else await _repository.SaveChunkingPolicyAsync(row, cancellationToken);
        if (strategyChanged)
        {
            var embeddingProfile = await _embeddingProfiles.GetAsync(tenantId, orgId, knowledgeBaseId, cancellationToken);
            if (embeddingProfile is not null)
            {
                var expectedRevision = embeddingProfile.Revision;
                embeddingProfile.Revision = checked(expectedRevision + 1);
                embeddingProfile.TargetGeneration = checked(embeddingProfile.TargetGeneration + 1);
                embeddingProfile.Status = "pending_rebuild";
                embeddingProfile.LastErrorCode = null;
                embeddingProfile.UpdatedAt = now;
                embeddingProfile.UpdatedBy = actorUserId;
                if ((await _documentRepository.ListDocumentsAsync(tenantId, orgId, knowledgeBaseId, cancellationToken)).Count == 0)
                {
                    embeddingProfile.ActiveProviderId = embeddingProfile.ProviderId;
                    embeddingProfile.ActiveProviderRevision = embeddingProfile.ProviderRevision;
                    embeddingProfile.ActiveModel = embeddingProfile.Model;
                    embeddingProfile.ActiveDimension = embeddingProfile.Dimension;
                    embeddingProfile.ActiveProfileRevision = embeddingProfile.Revision;
                    embeddingProfile.ActiveGeneration = embeddingProfile.TargetGeneration;
                    embeddingProfile.Status = "ready";
                }
                await _embeddingProfiles.SaveAsync(embeddingProfile, expectedRevision, cancellationToken);
            }
        }
        await _dbContext.CommitAsync(cancellationToken);
        if (command.ReprocessDocuments)
        {
            var documents = await _documentRepository.ListDocumentsAsync(tenantId, orgId, knowledgeBaseId, cancellationToken);
            foreach (var document in documents.Where(x => !x.IsDeleted))
                await ProcessDocumentAsync(actorUserId, tenantId, orgId, knowledgeBaseId, document.Id, cancellationToken);
        }
        var pending = await HasPendingDocumentsAsync(tenantId, orgId, knowledgeBaseId, policy, cancellationToken);
        return ToDto(policy, pending);
    }

    /// <summary>
    /// 按当前分块策略预览文档切分结果。
    /// </summary>
    public async Task<AssistantKnowledgeChunkPreviewDto> PreviewChunkingAsync(
        string actorUserId, string tenantId, string orgId, string knowledgeBaseId, string documentId,
        SaveAssistantKnowledgeChunkingPolicyCommand command, CancellationToken cancellationToken = default)
    {
        _ = await RequireBaseAsync(tenantId, orgId, knowledgeBaseId, cancellationToken);
        var document = await _documentRepository.GetDocumentAsync(tenantId, orgId, knowledgeBaseId, documentId, cancellationToken)
            ?? throw new BizResultException(BusinessCodes.NotFound, "knowledge_document_not_found");
        var asset = await _assetService.GetContentAsync(document.AssetId, tenantId, orgId, new AssetAclContext(true, actorUserId, orgId, null), cancellationToken);
        await using var content = asset.Content;
        var parsed = await _documentParser.ParseAsync(asset.ContentType, document.FileName, content, cancellationToken);
        var policy = ToPolicy(command, 1);
        ValidatePolicy(policy);
        var pieces = AssistantKnowledgeChunker.Split(parsed.Text, policy);
        var segments = pieces.Take(100).Select((text, index) =>
            new AssistantKnowledgeDocumentPreviewSegment($"preview-{index}", index, text, $"{document.FileName} · {index + 1}", false)).ToArray();
        return new AssistantKnowledgeChunkPreviewDto(document.Id, pieces.Count, segments);
    }

    /// <summary>
    /// 读取知识库固定问答列表。
    /// </summary>
    public async Task<IReadOnlyList<AssistantKnowledgeQnaDto>> ListQnaAsync(
        string actorUserId, string tenantId, string orgId, string knowledgeBaseId, CancellationToken cancellationToken = default)
    {
        _ = await RequireBaseAsync(tenantId, orgId, knowledgeBaseId, cancellationToken);
        return (await _repository.ListQnaAsync(tenantId, orgId, knowledgeBaseId, cancellationToken))
            .Select(Map).ToArray();
    }

    /// <summary>
    /// 标准化问题并保存问答内容、排序、启用状态和推荐设置。
    /// </summary>
    public async Task<AssistantKnowledgeQnaDto> SaveQnaAsync(
        string actorUserId, string tenantId, string orgId, string knowledgeBaseId,
        SaveAssistantKnowledgeQnaCommand command, CancellationToken cancellationToken = default)
    {
        _ = await RequireBaseAsync(tenantId, orgId, knowledgeBaseId, cancellationToken);
        var question = command.Question?.Trim() ?? string.Empty;
        if (question.Length == 0)
            throw new BizResultException(BusinessCodes.BusinessException, "knowledge_qna_question_required");
        if (question.Length > 500)
            throw new BizResultException(BusinessCodes.BusinessException, "knowledge_qna_question_too_long");
        var normalized = NormalizeQuestion(question);
        var duplicate = await _repository.GetQnaByNormalizedQuestionAsync(tenantId, orgId, knowledgeBaseId, normalized, command.Id, cancellationToken);
        if (duplicate is not null)
            throw new BizResultException(BusinessCodes.Conflict, "knowledge_qna_question_duplicate");

        var now = DateTime.UtcNow;
        AssistantKnowledgeRepository.AssistantKnowledgeQnaRow row;
        if (!string.IsNullOrWhiteSpace(command.Id))
        {
            row = await _repository.GetQnaAsync(tenantId, orgId, knowledgeBaseId, command.Id, cancellationToken)
                ?? throw new BizResultException(BusinessCodes.NotFound, "knowledge_qna_not_found");
            row.Question = question; row.NormalizedQuestion = normalized;
            row.Answer = string.IsNullOrWhiteSpace(command.Answer) ? null : command.Answer.Trim();
            row.Enabled = command.Enabled; row.SortOrder = Math.Max(0, command.SortOrder);
            row.ShowAsSuggestion = command.ShowAsSuggestion;
            row.UpdatedAt = now; row.UpdatedBy = actorUserId;
            await _repository.SaveQnaAsync(row, cancellationToken);
        }
        else
        {
            row = new AssistantKnowledgeRepository.AssistantKnowledgeQnaRow
            {
                Id = Guid.NewGuid().ToString("N"), KnowledgeBaseId = knowledgeBaseId, TenantId = tenantId, OrgId = orgId,
                Question = question, NormalizedQuestion = normalized,
                Answer = string.IsNullOrWhiteSpace(command.Answer) ? null : command.Answer.Trim(),
                Enabled = command.Enabled, SortOrder = Math.Max(0, command.SortOrder),
                ShowAsSuggestion = command.ShowAsSuggestion,
                CreatedAt = now, UpdatedAt = now, CreatedBy = actorUserId, UpdatedBy = actorUserId
            };
            await _repository.AddQnaAsync(row, cancellationToken);
        }
        await _dbContext.CommitAsync(cancellationToken);
        return Map(row);
    }

    /// <summary>
    /// 在所属知识库范围内删除指定固定问答。
    /// </summary>
    public async Task DeleteQnaAsync(
        string actorUserId, string tenantId, string orgId, string knowledgeBaseId, string qnaId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
            throw new BizResultException(BusinessCodes.Forbidden, "knowledge_scope_required");
        _ = await RequireBaseAsync(tenantId, orgId, knowledgeBaseId, cancellationToken);
        _ = await _repository.GetQnaAsync(tenantId, orgId, knowledgeBaseId, qnaId, cancellationToken)
            ?? throw new BizResultException(BusinessCodes.NotFound, "knowledge_qna_not_found");
        await _repository.DeleteQnaAsync(tenantId, orgId, knowledgeBaseId, qnaId, actorUserId, DateTime.UtcNow, cancellationToken);
        await _dbContext.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// 按固定问答匹配规则验证问题是否命中，返回直接回答结果。
    /// </summary>
    public async Task<AssistantKnowledgeQnaTestResult> TestQnaAsync(
        string actorUserId, string tenantId, string orgId, string knowledgeBaseId, string qnaId,
        TestAssistantKnowledgeQnaCommand command, CancellationToken cancellationToken = default)
    {
        _ = actorUserId;
        _ = await RequireBaseAsync(tenantId, orgId, knowledgeBaseId, cancellationToken);
        var row = await _repository.GetQnaAsync(tenantId, orgId, knowledgeBaseId, qnaId, cancellationToken)
            ?? throw new BizResultException(BusinessCodes.NotFound, "knowledge_qna_not_found");
        var question = command.Question?.Trim() ?? string.Empty;
        if (question.Length == 0 || question.Length > 500)
            throw new BizResultException(BusinessCodes.BusinessException, "knowledge_qna_question_invalid");
        var matched = NormalizeQuestion(question) == row.NormalizedQuestion;
        var direct = matched && row.Enabled && !string.IsNullOrWhiteSpace(row.Answer);
        return new AssistantKnowledgeQnaTestResult(question, matched, direct ? row.Answer : null,
            direct ? "direct" : "generated", false);
    }

    /// <summary>
    /// 列出知识库文档及其处理状态。
    /// </summary>
    public async Task<IReadOnlyList<AssistantKnowledgeDocumentDto>> ListDocumentsAsync(
        string actorUserId, string tenantId, string orgId, string knowledgeBaseId,
        CancellationToken cancellationToken = default)
    {
        _ = await RequireBaseAsync(tenantId, orgId, knowledgeBaseId, cancellationToken);
        var documents = await _documentRepository.ListDocumentsAsync(tenantId, orgId, knowledgeBaseId, cancellationToken);
        var jobs = await _documentRepository.ListIndexJobsAsync(tenantId, orgId, knowledgeBaseId, cancellationToken);
        var profile = await _embeddingProfiles.GetAsync(tenantId, orgId, knowledgeBaseId, cancellationToken);
        var result = new List<AssistantKnowledgeDocumentDto>(documents.Count);
        foreach (var document in documents)
        {
            var version = await _documentRepository.GetVersionAsync(
                tenantId, orgId, document.Id, document.CurrentVersionNo, cancellationToken);
            var documentJobs = jobs.Where(job => job.DocumentId == document.Id
                && (version is null || job.DocumentVersionId == version.Id)).ToArray();
            var relevant = profile is null
                ? documentJobs.OrderByDescending(job => job.CreatedAt).FirstOrDefault()
                : documentJobs.Where(job => MatchesTargetProfile(job, profile))
                    .OrderByDescending(job => job.CreatedAt).FirstOrDefault()
                    ?? documentJobs.Where(job => MatchesActiveProfile(job, profile))
                        .OrderByDescending(job => job.CreatedAt).FirstOrDefault();
            result.Add(Map(document, relevant, profile));
        }
        return result;
    }

    /// <summary>
    /// 将文档保存为私有资产，并创建知识文档记录。
    /// </summary>
    public async Task<AssistantKnowledgeDocumentDto> UploadDocumentAsync(
        string actorUserId, string tenantId, string orgId, string knowledgeBaseId,
        AssistantKnowledgeDocumentUpload file, CancellationToken cancellationToken = default)
    {
        _ = await RequireBaseAsync(tenantId, orgId, knowledgeBaseId, cancellationToken);
        if (file.SizeBytes <= 0 || file.SizeBytes > 50 * 1024 * 1024)
            throw new BizResultException(BusinessCodes.BusinessException, "knowledge_document_size_invalid");
        if (string.IsNullOrWhiteSpace(file.FileName))
            throw new BizResultException(BusinessCodes.BusinessException, "knowledge_document_file_required");

        await using var buffer = new MemoryStream();
        await file.Content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length == 0)
            throw new BizResultException(BusinessCodes.BusinessException, "knowledge_document_file_required");
        buffer.Position = 0;
        /*
         * FormFile 的 ContentType 依赖 Headers，先初始化请求头再赋值，
         * 否则上传会在进入资产校验逻辑前抛出异常。
         */
        var formFile = new FormFile(buffer, 0, buffer.Length, "file", Path.GetFileName(file.FileName))
        {
            Headers = new HeaderDictionary()
        };
        formFile.ContentType = file.ContentType;
        var asset = await _assetService.UploadAsync(
            formFile, tenantId, orgId, null, "assistant-document", actorUserId, AssetCategoryRegistry.VisibilityPrivate, cancellationToken);

        var now = DateTime.UtcNow;
        var document = new AssistantKnowledgeDocumentRepository.AssistantKnowledgeDocumentRow
        {
            Id = Guid.NewGuid().ToString("N"), TenantId = tenantId, OrgId = orgId, KnowledgeBaseId = knowledgeBaseId,
            AssetId = asset.AssetId, FileName = asset.FileName, ContentType = asset.ContentType, SizeBytes = asset.SizeBytes,
            CurrentVersionNo = 1, Status = "uploaded", CreatedAt = now, CreatedBy = actorUserId, UpdatedAt = now, UpdatedBy = actorUserId
        };
        var policyRow = await _repository.GetChunkingPolicyAsync(tenantId, orgId, knowledgeBaseId, cancellationToken);
        var uploadPolicy = policyRow is null ? AssistantKnowledgeChunkingPolicy.Default : FromRow(policyRow);
        var embeddingProfile = await _embeddingProfiles.GetAsync(tenantId, orgId, knowledgeBaseId, cancellationToken);
        if (embeddingProfile is not null)
        {
            var expectedRevision = embeddingProfile.Revision;
            embeddingProfile.Revision = checked(expectedRevision + 1);
            if (embeddingProfile.ActiveGeneration == embeddingProfile.TargetGeneration)
                embeddingProfile.TargetGeneration = checked(embeddingProfile.TargetGeneration + 1);
            embeddingProfile.Status = "pending_rebuild";
            embeddingProfile.LastErrorCode = null;
            embeddingProfile.UpdatedAt = now;
            embeddingProfile.UpdatedBy = actorUserId;
            await _embeddingProfiles.SaveAsync(embeddingProfile, expectedRevision, cancellationToken);
        }
        var version = new AssistantKnowledgeDocumentRepository.AssistantKnowledgeDocumentVersionRow
        {
            Id = Guid.NewGuid().ToString("N"), TenantId = tenantId, OrgId = orgId, DocumentId = document.Id,
            VersionNo = 1, ParserVersion = "pending", ChunkStrategy = uploadPolicy.StrategyKey,
            ChunkPolicyJson = JsonSerializer.Serialize(uploadPolicy),
            Status = "uploaded", IndexStatus = "queued", CreatedAt = now, CreatedBy = actorUserId
        };
        var job = new AssistantKnowledgeDocumentRepository.AssistantKnowledgeIndexJobRow
        {
            Id = Guid.NewGuid().ToString("N"), TenantId = tenantId, OrgId = orgId, KnowledgeBaseId = knowledgeBaseId,
            DocumentId = document.Id, DocumentVersionId = version.Id, VersionNo = 1, Status = "queued", CreatedAt = now,
            Generation = embeddingProfile?.TargetGeneration, EmbeddingProviderId = embeddingProfile?.ProviderId,
            EmbeddingProviderRevision = embeddingProfile?.ProviderRevision, EmbeddingModel = embeddingProfile?.Model,
            EmbeddingDimension = embeddingProfile?.Dimension, ChunkPolicyRevision = uploadPolicy.Revision,
            ChunkPolicyJson = JsonSerializer.Serialize(uploadPolicy)
        };
        await _documentRepository.AddDocumentAsync(document, cancellationToken);
        await _documentRepository.AddVersionAsync(version, cancellationToken);
        await _documentRepository.AddIndexJobAsync(job, cancellationToken);
        await _dbContext.CommitAsync(cancellationToken);
        return Map(document, job);
    }

    /// <summary>
    /// 为指定文档执行解析、分块和向量索引流程，保存版本及任务状态。
    /// </summary>
    public async Task<AssistantKnowledgeDocumentProcessResult> ProcessDocumentAsync(
        string actorUserId, string tenantId, string orgId, string knowledgeBaseId, string documentId,
        CancellationToken cancellationToken = default)
    {
        _ = await RequireBaseAsync(tenantId, orgId, knowledgeBaseId, cancellationToken);
        var document = await _documentRepository.GetDocumentAsync(tenantId, orgId, knowledgeBaseId, documentId, cancellationToken)
            ?? throw new BizResultException(BusinessCodes.NotFound, "knowledge_document_not_found");
        var version = await _documentRepository.GetVersionAsync(tenantId, orgId, documentId, document.CurrentVersionNo, cancellationToken)
            ?? throw new BizResultException(BusinessCodes.NotFound, "knowledge_document_version_not_found");
        var policyRow = await _repository.GetChunkingPolicyAsync(tenantId, orgId, knowledgeBaseId, cancellationToken);
        var policy = policyRow is null ? AssistantKnowledgeChunkingPolicy.Default : FromRow(policyRow);
        ValidatePolicy(policy);
        var jobs = await _documentRepository.ListIndexJobsAsync(tenantId, orgId, knowledgeBaseId, cancellationToken);
        var embeddingProfile = await _embeddingProfiles.GetAsync(tenantId, orgId, knowledgeBaseId, cancellationToken);
        var storedChunks = await _documentRepository.ListChunksAsync(tenantId, orgId, version.Id, cancellationToken);
        if ((version.Status == "ready" || storedChunks.Count > 0)
            && !string.Equals(version.ChunkStrategy, policy.StrategyKey, StringComparison.Ordinal))
        {
            var nowVersion = DateTime.UtcNow;
            var nextVersion = new AssistantKnowledgeDocumentRepository.AssistantKnowledgeDocumentVersionRow
            {
                Id = Guid.NewGuid().ToString("N"), TenantId = tenantId, OrgId = orgId, DocumentId = documentId,
                VersionNo = version.VersionNo + 1, ParserVersion = "pending", ChunkStrategy = policy.StrategyKey,
                ChunkPolicyJson = JsonSerializer.Serialize(policy), Status = "uploaded", IndexStatus = "queued",
                CreatedAt = nowVersion, CreatedBy = actorUserId
            };
            var nextJob = new AssistantKnowledgeDocumentRepository.AssistantKnowledgeIndexJobRow
            {
                Id = Guid.NewGuid().ToString("N"), TenantId = tenantId, OrgId = orgId, KnowledgeBaseId = knowledgeBaseId,
                DocumentId = documentId, DocumentVersionId = nextVersion.Id, VersionNo = nextVersion.VersionNo,
                Status = "queued", CreatedAt = nowVersion, Generation = embeddingProfile?.TargetGeneration,
                EmbeddingProviderId = embeddingProfile?.ProviderId, EmbeddingProviderRevision = embeddingProfile?.ProviderRevision,
                EmbeddingModel = embeddingProfile?.Model, EmbeddingDimension = embeddingProfile?.Dimension,
                ChunkPolicyRevision = policyRow?.Revision ?? policy.Revision, ChunkPolicyJson = JsonSerializer.Serialize(policy)
            };
            document.CurrentVersionNo = nextVersion.VersionNo;
            await _documentRepository.AddVersionAsync(nextVersion, cancellationToken);
            await _documentRepository.AddIndexJobAsync(nextJob, cancellationToken);
            await _documentRepository.UpdateDocumentAsync(document, cancellationToken);
            await _dbContext.CommitAsync(cancellationToken);
            version = nextVersion;
            jobs = jobs.Append(nextJob).ToArray();
        }

        var generation = embeddingProfile?.TargetGeneration;
        var providerId = embeddingProfile?.ProviderId;
        var providerRevision = embeddingProfile?.ProviderRevision;
        var model = embeddingProfile?.Model;
        var dimension = embeddingProfile?.Dimension;
        var matchingJobs = jobs.Where(value => value.DocumentVersionId == version.Id
            && value.Generation == generation
            && value.EmbeddingProviderId == providerId
            && value.EmbeddingProviderRevision == providerRevision
            && value.EmbeddingModel == model
            && value.EmbeddingDimension == dimension)
            .OrderByDescending(value => value.CreatedAt).ToArray();
        var job = matchingJobs.FirstOrDefault();
        if (embeddingProfile is null && version.Status == "ready" && version.IndexStatus == "needs_embedding_config"
            && version.ChunkStrategy == policy.StrategyKey && job?.Status == "needs_embedding_config")
            return new AssistantKnowledgeDocumentProcessResult(Map(document, job), Map(job));
        if (job?.Status == "succeeded")
        {
            await TryPromoteGenerationAsync(tenantId, orgId, knowledgeBaseId, cancellationToken);
            var latestProfile = await _embeddingProfiles.GetAsync(tenantId, orgId, knowledgeBaseId, cancellationToken);
            return new AssistantKnowledgeDocumentProcessResult(Map(document, job, latestProfile), Map(job));
        }
        if (job?.Status == "processing")
            return new AssistantKnowledgeDocumentProcessResult(Map(document, job), Map(job));
        if (job is null || job.Status is "failed" or "needs_embedding_config" or "succeeded")
        {
            job = new AssistantKnowledgeDocumentRepository.AssistantKnowledgeIndexJobRow
            {
                Id = Guid.NewGuid().ToString("N"), TenantId = tenantId, OrgId = orgId, KnowledgeBaseId = knowledgeBaseId,
                DocumentId = documentId, DocumentVersionId = version.Id, VersionNo = version.VersionNo,
                Status = "queued", AttemptCount = matchingJobs.Select(value => value.AttemptCount).DefaultIfEmpty(0).Max(),
                CreatedAt = DateTime.UtcNow, Generation = generation, EmbeddingProviderId = providerId,
                EmbeddingProviderRevision = providerRevision, EmbeddingModel = model, EmbeddingDimension = dimension,
                ChunkPolicyRevision = policyRow?.Revision ?? policy.Revision, ChunkPolicyJson = JsonSerializer.Serialize(policy)
            };
            await _documentRepository.AddIndexJobAsync(job, cancellationToken);
        }

        var hasActiveGeneration = embeddingProfile is not null && embeddingProfile.ActiveGeneration > 0
            && jobs.Any(value => value.DocumentVersionId == version.Id && value.Status == "succeeded"
                && value.Generation == embeddingProfile.ActiveGeneration
                && value.EmbeddingProviderId == embeddingProfile.ActiveProviderId
                && value.EmbeddingProviderRevision == embeddingProfile.ActiveProviderRevision
                && value.EmbeddingModel == embeddingProfile.ActiveModel
                && value.EmbeddingDimension == embeddingProfile.ActiveDimension);
        var now = DateTime.UtcNow;
        if (version.Status != "ready")
        {
            document.Status = "processing";
            version.Status = "processing";
        }
        if (!hasActiveGeneration)
            version.IndexStatus = embeddingProfile is null ? "needs_embedding_config" : "processing";
        document.LastErrorCode = null;
        document.UpdatedAt = now;
        document.UpdatedBy = actorUserId;
        job.Status = "processing";
        job.ChunkPolicyRevision = policy.Revision;
        job.ChunkPolicyJson = JsonSerializer.Serialize(policy);
        job.AttemptCount++;
        job.StartedAt = now;
        job.ErrorCode = null;
        job.CompletedAt = null;
        await _documentRepository.UpdateDocumentAsync(document, cancellationToken);
        await _documentRepository.UpdateVersionAsync(version, cancellationToken);
        await _documentRepository.UpdateIndexJobAsync(job, cancellationToken);
        await _dbContext.CommitAsync(cancellationToken);

        var indexCommitted = false;
        try
        {
            var assetContent = await _assetService.GetContentAsync(
                document.AssetId, tenantId, orgId,
                new AssetAclContext(true, actorUserId, orgId, null), cancellationToken);
            await using var content = assetContent.Content;
            var parsed = await _documentParser.ParseAsync(assetContent.ContentType, document.FileName, content, cancellationToken);
            version.ContentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(parsed.Text))).ToLowerInvariant();
            var chunks = BuildChunks(parsed.Text, tenantId, orgId, version.Id, actorUserId, policy);
            version.ChunkStrategy = policy.StrategyKey;
            version.ChunkPolicyJson = JsonSerializer.Serialize(policy);
            var existing = await _documentRepository.ListChunksAsync(tenantId, orgId, version.Id, cancellationToken);
            if (existing.Count == 0)
            {
                await _documentRepository.AddChunksAsync(chunks, cancellationToken);
            }

            var rows = existing.Count == 0 ? chunks : existing;
            job.ParserVersion = parsed.ParserVersion;
            job.ContentHash = version.ContentHash;
            job.ChunkPolicyRevision = policyRow?.Revision ?? policy.Revision;
            job.ChunkPolicyJson = version.ChunkPolicyJson;
            version.ParserVersion = parsed.ParserVersion;
            version.ChunkCount = rows.Count;
            version.Status = "ready";
            document.Status = "ready";
            document.LastErrorCode = null;

            if (embeddingProfile is null)
            {
                version.IndexStatus = "needs_embedding_config";
                version.IndexedAt = null;
                version.ErrorCode = null;
                job.Status = "needs_embedding_config";
                job.ChunkCount = rows.Count;
                job.CompletedAt = DateTime.UtcNow;
                document.UpdatedAt = DateTime.UtcNow;
                document.UpdatedBy = actorUserId;
                await _documentRepository.UpdateDocumentAsync(document, cancellationToken);
                await _documentRepository.UpdateVersionAsync(version, cancellationToken);
                await _documentRepository.UpdateIndexJobAsync(job, cancellationToken);
                await _dbContext.CommitAsync(cancellationToken);
                return new AssistantKnowledgeDocumentProcessResult(Map(document, job), Map(job));
            }

            var executionProfile = ToExecutionProfile(embeddingProfile, generation!.Value);
            await _policies.EnsureEmbeddingAvailableAsync(executionProfile, cancellationToken);
            var latestProfile = await _embeddingProfiles.GetAsync(tenantId, orgId, knowledgeBaseId, cancellationToken);
            if (latestProfile is null || latestProfile.Revision != executionProfile.ProfileRevision
                || latestProfile.TargetGeneration != executionProfile.Generation
                || latestProfile.ProviderId != executionProfile.ProviderId
                || latestProfile.ProviderRevision != executionProfile.ProviderRevision
                || latestProfile.Model != executionProfile.Model || latestProfile.Dimension != executionProfile.Dimension)
                throw new InvalidOperationException("assistant_embedding_profile_changed");
            var embeddings = await _embeddingProvider.EmbedAsync(
                rows.Select(x => x.Content).ToArray(), tenantId, orgId, executionProfile, cancellationToken);
            if (embeddings.ProfileId != executionProfile.ProviderProfileRef || embeddings.Dimension != executionProfile.Dimension
                || embeddings.Vectors.Count != rows.Count
                || embeddings.Vectors.Any(vector => !AssistantEmbeddingValidation.IsValid(vector, executionProfile.Dimension)))
                throw new InvalidOperationException("assistant_embedding_response_invalid");
            var vectorChunks = rows.Zip(embeddings.Vectors, (chunk, vector) => new AssistantVectorChunk(
                chunk.Id, chunk.ContentHash, document.Id, version.VersionNo, chunk.ChunkIndex, chunk.Content, vector)).ToArray();
            var collection = AssistantKnowledgeIndexNaming.Collection(tenantId, orgId, knowledgeBaseId,
                EmbeddingCollectionProfileId(executionProfile), executionProfile.Generation);
            await _vectorIndexStore.UpsertAsync(collection, executionProfile.Dimension, vectorChunks, cancellationToken);
            var readback = await _vectorIndexStore.ReadBackAsync(collection, vectorChunks, cancellationToken);
            var readbackByChunk = readback.GroupBy(value => value.ChunkId, StringComparer.Ordinal)
                .Where(group => group.Count() == 1)
                .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
            if (readback.Count != vectorChunks.Length || readbackByChunk.Count != vectorChunks.Length
                || vectorChunks.Any(chunk => !readbackByChunk.TryGetValue(chunk.ChunkId, out var point)
                    || point.ContentHash != chunk.ContentHash
                    || point.PointId != AssistantKnowledgeIndexNaming.PointId(chunk.ChunkId)))
                throw new InvalidOperationException("assistant_vector_index_readback_incomplete");

            foreach (var row in rows)
                row.PointId = AssistantKnowledgeIndexNaming.PointId(row.Id);
            await _documentRepository.UpdateChunkPointIdsAsync(rows, cancellationToken);

            version.IndexedAt = DateTime.UtcNow;
            version.ErrorCode = null;
            version.IndexStatus = "ready";
            document.UpdatedAt = DateTime.UtcNow;
            document.UpdatedBy = actorUserId;
            job.Status = "succeeded";
            job.ChunkCount = rows.Count;
            job.CompletedAt = DateTime.UtcNow;
            await _documentRepository.UpdateDocumentAsync(document, cancellationToken);
            await _documentRepository.UpdateVersionAsync(version, cancellationToken);
            await _documentRepository.UpdateIndexJobAsync(job, cancellationToken);
            await _dbContext.CommitAsync(cancellationToken);
            indexCommitted = true;
            await TryPromoteGenerationAsync(tenantId, orgId, knowledgeBaseId, cancellationToken);
            latestProfile = await _embeddingProfiles.GetAsync(tenantId, orgId, knowledgeBaseId, cancellationToken);
            if (latestProfile is not null && HasSuccessfulActiveJob(
                await _documentRepository.ListIndexJobsAsync(tenantId, orgId, knowledgeBaseId, cancellationToken), version.Id, latestProfile))
            {
                version.IndexStatus = "ready";
                await _documentRepository.UpdateVersionAsync(version, cancellationToken);
                await _dbContext.CommitAsync(cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException && !indexCommitted)
        {
            var errorCode = ClassifyIndexError(ex);
            var parsedChunks = await _documentRepository.ListChunksAsync(tenantId, orgId, version.Id, cancellationToken);
            if (!hasActiveGeneration)
            {
                document.Status = parsedChunks.Count > 0 ? "ready" : "failed";
                version.Status = document.Status;
                version.IndexStatus = "failed";
                version.ErrorCode = errorCode;
            }
            document.LastErrorCode = parsedChunks.Count > 0 ? null : errorCode;
            document.UpdatedAt = DateTime.UtcNow;
            document.UpdatedBy = actorUserId;
            job.Status = "failed";
            job.ErrorCode = errorCode;
            job.CompletedAt = DateTime.UtcNow;
            await _documentRepository.UpdateDocumentAsync(document, cancellationToken);
            await _documentRepository.UpdateVersionAsync(version, cancellationToken);
            await _documentRepository.UpdateIndexJobAsync(job, cancellationToken);
            await _dbContext.CommitAsync(cancellationToken);
        }

        return new AssistantKnowledgeDocumentProcessResult(Map(document, job,
            await _embeddingProfiles.GetAsync(tenantId, orgId, knowledgeBaseId, cancellationToken)), Map(job));
    }

    /// <summary>
    /// 读取文档原文、结构和片段，供管理端预览。
    /// </summary>
    public async Task<AssistantKnowledgeDocumentPreviewDto> GetDocumentPreviewAsync(
        string actorUserId, string tenantId, string orgId, string knowledgeBaseId, string documentId,
        CancellationToken cancellationToken = default)
    {
        _ = await RequireBaseAsync(tenantId, orgId, knowledgeBaseId, cancellationToken);
        var document = await _documentRepository.GetDocumentAsync(tenantId, orgId, knowledgeBaseId, documentId, cancellationToken)
            ?? throw new BizResultException(BusinessCodes.NotFound, "knowledge_document_not_found");
        var status = ParseDocumentStatus(document.Status);
        var version = await _documentRepository.GetVersionAsync(tenantId, orgId, documentId, document.CurrentVersionNo, cancellationToken);
        if (version is null)
            throw new BizResultException(BusinessCodes.NotFound, "knowledge_document_version_not_found");

        var chunks = await _documentRepository.ListChunksAsync(tenantId, orgId, version.Id, cancellationToken);
        var assetContent = await _assetService.GetContentAsync(document.AssetId, tenantId, orgId,
            new AssetAclContext(true, actorUserId, orgId, null), cancellationToken);
        await using var content = assetContent.Content;
        var parsed = await _documentParser.ParseAsync(assetContent.ContentType, document.FileName, content, cancellationToken);
        var originalText = parsed.Text;

        var structure = originalText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Select(line => new AssistantKnowledgeDocumentPreviewBlock(
                line.StartsWith('#') ? "heading" : "paragraph", line.TrimStart('#', ' ').Trim(), line.StartsWith('#') ? Math.Min(6, line.TakeWhile(c => c == '#').Count()) : 0))
            .ToArray();
        var profile = await _embeddingProfiles.GetAsync(tenantId, orgId, knowledgeBaseId, cancellationToken);
        var jobs = await _documentRepository.ListIndexJobsAsync(tenantId, orgId, knowledgeBaseId, cancellationToken);
        var available = profile is not null && version.Status == "ready"
            && HasSuccessfulActiveJob(jobs, version.Id, profile);
        var segments = chunks.Select(chunk => new AssistantKnowledgeDocumentPreviewSegment(
            chunk.Id, chunk.ChunkIndex, chunk.Content, $"{document.FileName} · {chunk.ChunkIndex + 1}", available)).ToArray();
        return new AssistantKnowledgeDocumentPreviewDto(document.Id, document.FileName, document.ContentType, status,
            originalText, structure, segments);
    }

    /// <summary>
    /// 在知识库生效的索引代次上执行检索测试，返回命中片段及来源。
    /// </summary>
    public async Task<AssistantKnowledgeRetrievalTestResult> TestRetrievalAsync(
        string actorUserId, string tenantId, string orgId, string knowledgeBaseId,
        TestAssistantKnowledgeRetrievalCommand command, CancellationToken cancellationToken = default)
    {
        _ = actorUserId;
        _ = await RequireBaseAsync(tenantId, orgId, knowledgeBaseId, cancellationToken);
        var query = (command.Question ?? string.Empty).Trim();
        if (query.Length == 0 || query.Length > 500)
            throw new BizResultException(BusinessCodes.BusinessException, "knowledge_qna_question_invalid");

        var profile = await _embeddingProfiles.GetAsync(tenantId, orgId, knowledgeBaseId, cancellationToken);
        if (profile is null || profile.ActiveGeneration < 1 || profile.ActiveProviderId is null
            || profile.ActiveProviderRevision is null || profile.ActiveModel is null || profile.ActiveDimension is null)
            throw new BizResultException(BusinessCodes.Conflict, "knowledge_embedding_not_ready");
        var activeProfile = ToActiveExecutionProfile(profile);
        var jobs = await _documentRepository.ListIndexJobsAsync(tenantId, orgId, knowledgeBaseId, cancellationToken);
        var documents = await _documentRepository.ListDocumentsAsync(tenantId, orgId, knowledgeBaseId, cancellationToken);
        var candidates = new Dictionary<string, (AssistantKnowledgeDocumentRepository.AssistantKnowledgeChunkRow Chunk,
            AssistantKnowledgeDocumentRepository.AssistantKnowledgeDocumentRow Document)>();
        foreach (var document in documents.Where(x => !x.IsDeleted))
        {
            /*
             * 重新分块或切换模型时，当前文档版本可能早于目标代次就绪而先递增。
             * 检索测试继续使用最近完成的生效索引任务，避免重建失败导致旧索引不可用。
             */
            var activeJob = jobs.Where(job => job.DocumentId == document.Id
                    && job.Status == "succeeded" && MatchesActiveProfile(job, profile))
                .OrderByDescending(job => job.VersionNo)
                .ThenByDescending(job => job.CompletedAt ?? job.CreatedAt)
                .FirstOrDefault();
            if (activeJob is null) continue;
            var version = await _documentRepository.GetVersionAsync(
                tenantId, orgId, document.Id, activeJob.VersionNo, cancellationToken);
            if (version is null || version.Id != activeJob.DocumentVersionId
                || version.Status != "ready" || version.IndexStatus != "ready") continue;
            var chunks = await _documentRepository.ListChunksAsync(tenantId, orgId, version.Id, cancellationToken);
            foreach (var chunk in chunks)
                candidates[chunk.Id] = (chunk, document);
        }

        if (candidates.Count == 0)
            return new AssistantKnowledgeRetrievalTestResult(query, Array.Empty<AssistantKnowledgeRetrievalMatch>());

        var embedding = await _embeddingProvider.EmbedAsync(new[] { query }, tenantId, orgId, activeProfile, cancellationToken);
        var collection = AssistantKnowledgeIndexNaming.Collection(tenantId, orgId, knowledgeBaseId,
            EmbeddingCollectionProfileId(activeProfile), activeProfile.Generation);
        IReadOnlyList<AssistantVectorSearchHit> vectorHits;
        try
        {
            vectorHits = await _vectorIndexStore.SearchAsync(
                collection, embedding.Vectors[0], 20, cancellationToken, candidates.Keys);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            vectorHits = Array.Empty<AssistantVectorSearchHit>();
        }

        var terms = NormalizeQuestion(query)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var matches = new List<AssistantKnowledgeRetrievalMatch>();
        foreach (var hit in vectorHits)
        {
            if (!candidates.TryGetValue(hit.ChunkId, out var candidate))
                continue;
            var normalized = NormalizeQuestion(candidate.Chunk.Content);
            var lexical = terms.Length == 0
                ? 0d
                : (double)terms.Count(term => normalized.Contains(term, StringComparison.Ordinal)) / terms.Length;
            var relevance = Math.Clamp(hit.Score * 0.75d + lexical * 0.25d, 0d, 1d);
            matches.Add(new AssistantKnowledgeRetrievalMatch(
                candidate.Chunk.Id, candidate.Document.Id, candidate.Document.FileName,
                candidate.Chunk.ChunkIndex, candidate.Chunk.Content, relevance));
        }

        return new AssistantKnowledgeRetrievalTestResult(
            query,
            matches.OrderByDescending(x => x.Relevance)
                .ThenBy(x => x.DocumentName, StringComparer.Ordinal)
                .ThenBy(x => x.SegmentIndex)
                .Take(20)
                .ToArray());
    }

    /// <summary>
    /// 读取知识库索引任务及进度、失败信息。
    /// </summary>
    public async Task<IReadOnlyList<AssistantKnowledgeIndexJobDto>> ListIndexJobsAsync(
        string actorUserId, string tenantId, string orgId, string knowledgeBaseId,
        CancellationToken cancellationToken = default)
    {
        _ = await RequireBaseAsync(tenantId, orgId, knowledgeBaseId, cancellationToken);
        return (await _documentRepository.ListIndexJobsAsync(tenantId, orgId, knowledgeBaseId, cancellationToken))
            .Select(Map).ToArray();
    }

    private async Task<AssistantKnowledgeRepository.AssistantKnowledgeBaseRow> RequireBaseAsync(string tenantId, string orgId, string id, CancellationToken cancellationToken)
        => await _repository.GetBaseAsync(tenantId, orgId, id, cancellationToken)
            ?? throw new BizResultException(BusinessCodes.NotFound, "knowledge_base_not_found");

    private async Task<IReadOnlyList<AssistantKnowledgeEmbeddingModelOptionDto>> ListAvailableEmbeddingModelsAsync(
        string tenantId, string orgId, CancellationToken cancellationToken)
    {
        var providers = await _policies.ListProvidersAsync(includeSettings: false, cancellationToken);
        return providers.Where(provider => provider.Enabled)
            .SelectMany(provider => provider.Models
                .Where(model => model.Purpose == "embedding" && model.Dimension is > 0)
                .Select(model => new AssistantKnowledgeEmbeddingModelOptionDto(
                    provider.Id, provider.Revision, provider.Name, model.Id, model.Dimension!.Value)))
            .OrderBy(option => option.ProviderName, StringComparer.Ordinal)
            .ThenBy(option => option.Model, StringComparer.Ordinal)
            .ToArray();
    }

    private static AssistantKnowledgeEmbeddingProfileDto ToEmbeddingProfileDto(
        AssistantKnowledgeEmbeddingProfileRepository.AssistantKnowledgeEmbeddingProfileRow? row,
        int documentCount,
        int targetReadyCount,
        IReadOnlyList<AssistantKnowledgeEmbeddingModelOptionDto> availableModels,
        IReadOnlyList<AssistantKnowledgeDocumentRepository.AssistantKnowledgeIndexJobRow>? jobs = null,
        bool targetAvailable = true,
        bool activeAvailable = false)
    {
        if (row is null)
            return new AssistantKnowledgeEmbeddingProfileDto(0, "not_configured", null, null,
                documentCount, 0, null, availableModels);

        var status = row.Status;
        var lastError = row.LastErrorCode;
        var targetJobs = jobs?.Where(job => job.Generation == row.TargetGeneration
            && job.EmbeddingProviderId == row.ProviderId
            && job.EmbeddingProviderRevision == row.ProviderRevision
            && job.EmbeddingModel == row.Model
            && job.EmbeddingDimension == row.Dimension)
            .GroupBy(job => job.DocumentId)
            .Select(group => group.OrderByDescending(job => job.VersionNo).ThenByDescending(job => job.CreatedAt).First())
            .ToArray() ?? Array.Empty<AssistantKnowledgeDocumentRepository.AssistantKnowledgeIndexJobRow>();
        if (targetReadyCount < documentCount && status == "ready") status = "pending_rebuild";
        if (status != "ready" && targetJobs.Any(job => job.Status == "failed"))
        {
            status = "failed";
            lastError = targetJobs.Where(job => job.Status == "failed")
                .OrderByDescending(job => job.CreatedAt).Select(job => job.ErrorCode).FirstOrDefault();
        }
        else if (status != "ready" && targetJobs.Any(job => job.Status == "processing"))
            status = "rebuilding";
        if (!targetAvailable) status = "model_unavailable";

        var target = new AssistantKnowledgeEmbeddingSelectionDto(
            row.ProviderId, row.ProviderRevision, row.Model, row.Dimension, row.TargetGeneration);
        var active = row.ActiveProviderId is not null && row.ActiveProviderRevision is not null
            && row.ActiveModel is not null && row.ActiveDimension is not null && row.ActiveGeneration > 0
            ? new AssistantKnowledgeEmbeddingSelectionDto(row.ActiveProviderId, row.ActiveProviderRevision.Value,
                row.ActiveModel, row.ActiveDimension.Value, row.ActiveGeneration)
            : null;
        return new AssistantKnowledgeEmbeddingProfileDto(row.Revision, status, target, active,
            documentCount, targetReadyCount, lastError, availableModels, activeAvailable);
    }

    private static AssistantKnowledgeEmbeddingExecutionProfile ToExecutionProfile(
        AssistantKnowledgeEmbeddingProfileRepository.AssistantKnowledgeEmbeddingProfileRow row, long generation)
        => new(row.ProviderId, row.ProviderRevision, row.Model, row.Dimension, row.Revision, generation);

    private static AssistantKnowledgeEmbeddingExecutionProfile ToActiveExecutionProfile(
        AssistantKnowledgeEmbeddingProfileRepository.AssistantKnowledgeEmbeddingProfileRow row)
        => new(row.ActiveProviderId!, row.ActiveProviderRevision!.Value, row.ActiveModel!,
            row.ActiveDimension!.Value, row.ActiveProfileRevision ?? row.Revision, row.ActiveGeneration);

    private static string EmbeddingCollectionProfileId(AssistantKnowledgeEmbeddingExecutionProfile profile)
        => AssistantKnowledgeIndexNaming.ProfileId(profile);

    private static bool MatchesTargetProfile(AssistantKnowledgeDocumentRepository.AssistantKnowledgeIndexJobRow job,
        AssistantKnowledgeEmbeddingProfileRepository.AssistantKnowledgeEmbeddingProfileRow profile)
        => job.Generation == profile.TargetGeneration && job.EmbeddingProviderId == profile.ProviderId
            && job.EmbeddingProviderRevision == profile.ProviderRevision && job.EmbeddingModel == profile.Model
            && job.EmbeddingDimension == profile.Dimension;

    private static bool MatchesActiveProfile(AssistantKnowledgeDocumentRepository.AssistantKnowledgeIndexJobRow job,
        AssistantKnowledgeEmbeddingProfileRepository.AssistantKnowledgeEmbeddingProfileRow profile)
        => profile.ActiveGeneration > 0 && job.Generation == profile.ActiveGeneration
            && job.EmbeddingProviderId == profile.ActiveProviderId
            && job.EmbeddingProviderRevision == profile.ActiveProviderRevision && job.EmbeddingModel == profile.ActiveModel
            && job.EmbeddingDimension == profile.ActiveDimension;

    private static bool HasSuccessfulActiveJob(
        IEnumerable<AssistantKnowledgeDocumentRepository.AssistantKnowledgeIndexJobRow> jobs, string documentVersionId,
        AssistantKnowledgeEmbeddingProfileRepository.AssistantKnowledgeEmbeddingProfileRow profile)
        => jobs.Any(job => job.DocumentVersionId == documentVersionId && job.Status == "succeeded" && MatchesActiveProfile(job, profile));

    private static bool HasSuccessfulJob(
        IEnumerable<AssistantKnowledgeDocumentRepository.AssistantKnowledgeIndexJobRow> jobs,
        string documentVersionId,
        AssistantKnowledgeEmbeddingProfileRepository.AssistantKnowledgeEmbeddingProfileRow profile,
        long generation)
        => jobs.Any(job => job.DocumentVersionId == documentVersionId && job.Status == "succeeded"
            && job.Generation == generation && job.EmbeddingProviderId == profile.ProviderId
            && job.EmbeddingProviderRevision == profile.ProviderRevision && job.EmbeddingModel == profile.Model
            && job.EmbeddingDimension == profile.Dimension);

    private async Task TryPromoteGenerationAsync(
        string tenantId, string orgId, string knowledgeBaseId, CancellationToken cancellationToken)
    {
        var profile = await _embeddingProfiles.GetAsync(tenantId, orgId, knowledgeBaseId, cancellationToken);
        if (profile is null || profile.Status == "ready" && profile.ActiveGeneration == profile.TargetGeneration)
            return;
        await _policies.EnsureEmbeddingAvailableAsync(ToExecutionProfile(profile, profile.TargetGeneration), cancellationToken);
        var policyRow = await _repository.GetChunkingPolicyAsync(tenantId, orgId, knowledgeBaseId, cancellationToken);
        var policy = policyRow is null ? AssistantKnowledgeChunkingPolicy.Default : FromRow(policyRow);
        var documents = await _documentRepository.ListDocumentsAsync(tenantId, orgId, knowledgeBaseId, cancellationToken);
        var jobs = await _documentRepository.ListIndexJobsAsync(tenantId, orgId, knowledgeBaseId, cancellationToken);
        foreach (var document in documents)
        {
            var version = await _documentRepository.GetVersionAsync(
                tenantId, orgId, document.Id, document.CurrentVersionNo, cancellationToken);
            if (version is null || version.Status != "ready"
                || !string.Equals(version.ChunkStrategy, policy.StrategyKey, StringComparison.Ordinal)
                || !HasSuccessfulJob(jobs, version.Id, profile, profile.TargetGeneration))
                return;
        }

        var expectedRevision = profile.Revision;
        profile.ActiveProviderId = profile.ProviderId;
        profile.ActiveProviderRevision = profile.ProviderRevision;
        profile.ActiveModel = profile.Model;
        profile.ActiveDimension = profile.Dimension;
        profile.ActiveProfileRevision = profile.Revision;
        profile.ActiveGeneration = profile.TargetGeneration;
        profile.Status = "ready";
        profile.LastErrorCode = null;
        profile.UpdatedAt = DateTime.UtcNow;
        try
        {
            await _embeddingProfiles.TryPromoteAsync(profile, expectedRevision, profile.TargetGeneration, cancellationToken);
            await _dbContext.CommitAsync(cancellationToken);
        }
        catch (BizResultException ex) when (ex.Message == "knowledge_embedding_profile_revision_conflict")
        {
            /*
             * 目标配置已被较新的修改替换；已完成任务仍属于其冻结的索引代次。
             * 调用方随后重新读取当前配置，不让旧任务覆盖新目标。
             */
        }
    }

    private static AssistantKnowledgeQnaDto Map(AssistantKnowledgeRepository.AssistantKnowledgeQnaRow row)
        => new(row.Id, row.KnowledgeBaseId, row.Question, row.Answer, row.Enabled, row.SortOrder, row.ShowAsSuggestion, row.UpdatedAt);

    private static AssistantKnowledgeDocumentDto Map(
        AssistantKnowledgeDocumentRepository.AssistantKnowledgeDocumentRow document,
        AssistantKnowledgeDocumentRepository.AssistantKnowledgeIndexJobRow? job,
        AssistantKnowledgeEmbeddingProfileRepository.AssistantKnowledgeEmbeddingProfileRow? profile = null)
        => new(document.Id, document.KnowledgeBaseId, document.AssetId, document.FileName, document.ContentType,
            document.SizeBytes, document.CurrentVersionNo, ParseDocumentStatus(document.Status),
            job?.ChunkCount ?? 0, job is null ? null : ParseJobStatus(job.Status), document.LastErrorCode, document.UpdatedAt,
            job?.Status == "needs_embedding_config" ? "needs_embedding_config"
                : job?.Status == "succeeded"
                    ? profile is not null && MatchesActiveProfile(job, profile) ? "ready" : "pending_rebuild"
                    : job?.Status ?? "queued",
            job?.Generation);

    private static AssistantKnowledgeIndexJobDto Map(AssistantKnowledgeDocumentRepository.AssistantKnowledgeIndexJobRow job)
        => new(job.Id, job.DocumentId, job.VersionNo, ParseJobStatus(job.Status), job.AttemptCount,
            job.ChunkCount, job.ErrorCode, job.CreatedAt, job.CompletedAt,
            job.Generation, job.EmbeddingModel, job.EmbeddingDimension);

    private static AssistantKnowledgeDocumentStatus ParseDocumentStatus(string value)
        => value switch
        {
            "processing" => AssistantKnowledgeDocumentStatus.Processing,
            "ready" => AssistantKnowledgeDocumentStatus.Ready,
            "failed" => AssistantKnowledgeDocumentStatus.Failed,
            "archived" => AssistantKnowledgeDocumentStatus.Archived,
            _ => AssistantKnowledgeDocumentStatus.Uploaded
        };

    private static AssistantKnowledgeIndexJobStatus ParseJobStatus(string value)
        => value switch
        {
            "processing" => AssistantKnowledgeIndexJobStatus.Processing,
            "succeeded" => AssistantKnowledgeIndexJobStatus.Succeeded,
            "failed" => AssistantKnowledgeIndexJobStatus.Failed,
            "needs_embedding_config" => AssistantKnowledgeIndexJobStatus.NeedsEmbeddingConfig,
            _ => AssistantKnowledgeIndexJobStatus.Queued
        };

    private static string ClassifyIndexError(Exception error) => error switch
    {
        BizResultException => "knowledge_embedding_model_unavailable",
        RpcException => "assistant_vector_index_unavailable",
        InvalidOperationException when error.Message is "assistant_embedding_response_invalid"
            or "assistant_embedding_dimension_mismatch" or "assistant_embedding_provider_failed"
            or "assistant_vector_index_readback_incomplete" or "assistant_embedding_profile_changed" => error.Message,
        JsonException or FormatException => "assistant_embedding_response_invalid",
        _ => "knowledge_document_index_failed"
    };

    private static IReadOnlyList<AssistantKnowledgeDocumentRepository.AssistantKnowledgeChunkRow> BuildChunks(
        string text, string tenantId, string orgId, string versionId, string actorUserId, AssistantKnowledgeChunkingPolicy policy)
    {
        var pieces = AssistantKnowledgeChunker.Split(text, policy);
        var result = new List<AssistantKnowledgeDocumentRepository.AssistantKnowledgeChunkRow>();
        for (var index = 0; index < pieces.Count; index++)
        {
            var content = pieces[index];
            result.Add(new AssistantKnowledgeDocumentRepository.AssistantKnowledgeChunkRow
            {
                Id = Guid.NewGuid().ToString("N"), TenantId = tenantId, OrgId = orgId,
                DocumentVersionId = versionId, ChunkIndex = index, Content = content,
                ContentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant(),
                TokenCount = content.Length, CreatedAt = DateTime.UtcNow, CreatedBy = actorUserId
            });
        }
        return result;
    }

    private static AssistantKnowledgeChunkingPolicy ToPolicy(SaveAssistantKnowledgeChunkingPolicyCommand command, long revision)
        => new(command.Mode, command.MaxChunkSize, command.MinChunkSize, command.OverlapSize, command.MaxHeadingLevel,
            command.PreserveHeadingPath, command.MergeShortChunks, revision);

    private static void ValidatePolicy(AssistantKnowledgeChunkingPolicy policy)
    {
        try { policy.Validate(); }
        catch (ArgumentOutOfRangeException)
        {
            throw new BizResultException(BusinessCodes.BusinessException, "knowledge_chunking_policy_invalid");
        }
    }

    private static AssistantKnowledgeChunkingPolicy FromRow(AssistantKnowledgeRepository.AssistantKnowledgeChunkingPolicyRow row)
        => new(Enum.TryParse<AssistantKnowledgeChunkingMode>(row.Mode, true, out var mode) ? mode : AssistantKnowledgeChunkingMode.Hybrid,
            row.MaxChunkSize, row.MinChunkSize, row.OverlapSize, row.MaxHeadingLevel, row.PreserveHeadingPath, row.MergeShortChunks, row.Revision);

    private static AssistantKnowledgeChunkingPolicyDto ToDto(AssistantKnowledgeChunkingPolicy policy, bool pending)
        => new(policy.Mode, policy.MaxChunkSize, policy.MinChunkSize, policy.OverlapSize, policy.MaxHeadingLevel,
            policy.PreserveHeadingPath, policy.MergeShortChunks, policy.Revision, pending);

    private async Task<bool> HasPendingDocumentsAsync(string tenantId, string orgId, string knowledgeBaseId,
        AssistantKnowledgeChunkingPolicy policy, CancellationToken cancellationToken)
    {
        foreach (var document in await _documentRepository.ListDocumentsAsync(tenantId, orgId, knowledgeBaseId, cancellationToken))
        {
            if (document.Status != "ready") return true;
            var version = await _documentRepository.GetVersionAsync(tenantId, orgId, document.Id, document.CurrentVersionNo, cancellationToken);
            if (version is null || !string.Equals(version.ChunkStrategy, policy.StrategyKey, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private static void Apply(AssistantKnowledgeRepository.AssistantKnowledgeChunkingPolicyRow row, AssistantKnowledgeChunkingPolicy policy, string actor, DateTime now)
    {
        row.Mode = policy.Mode.ToString().ToLowerInvariant(); row.MaxChunkSize = policy.MaxChunkSize; row.MinChunkSize = policy.MinChunkSize;
        row.OverlapSize = policy.OverlapSize; row.MaxHeadingLevel = policy.MaxHeadingLevel; row.PreserveHeadingPath = policy.PreserveHeadingPath;
        row.MergeShortChunks = policy.MergeShortChunks; row.Revision = policy.Revision; row.UpdatedAt = now; row.UpdatedBy = actor;
    }

    /// <summary>
    /// 标准化固定问答的问题文本，保证保存与匹配使用一致规则。
    /// </summary>
    public static string NormalizeQuestion(string value)
        => string.Join(' ', value.Trim().Normalize(System.Text.NormalizationForm.FormKC).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
}
