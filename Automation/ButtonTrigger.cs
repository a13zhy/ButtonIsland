using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using ButtonIsland.Services;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.Shared;
using ClassIsland.Shared.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace ButtonIsland.Automation;

/// <summary>触发器 ID 常量。</summary>
public static class ButtonTriggerIds
{
    public const string Clicked = "a13zhy.buttonisland.buttonClicked";

    public const string Reclicked = "a13zhy.buttonisland.buttonReclicked";

    /// <summary>设置里填这个 ID 表示"任意按钮"。</summary>
    public const string AnyButtonId = "*";

    public static bool IsOurs(string? id) => id == Clicked || id == Reclicked;
}

/// <summary>
/// 触发器设置：只记"监听哪个按钮"。
///
/// 这个类型必须是 TriggerSettingsControlBase{T} 的直接泛型参数：
/// ClassIsland 的 ActivateTrigger 用 SettingsControlType.BaseType.GetGenericArguments()
/// 取设置类型，中间多套一层抽象基类就拿不到类型，设置会一直是 null（触发器永远不触发）。
/// </summary>
public class ButtonTriggerSettings
{
    public string ButtonId { get; set; } = "";
}

/// <summary>
/// 触发器基类：订阅按钮点击事件总线，命中自己设置的按钮就 Trigger()。
/// Trigger() 是 protected，只能由触发器自己调用 —— 所以必须走这种"实例自己订阅"的模式。
/// </summary>
public abstract class ButtonTriggerBase : TriggerBase<ButtonTriggerSettings>
{
    private readonly ButtonEventHub _hub;
    private readonly bool _reclick;

    protected ButtonTriggerBase(ButtonEventHub hub, bool reclick)
    {
        _hub = hub;
        _reclick = reclick;
    }

    public override void Loaded()
    {
        _hub.Clicked += OnClicked;
        var actionSet = AssociatedWorkflow?.ActionSet;
        ButtonStore.Log(
            $"触发器加载：{TriggerName}，按钮='{ResolveSettings()?.ButtonId}'，原始设置类型={RawSettingsTypeName()}" +
            $"，工作流「{actionSet?.Name}」(允许反转={actionSet?.IsRevertEnabled}, 状态={actionSet?.Status})");
    }

    public override void UnLoaded()
    {
        _hub.Clicked -= OnClicked;
        ButtonStore.Log($"触发器卸载：{TriggerName}");
    }

