using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ButtonIsland.Automation;
using ButtonIsland.Services;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Enums.SettingsWindow;
using ClassIsland.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace ButtonIsland.Views;

/// <summary>
/// 插件设置页（ClassIsland「设置」窗口里的 ButtonIsland 页）。
///
/// 这里配的是"悬浮窗长什么样、怎么弹"和"快捷键"，按钮本身的增删改还是在按钮岛编辑窗里做。
/// 页面用 ClassIsland 自带的控件样式，跟随主题，不硬编码深色。
/// </summary>
[SettingsPageInfo("a13zhy.buttonisland.settings", "ButtonIsland 按钮岛", "\uE7C4", "\uE7C4", SettingsPageCategory.External)]
public class ButtonIslandSettingsPage : SettingsPageBase
{
    private readonly StackPanel _selfCheck = new() { Spacing = 6 };
    private readonly TextBlock _hotkeyStatus = new() { FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _saveStatus = new() { FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap };

    private readonly CheckBox _showFloating = new() { Content = "在屏幕右下角显示按钮岛（收起后是一条细手柄）" };
    private readonly CheckBox _autoCollapse = new() { Content = "面板展开后，鼠标离开一段时间自动收起" };
    private readonly NumericUpDown _autoSeconds = new() { Minimum = 3, Maximum = 600, Increment = 1, Width = 120 };
    private readonly NumericUpDown _handleWidth = new() { Minimum = 12, Maximum = 64, Increment = 2, Width = 120 };
    private readonly NumericUpDown _bottomMargin = new() { Minimum = 0, Maximum = 600, Increment = 4, Width = 120 };
    private readonly TextBox _hotkeyToggle = new() { Width = 150, Watermark = "Ctrl+Alt+B" };
    private readonly TextBox _hotkeyEditor = new() { Width = 150, Watermark = "Ctrl+Alt+E" };

    private ButtonStore? Store => ButtonStore.Current;

    private bool _loading;

    public ButtonIslandSettingsPage()
    {
        Content = Build();
        LoadValues();
        RefreshSelfCheck();
        _hotkeyStatus.Text = "当前状态：" + (GlobalHotkeyService.Current?.Status ?? "快捷键服务还没起来（重启 ClassIsland 试试）");
    }

    private static void OnCheckChanged(CheckBox box, Action onChanged)
    {
        box.PropertyChanged += (_, e) =>
        {
            if (e.Property == Avalonia.Controls.Primitives.ToggleButton.IsCheckedProperty)
            {
                onChanged();
            }
        };
    }

    private static void OnNumberChanged(NumericUpDown box, Action onChanged)
    {
        box.PropertyChanged += (_, e) =>
        {
            if (e.Property == NumericUpDown.ValueProperty)
            {
                onChanged();
            }
        };
    }

    private Control Build()
    {
        OnCheckChanged(_showFloating, SaveAndApply);
        OnCheckChanged(_autoCollapse, SaveAndApply);
        OnNumberChanged(_autoSeconds, SaveAndApply);
        OnNumberChanged(_handleWidth, SaveAndApply);
        OnNumberChanged(_bottomMargin, SaveAndApply);

        var toggleTest = new Button { Content = "展开 / 收起", Padding = new Thickness(12, 5, 12, 5) };
        toggleTest.Click += (_, _) =>
        {
            FloatingWindowController.Current?.TogglePanel();
            _saveStatus.Text = "已切换面板状态。";
        };

        var openEditor = new Button { Content = "打开按钮编辑窗", Padding = new Thickness(12, 5, 12, 5) };
        openEditor.Click += (_, _) => FloatingWindowController.Current?.OpenEditor();

        var applyHotkey = new Button { Content = "应用快捷键", Padding = new Thickness(12, 5, 12, 5) };
        applyHotkey.Click += (_, _) => SaveAndApply();

        var openLog = new Button { Content = "打开插件日志", Padding = new Thickness(12, 5, 12, 5) };
        openLog.Click += (_, _) => OpenLog();

        var refreshCheck = new Button { Content = "刷新自检", Padding = new Thickness(12, 5, 12, 5) };
        refreshCheck.Click += (_, _) => RefreshSelfCheck();

        var root = new StackPanel
        {
            Spacing = 14,
            Margin = new Thickness(0, 0, 18, 24)
        };

        root.Children.Add(new TextBlock
        {
            Text = "按钮岛上的按钮点一下，会通过「自动化」里的触发器交给你自己的自动化工作流去执行。本页负责悬浮窗外观和快捷键。",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.72,
            FontSize = 12
        });

        root.Children.Add(Card(
            "悬浮窗",
            Row("显示", _showFloating),
            Row("自动收起", _autoCollapse),
            Row("等待秒数", _autoSeconds, "鼠标离开面板后，等这么久再收起"),
            Row("手柄宽度", _handleWidth, "收起状态那条手柄的宽度（像素）"),
            Row("距屏幕底部", _bottomMargin, "数字越大，手柄越靠上"),
            Buttons(toggleTest, openEditor)));

        root.Children.Add(Card(
            "全局快捷键",
            Row("切换面板", _hotkeyToggle, "例如 Ctrl+Alt+B；支持字母、数字、F1-F24"),
            Row("打开编辑窗", _hotkeyEditor, "例如 Ctrl+Alt+E"),
            Buttons(applyHotkey),
            _hotkeyStatus));

        root.Children.Add(Card(
            "自检",
            new TextBlock
            {
                Text = "点这里的「触发一次」等于在悬浮窗上点一下按钮。如果这里能触发自动化、悬浮窗上点不动，那就是悬浮窗那边没收到点击。",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Opacity = 0.72
            },
            _selfCheck,
            Buttons(refreshCheck, openLog)));

        root.Children.Add(_saveStatus);

        return new ScrollViewer
        {
            Content = root,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        };
    }

    private static Control Row(string label, Control input, string? hint = null)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(0, 4, 0, 4)
        };
        row.Children.Add(new TextBlock
        {
            Text = label,
            Width = 96,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 13
        });
        row.Children.Add(input);

