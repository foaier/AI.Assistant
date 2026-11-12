#nullable enable
namespace SmartAssistant.Models.Assistants;

/// <summary>
/// 发布助手时提交的草稿与发布指针修订号。
/// </summary>
public sealed class PublishAssistantRequest
{
    /// <summary>
    /// 记录调用方期望的草稿修订，用于拒绝并发覆盖。
    /// </summary>
    public long ExpectedDraftRevision { get; set; }

    /// <summary>
    /// 记录调用方期望的发布指针修订，用于拒绝并发发布冲突。
    /// </summary>
    public long? ExpectedPointerRevision { get; set; }
}
