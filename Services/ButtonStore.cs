using System;
using System.IO;
using System.Text.Json;
using ButtonIsland.Models;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions;

namespace ButtonIsland.Services;

/// <summary>
/// 按钮数据的读取与保存。
/// </summary>
public sealed class ButtonStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly PluginBase _plugin;
    private readonly string? _overrideFolder;

    public PluginConfig Config { get; private set; } = new();
    public string FilePath { get; private set; } = "";

    public ButtonStore(PluginBase plugin, string? overrideFolder = null)
    {
        _plugin = plugin;
        _overrideFolder = overrideFolder;
        Current = this;
    }

    /// <summary>当前进程里的实例，供设置页、本身从宿主容器取不到服务的场景兜底。</summary>
    public static ButtonStore? Current { get; private set; }

    /// <summary>数据目录，优先使用 ClassIsland 分配的插件配置目录。</summary>
    public string FolderPath
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_overrideFolder))
            {
                return _overrideFolder!;
            }

            try
            {
                var folder = _plugin.PluginConfigFolder;
                if (!string.IsNullOrWhiteSpace(folder))
                {
                    return folder;
                }
            }
            catch
            {
                // 忽略，走兜底目录
            }

            try
            {
                return Path.Combine(CommonDirectories.AppDataFolderPath, "Plugins", "ButtonIsland");
            }
            catch
            {
                return Path.Combine(Path.GetTempPath(), "ButtonIsland");
            }
        }
    }

    public void Load()
    {
        try
        {
            Directory.CreateDirectory(FolderPath);
            FilePath = Path.Combine(FolderPath, "buttons.json");

            if (!File.Exists(FilePath))
            {
                Config = new PluginConfig();
                Save();
                return;
            }

            var json = File.ReadAllText(FilePath);
            var cfg = JsonSerializer.Deserialize<PluginConfig>(json, JsonOptions);
            Config = cfg ?? new PluginConfig();
            Config.Buttons ??= new();
        }
        catch (Exception ex)
        {
            Log("读取配置失败：" + ex);
            Config = new PluginConfig();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(FolderPath);
            if (string.IsNullOrEmpty(FilePath))
            {
                FilePath = Path.Combine(FolderPath, "buttons.json");
            }

            var json = JsonSerializer.Serialize(Config, JsonOptions);
            File.WriteAllText(FilePath, json);
        }
        catch (Exception ex)
        {
            Log("保存配置失败：" + ex);
        }
    }

    public static void Log(string message)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(Path.GetTempPath(), "ButtonIsland.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\r\n");
        }
        catch
        {
            // 日志失败不影响功能
        }
    }
}