        if (!string.IsNullOrEmpty(hint))
        {
            row.Children.Add(new TextBlock
            {
                Text = hint,
                FontSize = 11,
                Opacity = 0.6,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 360
            });
        }

        return row;
    }

    private static Control Buttons(params Control[] items)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 8, 0, 0)
        };
        foreach (var item in items)
        {
            panel.Children.Add(item);
        }

        return panel;
    }

    private static Control Card(string title, params Control[] children)
    {
        var inner = new StackPanel { Spacing = 2 };
        inner.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        });

        foreach (var child in children)
        {
            inner.Children.Add(child);
        }

        return new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.Parse("#22808080")),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14),
            Child = inner
        };
    }

    private void LoadValues()
    {
        var store = Store;
        if (store == null)
        {
            return;
        }

        _loading = true;
        try
        {
            var cfg = store.Config;
            _showFloating.IsChecked = cfg.ShowFloating;
            _autoCollapse.IsChecked = cfg.AutoCollapse;
            _autoSeconds.Value = Math.Clamp(cfg.AutoCollapseSeconds, 3, 600);
            _handleWidth.Value = (decimal)Math.Clamp(cfg.HandleWidth, 12, 64);
            _bottomMargin.Value = (decimal)Math.Clamp(cfg.BottomMargin, 0, 600);
            _hotkeyToggle.Text = cfg.HotkeyToggle;
            _hotkeyEditor.Text = cfg.HotkeyEditor;
        }
        finally
        {
            _loading = false;
        }
    }

    private void SaveAndApply()
    {
        if (_loading)
        {
            return;
        }

        var store = Store;
        if (store == null)
        {
            _saveStatus.Text = "还没拿到按钮数据（插件可能尚未初始化完成）。";
            return;
        }

        try
        {
            var cfg = store.Config;
            cfg.ShowFloating = _showFloating.IsChecked == true;
            cfg.AutoCollapse = _autoCollapse.IsChecked == true;
            cfg.AutoCollapseSeconds = (int)(_autoSeconds.Value ?? 12m);
            cfg.HandleWidth = (double)(_handleWidth.Value ?? 22m);
            cfg.BottomMargin = (double)(_bottomMargin.Value ?? 36m);
            cfg.HotkeyToggle = _hotkeyToggle.Text?.Trim() ?? "";
            cfg.HotkeyEditor = _hotkeyEditor.Text?.Trim() ?? "";

            store.Save();
            FloatingWindowController.Current?.ApplyAppearance();

            var hotkey = GlobalHotkeyService.Current;
            var status = hotkey?.Apply(cfg.HotkeyToggle, cfg.HotkeyEditor) ?? "快捷键服务未启动";
            _hotkeyStatus.Text = "快捷键：" + status;
            _saveStatus.Text = "已保存并应用。";
        }
        catch (Exception ex)
        {
            _saveStatus.Text = "保存失败：" + ex.Message;
            ButtonStore.Log("设置页保存失败：" + ex);
        }
    }

    private void RefreshSelfCheck()
    {
        _selfCheck.Children.Clear();

        var registered = new List<string>();
        var automationReady = false;
        try
        {
            var automation = IAppHost.Host?.Services.GetService<IAutomationService>();
            if (automation != null)
            {
                automationReady = true;
                foreach (var info in IAutomationService.RegisteredTriggers)
                {
                    if (ButtonTriggerIds.IsOurs(info.Id))
                    {
                        registered.Add(info.Name);
                    }
                }
            }
        }
        catch
        {
            // 自检失败不影响页面
        }

        if (!automationReady)
        {
            _selfCheck.Children.Add(new TextBlock
            {
                Text = "自动化服务还没就绪（ClassIsland 正在启动），稍等一会儿点「刷新自检」。",
                FontSize = 12,
                Opacity = 0.75,
                TextWrapping = TextWrapping.Wrap
            });
        }
        else
        {
            _selfCheck.Children.Add(new TextBlock
            {
                Text = registered.Count >= 2
                    ? "触发器已注册：" + string.Join("、", registered)
                    : $"触发器注册好像不完整（只找到 {registered.Count} 个），重启 ClassIsland 试试",
                FontSize = 12,
                Foreground = registered.Count >= 2
                    ? new SolidColorBrush(Color.Parse("#2E9E5B"))
                    : new SolidColorBrush(Color.Parse("#C0503F")),
                TextWrapping = TextWrapping.Wrap
            });
        }

        var store = Store;
        var inspector = AutomationInspector.Current ?? new AutomationInspector();
        var hub = ButtonEventHub.Current;

        if (store == null || store.Config.Buttons.Count == 0)
        {
            _selfCheck.Children.Add(new TextBlock { Text = "还没有按钮。", FontSize = 12, Opacity = 0.7 });
            return;
        }

        foreach (var button in store.Config.Buttons.ToList())
        {
            var refs = inspector.CountReferences(button.Id);
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Margin = new Thickness(0, 3, 0, 3)
            };

            var nameText = new TextBlock
            {
                Text = $"{button.Icon} {button.Name}",
                Width = 160,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 13
            };
            ToolTip.SetTip(nameText, "按钮 ID：" + button.Id);
            row.Children.Add(nameText);

            row.Children.Add(new TextBlock
            {
                Text = refs == 0 ? "未被任何自动化引用" : $"被 {refs} 条自动化引用",
                Width = 160,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12,
                Opacity = refs == 0 ? 0.9 : 0.7,
                Foreground = refs == 0 ? new SolidColorBrush(Color.Parse("#C0503F")) : null
            });

            var fire = new Button { Content = "触发一次", Padding = new Thickness(10, 3, 10, 3) };
            fire.Click += (_, _) =>
            {
                if (hub == null)
                {
                    return;
                }

                var again = hub.Click(button.Id);
                _saveStatus.Text = $"已手动触发「{button.Name}」（{(again ? "再次点击" : "点击")}），去看自动化有没有执行。";
            };
            row.Children.Add(fire);

            _selfCheck.Children.Add(row);
        }

        AddWorkflowChecks(inspector, automationReady);
    }

    /// <summary>
    /// 逐条检查引用了按钮的工作流：会不会真的执行、被 ClassIsland 静默丢弃的原因是什么。
    /// 这是因为 ClassIsland 在 `IsRevertEnabled && Status != Normal` 时会直接 return，且不留日志。
    /// </summary>
    private void AddWorkflowChecks(AutomationInspector inspector, bool automationReady)
    {
        if (!automationReady)
        {
            return;
        }

        List<AutomationInspector.WorkflowCheck> checks;
        try
        {
            checks = inspector.Diagnose();
        }
        catch
        {
            return;
        }

        _selfCheck.Children.Add(new TextBlock
        {
            Text = checks.Count == 0 ? "还没有任何工作流使用 ButtonIsland 的触发器。" : "工作流诊断：",
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 10, 0, 2),
            TextWrapping = TextWrapping.Wrap
        });

        if (checks.Count == 0)
        {
            return;
        }

        foreach (var check in checks)
        {
            var row = new StackPanel { Spacing = 2, Margin = new Thickness(0, 4, 0, 4) };

            row.Children.Add(new TextBlock
            {
                Text = $"「{check.WorkflowName}」· {check.TriggerName} · 按钮={DescribeButton(check.ButtonId)}" +
                       (check.Revert ? $" · 允许反转 · 状态={check.Status}" : " · 不反转"),
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap
            });

            row.Children.Add(new TextBlock
            {
                Text = check.Note,
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.Parse(check.WillRun ? "#2E9E5B" : "#C0503F")),
                TextWrapping = TextWrapping.Wrap
            });

            if (!check.WillRun && check.Revert)
            {
                var fix = new Button
                {
                    Content = "关掉这个工作流的「允许反转」",
                    Padding = new Thickness(10, 3, 10, 3),
                    Margin = new Thickness(0, 2, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                fix.Click += (_, _) => DisableRevert(check.WorkflowName);
                row.Children.Add(fix);
            }

            _selfCheck.Children.Add(row);
        }
    }

    private string DescribeButton(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return "（未选择）";
        }

        if (id == ButtonTriggerIds.AnyButtonId)
        {
            return "任意按钮";
        }

        var name = Store?.Config.Buttons.FirstOrDefault(b => b.Id == id)?.Name;
        return name == null ? id : name;
    }

    /// <summary>
    /// 把某个工作流的「允许反转」关掉。
    /// 这是解决"触发器命中但自动化不执行"最直接的办法：
    /// ClassIsland 在 IsRevertEnabled=true 且动作组状态不是 Normal 时会静默丢弃触发。
    /// </summary>
    private void DisableRevert(string workflowName)
    {
        try
        {
            var automation = IAppHost.Host?.Services.GetService<IAutomationService>();
            if (automation == null)
            {
                _saveStatus.Text = "自动化服务还没就绪。";
                return;
            }

            var hit = false;
            foreach (var workflow in automation.Workflows)
            {
                if (workflow.ActionSet?.Name != workflowName)
                {
                    continue;
                }

                workflow.ActionSet.IsRevertEnabled = false;
                hit = true;
            }

            if (!hit)
            {
                _saveStatus.Text = "没找到这个工作流，可能改名了。";
                return;
            }

            automation.SaveConfig("ButtonIsland：关闭工作流的允许反转");
            ButtonStore.Log($"已关闭工作流「{workflowName}」的允许反转");
            _saveStatus.Text = $"已关闭「{workflowName}」的允许反转，再点一次按钮试试。";
            RefreshSelfCheck();
        }
        catch (Exception ex)
        {
            _saveStatus.Text = "操作失败：" + ex.Message;
            ButtonStore.Log("关闭允许反转失败：" + ex);
        }
    }

    private void OpenLog()
    {
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "ButtonIsland.log");
            if (!File.Exists(path))
            {
                File.WriteAllText(path, "");
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _saveStatus.Text = "打开日志失败：" + ex.Message;
        }
    }
}
