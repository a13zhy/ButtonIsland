using System;
using System.Collections.Generic;

namespace ButtonIsland.Services;

/// <summary>
/// 按钮点击事件总线。
///
/// 悬浮窗只负责"某个按钮被点了"，它不知道也不关心有哪些自动化在工作。
/// 触发器实例订阅这里的事件，自己判断"设置里选的按钮是不是我"，再调用 Trigger()。
/// 这样按钮与自动化的关系完全由 ClassIsland 的自动化配置决定。
/// </summary>
public sealed class ButtonEventHub
{
    private readonly Dictionary<string, bool> _states = new();
    private readonly object _sync = new();

    public ButtonEventHub()
    {
        Current = this;
    }

    /// <summary>当前进程里的实例，供设置页/自检按钮使用。</summary>
    public static ButtonEventHub? Current { get; private set; }

    /// <summary>(按钮 Id, 是否为"再次点击")</summary>
    public event Action<string, bool>? Clicked;

    /// <summary>
    /// 触发一次按钮点击。返回值表示这一次是否属于"再次点击"。
    ///
    /// 状态机：按钮默认是"关"。第一次点击 → 触发"被点击"，切到"开"；
    /// 在"开"状态下再点 → 触发"被再次点击"，切回"关"。
    /// 因此同一个按钮可以当开关用，只配"被点击"就是只响应奇数次点击，
    /// 两个触发器都配上就是每次点击都执行。
    /// </summary>
    public bool Click(string buttonId)
    {
        bool isAgain;
        lock (_sync)
        {
            var on = _states.TryGetValue(buttonId, out var state) && state;
            isAgain = on;
            _states[buttonId] = !on;
        }

        ButtonStore.Log($"按钮点击：{buttonId}，再次点击={isAgain}");

        try
        {
            Clicked?.Invoke(buttonId, isAgain);
        }
        catch (Exception ex)
        {
            ButtonStore.Log("触发自动化时异常：" + ex);
        }

        return isAgain;
    }

    /// <summary>按钮当前是不是"开"状态（悬浮窗用它给磁贴加高亮）。</summary>
    public bool IsOn(string buttonId)
    {
        lock (_sync)
        {
            return _states.TryGetValue(buttonId, out var state) && state;
        }
    }

    /// <summary>按钮被删除时清掉它的开关状态。</summary>
    public void Forget(string buttonId)
    {
        lock (_sync)
        {
            _states.Remove(buttonId);
        }
    }
}
