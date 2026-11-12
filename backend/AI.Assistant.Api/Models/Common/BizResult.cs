#nullable enable
namespace AI.Assistant.Models.Common;

/// <summary>
/// 统一的业务响应封装，使用业务码、提示信息和返回对象表达接口结果。
/// </summary>
public sealed record BizResult<T>(
    int BusinessCode,
    string BusinessMessage,
    T? ReturnObj)
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
