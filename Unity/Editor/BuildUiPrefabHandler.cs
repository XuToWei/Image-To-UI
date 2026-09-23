using System;
using System.Threading.Tasks;
using AgentBridge;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace ImageToUI.Editor
{
    /// <summary>Unity Agent Bridge discovers this handler through TypeCache.</summary>
    public sealed class BuildUiPrefabHandler : ICommandHandler
    {
        public string Command => "build_ui_prefab";
        public string Description => "从 ui_structure.json 和切图目录组装纯 UGUI Prefab。各状态生成显隐分组，默认只激活当前状态，不附加自定义运行时脚本；返回资源和状态节点路径。";
        public string Group => "Prefab";
        public bool CanDisable => true;
        public CommandBatchMode BatchMode => CommandBatchMode.NotAllowed;

        public Task<object> ExecuteAsync(JObject parameters)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new CommandException("UI_EDIT_MODE_REQUIRED", "Generate UI prefabs in Edit Mode.");
            try { return Task.FromResult<object>(UiPrefabBuilder.Build(parameters)); }
            catch (CommandException) { throw; }
            catch (JsonException ex) { throw new CommandException("UI_INVALID_STRUCTURE", ex.Message); }
            catch (ArgumentException ex) { throw new CommandException("UI_INVALID_STRUCTURE", ex.Message); }
            catch (Exception ex) { throw new CommandException("UI_PREFAB_BUILD_FAILED", ex.Message); }
        }

        public JObject ParamsSchema { get; } = new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["structurePath"] = new JObject { ["type"] = "string", ["description"] = "ui_structure.json 的绝对路径或 Unity 工程相对路径" },
                ["assetsPath"] = new JObject
                {
                    ["oneOf"] = new JArray(
                        new JObject { ["type"] = "string", ["minLength"] = 1 },
                        new JObject { ["type"] = "array", ["minItems"] = 1, ["items"] = new JObject { ["type"] = "string", ["minLength"] = 1 } }),
                    ["description"] = "一个切图目录字符串或非空目录字符串数组；重叠目录去重，同名歧义须用唯一相对路径消歧。优先引用 Assets/Packages 资源，复用导入和派生资源。"
                },
                ["prefabPath"] = new JObject { ["type"] = "string", ["description"] = "目标 Prefab，必须是 Assets/ 下的 .prefab 路径" },
                ["fontMap"] = new JObject { ["type"] = "object", ["description"] = "可选：fontFamily 字符串到字体文件路径的映射" },
                ["defaultFontPath"] = new JObject { ["type"] = "string", ["description"] = "可选：找不到指定字体时使用的字体文件" },
                ["overwrite"] = new JObject { ["type"] = "boolean", ["default"] = false }
            },
            ["required"] = new JArray("structurePath", "assetsPath", "prefabPath")
        };
    }
}