    private void OnClicked(string buttonId, bool isAgain)
    {
        // 「被点击」= 每一次点击都触发；
        // 「被再次点击」= 只在按钮处于"开"状态（上一次点击过）的那一次点击触发。
        if (_reclick && !isAgain)
        {
            return;
        }

        // 每次点击都重新取一次设置：用户在自动化里改了按钮，不用重启也生效。
        var settings = ResolveSettings();
        var target = settings?.ButtonId?.Trim();

        if (string.IsNullOrEmpty(target))
        {
            ButtonStore.Log($"触发器跳过：{TriggerName} 还没选按钮（设置类型={RawSettingsTypeName()}）");
            return;
        }

        // "*" = 任意按钮。
        if (target != ButtonTriggerIds.AnyButtonId && target != buttonId)
        {
            return;
        }

        var actionSet = AssociatedWorkflow?.ActionSet;
        var revert = actionSet?.IsRevertEnabled == true;
        var status = actionSet?.Status ?? ActionSetStatus.Normal;

        try
        {
            // 工作流开了「允许反转」= ClassIsland 把它当成一个开关量：
            //   Trigger()        → 执行动作组（要求 Status == Normal）
            //   TriggerRevert()  → 恢复动作组（要求 Status == IsOn）
            // 所以「再次点击」在这种工作流里必须走 TriggerRevert()，否则会被
            // TriggerTriggered 里那行 `IsRevertEnabled && Status != Normal => return` 静默丢掉。
            if (revert)
            {
                if (_reclick)
                {
                    if (status != ActionSetStatus.IsOn)
                    {
                        ButtonStore.Log(
                            $"触发器跳过：{TriggerName} 命中，但工作流「{actionSet?.Name}」允许反转且当前状态={status}，恢复请求会被 ClassIsland 忽略");
                        return;
                    }

                    ButtonStore.Log($"触发器命中恢复：{TriggerName}（工作流「{actionSet?.Name}」）");
                    TriggerRevert();
                    return;
                }

                if (status != ActionSetStatus.Normal)
                {
                    ButtonStore.Log(
                        $"触发器被丢弃：{TriggerName} 命中，但工作流「{actionSet?.Name}」开了「允许反转」且状态={status}，" +
                        "ClassIsland 会直接 return —— 关掉「允许反转」，或改用「被再次点击」触发恢复");
                    return;
                }
            }

            ButtonStore.Log($"触发器命中：{TriggerName}（再次点击={isAgain}，允许反转={revert}，状态={status}）");
            Trigger();

            // ClassIsland 执行完会把允许反转的动作组状态改成「已开启」，
            // 用它来给日志一个"到底执行没执行"的结论。
            if (revert && actionSet != null)
            {
                var set = actionSet;
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(1500);
                    ButtonStore.Log($"触发器执行结果：工作流「{set.Name}」动作组状态 = {set.Status}");
                });
            }
        }
        catch (Exception ex)
        {
            ButtonStore.Log($"触发器调用 Trigger() 失败：{ex}");
        }
    }

    /// <summary>
    /// 拿设置。正常情况下 ClassIsland 已经塞了强类型实例；
    /// 万一 SettingsInternal 还是 JsonElement（早期版本/异常路径），这里兜底反序列化。
    /// </summary>
    private ButtonTriggerSettings? ResolveSettings()
    {
        var typed = Settings;
        if (typed != null)
        {
            return typed;
        }

        var raw = RawSettings();
        if (raw is JsonElement json)
        {
            try
            {
                var result = JsonSerializer.Deserialize<ButtonTriggerSettings>(json.GetRawText());
                if (result != null)
                {
                    ButtonStore.Log($"触发器设置从 JsonElement 反序列化成功：{result.ButtonId}");
                    return result;
                }
            }
            catch (Exception ex)
            {
                ButtonStore.Log($"触发器设置反序列化失败：{ex.Message}");
            }
        }

        return null;
    }

    private object? RawSettings()
    {
        try
        {
            var type = GetType();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            while (type != null && type.GetProperty("SettingsInternal", flags) == null)
            {
                type = type.BaseType;
            }

            return type?.GetProperty("SettingsInternal", flags)?.GetValue(this);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>SettingsInternal 是 internal，用反射读它，只在写日志时用。</summary>
    private string RawSettingsTypeName()
    {
        var raw = RawSettings();
        return raw == null ? "null" : raw.GetType().Name;
    }

    private string TriggerName => _reclick ? "再次点击" : "点击";
}

/// <summary>当指定按钮被点击时（每次点击都触发）。</summary>
[TriggerInfo(ButtonTriggerIds.Clicked, "ButtonIsland 按钮被点击", "\uE7C8")]
public class ButtonClickedTrigger : ButtonTriggerBase
{
    public ButtonClickedTrigger(ButtonEventHub hub) : base(hub, reclick: false)
    {
    }
}

/// <summary>当同一个按钮再次被点击时（即它处于"开"状态下的那一次点击）。</summary>
[TriggerInfo(ButtonTriggerIds.Reclicked, "ButtonIsland 按钮被再次点击", "\uE7C9")]
public class ButtonReclickedTrigger : ButtonTriggerBase
{
    public ButtonReclickedTrigger(ButtonEventHub hub) : base(hub, reclick: true)
    {
    }
}

/// <summary>
/// 设置面板的具体界面：一个下拉框选按钮 + 一个文本框显示/手填按钮 ID。
/// 放在独立类里，是为了让两个具体设置控件都能**直接**继承 TriggerSettingsControlBase{T}。
/// </summary>
internal sealed class ButtonSettingsPanel
{
    private readonly TriggerSettingsControlBase<ButtonTriggerSettings> _owner;
    private readonly ComboBox _combo;
    private readonly TextBox _idBox;
    private readonly TextBlock _hint;
    private bool _suppress;

    public ButtonSettingsPanel(TriggerSettingsControlBase<ButtonTriggerSettings> owner, string hintText)
    {
        _owner = owner;

        _combo = new ComboBox
        {
            MinWidth = 200,
            PlaceholderText = "（选择按钮）"
        };
        _combo.SelectionChanged += (_, _) =>
        {
            if (_suppress)
            {
                return;
            }

            Apply((_combo.SelectedItem as ButtonChoice)?.Id ?? "");
        };

        _idBox = new TextBox
        {
            Width = 190,
            Watermark = "按钮 ID",
            VerticalContentAlignment = VerticalAlignment.Center
        };
        _idBox.TextChanged += (_, _) =>
        {
            if (_suppress)
            {
                return;
            }

            Apply(_idBox.Text?.Trim() ?? "");
        };

        _hint = new TextBlock
        {
            Text = hintText,
            FontSize = 11,
            Opacity = 0.65,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };

        var refresh = new Button
        {
            Content = "刷新按钮列表",
            Padding = new Thickness(10, 4, 10, 4)
        };
        refresh.Click += (_, _) => Refresh();

        // 等于在悬浮窗上点一下这个按钮：用来验证"触发器 → 自动化"整条链路。
        var fire = new Button
        {
            Content = "试触发一次",
            Padding = new Thickness(10, 4, 10, 4)
        };
        fire.Click += (_, _) =>
        {
            var id = _owner.Settings?.ButtonId?.Trim();
            if (string.IsNullOrEmpty(id) || id == ButtonTriggerIds.AnyButtonId)
            {
                _hint.Text = "先选一个具体按钮才能试触发（「任意按钮」没法模拟）。";
                return;
            }

            var hub = ButtonEventHub.Current ?? IAppHost.Host?.Services.GetService<ButtonEventHub>();
            if (hub == null)
            {
                _hint.Text = "插件服务还没起来，稍后再试。";
                return;
            }

            var again = hub.Click(id);
            _hint.Text = $"已模拟一次{(again ? "再次" : "")}点击，去看自动化有没有执行。";
        };

        Content = new StackPanel
        {
            Spacing = 6,
            Margin = new Thickness(0, 6, 0, 6),
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children = { _combo, _idBox, refresh, fire }
                },
                _hint
            }
        };
    }

    public Control Content { get; }

    public void Refresh()
    {
        var store = ButtonStore.Current ?? IAppHost.Host?.Services.GetService<ButtonStore>();
        store?.Load();

        var choices = new List<ButtonChoice>
        {
            new(ButtonTriggerIds.AnyButtonId, "（任意按钮）")
        };
        if (store != null)
        {
            choices.AddRange(store.Config.Buttons.Select(
                b => new ButtonChoice(b.Id, string.IsNullOrWhiteSpace(b.Name) ? "未命名按钮" : b.Name)));
        }

        _suppress = true;
        _combo.ItemsSource = choices;
        var current = _owner.Settings?.ButtonId ?? "";
        _combo.SelectedItem = choices.FirstOrDefault(c => c.Id == current);
        _idBox.Text = current;
        _suppress = false;

        if (string.IsNullOrEmpty(current))
        {
            _hint.Text = "还没选按钮 —— 不选按钮这条触发器不会触发。";
        }
        else if (current == ButtonTriggerIds.AnyButtonId)
        {
            _hint.Text = "当前绑定：所有按钮（任何一个按钮被点击都会触发）。";
        }
        else
        {
            var name = store?.Config.Buttons.FirstOrDefault(b => b.Id == current)?.Name;
            _hint.Text = name == null
                ? $"当前绑定：{current}（按钮列表里找不到这个 ID，请检查）"
                : $"当前绑定：「{name}」";
        }
    }

    private void Apply(string id)
    {
        if (_owner.Settings != null)
        {
            _owner.Settings.ButtonId = id;
        }

        ButtonStore.Log($"触发器设置改为按钮：'{id}'");

        _suppress = true;
        _idBox.Text = id;
        if (_combo.SelectedItem is ButtonChoice current && current.Id != id)
        {
            _combo.SelectedItem = null;
        }

        _suppress = false;

        var store = ButtonStore.Current;
        if (string.IsNullOrEmpty(id))
        {
            _hint.Text = "还没选按钮 —— 不选按钮这条触发器不会触发。";
            return;
        }

        if (id == ButtonTriggerIds.AnyButtonId)
        {
            _hint.Text = "当前绑定：所有按钮（任何一个按钮被点击都会触发）。";
            return;
        }

        var name = store?.Config.Buttons.FirstOrDefault(b => b.Id == id)?.Name;
        _hint.Text = name == null ? $"当前绑定：{id}" : $"当前绑定：「{name}」";
    }

    private sealed class ButtonChoice
    {
        public ButtonChoice(string id, string name)
        {
            Id = id;
            Name = name;
        }

        public string Id { get; }

        public string Name { get; }

        public override string ToString() => Name;
    }
}

/// <summary>被点击触发器的设置控件。必须直接继承 TriggerSettingsControlBase{T}。</summary>
public class ButtonClickedTriggerSettingsControl : TriggerSettingsControlBase<ButtonTriggerSettings>
{
    private readonly ButtonSettingsPanel _panel;

    public ButtonClickedTriggerSettingsControl()
    {
        _panel = new ButtonSettingsPanel(
            this,
            "按钮每点一次都触发这个自动化。想区分「第一次」和「再点一次」，用另一个触发器。");
        Content = _panel.Content;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _panel.Refresh();
    }
}

/// <summary>被再次点击触发器的设置控件。必须直接继承 TriggerSettingsControlBase{T}。</summary>
public class ButtonReclickedTriggerSettingsControl : TriggerSettingsControlBase<ButtonTriggerSettings>
{
    private readonly ButtonSettingsPanel _panel;

    public ButtonReclickedTriggerSettingsControl()
    {
        _panel = new ButtonSettingsPanel(
            this,
            "只有当这个按钮已经处于「开」状态（上次点击过它）时，这一次点击才触发。");
        Content = _panel.Content;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _panel.Refresh();
    }
}
