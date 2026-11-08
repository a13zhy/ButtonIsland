using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Microsoft.Extensions.Hosting;

namespace ButtonIsland.Services;

/// <summary>
/// 插件宿主服务：ClassIsland 启动完成后拉起悬浮窗、装快捷键、跑一次诊断。
/// </summary>
public sealed class PluginHost : IHostedService
{
    private readonly FloatingWindowController _controller;
    private readonly AutomationInspector _inspector;
    private readonly ButtonStore _store;
    private readonly GlobalHotkeyService _hotkey;

    public PluginHost(
        FloatingWindowController controller,
        AutomationInspector inspector,
        ButtonStore store,
        GlobalHotkeyService hotkey)
    {
        _controller = controller;
        _inspector = inspector;
        _store = store;
        _hotkey = hotkey;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _controller.StartWhenReady();

        _hotkey.Pressed += OnHotkey;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _inspector.LogDiagnostics();
        };
        timer.Start();

        // 快捷键不用等太久，但也要等应用起来（否则注册线程里的 UI 回调没有 Dispatcher 可用）。
        var hotkeyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        hotkeyTimer.Tick += (_, _) =>
        {
            hotkeyTimer.Stop();
            try
            {
                _store.Load();
                _hotkey.Apply(_store.Config.HotkeyToggle, _store.Config.HotkeyEditor);
            }
            catch (Exception ex)
            {
                ButtonStore.Log("启动快捷键失败：" + ex);
            }
        };
        hotkeyTimer.Start();

        return Task.CompletedTask;
    }

    private void OnHotkey(string name)
    {
        switch (name)
        {
            case "toggle":
                _controller.TogglePanel();
                break;
            case "editor":
                _controller.OpenEditor();
                break;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _hotkey.Pressed -= OnHotkey;
        _hotkey.Stop();
        return Task.CompletedTask;
    }
}
