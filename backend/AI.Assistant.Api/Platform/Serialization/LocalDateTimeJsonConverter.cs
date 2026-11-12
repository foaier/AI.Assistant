using System.Text.Json;
using System.Text.Json.Serialization;

namespace AI.Assistant.Platform.Serialization;

/// <summary>
/// 统一按服务器本地时间读写日期，输出时保留时区偏移，避免前端误按 UTC 显示。
/// </summary>
public sealed class LocalDateTimeJsonConverter : JsonConverter<DateTime>
{
    /// <summary>
    /// 将请求中的日期还原为本地时间；没有时区标记的日期按本地时间解释。
    /// </summary>
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return AsLocal(reader.GetDateTime());
    }

    /// <summary>
    /// 将日期写为包含本地时区偏移的字符串，数据库读出的无时区日期保持原有钟面时间。
    /// </summary>
    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(AsLocal(value));
    }

    /// <summary>
    /// 为无时区日期补充本地标记，带时区的日期转换为对应的本地时间。
    /// </summary>
    private static DateTime AsLocal(DateTime value)
    {
        return value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Local)
            : value.ToLocalTime();
    }
}
