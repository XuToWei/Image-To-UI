# Unity Prefab Export

Use when the user asks for a Unity Prefab from an existing `ui_structure.json`
or includes Prefab assembly in the reconstruction task. The optional repository
`Unity/` package supplies the `build_ui_prefab` UnityAgentBridge command.

The exporter is **Editor-only**. Generate native UGUI objects and ordinary
project assets, with no custom runtime controller, embedded JSON interpreter,
or exporter-specific resource registry.

1. Locate the target Unity project and read its installed Bridge's `AGENT.md`.
   Discover the command and parameter schema with live `list_commands`.
2. If unavailable, check installation from
   `https://github.com/XuToWei/Image-To-UI.git?path=Unity` through Unity Package
   Manager. The suffix selects the directory containing `package.json`.
   Installation details are in `Unity/README.md`; the Unity directory is
   optional and is not part of a standalone copy of this analysis skill.
3. Supply the structure, sliced-asset root, and an `Assets/` Prefab destination.
   Referenced images/fonts are copied into generated assets. Resolve ambiguous
   basenames and unavailable fonts using relative paths and `fontMap`.
4. Read the result and warnings, including `stateObjects` with final hierarchy
   paths. Verify the saved Prefab and inspect it in Unity when visual checking
   is part of the task.

Export every declared state as a complete `States/<name>/Content` branch.
Only `state.current` starts active; each branch retains its own `visible`
settings. Nested state snapshots include ancestor overrides. Keep alternate
artwork and labels available for developers to activate themselves. Do not
add a state controller or infer gameplay events.

Bake positions, native anchors, initial progress appearance, scroll references,
and initial visibility. Hue shifts become texture variants. Application code
owns future visibility, progress updates, device safe-area changes, and other
business behavior. Generated Prefabs remain independent of the exporter.

Preserve analysis artifacts and approximations. Unity font metrics and Outline
rasterization may differ from Python previews; use target-engine evidence
before claiming pixel-for-pixel agreement.
