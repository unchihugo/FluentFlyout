# PiP Toggle —— FluentFlyout 的浏览器画中画搭档扩展

一个很小的 Edge/Chrome 扩展，只做一件事：**把当前正在播放的网页视频切进/切出画中画**。
它注册的快捷键是**系统级（全局）**的，所以浏览器在后台、没有被激活时也能触发——FluentFlyout 就是靠发这个
组合键来"隔空"操作画中画的，全程不碰任何浏览器窗口。

## 安装（只需一次）

1. 打开 `edge://extensions/`
2. 打开左下角的**开发人员模式**
3. 点**加载解压缩的扩展**，选择本文件夹（`edge-pip-extension`）
4. 打开 `edge://extensions/shortcuts`，确认两条都在、且**范围都选"全局"**：

   | 命令 | 建议快捷键 | 范围 |
   | --- | --- | --- |
   | 激活扩展（`_execute_action`） | `Ctrl+Shift+6` | **全局** ← FluentFlyout 用的就是这条 |
   | Toggle picture-in-picture for the playing video | `Ctrl+Shift+6` | **全局** |

   范围不是"全局"的话，浏览器一失去焦点快捷键就失效（这正是当初"只能在浏览器里用"的原因）。
   **另外**：这个组合键还必须没有被别的程序占用 —— 被占用时 Edge 会静默注册失败，按了完全没反应，
   用 `FluentFlyout/tools/hotkey-availability.ps1` 可以查哪些组合是空闲的，
   `tools/hotkey-diag.ps1` 可以验证某个组合能否被合成按键触发（本机实测：`9` 被占用、`7` 无反应、`6` 正常）。
   `commands` 在全局模式下的建议键只能是 `Ctrl+Shift+0..9`，被别的扩展占用就手动设一个，
   改完记得同步改 FluentFlyout 里的 `BrowserPipHelper.PipHotkeyText`。

## 用法

| 操作 | 效果 |
| --- | --- |
| 按 `Ctrl+Shift+6`（全局） | 正在播放的视频进入画中画；已有画中画则退出。**FluentFlyout 用的就是这条** |
| 按 `Ctrl+Shift+6`（全局） | 同上，走扩展命令这条路 |
| 点扩展图标 | 同上 |
| 点 FluentFlyout 任务栏小组件 | 有画中画 → 让浏览器干净退出（视频继续播）；没有且有浏览器正在播放 → 发 `Ctrl+Shift+6`；若浏览器拒绝了，就照旧弹出媒体浮窗（不会毫无反应） |

扩展会在图标上闪角标（`PiP` 已进入 / `off` 已退出 / `arm` 已登记自动画中画 / `-` 该页面没在放视频（正常，不是错误） / `!` 真的失败了），并把结果写进图标的**悬停提示**：
成功显示 "entered/left picture-in-picture"，失败会写明原因和触发方式，例如
`did nothing: NotAllowedError - the browser wants a fresh user gesture (click the page or the toolbar button) (shortcut toggle-pip)`。

## 为什么需要扩展

Chromium 不把"进入画中画"这个能力开放给别的进程：没有命令行参数、没有窗口消息、也没有 COM 接口。
外部程序唯一能做的是模拟人去点界面（要先把浏览器拉到前台，而且 YouTube/B 站 的自带右键菜单会顶掉
浏览器菜单）——所以正路只有两条：页面/用户自己触发，或者扩展用 `chrome.scripting` 调用
`video.requestPictureInPicture()`。

**关闭**画中画外部程序倒是能做（那个小窗是普通顶层窗口，发 `WM_CLOSE` 即可），但实测（Chromium）：
这样关掉之后**浏览器会把视频暂停**——它把这当成"画面被拿走了"。而通过页面 API 退出
（`document.exitPictureInPicture()`，也就是本扩展做的事）播放会继续。所以 FluentFlyout 优先发
本扩展的快捷键来关，只有在扩展没装/没响应时才退回 `WM_CLOSE`（代价就是暂停）。

## 已知限制

- 站点用 `disablepictureinpicture` / `controlsList="nopictureinpicture"` 主动禁用的（例如 Netflix）不行；
- DRM 受保护内容一般不行；
- 页面里必须真的存在 `<video>`（把画面画进 canvas/WebGL 的自定义播放器不行）；
- 视频在跨域 iframe 里没问题（扩展会注入到所有 frame），但顶层页面脚本自己够不着。

### "进入"为什么有时会被拒绝（Chromium 的规矩）

