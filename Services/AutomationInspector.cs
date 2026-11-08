using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ButtonIsland.Automation;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Shared;
using ClassIsland.Shared.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace ButtonIsland.Services;

/// <summary>
/// 只读地看自动化配置，用来告诉用户"这个按钮被几条自动化引用了"。
/// 插件不执行任何工作流，执行权完全在 ClassIsland 的自动化引擎手里。
/// </summary>
public sealed class AutomationInspector
{
    private static IAutomationService? Service =>
        IAppHost.Host?.Services.GetService<IAutomationService>();

    /// <summary>当前进程里的实例（自检页用）。</summary>
    public static AutomationInspector? Current { get; private set; }

    public AutomationInspector()
    {
        Current = this;
    }

    /// <summary>统计有多少个触发器引用了这个按钮。</summary>
    public int CountReferences(string buttonId)
    {
        if (string.IsNullOrEmpty(buttonId))
        {
            return 0;
        }

        var count = 0;
        try
        {
            var service = Service;
            if (service == null)
            {
                return 0;
            }

            foreach (var workflow in service.Workflows)
            {
                foreach (var trigger in workflow.Triggers)
                {
                    if (!ButtonTriggerIds.IsOurs(trigger.Id))
                    {
                        continue;
                    }

                    if (ReadButtonId(trigger.Settings) == buttonId || ReadButtonId(trigger.Settings) == ButtonTriggerIds.AnyButtonId)
                    {
                        count++;
                    }
                }
            }
        }
        catch
        {
            // 自动化配置里偶尔会有半成品数据，读失败就当 0 条
        }

        return count;
    }

    /// <summary>所有引用了按钮的触发器名称，便于在编辑窗里显示。</summary>
    public List<string> DescribeReferences(string buttonId)
    {
        var result = new List<string>();
        try
        {
            var service = Service;
            if (service == null)
            {
                return result;
            }

            foreach (var workflow in service.Workflows)
            {
                foreach (var trigger in workflow.Triggers)
                {
                    if (!ButtonTriggerIds.IsOurs(trigger.Id) || ReadButtonId(trigger.Settings) != buttonId)
                    {
                        continue;
                    }

                    var name = trigger.Id == ButtonTriggerIds.Clicked ? "被点击" : "被再次点击";
                    result.Add($"{workflow.ActionSet?.Name} · {name}");
                }
            }
        }
        catch
        {
            // 同上
        }

        return result;
    }

    /// <summary>
    /// 逐条检查"引用了按钮的工作流"会不会真的执行，并给出会被 ClassIsland 静默丢弃的原因。
    ///
    /// ClassIsland 的 TriggerTriggered 里有一道静默闸门：
    ///   if (ActionSet.IsRevertEnabled &amp;&amp; ActionSet.Status != Normal) return;
    /// 动作组开了「允许反转」且状态不是「正常」时，点击不会执行、也不会留日志。
    /// </summary>
    public List<WorkflowCheck> Diagnose()
    {
        var result = new List<WorkflowCheck>();
        try
        {
            var service = Service;
            if (service == null)
            {
                return result;
            }

            foreach (var workflow in service.Workflows)
            {
                foreach (var trigger in workflow.Triggers)
                {
                    if (!ButtonTriggerIds.IsOurs(trigger.Id))
                    {
                        continue;
                    }

                    var actionSet = workflow.ActionSet;
                    var revert = actionSet?.IsRevertEnabled == true;
                    var status = actionSet?.Status;
                    var buttonId = ReadButtonId(trigger.Settings);
                    var isReclick = trigger.Id == ButtonTriggerIds.Reclicked;

                    var willRun = true;
                    var note = "";
                    if (string.IsNullOrEmpty(buttonId))
                    {
                        willRun = false;
                        note = "触发器没选按钮，永远不会触发";
                    }
                    else if (actionSet == null || !actionSet.IsEnabled)
                    {
                        willRun = false;
                        note = "动作组被禁用";
                    }
                    else if (revert && !isReclick && status != ActionSetStatus.Normal)
                    {
                        willRun = false;
                        note = "开了「允许反转」，但动作组状态不是「正常」→ ClassIsland 会静默丢弃这次触发。关掉「允许反转」，或改用「被再次点击」来恢复";
                    }
                    else if (revert && isReclick && status != ActionSetStatus.IsOn)
                    {
                        willRun = false;
                        note = "「被再次点击」在允许反转的工作流里是「恢复」语义，动作组当前不是「已开启」→ 不会被恢复";
                    }
                    else if (revert)
                    {
                        note = isReclick ? "点了是「恢复动作」（开关关掉）" : "第一次点是「执行动作」（开关打开）";
                    }
                    else
                    {
                        note = "每点一次都会执行";
                    }

                    result.Add(new WorkflowCheck(
                        actionSet?.Name ?? "(未命名工作流)",
                        isReclick ? "被再次点击" : "被点击",
                        buttonId,
                        revert,
                        status?.ToString() ?? "?",
                        willRun,
                        note));
                }
            }
        }
        catch
        {
            // 自检失败不影响页面
        }

        return result;
    }

    /// <summary>工作流自检结果。</summary>
    public sealed record WorkflowCheck(
        string WorkflowName,
        string TriggerName,
        string ButtonId,
        bool Revert,
        string Status,
        bool WillRun,
        string Note);

    /// <summary>把自动化里的触发器情况写进插件日志，便于排查"按钮点了没反应"。</summary>
    public void LogDiagnostics()
    {
        try
        {
            var service = Service;
            if (service == null)
            {
                ButtonStore.Log("诊断：自动化服务还不可用");
                return;
            }

            var total = 0;
            foreach (var workflow in service.Workflows)
            {
                foreach (var trigger in workflow.Triggers)
                {
                    total++;
                    ButtonStore.Log(
                        $"诊断：工作流「{workflow.ActionSet?.Name}」触发器 {trigger.Id}，设置类型 {trigger.Settings?.GetType().Name ?? "null"}，按钮={ReadButtonId(trigger.Settings)}");
                }
            }

            ButtonStore.Log($"诊断：共 {service.Workflows.Count} 条工作流 / {total} 个触发器");
        }
        catch (Exception ex)
        {
            ButtonStore.Log("诊断失败：" + ex.Message);
        }
    }

    private static string ReadButtonId(object? settings)
    {
        switch (settings)
        {
            case null:
                return "";
            case ButtonTriggerSettings typed:
                return typed.ButtonId ?? "";
            case JsonElement element when element.ValueKind == JsonValueKind.Object:
                return element.TryGetProperty("ButtonId", out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString() ?? ""
                    : "";
            default:
                return "";
        }
    }
}
