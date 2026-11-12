namespace AI.Assistant.Extensions.Common
{
    /// <summary>
    /// 为本地适配层提供空引用及空字符串参数校验。
    /// </summary>
    public static class ArgumentCheck
    {
        /// <summary>
        /// 校验引用不为空；缺失时抛出参数错误。
        /// </summary>
        public static void NotNull(object? value, string name = "", string message = "")
        {
            if (value is null)
                throw new ArgumentException(message, name);
        }
        /// <summary>
        /// 校验集合或文本不为空；缺失时抛出参数错误。
        /// </summary>
        public static void NotNullOrEmpty(string? value, string name = "", string message = "")
        {
            if (string.IsNullOrEmpty(value))
                throw new ArgumentException(message, name);
        }
    }
}
namespace AI.Assistant.Platform
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
namespace AI.Assistant.Platform.Events
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
