using ButtonIsland.Automation;
using ButtonIsland.Services;
using ButtonIsland.Views;
using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Extensions.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ButtonIsland;

/// <summary>
/// ButtonIsland 插件入口。
///
/// 插件做三件事：
/// 1. 画出右下角的按钮悬浮窗（可收起成手柄）；
/// 2. 把"按钮被点击/被再次点击"注册成 ClassIsland 的自动化触发器；
/// 3. 提供设置页和全局快捷键。
/// 点按钮之后执行什么，由用户在「自动化」里自己搭。
/// </summary>
[PluginEntrance]
public class Plugin : PluginBase
{
    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        var store = new ButtonStore(this);
        services.AddSingleton(store);
        services.AddSingleton<ButtonEventHub>();
        services.AddSingleton<AutomationInspector>();
        services.AddSingleton<GlobalHotkeyService>();
        services.AddSingleton<FloatingWindowController>();
        services.AddHostedService<PluginHost>();

        // 注册自动化触发器：注册后，自动化工作流的「触发」下拉里就能选到它们。
        services.AddTrigger<ButtonClickedTrigger, ButtonClickedTriggerSettingsControl>();
        services.AddTrigger<ButtonReclickedTrigger, ButtonReclickedTriggerSettingsControl>();

        // 设置窗口里的「ButtonIsland 按钮岛」页。
        services.AddSettingsPage<ButtonIslandSettingsPage>();

        ButtonStore.Log($"插件初始化，配置目录：{store.FolderPath}");
    }
}
