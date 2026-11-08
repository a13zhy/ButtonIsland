using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia.Threading;

namespace ButtonIsland.Services;

/// <summary>
/// 全局快捷键（系统级，不要求窗口有焦点）。
///
/// 实现方式：后台线程 + RegisterHotKey(hWnd=NULL) + GetMessage 循环。
/// 热键是"绑在线程"上的，所以注册/注销都必须在同一个线程里做；
/// 配置变化时整体停掉旧线程再起新线程。
/// </summary>
public sealed class GlobalHotkeyService
{
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint WmHotkey = 0x0312;
    private const uint WmQuit = 0x0012;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern IntPtr GetMessage(out Msg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(int idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern int GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    private sealed class Binding
    {
        public int Id = 0;
        public uint Modifiers;
        public uint VirtualKey;
        public string Name = "";
        public string Gesture = "";
    }

    /// <summary>某条快捷键被按下，参数是它的名字（toggle / editor）。</summary>
    public event Action<string>? Pressed;

    private readonly object _sync = new();
    private Thread? _thread;
    private volatile int _threadId;
    private volatile bool _running;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly List<Binding> _bindings = new();

    public string ToggleGesture { get; private set; } = "";

    public string EditorGesture { get; private set; } = "";

    /// <summary>最后一次应用的结果说明（给设置页显示）。</summary>
    public string Status { get; private set; } = "未启用";

    /// <summary>同一个进程内的实例，设置页/宿主用它。</summary>
    public static GlobalHotkeyService? Current { get; private set; }

    public GlobalHotkeyService()
    {
        Current = this;
    }

    /// <summary>按配置应用两条快捷键。返回给用户看的说明。</summary>
    public string Apply(string toggleGesture, string editorGesture)
    {
        Stop();
        ToggleGesture = toggleGesture?.Trim() ?? "";
        EditorGesture = editorGesture?.Trim() ?? "";

        var list = new List<Binding>();
        var errors = new List<string>();

        var toggle = Parse("toggle", ToggleGesture, errors);
        if (toggle != null)
        {
            list.Add(toggle);
        }

        var editor = Parse("editor", EditorGesture, errors);
        if (editor != null && !SameKey(toggle, editor))
        {
            list.Add(editor);
        }
        else if (editor != null)
        {
            errors.Add("两条快捷键不能相同");
        }

        lock (_sync)
        {
            _bindings.Clear();
            _bindings.AddRange(list);
            _running = true;
        }

        if (list.Count == 0)
        {
            Status = errors.Count > 0 ? string.Join("；", errors) : "未设置快捷键";
            ButtonStore.Log("快捷键：" + Status);
            return Status;
        }

        _ready.Reset();
        _thread = new Thread(ThreadMain)
        {
            IsBackground = true,
            Name = "ButtonIsland.Hotkey"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(2));

        var registered = 0;
        lock (_sync)
        {
            foreach (var b in _bindings)
            {
                if (b.Id > 0)
                {
                    registered++;
                }
            }
        }

        if (errors.Count > 0)
        {
            Status = string.Join("；", errors);
        }
        else if (registered == 0)
        {
            Status = "快捷键注册失败（可能已被其它程序占用）";
        }
        else
        {
            Status = $"已启用 {registered} 条快捷键";
        }

        ButtonStore.Log($"快捷键：{Status}（{ToggleGesture} / {EditorGesture}）");
        return Status;
    }

    public void Stop()
    {
        if (_thread == null)
        {
            return;
        }

        try
        {
            _running = false;
            PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
            _thread.Join(1500);
        }
        catch
        {
            // 退出失败不影响主流程
        }

        _thread = null;
        _threadId = 0;
    }

    private void ThreadMain()
    {
        _threadId = GetCurrentThreadId();
        var owned = new List<Binding>();

        lock (_sync)
        {
            var id = 1;
            foreach (var b in _bindings)
            {
                b.Id = id++;
                if (RegisterHotKey(IntPtr.Zero, b.Id, b.Modifiers, b.VirtualKey))
                {
                    owned.Add(b);
                }
                else
                {
                    b.Id = 0;
                }
            }
        }

        _ready.Set();

        Msg msg;
        while (_running)
        {
            var result = GetMessage(out msg, IntPtr.Zero, 0, 0);
            if (result == IntPtr.Zero || (int)result == -1)
            {
                break;
            }

            if (msg.message == WmHotkey)
            {
                var id = (int)msg.wParam;
                Binding? hit;
                lock (_sync)
                {
                    hit = owned.FirstOrDefault(x => x.Id == id);
                }

                if (hit != null)
                {
                    var name = hit.Name;
                    ButtonStore.Log($"快捷键按下：{hit.Gesture}（{name}）");
                    Dispatcher.UIThread.Post(() => Pressed?.Invoke(name));
                }
            }
        }

        foreach (var b in owned)
        {
            UnregisterHotKey(IntPtr.Zero, b.Id);
        }
    }

    private static bool SameKey(Binding? a, Binding? b)
    {
        return a != null && b != null && a.Modifiers == b.Modifiers && a.VirtualKey == b.VirtualKey;
    }

    private static Binding? Parse(string name, string gesture, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(gesture))
        {
            return null;
        }

        var parts = gesture.Split('+', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        uint mods = 0;
        var keyPart = "";

        foreach (var raw in parts)
        {
            var part = raw.Trim();
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    mods |= ModControl;
                    break;
                case "alt":
                    mods |= ModAlt;
                    break;
                case "shift":
                    mods |= ModShift;
                    break;
                case "win":
                case "meta":
                    mods |= ModWin;
                    break;
                default:
                    keyPart = part;
                    break;
            }
        }

        var vk = ToVirtualKey(keyPart);
        if (vk == 0)
        {
            errors.Add($"快捷键「{gesture}」的按键无法识别（支持字母、数字、F1-F24）");
            return null;
        }

        if (mods == 0)
        {
            errors.Add($"快捷键「{gesture}」至少要带一个修饰键（Ctrl/Alt/Shift/Win）");
            return null;
        }

        var binding = new Binding
        {
            Name = name,
            Gesture = gesture,
            Modifiers = mods,
            VirtualKey = vk
        };
        return binding;
    }

    private static uint ToVirtualKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return 0;
        }

        key = key.Trim();
        if (key.Length == 1)
        {
            var ch = char.ToUpperInvariant(key[0]);
            if (ch >= 'A' && ch <= 'Z')
            {
                return (uint)ch;
            }

            if (ch >= '0' && ch <= '9')
            {
                return (uint)ch;
            }

            return 0;
        }

        if (key.Length <= 3 && (key[0] == 'F' || key[0] == 'f'))
        {
            if (int.TryParse(key[1..], out var n) && n >= 1 && n <= 24)
            {
                return (uint)(0x70 + n - 1);
            }
        }

        return key.ToLowerInvariant() switch
        {
            "space" => 0x20,
            "enter" => 0x0D,
            "tab" => 0x09,
            "esc" => 0x1B,
            "escape" => 0x1B,
            _ => 0
        };
    }
}
