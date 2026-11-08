# ButtonIsland · 按钮岛

给 [ClassIsland](https://github.com/ClassIsland/ClassIsland) 写的插件：在屏幕右下角放一排快捷按钮，点一下就触发你自己的自动化工作流。

插件本身**不执行任何动作**。它只干两件事：把按钮画出来，把「某个按钮被点了」这件事变成 ClassIsland 自动化里可选的触发器。按钮点完干什么——提醒、切课表、开投影、放铃声——全在 ClassIsland 的自动化页里自己搭。

> 适配 ClassIsland 2.1.0.x，插件 API 版本 2.1.0.0。

## 功能

- **右下角悬浮窗**：默认收起成一条 22px 宽的小手柄，点手柄拉出面板，鼠标离开若干秒自动收回。收起不是把窗口推到屏幕外，而是真的把窗口宽从「面板宽度」改成「手柄宽度」。
- **按钮管理**：编辑窗里可以增、删、改名称、改图标（一个 emoji 字符）、改配色、上下调整顺序。改动立即写入 `buttons.json`。
- **点击即触发器**：两个触发器 `被点击` / `被再次点击`，按钮下拉里可以选具体按钮，也可以选「（任意按钮）」。
- **状态可见**：按钮处于「开」状态时描边加粗、底色提亮，因为「被再次点击」什么时候响就取决于这个状态。
- **设置页**：在 ClassIsland 设置里多一页 `ButtonIsland 按钮岛`，配悬浮窗显示/自动收起/等待秒数/手柄宽度/离底部距离，以及两个全局快捷键；页内还有自检区，逐个列出按钮被几条自动化引用、每条工作流倒底会不会执行。
- **全局快捷键**：默认 `Ctrl+Alt+B` 展开/收起面板，`Ctrl+Alt+E` 打开按钮编辑窗，可在设置页改。

## 安装

**用 cipx**：到 [Releases](../../releases) 下载 `ButtonIsland.cipx`，拖进 ClassIsland 的插件页面（或在插件页点安装选文件），重启 ClassIsland。

**手动**：把 cipx 解压到 `data/Plugins/ButtonIsland/`，确认里面有 `ButtonIsland.dll`、`ButtonIsland.deps.json`、`manifest.yml`、`icon.png`，重启。

插件日志在 `%TEMP%\ButtonIsland.log`，设置页里的「打开插件日志」直接就能开。

## 把按钮接到自动化

1. 打开 ClassIsland 的 **自动化** 页，新建（或编辑已有的）工作流。
2. 在「触发器」里选 **ButtonIsland · 按钮被点击** 或 **ButtonIsland · 按钮被再次点击**。
3. 触发器的设置面板里从下拉框选一个按钮，或选「（任意按钮）」。面板里还有一个「试触发一次」，不碰悬浮窗、直接模拟一次点击，用来验证这条链路是不是通的。
4. 动作自己搭，保存。

### 为什么「绑定上了但不执行」

ClassIsland 在 `AutomationService.TriggerTriggered` 里有四道闸门，任何一道不过就直接 `return`，而且用的是 `LogTrace` 级别——普通日志文件里看不到，看起来就像触发器没响：

```csharp
if (!Settings.IsAutomationEnabled) return;
if (sender is not TriggerBase trigger) return;
var workflow = trigger.AssociatedWorkflow;
if (!workflow.ActionSet.IsEnabled) return;
if (workflow.ActionSet.IsRevertEnabled && workflow.ActionSet.Status != ActionSetStatus.Normal) return;
if (workflow.IsConditionEnabled && !RulesetService.IsRulesetSatisfied(workflow.Ruleset)) return;
ActionService.InvokeActionSetAsync(workflow.ActionSet);
```

其中第三条最容易踩：工作流开了**「允许反转」**，并且动作组状态不是 `Normal`（`ActionSetStatus`：`Normal=0 / Invoking=1 / IsOn=2 / Reverting=3`），那么每一次触发都会被静静丢掉。

所以插件的两个触发器会按工作流配置自适配：

| 工作流配置 | 插件行为 |
| --- | --- |
| 关掉「允许反转」 | 每次点击都 `Trigger()`，工作流一定被调用 |
| 开了「允许反转」+ `被点击` | 只在 `Status == Normal` 时触发，否则只写一行日志说明会被丢弃 |
| 开了「允许反转」+ `被再次点击` | 在 `Status == IsOn` 时走 `TriggerRevert()`，这就是 ClassIsland 原生语义 |

如果只想让按钮「点一下就跑一次」，直接在工作流里把「允许反转」关掉最省事。设置页的「工作流诊断」会自动把会被丢的工作流标红，并给一个一键关掉「允许反转」的按钮。

日志里的关键词：`触发器加载`（触发器拿到设置）、`触发器命中`、`触发器跳过`（设置里没选按钮）、`触发器被丢弃`、`触发器命中恢复`、`触发器执行结果`（1.5 秒后回读 Actionset 状态）。

## 运行期文件

| 内容 | 位置 |
| --- | --- |
| 按钮与悬浮窗配置 | `data/Config/Plugins/a13zhy.buttonisland/buttons.json` |
| 插件日志 | `%TEMP%\ButtonIsland.log` |

## 从源码构建

```powershell
# 需要 .NET 8 SDK
dotnet build -c Release

# 打成 cipx（会自动从 assets/icon.png.b64 恢复 icon.png）
powershell -File tools/pack.ps1
```

构建产物在 `bin/Release/net8.0-windows`，需要 `ButtonIsland.dll` 和 `ButtonIsland.deps.json` 两个文件，加上仓库根目录的 `manifest.yml` 和 `icon.png`，四个文件一起打包才是 cipx。

仓库带一个 GitHub Actions 工作流：往 `main` 推代码时自动构建、按 `manifest.yml` 里的版本号打 cipx、发一个同名的 Release。版本号改一下就发新版本；同版本号重复推不会再发一次。

## 一些实现上的坑（给改代码的人）

- **设置控件必须直接继承 `TriggerSettingsControlBase<T>`**。ClassIsland 的 `ActivateTrigger` 用 `info.SettingsControlType.BaseType.GetGenericArguments()` 取设置类型；如果中间隔了一层非泛型基类，`BaseType` 取不到泛型参数，就不会反序列化设置，`Settings` 永远 `null`，面板不出现、永远不会触发。所以 `Views` 里两个设置控件都是直接继承，公共 UI 抽成普通类 `ButtonSettingsPanel`。
- **收起/拉出必须真改窗口宽度**。以前的做法是「窗口保持全尺寸、把 panel 推到屏幕外」，真机上窗口管理器会把窗口夹回工作区，结果收起时面板还全留在屏幕上、手柄也点不到。
- **插件不能从外部触发某个触发器实例**。`TriggerBase.Trigger()` 是 `protected`，`Triggered` 事件是 `internal`，所以只能走「事件总线 + 触发器在 `Loaded()` 里自己订阅」这条路。

## 许可

MIT
