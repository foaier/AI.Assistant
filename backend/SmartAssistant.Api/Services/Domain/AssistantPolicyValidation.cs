#nullable enable
using System.Text.RegularExpressions;
using SmartAssistant.Models.Policies;
using SmartAssistant.Platform;

namespace SmartAssistant.Services.Domain;

/// <summary>
/// 在模型供应商及业务策略进入持久化前校验必要字段和能力约束。
/// </summary>
public static class AssistantPolicyValidation
{
    /// <summary>
    /// 校验模型供应商设置及其可用模型约束。
    /// </summary>
    public static AssistantModelServiceSettings Provider(AssistantModelServiceSettings value)
    {
        if (value is null) Fail("assistant_provider_settings_required");
        var name = Text(value!.Name, 120, "assistant_provider_name_required");
        if (!Uri.TryCreate(value.BaseUrl?.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0
            || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            Fail("assistant_provider_base_url_invalid");
        var reference = Text(value.SecretReference, 4096, "assistant_provider_secret_reference_required");
        if (string.Equals(reference, "[configured]", StringComparison.Ordinal))
            Fail("assistant_provider_secret_reference_invalid");
        var version = Text(value.SecretVersion, 80, "assistant_provider_secret_version_required");
        if (value.TimeoutSeconds is < 5 or > 180) Fail("assistant_provider_timeout_invalid");
        if (value.Models is null || value.Models.Count is < 1 or > 100) Fail("assistant_provider_models_required");
        var models = value.Models!.Select(x => x is null
            ? throw Error("assistant_provider_model_invalid")
            : x with { Id = Text(x.Id, 120, "assistant_provider_model_invalid"), Purpose = x.Purpose?.Trim() ?? "" }).ToArray();
        if (models.Any(x => x.Purpose is not ("chat" or "embedding" or "rerank"))
            || models.Any(x => x.Purpose == "embedding"
                ? x.Dimension is null or < 1 or > 32768
                : x.Dimension is not null)
            || models.Select(x => (x.Id, x.Purpose)).Distinct().Count() != models.Length)
            Fail("assistant_provider_models_invalid");
        return value with { Name = name, BaseUrl = uri!.ToString().TrimEnd('/'), SecretReference = reference, SecretVersion = version, Models = models };
    }

    /// <summary>
    /// 校验业务策略设置中的范围、取值和相互依赖关系。
    /// </summary>
    public static AssistantBusinessPolicySettings Policy(AssistantBusinessPolicySettings value)
    {
        if (value is null) Fail("assistant_policy_settings_required");
        var name = Text(value!.Name, 120, "assistant_policy_name_required");
        var business = Text(value.BusinessCode, 80, "assistant_policy_business_required");
        if (!Regex.IsMatch(business, "^[a-z][a-z0-9_-]{0,79}$")) Fail("assistant_policy_business_invalid");
        if (value.ProviderRevision < 1 || string.IsNullOrWhiteSpace(value.ProviderId)) Fail("assistant_policy_provider_required");
        var model = Text(value.Model, 120, "assistant_policy_model_required");
        var instructions = Text(value.Instructions, 8000, "assistant_policy_instructions_required");
        if (!double.IsFinite(value.Temperature) || value.Temperature is < 0 or > 2
            || value.MaxOutputTokens is < 64 or > 16384 || value.RetrievalLimit is < 1 or > 20)
            Fail("assistant_policy_limits_invalid");
        var tools = value.Tools ?? Array.Empty<AssistantToolBinding>();
        if (tools.Count > 16 || tools.Any(x => x is null
            || string.IsNullOrWhiteSpace(x.ToolId)
            || x.ToolId.Trim().Length > 160
            || string.IsNullOrWhiteSpace(x.ContractVersion)
            || x.ContractVersion.Trim().Length > 64
            || !Regex.IsMatch(x.ToolId.Trim(), "^[a-z][a-z0-9._-]{0,159}$")
            || !Regex.IsMatch(x.ContractVersion.Trim(), "^[a-zA-Z0-9._-]{1,64}$")) == true
            || tools.Select(x => (x.ToolId.Trim(), x.ContractVersion.Trim())).Distinct().Count() != tools.Count)
            Fail("assistant_policy_tools_invalid");
        if (value.HumanHandoffEnabled) Fail("assistant_external_capability_not_ready");
        return value with { Name = name, BusinessCode = business, Model = model, Instructions = instructions,
            NoAnswerMessage = Text(value.NoAnswerMessage, 1000, "assistant_policy_no_answer_required"),
            Tools = value.Tools?.Select(x => x with { ToolId = x.ToolId.Trim(), ContractVersion = x.ContractVersion.Trim() }).ToArray() };
    }

    /// <summary>
    /// 校验必填文本及其最大长度，并返回去除首尾空白后的值。
    /// </summary>
    private static string Text(string? value, int max, string code)
        => string.IsNullOrWhiteSpace(value) || value.Trim().Length > max ? throw Error(code) : value.Trim();
    /// <summary>
    /// 构造包含稳定错误码的输入校验异常。
    /// </summary>
    private static BizResultException Error(string code) => new(BusinessCodes.BusinessException, code);
    /// <summary>
    /// 抛出带有稳定错误信息的业务校验异常。
    /// </summary>
    private static void Fail(string code) => throw Error(code);
}
