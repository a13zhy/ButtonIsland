using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ButtonIsland.Models;
using ButtonIsland.Services;
using HAlign = Avalonia.Layout.HorizontalAlignment;
using VAlign = Avalonia.Layout.VerticalAlignment;
using Orient = Avalonia.Layout.Orientation;

namespace ButtonIsland.Views;

/// <summary>
/// 右下角悬浮窗。
///
/// 收起/拉出的做法：窗口宽度本身就是"手柄 22px"和"面板全宽"两个值，
/// 收起时窗口真的只有 22px 宽 —— 不做"把窗口推到屏幕外"这种取巧做法。
/// 面板用固定宽度右对齐放在窗口里，窗口变窄时只露出最右边的 22px 手柄，
/// 变宽时从左边一点点露出来，所以内容不会在动画过程中被挤压重排。
/// </summary>
public sealed class FloatingWindow : Window
{
    private const double TileCell = 94;
    private const double PanelPadding = 12;
    private const double DefaultHandleWidth = 22;
    private const double HeaderHeight = 32;
    private const double FooterHeight = 24;
    private const double EmptyContentHeight = 118;
    private const double CollapsedHeight = 104;
    private const double DefaultBottomMargin = 36;

    private readonly ButtonStore _store;
    private readonly ButtonEventHub _hub;
    private readonly Action _openEditor;

    private readonly Grid _tiles = new();
    private readonly Dictionary<string, (Border Tile, Color Color)> _tileIndex = new();
    private readonly TextBlock _status = new();
    private readonly TextBlock _chevron = new();
    private readonly Border _handle = new();
    private Border _panel = null!;
    private Grid _root = null!;

    private double _scaling = 1;
    private int _rightPx;
    private int _bottomPx;
    private double _panelWidthDip = 282 + PanelPadding * 2;
    private double _expandedHeightDip = 360;
    private double _handleWidthDip = DefaultHandleWidth;
    private double _marginDip = DefaultBottomMargin;
    private bool _expanded;
    private bool _pointerInside;
    private DispatcherTimer? _statusTimer;
    private DispatcherTimer? _autoTimer;

    public FloatingWindow(ButtonStore store, ButtonEventHub hub, Action openEditor)
    {
        _store = store;
        _hub = hub;
        _openEditor = openEditor;

        ReadAppearance();

        Title = "ButtonIsland";
        Width = _handleWidthDip;
        Height = CollapsedHeight;
        SystemDecorations = SystemDecorations.None;
        WindowStartupLocation = WindowStartupLocation.Manual;
        CanResize = false;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        FontFamily = new FontFamily("Microsoft YaHei UI, Microsoft YaHei, Segoe UI");

        _status.Text = "点按钮触发自动化";
        _status.FontSize = 11;
        _status.Foreground = Ui.Brush(Ui.TextSecondary);
        _status.VerticalAlignment = VAlign.Center;
        _status.TextTrimming = TextTrimming.CharacterEllipsis;

        _chevron.Text = "‹";
        _chevron.FontSize = 19;
        _chevron.FontWeight = FontWeight.SemiBold;
        _chevron.Foreground = Ui.Brush("#E6FFFFFF");
        _chevron.HorizontalAlignment = HAlign.Center;
        _chevron.VerticalAlignment = VAlign.Center;

        _handle.Width = _handleWidthDip;
        _handle.Background = Ui.Brush("#F0181D26");
        _handle.BorderBrush = Ui.Brush("#3AFFFFFF");
        _handle.BorderThickness = new Thickness(1, 1, 1, 1);
        _handle.CornerRadius = new CornerRadius(9);
        _handle.Cursor = new Cursor(StandardCursorType.Hand);
        _handle.Child = _chevron;
        _handle.PointerPressed += OnHandlePressed;
        _handle.PointerEntered += (_, _) => _handle.Background = Ui.Brush("#F0262D3C");
        _handle.PointerExited += (_, _) => _handle.Background = Ui.Brush("#F0181D26");

        Content = BuildLayout();

        Opened += (_, _) => OnOpened();
        PointerEntered += (_, _) =>
        {
            _pointerInside = true;
            _autoTimer?.Stop();
        };
        PointerExited += (_, _) =>
        {
            _pointerInside = false;
            StartAutoCollapse();
        };
    }

