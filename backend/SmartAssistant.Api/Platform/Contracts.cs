using System.Security.Claims;

namespace SmartAssistant.Extensions.Common
{
    /// <summary>
    /// 统一的业务响应封装，使用业务码、提示信息和返回对象表达接口结果。
    /// </summary>
    public sealed record BizResult<T>(int BusinessCode, string BusinessMessage, T? ReturnObj)
    {
        /// <summary>
        /// 表示对应业务操作是否成功。
        /// </summary>
        public bool IsSuccess => BusinessCode == 0;
    }
    /// <summary>
    /// 统一的业务响应封装，使用业务码、提示信息和返回对象表达接口结果。
    /// </summary>
    public static class BizResult
    {
        /// <summary>
        /// 将业务返回值包装为统一格式的成功响应。
        /// </summary>
        public static BizResult<T> BusinessSuccess<T>(T value) => new(0, "success", value);
    }
    /// <summary>
    /// 为本地适配层提供空引用及空字符串参数校验。
    /// </summary>
    public static class ArgumentCheck
    {
        /// <summary>
        /// 校验引用不为空；缺失时抛出参数错误。
        /// </summary>
        public static void NotNull(object? value, string name = "", string message = "")
        { if (value is null) throw new ArgumentException(message, name); }
        /// <summary>
        /// 校验集合或文本不为空；缺失时抛出参数错误。
        /// </summary>
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
        /// <summary>
        /// 定义业务代码使用的业务配置值。
        /// </summary>
        public const int BusinessException = 1001, NotFound = 1002, Forbidden = 1003, Conflict = 1004;
    }
    /// <summary>
    /// 携带业务码与业务提示，由宿主中间件转换为统一的 HTTP 错误响应。
    /// </summary>
    public sealed class BizResultException(int businessCode, string businessMessage) : Exception(businessMessage)
    {
        /// <summary>
        /// 保存统一业务响应的结果代码。
        /// </summary>
        public int BusinessCode { get; } = businessCode;
        /// <summary>
        /// 保存统一业务响应的提示信息。
        /// </summary>
        public string BusinessMessage { get; } = businessMessage;
    }
}
namespace SmartAssistant.Platform.Events
{
    /// <summary>
    /// 领域事件的基础类型，事件标识用于关联本地 Outbox 记录。
    /// </summary>
    public abstract class DomainEvent
    {
        /// <summary>
        /// 记录当前数据的唯一标识。
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
    }
}
namespace SmartAssistant.Authentication
{
    /// <summary>
    /// 从当前请求的身份声明中提取领域审计所需的操作人标识。
    /// </summary>
    public static class UserContextExtensions
    {
        /// <summary>
        /// 获取用户。
        /// </summary>
        public static string GetUserId(this ClaimsPrincipal principal) =>
            principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new SmartAssistant.Platform.BizResultException(1003, "authentication_required");
    }
}
namespace SmartAssistant.Modules.Admin.Public.Contracts
{
    /// <summary>
    /// 约定系统管理权限的强制校验操作。
    /// </summary>
    public interface IAdminPermissionService
    {
        /// <summary>
        /// 校验当前操作人具有平台超级管理员权限，否则拒绝访问管理接口。
        /// </summary>
        Task RequireSuperAdminAsync(string actor);
    }
    /// <summary>
    /// 根据独立应用的固定操作人实施系统管理权限校验。
    /// </summary>
    public sealed class LocalAdminPermissionService : IAdminPermissionService
    {
        /// <summary>
        /// 校验当前操作人具有平台超级管理员权限，否则拒绝访问管理接口。
        /// </summary>
        public Task RequireSuperAdminAsync(string actor)
        {
            if (actor != "administrator") throw new SmartAssistant.Platform.BizResultException(1003, "permission_denied");
            return Task.CompletedTask;
        }
    }
}