`video.requestPictureInPicture()` 要求**新鲜的"用户手势"**（Chrome 里约 5 秒内），唯一例外是
"已经有元素在画中画里"。而且**退出画中画会把这个手势重置**——所以会出现"第一次能进、后面进不去"
（[Stack Overflow 上就是这个现象](https://stackoverflow.com/questions/56252108/why-video-requestpictureinpicture-works-only-once)）。
所以：

- 你在浏览器里刚点过页面、或刚按过快捷键/点过扩展图标 → 能立刻进 ✓
- 浏览器在后台、页面很久没被碰过 → 直接进会被拒 ✗ —— 所以扩展会**先登记浏览器自带的自动画中画**（见下），
  这时你切走标签页，小窗会自己出现 ✓；FluentFlyout 也会在这种情况弹媒体浮窗兜底，不会"没反应" ✓
- **退出**不需要手势，所以"关闭"永远可靠 ✓

### 浏览器自带的自动画中画（Chrome/Edge 134+，不需要手势）

从 Chrome 134 起，只要页面给 media session 注册了 `"enterpictureinpicture"` 动作，**浏览器自己**会在你切走
标签页时把视频放进角落小窗，回到标签页再自动关掉（[官方说明](https://developer.chrome.com/blog/automatic-picture-in-picture-media-playback)）。
扩展会替你注册这个动作（属性注册不需要手势 ✓），所以：

1. 点小组件/按快捷键 → 登记 + 尝试立刻进入；
2. 立刻进不去也没关系：**你切走标签页的那一刻小窗会自己出现**；
3. 关闭时（点小组件/再按快捷键）会**干净退出并注销登记**，免得它在你关掉后又自己冒出来。

浏览器侧的前提条件（官方列出的）：顶层页面、媒体在顶层、两秒内有声音、有音频焦点、正在播放、
页面注册了该动作，并且**站点是你常用的**（Media Engagement Index 达标；否则首次会在站点信息面板里
问你是否允许"自动画中画"）。普通 `file://` 测试页不满足这些条件，所以本地测不出效果，要在真实站点上试。

## 小文件说明

- `manifest.json` —— MV3 清单：`scripting` 权限、`<all_urls>` 主机权限、两条 global 命令、无弹窗的 action
- `background.js` —— 服务工作者：先探测每个 frame（谁在画中画里、谁有可用的视频），再只对相关 frame 精确注入退出/进入，最后更新角标与悬停提示

## 维护须知（踩过的坑，改之前务必看）

1. **注入进页面的函数必须自包含** ✗✗
   `chrome.scripting.executeScript({ func })` **只序列化这个函数自己的源码**，它**不能调用同文件里的其它函数或常量**。
   一旦调用（例如早前的 `playerOfThisFrame()`），页面里会抛 `ReferenceError`，而调用方**看起来是成功的、只是返回空**
   —— 表现为"扩展什么都没做、图标变红"，非常难查。现在每个注入函数（`probeFrame` / `enterPictureInPicture` /
   `exitPictureInPicture` / `armAutoPictureInPicture` / `disarmAutoPictureInPicture`）都各自重复那段找视频的逻辑。
   日志里若出现 `{"result":"injection-empty"}` 就是这个问题复发。
2. **标签页枚举不要用 `chrome.windows.getAll({populate:true})`** ✗
   它需要 `tabs` 权限，没有权限时**不报错、只返回没有 tabs 的窗口**，于是扩展一个页面都看不到。现在用
   `chrome.tabs.query({})`（`id`/`active` 不需要权限），并保留 `windows` 兜底。
3. **快捷键必须是"全局 + 本机没被占用"** ✗
   被别的程序占用的组合键，浏览器会**静默注册失败**，按了完全没反应。查空闲键：
   `FluentFlyout/tools/hotkey-availability.ps1`；验证"合成按键能不能触发"：`tools/hotkey-diag.ps1`。
   本机实测：`Ctrl+Shift+9` 被占用 ✗、`Ctrl+Shift+7` 空闲但合成按键被吞 ✗、**`Ctrl+Shift+6` 正常 ✓**（当前使用）。
4. **`_execute_action`（"激活扩展"）不能设为全局** ✗ —— 范围下拉是灰的，所以 FluentFlyout 只能走命名命令。
5. **进入画中画需要"新鲜的用户手势"** ✗（Chromium 规则，约 5 秒；退出不需要）。
   扩展的策略：先登记浏览器自带的自动画中画（Chrome/Edge 134+，无需手势），再尝试立即进入。
   因此**蓝色 `PiP`** = 立即进入 ✓；**橙色 `arm`** = 已登记，切走标签页时小窗会自己出现 ✓。

## 版本流水（排查时看这个）

| 版本 | 变化 |
| --- | --- |
| 1.0.0 | 最初版本（自包含，**能工作**）|
| 1.3.0 | 抽出 `playerOfThisFrame()` 公共函数 → **注入函数全部失效**（第 1 条坑）|
| 1.6.0 | 增加 `chrome.storage.local` 自记账日志（`pipLog`）|
| 1.8.0 | 改用 `chrome.tabs.query` 枚举标签页（第 2 条坑）|
| 1.9.0/1.9.1 | 增加自检与注入空返回诊断 |
| **2.0.0** | 注入函数全部改回**自包含** → 恢复工作 ✓（本次）|
