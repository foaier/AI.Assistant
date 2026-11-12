#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SmartAssistant.Modules.Contracts;

/// <summary>
/// 助手草稿的原始配置与审计信息，供应用层传递草稿状态。
/// </summary>
public sealed record AssistantDraftDto(
    string Id,
    string TenantId,
    string OrgId,
    string Name,
    string? Description,
    string DraftJson,
    long DraftRevision,
    DateTime UpdatedAt,
    string UpdatedBy);

/// <summary>
/// 助手列表所需的名称、描述、草稿修订号和最近修改信息。
/// </summary>
public sealed record AssistantListItemDto(
    string Id,
    string Name,
    string? Description,
    long DraftRevision,
    DateTime UpdatedAt,
    string UpdatedBy);

/// <summary>
/// 管理端使用的助手详情，将草稿配置转换为可直接读取的 JSON 对象。
/// </summary>
public sealed record AssistantDto(
    string Id,
    string Name,
    string? Description,
    long DraftRevision,
    DateTime UpdatedAt,
    string UpdatedBy,
    JsonElement Configuration);

/// <summary>
/// 知识库概览，包含名称、问答数量、文档数量和更新时间。
/// </summary>
public sealed record AssistantKnowledgeBaseDto(
    string Id,
    string Name,
    string? Description,
    int QnaCount,
    int DocumentCount,
    DateTime UpdatedAt);

/// <summary>
/// 知识文档的分块模式：按标题、固定长度或混合方式切分。
/// </summary>
public enum AssistantKnowledgeChunkingMode
{
    Heading = 0,
    Fixed = 1,
    Hybrid = 2
}

/// <summary>
/// 管理端显示的分块策略及其修订号、待处理文档状态。
/// </summary>
public sealed record AssistantKnowledgeChunkingPolicyDto(
    AssistantKnowledgeChunkingMode Mode,
    int MaxChunkSize,
    int MinChunkSize,
    int OverlapSize,
    int MaxHeadingLevel,
    bool PreserveHeadingPath,
    bool MergeShortChunks,
    long Revision,
    bool HasPendingDocuments);

/// <summary>
/// 选定的嵌入供应商、模型、维度及索引代次。
/// </summary>
public sealed record AssistantKnowledgeEmbeddingSelectionDto(
    string ProviderId,
    long ProviderRevision,
    string Model,
    int Dimension,
    long Generation);

/// <summary>
/// 可供知识库选择的嵌入模型及其供应商修订和向量维度。
/// </summary>
public sealed record AssistantKnowledgeEmbeddingModelOptionDto(
    string ProviderId,
    long ProviderRevision,
    string ProviderName,
    string Model,
    int Dimension);

/// <summary>
/// 知识库嵌入配置状态，区分目标配置与已完成索引的生效配置。
/// </summary>
public sealed record AssistantKnowledgeEmbeddingProfileDto(
    long Revision,
    string Status,
    AssistantKnowledgeEmbeddingSelectionDto? Target,
    AssistantKnowledgeEmbeddingSelectionDto? Active,
    int DocumentCount,
    int TargetReadyDocumentCount,
    string? LastErrorCode,
    IReadOnlyList<AssistantKnowledgeEmbeddingModelOptionDto> AvailableModels,
    bool ActiveAvailable = false);

/// <summary>
/// 保存嵌入模型选择的命令，携带预期修订号以检测并发修改。
/// </summary>
public sealed record SaveAssistantKnowledgeEmbeddingProfileCommand(
    string ProviderId,
    long ProviderRevision,
    string Model,
    int Dimension,
    long ExpectedRevision);

/// <summary>
/// 保存分块策略的命令，并指定是否重新处理已有文档。
/// </summary>
public sealed record SaveAssistantKnowledgeChunkingPolicyCommand(
    AssistantKnowledgeChunkingMode Mode,
    int MaxChunkSize,
    int MinChunkSize,
    int OverlapSize,
    int MaxHeadingLevel,
    bool PreserveHeadingPath,
    bool MergeShortChunks,
    bool ReprocessDocuments);

/// <summary>
/// 预览文档按当前策略切分后的片段及预计片段数量。
/// </summary>
public sealed record AssistantKnowledgeChunkPreviewDto(
    string DocumentId,
    int EstimatedChunkCount,
    IReadOnlyList<AssistantKnowledgeDocumentPreviewSegment> Segments);

