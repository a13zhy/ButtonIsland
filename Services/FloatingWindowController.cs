using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ButtonIsland.Views;

namespace ButtonIsland.Services;

/// <summary>
/// 负责创建、维护悬浮窗与编辑窗口，并对外提供展开/收起/显示隐藏的入口
/// （设置页和全局快捷键都用这些入口）。
/// </summary>
public sealed class FloatingWindowController
{
    private readonly ButtonStore _store;
    private readonly ButtonEventHub _hub;
    private readonly AutomationInspector _inspector;

    private FloatingWindow? _floating;
    private EditorWindow? _editor;

    /// <summary>当前进程里的实例（设置页用）。</summary>
    public static FloatingWindowController? Current { get; private set; }

    public FloatingWindowController(ButtonStore store, ButtonEventHub hub, AutomationInspector inspector)
    {
        _store = store;
        _hub = hub;
        _inspector = inspector;
        Current = this;
    }

    public FloatingWindow? Window => _floating;

    public bool IsEditorOpen => _editor != null && _editor.IsVisible;

    public void StartWhenReady()
    {
        Dispatcher.UIThread.Post(() => TryStart(0));
    }

    private void TryStart(int attempt)
    {
        try
        {
            if (Application.Current == null ||
                Application.Current.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime)
            {
                Retry(attempt);
                return;
            }

            ShowFloating();
        }
        catch (Exception ex)
        {
            ButtonStore.Log("创建悬浮窗失败：" + ex);
            Retry(attempt);
        }
    }

    private void Retry(int attempt)
    {
        if (attempt >= 40)
        {
            ButtonStore.Log("多次尝试后仍未能创建悬浮窗，已放弃。");
            return;
        }

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            TryStart(attempt + 1);
        };
        timer.Start();
    }

    private void ShowFloating()
    {
        if (_floating != null)
        {
            ReconcileVisibility();
            return;
        }

        _store.Load();
        _floating = new FloatingWindow(_store, _hub, OpenEditor);
        ReconcileVisibility();
        ButtonStore.Log("悬浮窗已创建。");
    }

    /// <summary>按配置决定悬浮窗该不该显示。</summary>
    public void ReconcileVisibility()
    {
        var window = _floating;
        if (window == null)
        {
            return;
        }

        if (_store.Config.ShowFloating)
        {
            if (!window.IsVisible)
            {
                window.Show();
            }
        }
        else if (window.IsVisible)
        {
            window.Hide();
            ButtonStore.Log("悬浮窗已按设置隐藏");
        }
    }

    /// <summary>展开/收起切换（快捷键、设置页用）。</summary>
    public void TogglePanel()
    {
        var window = EnsureVisible();
        if (window == null)
        {
            return;
        }

        if (!window.IsVisible)
        {
            window.Show();
        }

        window.Toggle();
    }

    public void ExpandPanel()
    {
        var window = EnsureVisible();
        if (window == null)
        {
            return;
        }

        if (!window.IsVisible)
        {
            window.Show();
        }

        window.Expand();
    }

    public void CollapsePanel()
    {
        _floating?.Collapse();
    }

    /// <summary>设置页改了外观/按钮后统一套用。</summary>
    public void ApplyAppearance()
    {
        try
        {
            _store.Load();
            if (_floating == null)
            {
                ShowFloating();
                return;
            }

            ReconcileVisibility();
            _floating.ApplyAppearance();
        }
        catch (Exception ex)
        {
            ButtonStore.Log("套用外观失败：" + ex);
        }
    }

    /// <summary>按钮数据变化后刷新内容。</summary>
    public void Rebuild()
    {
        try
        {
            _floating?.Rebuild();
        }
        catch (Exception ex)
        {
            ButtonStore.Log("刷新悬浮窗失败：" + ex);
        }
    }

    private FloatingWindow? EnsureVisible()
    {
        if (_floating == null)
        {
            TryStart(0);
            return null;
        }

        return _floating;
    }

    public void OpenEditor()
    {
        try
        {
            if (_editor == null)
            {
                _editor = new EditorWindow(_store, _hub, _inspector, OnDataChanged);
                _editor.Closed += (_, _) => _editor = null;
            }

            if (!_editor.IsVisible)
            {
                _editor.Show();
            }

            _editor.Activate();
        }
        catch (Exception ex)
        {
            ButtonStore.Log("打开编辑窗口失败：" + ex);
        }
    }

    private void OnDataChanged()
    {
        Rebuild();
    }
}
