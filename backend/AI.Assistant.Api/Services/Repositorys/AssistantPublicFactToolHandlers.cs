#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AI.Assistant.Services.Contracts;
using AI.Assistant.Services.Event.Public.Contracts;
using AI.Assistant.Services.Schedule.Public.Contracts;
using AI.Assistant.Infrastructure;
using AI.Assistant.Models.Runtime;

namespace AI.Assistant.Services.Repositorys;

/// <summary>
/// 解析原业务工具的输入，并检查工具请求的范围约束。
/// </summary>
internal static class AssistantToolInput
{
    /// <summary>
    /// 校验字符串必填，并返回去除首尾空白后的值。
    /// </summary>
    public static string RequiredString(JsonElement input, string name)
    {
        if (input.ValueKind != JsonValueKind.Object
            || !input.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
            throw new BizResultException(BusinessCodes.BusinessException, $"assistant_tool_argument_{name}_required");
        return value.GetString()!.Trim();
    }

    /// <summary>
    /// 读取可选 JSON 字符串；字段缺失时返回空值，并校验类型和长度。
    /// </summary>
    public static string? OptionalString(JsonElement input, string name, int maxLength = 160)
    {
        if (input.ValueKind != JsonValueKind.Object || !input.TryGetProperty(name, out var value)
            || value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind != JsonValueKind.String)
            throw new BizResultException(BusinessCodes.BusinessException, $"assistant_tool_argument_{name}_invalid");
        var text = value.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (text.Length > maxLength)
            throw new BizResultException(BusinessCodes.BusinessException, $"assistant_tool_argument_{name}_too_long");
        return text;
    }

    /// <summary>
    /// 读取可选 JSON 整数；字段缺失时采用默认值，并限制在指定范围内。
    /// </summary>
    public static int OptionalInt(JsonElement input, string name, int fallback, int min, int max)
    {
        if (input.ValueKind != JsonValueKind.Object || !input.TryGetProperty(name, out var value)
            || value.ValueKind == JsonValueKind.Null)
            return fallback;
        if (!value.TryGetInt32(out var parsed) || parsed < min || parsed > max)
            throw new BizResultException(BusinessCodes.BusinessException, $"assistant_tool_argument_{name}_invalid");
        return parsed;
    }
}

/// <summary>
/// 原活动概览工具的参考实现；独立项目未接入对应业务模块，编译时排除此文件。
/// </summary>
public sealed class AssistantEventOverviewToolHandler : IAssistantToolHandler
{
    /// <summary>
    /// 保存事件公开查询依赖，供助手事件概览工具处理器执行相关操作。
    /// </summary>
    private readonly IEventPublicQuery _events;
    /// <summary>
    /// 接收并保存助手事件概览工具处理器执行所需的依赖，建立后续操作使用的运行状态。
    /// </summary>
    public AssistantEventOverviewToolHandler(IEventPublicQuery events) => _events = events;
    /// <summary>
    /// 关联当前使用的工具定义。
    /// </summary>
    public string ToolId => "public.event.overview.read";
    /// <summary>
    /// 表示助手事件概览工具处理器的契约版本信息。
    /// </summary>
    public string ContractVersion => "v1";

    /// <summary>
    /// 执行助手事件概览工具操作，并响应调用方的取消请求。
    /// </summary>
    public async Task<JsonElement> ExecuteAsync(JsonElement input, AssistantScopeSnapshot scope, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var eventId = AssistantToolInput.RequiredString(input, "eventId");
        var value = await _events.GetSummaryAsync(eventId);
        var publicEvent = EnsurePublicEvent(value, scope);

        return JsonSerializer.SerializeToElement(new
        {
            eventId = publicEvent.Id,
            name = publicEvent.Name,
            status = publicEvent.Status,
            startDate = publicEvent.StartDate,
            endDate = publicEvent.EndDate,
            timezone = publicEvent.Timezone,
            location = publicEvent.VenueId,
            venueType = publicEvent.VenueType,
            registrationOpen = publicEvent.RegistrationOpen,
            tradingOpen = publicEvent.TradingOpen
        });
    }

    /// <summary>
    /// 校验事件可安全发送给公开订阅者，不泄漏内部执行数据。
    /// </summary>
    internal static EventSummaryDto EnsurePublicEvent(EventSummaryDto? value, AssistantScopeSnapshot scope)
    {
        if (value is null
            || (value.Status is not ("published" or "ended" or "canceled")))
            throw new BizResultException(BusinessCodes.NotFound, "assistant_public_event_not_found");
        return value;
    }
}

/// <summary>
/// 原活动日程工具的参考实现；独立项目未接入对应业务模块，编译时排除此文件。
/// </summary>
public sealed class AssistantEventScheduleToolHandler : IAssistantToolHandler
{
    /// <summary>
    /// 保存事件公开查询依赖，供助手事件日程工具处理器执行相关操作。
    /// </summary>
    private readonly IEventPublicQuery _events;
    /// <summary>
    /// 保存日程公开查询依赖，供助手事件日程工具处理器执行相关操作。
    /// </summary>
    private readonly ISchedulePublicQuery _schedules;
    /// <summary>
    /// 接收并保存助手事件日程工具处理器执行所需的依赖，建立后续操作使用的运行状态。
    /// </summary>
    public AssistantEventScheduleToolHandler(IEventPublicQuery events, ISchedulePublicQuery schedules)
        => (_events, _schedules) = (events, schedules);
    /// <summary>
    /// 关联当前使用的工具定义。
    /// </summary>
    public string ToolId => "public.event.schedule.read";
    /// <summary>
    /// 表示助手事件日程工具处理器的契约版本信息。
    /// </summary>
    public string ContractVersion => "v1";

    /// <summary>
    /// 执行助手事件日程工具操作，并响应调用方的取消请求。
    /// </summary>
    public async Task<JsonElement> ExecuteAsync(JsonElement input, AssistantScopeSnapshot scope, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var eventId = AssistantToolInput.RequiredString(input, "eventId");
        var summary = await _events.GetSummaryAsync(eventId);
        AssistantEventOverviewToolHandler.EnsurePublicEvent(summary, scope);

        var keyword = AssistantToolInput.OptionalString(input, "keyword", 120);
        var dateText = AssistantToolInput.OptionalString(input, "date", 32);
        DateTime? date = null;
        if (dateText is not null)
        {
            if (!DateTime.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                throw new BizResultException(BusinessCodes.BusinessException, "assistant_tool_argument_date_invalid");
            date = parsed.Date;
        }
        var limit = AssistantToolInput.OptionalInt(input, "limit", 20, 1, 50);
        var items = await _schedules.ListScheduleItemsByEventAsync(
            eventId, keyword: keyword, status: "published", date: date, limit: limit);
        var visible = items.Where(item => string.Equals(item.Status, "published", StringComparison.OrdinalIgnoreCase))
            .Take(limit)
            .Select(item => new
            {
                id = item.Id,
                title = item.Title,
                summary = item.Description,
                startTime = item.StartTime,
                endTime = item.EndTime,
                location = item.Location,
                roomId = item.RoomId,
                status = item.Status
            }).ToArray();
        return JsonSerializer.SerializeToElement(new { eventId, items = visible });
    }
}
