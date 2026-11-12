#nullable enable
namespace SmartAssistant.Models.Publication;

/// <summary>
/// 撤回发布的命令，携带预期发布指针修订号。
/// </summary>
public sealed record RevokeAssistantPublicationCommand(
    long ExpectedPointerRevision);
