# Unity 导入器参考

这是一个仅包含 Editor 代码的 Unity UPM 包。安装后，UnityAgentBridge 自动发现
`build_ui_prefab`，把 `ui_structure.json` 与切图组装成使用标准 UGUI 组件的 Prefab。

**生成物没有自定义运行时脚本、运行时 JSON 解析器或导入器资源表依赖。**
显隐状态会成为实际的 GameObject 分组，默认只激活当前状态。业务控制由开发者实现。

支持 Unity 2022.3+，已在 Unity 6 `6000.3.18f1` 运行编辑器测试。

从效果图开始，请按[Codex → Unity 完整运行流程](../README.zh-CN.md#完整运行流程)
连续完成分析、复核和导入。本页补充 Unity 安装细节、命令参数和组件说明，
无需另开一项任务。

参考：[安装](#安装) · [生成 Prefab](#生成-prefab) ·
[在场景中使用](#在场景中使用) · [常见问题](#常见问题)。

## 准备输入

完整流程中的 Codex 会先在 `test/output-local` 生成并复核 `ui_structure.json`，
再把这份 JSON 和原切图根目录交给本命令。本页示例沿用同一组路径，
实际使用时替换为本次任务的输入和输出。

如果只想试用已有结构，可以将 `structurePath` 换为仓库案例
`test/output/ui_structure.json`；已有 JSON 时，Unity 导入器本身无需调用 Python。

## 安装

通过 GitHub 安装，确保本机已安装 Git 且 Unity 能调用它。
本仓库地址为 `https://github.com/XuToWei/Image-To-UI.git`，Unity 包位于
`Unity/` 子目录，因此 Package Manager 使用的完整地址为：

```text
https://github.com/XuToWei/Image-To-UI.git?path=Unity
```

### Package Manager 安装

1. 打开目标 Unity 工程的 Package Manager，选择 **Add package from git URL**。
2. 输入 `https://github.com/XuToWei/UnityAgentBridge.git?path=Unity`，等待依赖解析与编译。
   已安装 Bridge 时跳过这一步。
3. 再添加 `https://github.com/XuToWei/Image-To-UI.git?path=Unity`。
4. 等待编译结束，确认 Console 无编译错误。uGUI 和 Newtonsoft.Json 由包依赖解析。

### manifest 安装

也可在目标工程 `Packages/manifest.json` 的 `dependencies` 中加入这两项。
下面展示文件结构，请保留工程已有的其他依赖：

```json
{
  "dependencies": {
    "me.xw.unityagentbridge": "https://github.com/XuToWei/UnityAgentBridge.git?path=Unity",
    "com.image-to-ui.unity": "https://github.com/XuToWei/Image-To-UI.git?path=Unity"
  }
}
```

### 启用 Bridge 并检查安装

保持在 Edit Mode，打开 **Window > Agent Bridge**，启用 Bridge 宿主。
确认命令管理器的 `Prefab` 分组包含 `build_ui_prefab`，且该命令未被禁用。
Agent 首次使用时执行 `list_commands`，以返回结果确认命令和参数 schema。

Prefab 只依赖标准 UGUI 和生成目录中的素材；生成后即使卸载本导入器，也不会出现
本包的 Missing Script。项目仍需保留 uGUI。

## 生成 Prefab

这是[完整运行流程](../README.zh-CN.md#完整运行流程)的第 6 步。
Codex 完成本次 JSON 的分析与复核后，直接继续发出下面的 command/params。
`<repo>` 在调用前替换为仓库的绝对路径：

```json
{
  "command": "build_ui_prefab",
  "params": {
    "structurePath": "<repo>/test/output-local/ui_structure.json",
    "assetsPath": "<repo>/test/source/sprites",
    "prefabPath": "Assets/Generated/EmberfallMainUI.prefab"
  }
}
```

实际文件 IPC 按已安装 Bridge 的 `AGENT.md` 添加版本和全新 id，并遵守命令发现、
单请求、原子发布和响应 ack 规则。命令在 Edit Mode 执行，不支持 batch 或 Undo。

| 参数 | 含义 |
| --- | --- |
| `structurePath` | 必填。JSON 的绝对路径或 Unity 工程相对路径。 |
| `assetsPath` | 必填。切图根目录，可位于工程外。递归查找 PNG/JPG。 |
| `prefabPath` | 必填。`Assets/` 下的 `.prefab` 路径，缺失的父目录会创建。 |
| `fontMap` | 可选。JSON 的 `fontFamily` 字符串到字体文件路径的映射。 |
| `defaultFontPath` | 可选。指定字体缺失时使用的 TTF/OTF 文件。 |
| `overwrite` | 默认 `false`。覆盖已有目标时显式设为 `true`。 |

返回 Prefab 路径、GUID、资源目录、元素/图片/文字数量、`stateObjects` 和警告。
`stateObjects` 每项包含 JSON 元素路径、状态名、Prefab 中最终对象路径和初始
`activeSelf`，可用于定位滚动视口、进度裁剪或嵌套状态中的实际分组。

错误码包括 `UI_INVALID_STRUCTURE`、`UI_PREFAB_EXISTS`、
`UI_EDIT_MODE_REQUIRED`、`UI_PREFAB_BUILD_FAILED`。

### 字体映射与重新生成

字体与 JSON 中的 `fontFamily` 不在同一路径时，可使用 `fontMap`。以下参数应与
前面的三个必填参数一起使用，示例字体路径需要替换为项目中已有的 TTF/OTF 文件：

```json
{
  "fontMap": {
    "C:/Windows/Fonts/georgiab.ttf": "Assets/Fonts/MyUI-Bold.ttf"
  },
  "defaultFontPath": "Assets/Fonts/MyUI-Regular.ttf",
  "overwrite": true
}
```

`fontMap` 的键要对应 JSON 中的 `fontFamily`。需要覆盖已有目标时设置
`overwrite: true`；每次生成后都检查返回的 `warnings`。

## 在场景中使用

1. 在 Project 窗口找到返回的 `prefabPath`，双击进入 Prefab Mode 检查层级。
2. 将 Prefab 拖入 Hierarchy。它自带 Canvas、CanvasScaler 和 GraphicRaycaster，
   通常作为场景根对象使用。检查目标 Game View 尺寸下的锚点、文字和裁剪。
3. 保留返回的 `resourceFolder` 目录。示例首次导出通常会生成
   `Assets/Generated/EmberfallMainUI_Resources`；若目录已存在会使用唯一名称，
   以实际返回路径为准。
4. 对 JSON 已声明的状态，在 Inspector 中切换相应 GameObject 的激活复选框，
   或在项目脚本中使用 `SetActive`。下一节说明具体层级。
5. 为按钮配置 `onClick`，由项目更新进度和业务数据。需要点击、拖拽或滚轮时，
   场景应有 EventSystem 及与项目输入系统匹配的输入模块。

## 显隐状态的层级

例如 JSON 声明 `mode.state.current = "normal"`：

```text
Prefab
└── root
    └── mode
        └── States
            ├── normal             [激活]
            │   └── Content        [普通外观及完整子层级]
            └── selected           [关闭]
                └── Content        [选中外观及完整子层级]
```

每个声明的状态都从基础结构生成完整分支，应用该状态的素材、文字、颜色和显隐覆盖。
嵌套状态会保留父状态覆盖，再应用子状态。素材在分支之间复用。

`Content` 保留该状态自身的 `visible`：即使当前状态表示完全隐藏，仍保留一个被选中
的状态分组及其关闭的内容，其他状态的图像也不会丢失。

开发者可直接控制普通 GameObject：

```csharp
var states = instance.transform.Find("root/mode/States");
states.Find("normal").gameObject.SetActive(false);
states.Find("selected").gameObject.SetActive(true);
```

示例路径需要替换为实际层级；也可使用命令返回的 `stateObjects.objectPath`。
状态名中的路径分隔符会转义，冲突名称会唯一化。不会生成控制显隐、切换状态、
更新进度或驱动业务的脚本，也不会为 Button 接入事件。

## 组装内容

| JSON | 生成结果 |
| --- | --- |
| `canvas` | Canvas、CanvasScaler、GraphicRaycaster，以设计尺寸为参考分辨率。 |
| 层级、坐标、尺寸 | RectTransform，转换左上原点和 y 轴方向。 |
| `anchor`、`responsive` | 原生锚点、拉伸范围与偏移；可跟随父尺寸。 |
| `layout`、`align`、`vAlign` | 烘焙参考布局，并将能表达的跟随关系写入原生锚点。 |
| `image`、`rect`、`overlay` | Image、Sprite、颜色、局部透明度和九宫格。 |
| `hueShift` | 生成时烘焙为 PNG，保留 alpha，使用标准 UI 材质。 |
| `text` | UGUI Text、字体、字号、对齐、行距、横向缩放和 Outline。 |
| `button` | 标准 Button 与点击区域，事件留空。 |
| `state` | `States/<状态>/Content` 分支，默认仅当前状态激活。 |
| `progress` | 完整填充内容、原生裁剪视口和初始数值文字。 |
| `scroll` | ScrollRect、视口、RectMask2D、内容引用和初始滚动位置。 |
| `visible` | GameObject 初始显隐；参考布局保留其位置。 |

进度条的裁剪范围写入 `__ImageToUI_ProgressClip` 的锚点，完整素材保留在其
`Content` 中；零进度时关闭裁剪分组。运行时进度、数值文字、状态切换、按钮行为
及动态增删列表项由开发者接线。拖拽与点击需要场景中的 EventSystem。

`canvas.safeArea` 作为提供的边距烘焙进锚点偏移。设备安全区变化、超出参考布局范围的
特殊重排等由项目处理，导入器不生成持续更新脚本。文字和 Outline 采用 Unity 的实现，
应在目标字体和 Canvas 设置下复核显示效果。

## 资源与保存

- 复制被引用的素材到新的 `<PrefabName>_Resources` 目录，不修改源图片及 importer。
- basename 不唯一时要求使用相对切图根目录的完整路径。
- TTF/OTF 按 `fontMap` 和 JSON 路径查找并复制；字体替代会返回警告。
- 显式九宫格边距保存为原生 Sprite 资源。色相变体保存为 PNG。
- 在临时预览场景中组装，保存后清理临时对象，不改变当前场景或保存无关脏资源。
- 覆盖保留 Prefab GUID，旧资源目录保留以避免破坏其他引用。

此前导出的带 `UiPrefabController` 的 Prefab 需要重新生成。

## 常见问题

| 现象 | 处理 |
| --- | --- |
| Git 安装提示仓库根目录找不到 `package.json` | 使用带 `?path=Unity` 的完整 UPM 地址。 |
| `AgentBridge.Editor` 程序集缺失或接口编译失败 | 先安装兼容的 UnityAgentBridge，确认 Console 编译通过，再发现命令。 |
| `list_commands` 没有 `build_ui_prefab` | 检查是否安装了本包、Unity 是否完成编译、Bridge 是否启用，以及命令是否被禁用。 |
| 返回 `UI_EDIT_MODE_REQUIRED` | 退出 Play Mode 后调用。 |
| 返回 `UI_PREFAB_EXISTS` | 更换输出路径，或在需要覆盖时传 `overwrite: true`。 |
| 返回 `UI_INVALID_STRUCTURE` | 根据错误信息检查路径、JSON 必填字段、素材重名和组件声明；素材重名时使用相对根目录的完整路径。 |
| 文字字体不符 | 检查 `warnings`，提供 `fontMap` 或正确的 `defaultFontPath` 后重新生成。 |
| 看不到另一个状态 | 该分组默认关闭；按 `stateObjects.objectPath` 找到它，并检查状态分组及其 `Content` 的激活状态。 |
| 按钮或滚动区不响应 | 检查场景的 EventSystem、输入模块，以及项目是否接好了按钮事件。 |

## 测试

在工程 manifest 的 `testables` 中加入 `com.image-to-ui.unity`，
运行 EditMode 的 `ImageToUI.Tests`。测试实际保存、重新加载 Prefab，并检查状态分支、
显隐、嵌套覆盖、进度裁剪、滚动、原生锚点、资源持久化和场景隔离，还会断言生成物
没有本导入器的脚本或资源依赖。完整仓库可用时会导入 71 节点 Emberfall 案例。
