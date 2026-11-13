#nullable enable
using System.Text.Json;
using AI.Assistant.Models.Runtime;
using AI.Assistant.Infrastructure;

namespace AI.Assistant.Services.Application;

/// <summary>
/// 解析并校验助手展示配置，包括快捷链接、推荐问题和聊天背景色。
/// </summary>
public static class AssistantPresentation
{
    /// <summary>
    /// 定义助手展示配置使用的默认值颜色配置值。
    /// </summary>
    private const string DefaultPrimaryColor = "#2563eb";
    /// <summary>
    /// 定义助手展示配置使用的聊天文本颜色配置值。
    /// </summary>
    private const string ChatTextColor = "#172033";

    /// <summary>
    /// 判断链接是否符合允许的地址格式与协议约束。
    /// </summary>
    public static bool IsSafeLink(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2000 || value.Any(char.IsControl) || value.Contains('\\')) return false;
        if (value.StartsWith('/') && !value.StartsWith("//")) return true;
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && string.IsNullOrEmpty(uri.UserInfo) && !string.IsNullOrEmpty(uri.Host);
    }

    /// <summary>
    /// 仅允许安全的本地图标地址或受限 HTTPS 链接用于公开展示。
    /// </summary>
    private static bool IsSafeIconUrl(string? value)
    {
        if (!IsSafeLink(value)) return false;
        if (value!.StartsWith('/')) return true;
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps;
    }

    /// <summary>
    /// 读取并规范化助手展示的快捷链接配置。
    /// </summary>
    public static IReadOnlyList<AssistantQuickLink> Links(JsonElement configuration, bool validate = false)
    {
        var result = new List<AssistantQuickLink>();
        var enabled = !configuration.TryGetProperty("quickLinksEnabled", out var flag) || flag.ValueKind != JsonValueKind.False;
        if (!configuration.TryGetProperty("quickLinks", out var links)) return result;
        if (links.ValueKind != JsonValueKind.Array || links.GetArrayLength() > 12)
        {
            if (validate) throw Invalid();
            return result;
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in links.EnumerateArray())
        {
            var id = Text(item, "id"); var label = Text(item, "label"); var url = Text(item, "url");
            if (id.Length is 0 or > 100 || label.Length is 0 or > 60 || !IsSafeLink(url) || !ids.Add(id))
            {
                if (validate) throw Invalid();
                continue;
            }
            var icon = Text(item, "icon");
            if (icon is not ("map" or "calendar" or "help" or "contact" or "registration"
                or "exhibitor" or "products" or "transport" or "hotel" or "building" or "ticket" or "users" or "info"))
                icon = "link";
            string? iconUrl = null;
            if (item.TryGetProperty("iconUrl", out var customIcon))
            {
                if (customIcon.ValueKind == JsonValueKind.String)
                {
                    var candidate = customIcon.GetString()?.Trim();
                    if (!string.IsNullOrEmpty(candidate))
                    {
                        if (IsSafeIconUrl(candidate)) iconUrl = candidate;
                        else if (validate) throw Invalid();
                    }
                }
                else if (customIcon.ValueKind != JsonValueKind.Null && validate)
                {
                    throw Invalid();
                }
            }
            if (enabled && (!item.TryGetProperty("enabled", out var visible) || visible.ValueKind != JsonValueKind.False))
                result.Add(new(id, label, url, icon, iconUrl));
        }
        return result;
    }

    /// <summary>
    /// 读取并规范化建议问题列表。
    /// </summary>
    public static IReadOnlyList<string> Questions(JsonElement configuration, IEnumerable<string> knowledgeQuestions)
    {
        var own = configuration.TryGetProperty("suggestedQuestions", out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!.Trim())
            : Enumerable.Empty<string>();
        return own.Concat(knowledgeQuestions).Select(x => x.Trim()).Where(x => x.Length is > 0 and <= 200).Distinct(StringComparer.Ordinal).Take(20).ToArray();
    }

    /// <summary>
    /// 读取符合格式约束的聊天背景颜色。
    /// </summary>
    public static string? ChatBackgroundColor(JsonElement configuration)
    {
        var color = Text(configuration, "chatBackgroundColor");
        return IsHexColor(color) ? color : null;
    }

    /// <summary>
    /// 校验展示配置中的链接、推荐问题和背景等字段。
    /// </summary>
    public static void Validate(JsonElement configuration)
    {
        _ = Links(configuration, validate: true);
        ValidateTheme(configuration);
        if (configuration.TryGetProperty("suggestedQuestions", out var questions)
            && (questions.ValueKind != JsonValueKind.Array || questions.GetArrayLength() > 20
                || questions.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String || x.GetString()!.Trim().Length is 0 or > 200)))
            throw new BizResultException(BusinessCodes.BusinessException, "assistant_suggestions_invalid");
        foreach (var name in new[] { "historyEnabled", "quickLinksEnabled" })
            if (configuration.TryGetProperty(name, out var value) && value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new BizResultException(BusinessCodes.BusinessException, "assistant_configuration_invalid");
    }

    /// <summary>
    /// 校验聊天外观颜色和主题组合，拒绝不可读或格式错误的设置。
    /// </summary>
    private static void ValidateTheme(JsonElement configuration)
    {
        if (configuration.ValueKind != JsonValueKind.Object
            || !configuration.TryGetProperty("chatBackgroundColor", out var background))
            return;
        if (background.ValueKind == JsonValueKind.Null) return;
        if (background.ValueKind != JsonValueKind.String || !IsHexColor(background.GetString()))
            throw new BizResultException(BusinessCodes.BusinessException, "assistant_theme_color_invalid");

        var primary = Text(configuration, "primaryColor");
        if (primary.Length == 0) primary = DefaultPrimaryColor;
        if (!IsHexColor(primary)
            || ContrastRatio(primary, "#ffffff") < 4.5
            || ContrastRatio(background.GetString()!, ChatTextColor) < 4.5)
            throw new BizResultException(BusinessCodes.BusinessException, "assistant_theme_contrast_invalid");
    }

    /// <summary>
    /// 判断输入是否为格式有效的十六进制颜色值。
    /// </summary>
    private static bool IsHexColor(string? value)
        => value is { Length: 7 } && value[0] == '#'
            && value.AsSpan(1).ToString().All(Uri.IsHexDigit);

    /// <summary>
    /// 根据相对亮度计算前景色与背景色之间的对比度。
    /// </summary>
    private static double ContrastRatio(string first, string second)
    {
        var firstLuminance = RelativeLuminance(first);
        var secondLuminance = RelativeLuminance(second);
        return (Math.Max(firstLuminance, secondLuminance) + 0.05)
            / (Math.Min(firstLuminance, secondLuminance) + 0.05);
    }

    /// <summary>
    /// 按颜色通道的线性亮度计算相对亮度。
    /// </summary>
    private static double RelativeLuminance(string color)
    {
        /*
         * 从十六进制颜色值中读取指定颜色通道的数值。
         */
        double Channel(int offset)
        {
            var value = Convert.ToInt32(color.Substring(offset, 2), 16) / 255d;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(1) + 0.7152 * Channel(3) + 0.0722 * Channel(5);
    }

    /// <summary>
    /// 从 JSON 配置读取文本字段，并统一空值和空白字符处理。
    /// </summary>
    private static string Text(JsonElement value, string key)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var text) && text.ValueKind == JsonValueKind.String
            ? text.GetString()!.Trim() : string.Empty;
    /// <summary>
    /// 构造包含稳定业务错误码和消息的参数校验异常。
    /// </summary>
    private static BizResultException Invalid() => new(BusinessCodes.BusinessException, "assistant_quick_links_invalid");
}
