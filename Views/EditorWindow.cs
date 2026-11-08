using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Controls.Primitives;
using ButtonIsland.Models;
using ButtonIsland.Services;
using HAlign = Avalonia.Layout.HorizontalAlignment;
using VAlign = Avalonia.Layout.VerticalAlignment;

namespace ButtonIsland.Views;

/// <summary>
/// 按钮编辑窗口：只管"按钮长什么样"，
/// "点它执行什么"在 ClassIsland 的自动化页里配置。
/// </summary>
public sealed class EditorWindow : Window
{
    private readonly ButtonStore _store;
    private readonly ButtonEventHub _hub;
    private readonly AutomationInspector _inspector;
    private readonly Action _onChanged;

    private readonly StackPanel _rows = new();
    private readonly TextBlock _status = new();

    public EditorWindow(
        ButtonStore store,
        ButtonEventHub hub,
        AutomationInspector inspector,
        Action onChanged)
    {
        _store = store;
        _hub = hub;
        _inspector = inspector;
        _onChanged = onChanged;

        Title = "ButtonIsland · 按钮编辑";
        Width = 680;
        Height = 560;
        SystemDecorations = SystemDecorations.None;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = Ui.Brush("#FF0E1117");
        FontFamily = new FontFamily("Microsoft YaHei UI, Microsoft YaHei, Segoe UI");

        Content = BuildLayout();
        BuildRows();

        Opened += (_, _) => CenterOnScreen();
    }

    private void CenterOnScreen()
    {
        // 不依赖 WindowStartupLocation.CenterScreen（无边框窗口上它不总是生效），
        // 明确按工作区算一次位置。
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen == null)
        {
            return;
        }

        var area = screen.WorkingArea;
        var widthPx = (int)Math.Round(Width * screen.Scaling);
        var heightPx = (int)Math.Round(Height * screen.Scaling);