    private Control BuildLayout()
    {
        _panel = new Border
        {
            Width = _panelWidthDip,
            HorizontalAlignment = HAlign.Right,
            Background = Ui.Brush(Ui.Panel),
            CornerRadius = new CornerRadius(10, 0, 0, 10),
            Padding = new Thickness(PanelPadding, 10, PanelPadding, 10),
            Child = BuildPanelContent()
        };

        _root = new Grid
        {
            Width = _panelWidthDip + _handleWidthDip,
            HorizontalAlignment = HAlign.Right,
            ColumnDefinitions = new ColumnDefinitions($"*,{_handleWidthDip.ToString(CultureInfo.InvariantCulture)}"),
            ClipToBounds = true,
            Children = { _panel, _handle }
        };
        Grid.SetColumn(_handle, 1);

        return _root;
    }

    private Control BuildPanelContent()
    {
        var header = new Grid
        {
            Height = HeaderHeight,
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto")
        };

        var title = new TextBlock
        {
            Text = "快捷按钮",
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Foreground = Ui.Brush(Ui.TextPrimary),
            VerticalAlignment = VAlign.Center
        };

        var gear = new TextBlock
        {
            Text = "⚙",
            FontSize = 13,
            Foreground = Ui.Brush(Ui.Icon),
            VerticalAlignment = VAlign.Center,
            Margin = new Thickness(6, 0, 10, 0),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        gear.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            _openEditor();
        };
        gear.PointerEntered += (_, _) => gear.Foreground = Ui.Brush("#FFFFFFFF");
        gear.PointerExited += (_, _) => gear.Foreground = Ui.Brush(Ui.Icon);

        var fold = new TextBlock
        {
            Text = "›",
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Foreground = Ui.Brush(Ui.TextSecondary),
            VerticalAlignment = VAlign.Center,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        fold.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            Collapse();
        };
        fold.PointerEntered += (_, _) => fold.Foreground = Ui.Brush("#FFFFFFFF");
        fold.PointerExited += (_, _) => fold.Foreground = Ui.Brush(Ui.TextSecondary);

        header.Children.Add(title);
        header.Children.Add(gear);
        header.Children.Add(fold);
        Grid.SetColumn(gear, 1);
        Grid.SetColumn(fold, 2);

        _tiles.Width = _panelWidthDip - PanelPadding * 2;
        _tiles.HorizontalAlignment = HAlign.Left;

        var footer = new Border
        {
            Height = FooterHeight,
            Child = _status
        };

        return new StackPanel
        {
            Orientation = Orient.Vertical,
            Spacing = 6,
            Children = { header, _tiles, footer }
        };
    }

    private void OnOpened()
    {
        try
        {
            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            if (screen != null)
            {
                var area = screen.WorkingArea;
                _scaling = screen.Scaling > 0 ? screen.Scaling : 1;
                _rightPx = area.Right;
                _bottomPx = area.Bottom;
            }

            Rebuild();
            SetState(_store.Config.Collapsed, animate: false);
            ButtonStore.Log($"悬浮窗就绪：屏幕 {_rightPx}x{_bottomPx}，缩放 {_scaling}");
        }
        catch (Exception ex)
        {
            ButtonStore.Log("悬浮窗初始化失败：" + ex);
        }
    }

    /// <summary>按钮数据变化后重建内容并重新对齐。</summary>
    public void Rebuild()
    {
        try
        {
            var buttons = _store.Config.Buttons;
            var count = buttons.Count;
            var columns = count <= 1 ? 1 : Math.Min(3, count);
            var rows = count == 0 ? 0 : (int)Math.Ceiling(count / (double)columns);
            var contentHeight = count == 0 ? EmptyContentHeight : rows * TileCell;

            _panelWidthDip = columns * TileCell + PanelPadding * 2;
            _expandedHeightDip = HeaderHeight + contentHeight + FooterHeight + 20 + 13;

            _panel.Width = _panelWidthDip;
            _tiles.Width = _panelWidthDip - PanelPadding * 2;
            _root.Width = _panelWidthDip + _handleWidthDip;

            _tiles.Children.Clear();
            _tileIndex.Clear();
            _tiles.ColumnDefinitions = new ColumnDefinitions(
                string.Join(",", Repeat("94", columns)));
            _tiles.RowDefinitions = new RowDefinitions(
                string.Join(",", Repeat("94", rows == 0 ? 1 : rows)));
            _tiles.Height = contentHeight;

            if (count == 0)
            {
                _tiles.Children.Add(BuildEmpty());
            }
            else
            {
                for (var i = 0; i < count; i++)
                {
                    var tile = BuildTile(buttons[i]);
                    Grid.SetColumn(tile, i % columns);
                    Grid.SetRow(tile, i / columns);
                    _tiles.Children.Add(tile);
                }
            }

            ApplyInstant();
        }
        catch (Exception ex)
        {
            ButtonStore.Log("重建悬浮窗失败：" + ex);
        }
    }

