#nullable enable
using System;
using SqlSugar;

namespace SmartAssistant.Services.Domain.Entities;

/// <summary>
/// 查询草稿知识库引用时使用的数据库投影。
/// </summary>
[SugarTable("assistant_draft")]
internal sealed class AssistantDraftReferenceEntity
{

    /// <summary>
    /// 保存尚未发布的可编辑助手配置。
    /// </summary>
    [SugarColumn(ColumnName = "draft_json")]
    public string DraftJson { get; set; } = "{}";
}