/// <summary>
/// 知识库固定问答条目，包含启用状态、排序和推荐问题设置。
/// </summary>
public sealed record AssistantKnowledgeQnaDto(
    string Id,
    string KnowledgeBaseId,
    string Question,
    string? Answer,
    bool Enabled,
    int SortOrder,
    bool ShowAsSuggestion,
    DateTime UpdatedAt);

/// <summary>
/// 固定问答测试结果，说明是否命中以及是否调用了模型供应商。
/// </summary>
public sealed record AssistantKnowledgeQnaTestResult(
    string Question,
    bool Matched,
    string? Answer,
    string AnswerMode,
    bool ProviderInvoked);

/// <summary>
/// 文档预览结果，包含原文、结构块、分段和当前处理状态。
/// </summary>
public sealed record AssistantKnowledgeDocumentPreviewDto(
    string DocumentId,
    string FileName,
    string ContentType,
    AssistantKnowledgeDocumentStatus Status,
    string OriginalText,
    IReadOnlyList<AssistantKnowledgeDocumentPreviewBlock> Structure,
    IReadOnlyList<AssistantKnowledgeDocumentPreviewSegment> Segments);

/// <summary>
/// 文档预览中的结构块，记录块类型、文本和标题层级。
/// </summary>
public sealed record AssistantKnowledgeDocumentPreviewBlock(string Kind, string Text, int Level);

/// <summary>
/// 文档预览中的文本片段，记录顺序、来源标签和可用性。
/// </summary>
public sealed record AssistantKnowledgeDocumentPreviewSegment(
    string Id,
    int Index,
    string Text,
    string SourceLabel,
    bool Available);

/// <summary>
/// 检索测试命中的文档片段，包含来源信息、正文和相关度。
/// </summary>
public sealed record AssistantKnowledgeRetrievalMatch(
    string SegmentId,
    string DocumentId,
    string DocumentName,
    int SegmentIndex,
    string Text,
    double Relevance);

/// <summary>
/// 指定问题的知识检索测试结果。
/// </summary>
public sealed record AssistantKnowledgeRetrievalTestResult(
    string Question,
    IReadOnlyList<AssistantKnowledgeRetrievalMatch> Matches);

/// <summary>
/// 知识文档从上传、处理到可用、失败或归档的状态。
/// </summary>
public enum AssistantKnowledgeDocumentStatus
{
    Uploaded = 0,
    Processing = 1,
    Ready = 2,
    Failed = 3,
    Archived = 4
}

/// <summary>
/// 知识索引任务的排队、执行和结束状态。
/// </summary>
public enum AssistantKnowledgeIndexJobStatus
{
    Queued = 0,
    Processing = 1,
    Succeeded = 2,
    Failed = 3,
    NeedsEmbeddingConfig = 4
}

/// <summary>
/// 知识文档的管理信息，包含文件属性、版本与处理状态。
/// </summary>
public sealed record AssistantKnowledgeDocumentDto(
    string Id,
    string KnowledgeBaseId,
    string AssetId,
    string FileName,
    string ContentType,
    long SizeBytes,
    int CurrentVersionNo,
    AssistantKnowledgeDocumentStatus Status,
    int ChunkCount,
    AssistantKnowledgeIndexJobStatus? LastIndexJobStatus,
    string? LastErrorCode,
    DateTime UpdatedAt,
    string IndexStatus = "unknown",
    long? IndexGeneration = null);

/// <summary>
/// 索引任务的管理信息，用于追踪处理状态与失败原因。
/// </summary>
public sealed record AssistantKnowledgeIndexJobDto(
    string Id,
    string DocumentId,
    int DocumentVersionNo,
    AssistantKnowledgeIndexJobStatus Status,
    int AttemptCount,
    int ChunkCount,
    string? ErrorCode,
    DateTime CreatedAt,
    DateTime? CompletedAt,
    long? Generation = null,
    string? EmbeddingModel = null,
    int? EmbeddingDimension = null);

/// <summary>
/// 文档处理请求的结果，关联文档状态与索引任务。
/// </summary>
public sealed record AssistantKnowledgeDocumentProcessResult(
    AssistantKnowledgeDocumentDto Document,
    AssistantKnowledgeIndexJobDto Job);

/// <summary>
/// 知识文档上传参数，包含原始文件内容和文件属性。
/// </summary>
public sealed record AssistantKnowledgeDocumentUpload(
    string FileName,
    string ContentType,
    long SizeBytes,
    Stream Content);