    private static IEnumerable<string> Repeat(string value, int count)
    {
        for (var i = 0; i < count; i++)
        {
            yield return value;
        }
    }

    private Control BuildEmpty()
    {
        var hint = new StackPanel
        {
            Orientation = Orient.Vertical,
            Spacing = 6,
            HorizontalAlignment = HAlign.Center,
            VerticalAlignment = VAlign.Center,
            Children =
            {
                new TextBlock
                {
                    Text = "还没有按钮",
                    FontSize = 13,
                    Foreground = Ui.Brush(Ui.TextPrimary),
                    HorizontalAlignment = HAlign.Center
                },
                new TextBlock
                {
                    Text = "右键手柄或点 ⚙ 添加",
                    FontSize = 11,
                    Foreground = Ui.Brush(Ui.TextSecondary),
                    HorizontalAlignment = HAlign.Center
                }
            }
        };

        Grid.SetColumnSpan(hint, 3);

        return new Border
        {
            Width = _panelWidthDip - PanelPadding * 2,
            Height = EmptyContentHeight,
            Child = hint
        };
    }

    private Control BuildTile(ButtonItem item)
    {
        var color = Ui.Parse(item.Color, Color.Parse("#4C8DFF"));

        var icon = new TextBlock
        {
            Text = string.IsNullOrEmpty(item.Icon) ? "★" : item.Icon,
            FontSize = 24,
            FontFamily = new FontFamily("Segoe UI Emoji, Microsoft YaHei UI, Segoe UI"),
            Foreground = Ui.Brush(color),
            HorizontalAlignment = HAlign.Center,
            TextAlignment = TextAlignment.Center,
            MaxLines = 1
        };

        var name = new TextBlock
        {
            Text = item.Name,
            FontSize = 11,
            Foreground = Ui.Brush("#DDFFFFFF"),
            HorizontalAlignment = HAlign.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(6, 0, 6, 0)
        };

        var tile = new Border
        {
            Width = 84,
            Height = 84,
            Margin = new Thickness(5),
            CornerRadius = new CornerRadius(10),
            Background = Ui.Brush(Ui.Tint(color, 0x1A)),
            BorderBrush = Ui.Brush(Ui.Tint(color, 0x42)),
            BorderThickness = new Thickness(1),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new StackPanel
            {
                Orientation = Orient.Vertical,
                Spacing = 4,
                HorizontalAlignment = HAlign.Center,
                VerticalAlignment = VAlign.Center,
                Children = { icon, name }
            }
        };

        ToolTip.SetTip(tile, $"{item.Name}\n左键：交给自动化处理\n右键：编辑按钮");

        _tileIndex[item.Id] = (tile, color);
        ApplyTileState(tile, color, item.Id);

        tile.PointerEntered += (_, _) =>
        {
            tile.Background = Ui.Brush(Ui.Tint(color, 0x2C));
            tile.BorderBrush = Ui.Brush(Ui.Tint(color, 0x77));
        };
        tile.PointerExited += (_, _) =>
        {
            tile.Background = Ui.Brush(Ui.Tint(color, 0x1A));
            tile.BorderBrush = Ui.Brush(Ui.Tint(color, 0x42));
            ApplyTileState(tile, color, item.Id);
        };
        tile.PointerPressed += (_, e) =>
        {
            var point = e.GetCurrentPoint(tile);
            if (point.Properties.IsRightButtonPressed)
            {
                e.Handled = true;
                _openEditor();
            }
            else if (point.Properties.IsLeftButtonPressed)
            {
                e.Handled = true;
                OnTileClick(item);
            }
        };

        return tile;
    }

    private void OnTileClick(ButtonItem item)
    {
        try
        {
            var again = _hub.Click(item.Id);
            if (_tileIndex.TryGetValue(item.Id, out var info))
            {
                ApplyTileState(info.Tile, info.Color, item.Id);
            }

            var state = _hub.IsOn(item.Id) ? "开" : "关";
            SetStatus(again
                ? $"「{item.Name}」再次点击 → {state}"
                : $"「{item.Name}」已点击 → {state}", true);
            StartAutoCollapse();
        }
        catch (Exception ex)
        {
            SetStatus("点击处理失败：" + ex.Message, false);
            ButtonStore.Log("点击处理异常：" + ex);
        }
    }

