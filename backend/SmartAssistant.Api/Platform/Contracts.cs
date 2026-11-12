using System.Security.Claims;

namespace SmartAssistant.Extensions.Common
{
    /// <summary>
    /// 统一的业务响应封装，使用业务码、提示信息和返回对象表达接口结果。
    /// </summary>
    public sealed record BizResult<T>(int BusinessCode, string BusinessMessage, T? ReturnObj)
    { public bool IsSuccess => BusinessCode == 0; }
    /// <summary>
    /// 统一的业务响应封装，使用业务码、提示信息和返回对象表达接口结果。
    /// </summary>
    public static class BizResult
    {
        public static BizResult<T> BusinessSuccess<T>(T value) => new(0, "success", value);
    }
    /// <summary>
    /// 为本地适配层提供空引用及空字符串参数校验。
    /// </summary>
    public static class ArgumentCheck
    {
        public static void NotNull(object? value, string name = "", string message = "")
        { if (value is null) throw new ArgumentException(message, name); }
        public static void NotNullOrEmpty(string? value, string name = "", string message = "")
        { if (string.IsNullOrEmpty(value)) throw new ArgumentException(message, name); }
    }
}
namespace SmartAssistant.Platform
{
    /// <summary>
    /// 定义业务失败、资源不存在、访问拒绝和并发冲突等统一错误码。
    /// </summary>
    public static class BusinessCodes
    {
        public const int BusinessException = 1001, NotFound = 1002, Forbidden = 1003, Conflict = 1004;
    }
    /// <summary>
    /// 携带业务码与业务提示，由宿主中间件转换为统一的 HTTP 错误响应。
    /// </summary>
    public sealed class BizResultException(int businessCode, string businessMessage) : Exception(businessMessage)
    {
        public int BusinessCode { get; } = businessCode;
        public string BusinessMessage { get; } = businessMessage;
    }
}
namespace SmartAssistant.Platform.Events
{
    /// <summary>
    /// 领域事件的基础类型，事件标识用于关联本地 Outbox 记录。
    /// </summary>
    public abstract class DomainEvent { public string Id { get; set; } = Guid.NewGuid().ToString("N"); }
}
namespace SmartAssistant.Platform.Tenant
{
    /// <summary>
    /// 提供当前请求所属的租户和组织范围。
    /// </summary>
    public interface ITenantContext { string? TenantId { get; } string? OrgId { get; } }
    /// <summary>
    /// 为独立部署提供固定的单租户、单组织上下文。
    /// </summary>
    public sealed class LocalTenantContext : ITenantContext
    {
        public string TenantId => "assistant-local";
        public string OrgId => "assistant-local";
    }
}
namespace SmartAssistant.Authentication
{
    /// <summary>
    /// 从当前请求的身份声明中提取领域审计所需的操作人标识。
    /// </summary>
    public static class UserContextExtensions
    {
        public static string GetUserId(this ClaimsPrincipal principal) =>
            principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new SmartAssistant.Platform.BizResultException(1003, "authentication_required");
    }
}
namespace SmartAssistant.Modules.Admin.Public.Contracts
{
    /// <summary>
    /// 定义管理接口使用的组织读取与设置写入权限标识。
    /// </summary>
    public static class AdminPermissionConstants
    {
        public const string OrgRead = "org.read", OrgSettingsWrite = "org.settings.write";
    }
    /// <summary>
    /// 约定组织权限与系统管理权限的查询及强制校验操作。
    /// </summary>
    public interface IAdminPermissionService
    {
        Task<bool> HasOrgPermissionAsync(string actor, string orgId, string permission);
        Task RequireOrgPermissionAsync(string actor, string orgId, string permission);
        Task RequireSuperAdminAsync(string actor);
    }
    /// <summary>
    /// 根据独立应用的固定操作人与组织范围实施管理权限校验。
    /// </summary>
    public sealed class LocalAdminPermissionService : IAdminPermissionService
    {
        public Task<bool> HasOrgPermissionAsync(string actor, string orgId, string permission)
            => Task.FromResult(actor == "administrator" && orgId == "assistant-local");
        public Task RequireOrgPermissionAsync(string actor, string orgId, string permission)
        {
            if (actor != "administrator" || orgId != "assistant-local")
                throw new SmartAssistant.Platform.BizResultException(1003, "permission_denied");
            return Task.CompletedTask;
        }
        public Task RequireSuperAdminAsync(string actor)
        {
            if (actor != "administrator") throw new SmartAssistant.Platform.BizResultException(1003, "permission_denied");
            return Task.CompletedTask;
        }
    }
}
