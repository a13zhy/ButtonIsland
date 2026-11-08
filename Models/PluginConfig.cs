using System;
using System.Collections.ObjectModel;

namespace ButtonIsland.Models;

/// <summary>
/// 一个悬浮按钮。这里只描述按钮长什么样，
/// "点它会执行什么"由 ClassIsland 自动化决定，不在这里保存。
/// </summary>
public class ButtonItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "新按钮";

    public string Icon { get; set; } = "★";

    public string Color { get; set; } = "#4C8DFF";
}

/// <summary>
/// 插件配置，保存在 {配置目录}\buttons.json。
/// </summary>
public class PluginConfig
{
    /// <summary>悬浮窗是否处于收起状态。</summary>
    public bool Collapsed { get; set; } = true;

    /// <summary>是否显示右下角悬浮窗（手柄）。</summary>
    public bool ShowFloating { get; set; } = true;

    /// <summary>展开后无操作是否自动收起。</summary>
    public bool AutoCollapse { get; set; } = true;

    /// <summary>自动收起的等待秒数。</summary>
    public int AutoCollapseSeconds { get; set; } = 12;

    /// <summary>收起态手柄的宽度（像素）。</summary>
    public double HandleWidth { get; set; } = 22;

    /// <summary>悬浮窗距离屏幕底边的距离（像素）。</summary>
    public double BottomMargin { get; set; } = 36;

    /// <summary>切换面板的全局快捷键，例如 Ctrl+Alt+B。</summary>
    public string HotkeyToggle { get; set; } = "Ctrl+Alt+B";

    /// <summary>打开按钮编辑窗的全局快捷键，例如 Ctrl+Alt+E。</summary>
    public string HotkeyEditor { get; set; } = "Ctrl+Alt+E";

    public ObservableCollection<ButtonItem> Buttons { get; set; } = new();
}