    /// <summary>
    /// 按钮处于"开"状态时加粗描边 + 提亮底色。
    /// 这个状态决定「被再次点击」触发器什么时候响，所以直接画出来最直观。
    /// </summary>
    private void ApplyTileState(Border tile, Color color, string id)
    {
        var on = _hub.IsOn(id);
        tile.BorderThickness = new Thickness(on ? 2 : 1);
        tile.BorderBrush = Ui.Brush(Ui.Tint(color, on ? (byte)0xEE : (byte)0x42));
        tile.Background = Ui.Brush(Ui.Tint(color, on ? (byte)0x33 : (byte)0x1A));
    }

    private void OnHandlePressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(_handle);
        ButtonStore.Log($"手柄被按下：左键={point.Properties.IsLeftButtonPressed}，右键={point.Properties.IsRightButtonPressed}");
        e.Handled = true;

        if (point.Properties.IsRightButtonPressed)
        {
            _openEditor();
            return;
        }

        if (point.Properties.IsLeftButtonPressed)
        {
            Toggle();
        }
    }

    public void Toggle()
    {
        if (_expanded)
        {
            Collapse();
        }
        else
        {
            Expand();
        }
    }

    public void Expand()
    {
        if (_expanded)
        {
            StartAutoCollapse();
            return;
        }

        ButtonStore.Log("面板拉出");
        SetState(false, animate: false);
        _store.Config.Collapsed = false;
        _store.Save();
        StartAutoCollapse();
    }

    public void Collapse()
    {
        if (!_expanded)
        {
            return;
        }

        ButtonStore.Log("面板收起");
        _autoTimer?.Stop();
        SetState(true, animate: true);
        _store.Config.Collapsed = true;
        _store.Save();
    }

    private void SetState(bool collapsed, bool animate)
    {
        _expanded = !collapsed;
        _chevron.Text = collapsed ? "‹" : "›";

        // 不做宽度过渡动画：窗口宽度必须一次到底。
        // 之前"把窗口推出屏幕"的做法在真机上会被窗口管理器夹回工作区，
        // 导致收起时窗口其实还是全宽、手柄也点不到。
        ApplyInstant();
    }

    private void ApplyInstant()
    {
        Width = _expanded ? _panelWidthDip + _handleWidthDip : _handleWidthDip;
        Height = _expanded ? _expandedHeightDip : CollapsedHeight;
        Place();
    }

    /// <summary>从配置里读出外观参数（手柄宽度、距底边留白）。</summary>
    private void ReadAppearance()
    {
        _handleWidthDip = Math.Clamp(_store.Config.HandleWidth, 12, 64);
        _marginDip = Math.Clamp(_store.Config.BottomMargin, 0, 600);
    }

    /// <summary>设置页改了外观后重新套用，不用重启 ClassIsland。</summary>
    public void ApplyAppearance()
    {
        try
        {
            ReadAppearance();
            _handle.Width = _handleWidthDip;
            _root.ColumnDefinitions = new ColumnDefinitions(
                $"*,{_handleWidthDip.ToString(CultureInfo.InvariantCulture)}");
            _root.Width = _panelWidthDip + _handleWidthDip;
            ApplyInstant();
            ButtonStore.Log($"外观已更新：手柄宽 {_handleWidthDip}，距底 {_marginDip}");
        }
        catch (Exception ex)
        {
            ButtonStore.Log("更新外观失败：" + ex);
        }
    }

    private void Place()
    {
        var widthPx = (int)Math.Round(Width * _scaling);
        var heightPx = (int)Math.Round(Height * _scaling);
        var marginPx = (int)Math.Round(_marginDip * _scaling);

        Position = new PixelPoint(
            _rightPx - widthPx,
            _bottomPx - heightPx - marginPx);
    }

    private void StartAutoCollapse()
    {
        _autoTimer?.Stop();
        if (!_expanded || !_store.Config.AutoCollapse)
        {
            return;
        }

        var seconds = Math.Clamp(_store.Config.AutoCollapseSeconds, 3, 600);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _autoTimer = null;
            if (_expanded && !_pointerInside)
            {
                ButtonStore.Log("自动收起");
                Collapse();
            }
        };
        _autoTimer = timer;
        timer.Start();
    }

    private void SetStatus(string message, bool ok)
    {
        _status.Text = message;
        _status.Foreground = Ui.Brush(ok ? Ui.Success : Ui.Danger);

        _statusTimer?.Stop();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _statusTimer = null;
            _status.Text = "点按钮触发自动化";
            _status.Foreground = Ui.Brush(Ui.TextSecondary);
        };
        _statusTimer = timer;
        timer.Start();
    }
}