        Position = new PixelPoint(
            area.X + Math.Max(0, (area.Width - widthPx) / 2),
            area.Y + Math.Max(0, (area.Height - heightPx) / 2));
    }

    private Control BuildLayout()
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("46,*,58")
        };

        root.Children.Add(BuildHeader());

        var scroll = new ScrollViewer
        {
            Content = _rows,
            Padding = new Thickness(14, 10, 14, 10),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        var footer = BuildFooter();
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        return root;
    }

    private Control BuildHeader()
    {
        var title = new TextBlock
        {
            Text = "按钮编辑",
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Foreground = Ui.Brush(Ui.TextPrimary),
            VerticalAlignment = VAlign.Center
        };

        var subtitle = new TextBlock
        {
            Text = "点按钮做什么 → 在 ClassIsland「自动化」里加触发器",
            FontSize = 11,
            Foreground = Ui.Brush(Ui.TextSecondary),
            VerticalAlignment = VAlign.Center,
            Margin = new Thickness(16, 2, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var close = new TextBlock
        {
            Text = "✕",
            FontSize = 15,
            Foreground = Ui.Brush(Ui.TextSecondary),
            VerticalAlignment = VAlign.Center,
            Margin = new Thickness(10, 0, 0, 0),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        close.PointerPressed += (_, _) => Close();
        close.PointerEntered += (_, _) => close.Foreground = Ui.Brush(Ui.Danger);
        close.PointerExited += (_, _) => close.Foreground = Ui.Brush(Ui.TextSecondary);

        var bar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"),
            Children = { title, subtitle, close }
        };
        Grid.SetColumn(subtitle, 1);
        Grid.SetColumn(close, 3);

        var header = new Border
        {
            Background = Ui.Brush("#FF12161E"),
            BorderBrush = Ui.Brush(Ui.Line),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(16, 0, 16, 0),
            Child = bar
        };

        header.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(header).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
            }
        };

        return header;
    }

    private Control BuildFooter()
    {
        var add = MakeButton("＋ 添加按钮", primary: true);
        add.Click += (_, _) =>
        {
            _store.Config.Buttons.Add(new ButtonItem
            {
                Name = $"按钮 {_store.Config.Buttons.Count + 1}",
                Icon = "★",
                Color = Ui.Palette[_store.Config.Buttons.Count % Ui.Palette.Length]
            });
            Persist();
            BuildRows();
        };

        var refresh = MakeButton("刷新引用情况");
        refresh.Click += (_, _) => BuildRows();

        var file = MakeButton("配置文件");
        file.Click += (_, _) => SetStatus("配置文件：" + _store.FilePath, true);

        _status.Text = "改动会立即保存到 buttons.json";
        _status.FontSize = 11;
        _status.Foreground = Ui.Brush(Ui.TextSecondary);
        _status.VerticalAlignment = VAlign.Center;
        _status.TextTrimming = TextTrimming.CharacterEllipsis;

        var left = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { add, refresh, file }
        };

        var bar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Children = { left, _status }
        };
        Grid.SetColumn(_status, 1);
        _status.Margin = new Thickness(12, 0, 0, 0);

        return new Border
        {
            Background = Ui.Brush("#FF12161E"),
            BorderBrush = Ui.Brush(Ui.Line),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(14, 0, 14, 0),
            Child = bar
        };
    }

    private static Button MakeButton(string text, bool primary = false)
    {
        var button = new Button
        {
            Content = text,
            FontSize = 12,
            Padding = new Thickness(12, 6, 12, 6),
            CornerRadius = new CornerRadius(8),
            Background = Ui.Brush(primary ? Color.Parse("#FF4C8DFF") : Color.Parse("#14FFFFFF")),
            Foreground = Ui.Brush(primary ? Color.Parse("#FF0B1220") : Ui.TextPrimary),
            BorderThickness = new Thickness(0)
        };
        return button;
    }

    private void BuildRows()
    {
        _rows.Children.Clear();

        if (_store.Config.Buttons.Count == 0)
        {
            _rows.Children.Add(new TextBlock
            {
                Text = "还没有按钮，点下面的「＋ 添加按钮」。",
                FontSize = 12,
                Foreground = Ui.Brush(Ui.TextSecondary),
                Margin = new Thickness(2, 10, 0, 0)
            });
            return;
        }

        foreach (var item in _store.Config.Buttons)
        {
            _rows.Children.Add(BuildRow(item));
        }
    }

    private Control BuildRow(ButtonItem item)
    {
        var color = Ui.Parse(item.Color, Color.Parse("#4C8DFF"));

        var chip = new Border
        {
            Width = 12,
            Height = 12,
            CornerRadius = new CornerRadius(6),
            Background = Ui.Brush(color),
            VerticalAlignment = VAlign.Center,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        ToolTip.SetTip(chip, "点击切换颜色");
        chip.PointerPressed += (_, _) =>
        {
            item.Color = NextColor(item.Color);
            chip.Background = Ui.Brush(Ui.Parse(item.Color, Color.Parse("#4C8DFF")));
            Persist();
            _onChanged();
        };

        var icon = new TextBox
        {
            Text = item.Icon,
            Width = 52,
            FontSize = 13,
            MaxLength = 2,
            VerticalAlignment = VAlign.Center,
            Background = Ui.Brush("#10FFFFFF"),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6, 4, 6, 4),
            HorizontalContentAlignment = HAlign.Center
        };
        icon.TextChanged += (_, _) =>
        {
            item.Icon = icon.Text ?? "";
            Persist();
            _onChanged();
        };

        var name = new TextBox
        {
            Text = item.Name,
            FontSize = 13,
            VerticalAlignment = VAlign.Center,
            Background = Ui.Brush("#10FFFFFF"),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 4, 8, 4),
            Watermark = "按钮名称"
        };
        name.TextChanged += (_, _) =>
        {
            item.Name = name.Text ?? "";
            Persist();
            _onChanged();
        };

        var references = new TextBlock
        {
            Text = DescribeReferences(item),
            FontSize = 11,
            Foreground = Ui.Brush(Ui.TextSecondary),
            VerticalAlignment = VAlign.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 170
        };

        var test = MakeButton("试跑");
        ToolTip.SetTip(test, "模拟点击一次，验证自动化有没有接上");
        test.Click += (_, _) =>
        {
            var again = _hub.Click(item.Id);
            SetStatus(again ? $"已模拟「{item.Name}」再次点击" : $"已模拟「{item.Name}」点击", true);
        };

        var up = MakeButton("▲");
        up.Click += (_, _) => Move(item, -1);

        var down = MakeButton("▼");
        down.Click += (_, _) => Move(item, 1);

        var remove = MakeButton("✕");
        remove.Foreground = Ui.Brush(Ui.Danger);
        remove.Click += (_, _) =>
        {
            _store.Config.Buttons.Remove(item);
            _hub.Forget(item.Id);
            Persist();
            BuildRows();
        };

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto,Auto,Auto,Auto"),
            Height = 44,
            Margin = new Thickness(0, 0, 0, 6)
        };

        row.Children.Add(chip);
        row.Children.Add(icon);
        row.Children.Add(name);
        row.Children.Add(references);
        row.Children.Add(test);
        row.Children.Add(up);
        row.Children.Add(down);
        row.Children.Add(remove);

        Grid.SetColumn(chip, 0);
        Grid.SetColumn(icon, 1);
        Grid.SetColumn(name, 2);
        Grid.SetColumn(references, 3);
        Grid.SetColumn(test, 4);
        Grid.SetColumn(up, 5);
        Grid.SetColumn(down, 6);
        Grid.SetColumn(remove, 7);

        icon.Margin = new Thickness(8, 0, 6, 0);
        references.Margin = new Thickness(10, 0, 10, 0);
        test.Margin = new Thickness(0, 0, 6, 0);
        up.Margin = new Thickness(0, 0, 4, 0);
        down.Margin = new Thickness(0, 0, 6, 0);

        return new Border
        {
            Background = Ui.Brush("#0AFFFFFF"),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 0, 8, 0),
            Child = row
        };
    }

    private string DescribeReferences(ButtonItem item)
    {
        var descriptions = _inspector.DescribeReferences(item.Id);
        return descriptions.Count == 0
            ? "未被任何自动化引用"
            : $"{descriptions.Count} 条自动化：{string.Join("、", descriptions)}";
    }

    private static string NextColor(string? current)
    {
        var index = Array.IndexOf(Ui.Palette, current ?? "");
        return Ui.Palette[(index + 1) % Ui.Palette.Length];
    }

    private void Move(ButtonItem item, int delta)
    {
        var buttons = _store.Config.Buttons;
        var index = buttons.IndexOf(item);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= buttons.Count)
        {
            return;
        }

        buttons.RemoveAt(index);
        buttons.Insert(target, item);
        Persist();
        BuildRows();
    }

    private void Persist()
    {
        _store.Save();
        _onChanged();
    }

    private void SetStatus(string message, bool ok)
    {
        _status.Text = message;
        _status.Foreground = Ui.Brush(ok ? Ui.Success : Ui.Danger);
    }
}