/// <summary>
/// 创建知识库所需的名称和描述。
/// </summary>
public sealed record CreateAssistantKnowledgeBaseCommand(string Name, string? Description);

/// <summary>
/// 保存固定问答的内容、启用状态、排序和推荐设置。
/// </summary>
public sealed record SaveAssistantKnowledgeQnaCommand(
    string? Id,
    string Question,
    string? Answer,
    bool Enabled,
    int SortOrder,
    bool ShowAsSuggestion);

/// <summary>
/// 用于验证固定问答匹配行为的问题输入。
/// </summary>
public sealed record TestAssistantKnowledgeQnaCommand(string Question);

/// <summary>
/// 用于验证知识片段召回效果的问题输入。
/// </summary>
public sealed record TestAssistantKnowledgeRetrievalCommand(string Question);

/// <summary>
/// 定义知识库、问答、文档、分块配置和索引的应用操作。
/// </summary>
public interface IAssistantKnowledgeService
{
    /// <summary>
    /// 列出当前租户和组织范围内的知识库及问答、文档数量。
    /// </summary>
    Task<IReadOnlyList<AssistantKnowledgeBaseDto>> ListKnowledgeBasesAsync(
        string actorUserId, string tenantId, string orgId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 校验名称与范围后创建知识库。
    /// </summary>
    Task<AssistantKnowledgeBaseDto> CreateKnowledgeBaseAsync(
        string actorUserId, string tenantId, string orgId, CreateAssistantKnowledgeBaseCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查草稿和有效发布的知识库引用后执行删除，避免破坏已绑定的助手。
    /// </summary>
    Task DeleteKnowledgeBaseAsync(
        string actorUserId, string tenantId, string orgId, string knowledgeBaseId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取知识库当前分块策略与是否存在待重处理文档的信息。
    /// </summary>
    Task<AssistantKnowledgeChunkingPolicyDto> GetChunkingPolicyAsync(
        string actorUserId, string tenantId, string orgId, string knowledgeBaseId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 校验并保存分块参数，按请求决定是否为现有文档启动重新处理。
    /// </summary>
    Task<AssistantKnowledgeChunkingPolicyDto> SaveChunkingPolicyAsync(
        string actorUserId, string tenantId, string orgId, string knowledgeBaseId,
        SaveAssistantKnowledgeChunkingPolicyCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取目标与生效嵌入配置、文档完成进度和可选模型。
    /// </summary>
    Task<AssistantKnowledgeEmbeddingProfileDto> GetEmbeddingProfileAsync(
        string actorUserId, string tenantId, string orgId, string knowledgeBaseId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 校验嵌入模型与预期修订号后保存目标配置，并维护相应索引代次。
    /// </summary>
    Task<AssistantKnowledgeEmbeddingProfileDto> SaveEmbeddingProfileAsync(
        string actorUserId, string tenantId, string orgId, string knowledgeBaseId,
        SaveAssistantKnowledgeEmbeddingProfileCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按当前分块策略预览文档切分结果。
    /// </summary>
    Task<AssistantKnowledgeChunkPreviewDto> PreviewChunkingAsync(
        string actorUserId, string tenantId, string orgId, string knowledgeBaseId, string documentId,
        SaveAssistantKnowledgeChunkingPolicyCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取知识库固定问答列表。
    /// </summary>
    Task<IReadOnlyList<AssistantKnowledgeQnaDto>> ListQnaAsync(
        string actorUserId, string tenantId, string orgId, string knowledgeBaseId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 标准化问题并保存问答内容、排序、启用状态和推荐设置。
    /// </summary>
    Task<AssistantKnowledgeQnaDto> SaveQnaAsync(
        string actorUserId, string tenantId, string orgId, string knowledgeBaseId,
        SaveAssistantKnowledgeQnaCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// 在所属知识库范围内删除指定固定问答。
    /// </summary>
    Task DeleteQnaAsync(
        string actorUserId, string tenantId, string orgId, string knowledgeBaseId, string qnaId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 按固定问答匹配规则验证问题是否命中，返回直接回答结果。
    /// </summary>
    Task<AssistantKnowledgeQnaTestResult> TestQnaAsync(
        string actorUserId, string tenantId, string orgId, string knowledgeBaseId, string qnaId,
        TestAssistantKnowledgeQnaCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出知识库文档及其处理状态。
    /// </summary>
    Task<IReadOnlyList<AssistantKnowledgeDocumentDto>> ListDocumentsAsync(
        string actorUserId, string tenantId, string orgId, string knowledgeBaseId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 将文档保存为私有资产，并创建知识文档记录。
    /// </summary>
    Task<AssistantKnowledgeDocumentDto> UploadDocumentAsync(
        string actorUserId, string tenantId, string orgId, string knowledgeBaseId,
        AssistantKnowledgeDocumentUpload file, CancellationToken cancellationToken = default);

    /// <summary>
    /// 为指定文档执行解析、分块和向量索引流程，保存版本及任务状态。
    /// </summary>
    Task<AssistantKnowledgeDocumentProcessResult> ProcessDocumentAsync(
        string actorUserId, string tenantId, string orgId, string knowledgeBaseId, string documentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取文档原文、结构和片段，供管理端预览。
    /// </summary>
    Task<AssistantKnowledgeDocumentPreviewDto> GetDocumentPreviewAsync(
        string actorUserId, string tenantId, string orgId, string knowledgeBaseId, string documentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 在知识库生效的索引代次上执行检索测试，返回命中片段及来源。
    /// </summary>
    Task<AssistantKnowledgeRetrievalTestResult> TestRetrievalAsync(
        string actorUserId, string tenantId, string orgId, string knowledgeBaseId,
        TestAssistantKnowledgeRetrievalCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取知识库索引任务及进度、失败信息。
    /// </summary>
    Task<IReadOnlyList<AssistantKnowledgeIndexJobDto>> ListIndexJobsAsync(
        string actorUserId, string tenantId, string orgId, string knowledgeBaseId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 定义管理端查询会话明细和助手使用统计的应用契约。
/// </summary>
public interface IAssistantAnalyticsService
{
    /// <summary>
    /// 在指定助手范围内读取会话消息，供管理端查看会话内容。
    /// </summary>
    Task<AssistantMessagesPage> GetMessagesAsync(string actorUserId, string tenantId, string orgId, string assistantId,
        string conversationId, int limit, string? cursor, CancellationToken cancellationToken = default);

    /// <summary>
    /// 分页读取助手的会话活动信息。
    /// </summary>
    Task<AssistantConversationsPage> ListConversationsAsync(
        string actorUserId, string tenantId, string orgId, string assistantId, int limit, int page,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 汇总助手的会话数量、轮次结果和回答来源等统计数据。
    /// </summary>
    Task<AssistantAnalyticsSummaryDto> GetSummaryAsync(
        string actorUserId, string tenantId, string orgId, string assistantId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 创建助手草稿的命令，包含配置和可选的创建幂等键。
/// </summary>
public sealed record CreateAssistantDraftCommand(
    string Name,
    string? Description,
    string DraftJson,
    string? IdempotencyKey);

/// <summary>
/// 保存助手草稿的命令，携带预期草稿修订号以避免覆盖并发修改。
/// </summary>
public sealed record SaveAssistantDraftCommand(
    string Name,
    string? Description,
    string DraftJson,
    long ExpectedDraftRevision);

/// <summary>
/// 公开版本的有效或撤回状态。
/// </summary>
public enum AssistantPublicationStatus
{
    Revoked = 0,
    Active = 1
}

/// <summary>
/// 发布版本的只读快照、版本标识及发布和撤回审计信息。
/// </summary>
public sealed record AssistantPublishedVersionDto(
    string Id,
    string AssistantKey,
    string AssistantId,
    string TenantId,
    string OrgId,
    long VersionNo,
    string Name,
    string? Description,
    string SnapshotJson,
    string SnapshotHash,
    AssistantPublicationStatus Status,
    DateTime PublishedAt,
    string PublishedBy,
    DateTime? RevokedAt,
    string? RevokedBy);

/// <summary>
/// 发布草稿的并发前提，要求草稿和发布指针都匹配预期修订号。
/// </summary>
public sealed record PublishAssistantCommand(
    long ExpectedDraftRevision,
    long? ExpectedPointerRevision);

/// <summary>
/// 撤回发布的命令，携带预期发布指针修订号。
/// </summary>
public sealed record RevokeAssistantPublicationCommand(
    long ExpectedPointerRevision);

/// <summary>
/// 发布或撤回后的发布指针及版本结果。
/// </summary>
public sealed record AssistantPublicationResult(
    AssistantPublishedVersionDto Version,
    long PointerRevision,
    bool Changed);

/// <summary>
/// 管理端显示的当前发布状态、公开标识和版本信息。
/// </summary>
public sealed record AssistantPublicationState(long PointerRevision, bool Published, string AssistantKey,
    long VersionNo, DateTime? PublishedAt);

/// <summary>
/// 由服务端解析的公开助手权限与配置，确定当前可访问的发布范围。
/// </summary>
public sealed record AssistantPublicAuthority(
    string AssistantKey,
    AssistantScopeSnapshot Scope,
    string Name,
    string? Description,
    JsonElement Configuration,
    long VersionNo,
    string SnapshotHash);

/// <summary>
/// 定义助手发布、撤回及当前发布状态查询的应用契约。
/// </summary>
public interface IAssistantPublicationService
{
    /// <summary>
    /// 读取助手当前的发布状态、指针修订和公开访问标识。
    /// </summary>
    Task<AssistantPublicationState> GetStateAsync(string tenantId, string orgId, string assistantId, CancellationToken cancellationToken = default);
    /// <summary>
    /// 校验草稿与指针修订，冻结执行配置并在同一事务中保存发布版本、指针和领域事件。
    /// </summary>
    Task<AssistantPublicationResult> PublishAsync(
        string actorUserId,
        string tenantId,
        string orgId,
        string assistantId,
        PublishAssistantCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 校验预期发布指针修订，撤回当前版本并记录撤回事件。
    /// </summary>
    Task<AssistantPublicationResult> RevokeAsync(
        string actorUserId,
        string tenantId,
        string orgId,
        string assistantId,
        RevokeAssistantPublicationCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取当前指针指向的有效发布版本；未发布或已撤回时返回空值。
    /// </summary>
    Task<AssistantPublishedVersionDto?> GetCurrentAsync(
        string tenantId,
        string orgId,
        string assistantId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 按公开标识解析有效发布权限；不可用时返回空结果。
/// </summary>
public interface IAssistantPublicAuthorityResolver
{
    /// <summary>
    /// 按公开助手标识解析有效的发布快照及运行授权范围；不可用时返回空值。
    /// </summary>
    Task<AssistantPublicAuthority?> ResolveAsync(
        string assistantKey,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 定义助手草稿的增删改查及管理端数据转换契约。
/// </summary>
public interface IAssistantDraftService
{
    /// <summary>
    /// 读取当前租户和组织范围内的助手列表。
    /// </summary>
    Task<IReadOnlyList<AssistantListItemDto>> ListAsync(
        string actorUserId,
        string tenantId,
        string orgId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 创建草稿并转换为管理端使用的助手详情。
    /// </summary>
    Task<AssistantDto> CreateAssistantAsync(
        string actorUserId,
        string tenantId,
        string orgId,
        CreateAssistantDraftCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取草稿并将配置转换为管理端的 JSON 对象。
    /// </summary>
    Task<AssistantDto> GetAssistantAsync(
        string actorUserId,
        string tenantId,
        string orgId,
        string assistantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 保存草稿并返回更新后的管理端助手详情。
    /// </summary>
    Task<AssistantDto> SaveAssistantDraftAsync(
        string actorUserId,
        string tenantId,
        string orgId,
        string assistantId,
        SaveAssistantDraftCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除草稿前检查是否仍有有效发布，已发布助手必须先撤回。
    /// </summary>
    Task DeleteAssistantAsync(
        string actorUserId,
        string tenantId,
        string orgId,
        string assistantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 校验配置与幂等键后创建助手草稿，重复的相同请求复用已有结果。
    /// </summary>
    Task<AssistantDraftDto> CreateAsync(
        string actorUserId,
        string tenantId,
        string orgId,
        CreateAssistantDraftCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取指定助手草稿；资源不存在时返回相应业务错误。
    /// </summary>
    Task<AssistantDraftDto> GetAsync(
        string actorUserId,
        string tenantId,
        string orgId,
        string assistantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 校验配置绑定与预期草稿修订号后保存变更，并记录草稿保存事件。
    /// </summary>
    Task<AssistantDraftDto> SaveAsync(
        string actorUserId,
        string tenantId,
        string orgId,
        string assistantId,
        SaveAssistantDraftCommand command,
        CancellationToken cancellationToken = default);
}
