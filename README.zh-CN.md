# ImageToUI

[English](README.md)

ImageToUI 提供 `image-to-ui` Codex skill，用于根据 UI 效果图和切图目录生成
结构化的 `ui_structure.json`。

## 示例

示例输入：

- 效果图：`test/Design/0_Tutorial_1 1.png`
- 切图目录：`test/Sprite/`
- JSON 输出：`output/0_Tutorial_1 1/ui_structure.json`

示例 Codex 请求：

```text
使用 skills/image-to-ui，根据效果图 test/Design/0_Tutorial_1 1.png 和切图目录 test/Sprite，生成 output/0_Tutorial_1 1/ui_structure.json。
```

## 结果位置

主要结果：

```text
output/0_Tutorial_1 1/ui_structure.json
```

视觉对比结果：

```text
output/0_Tutorial_1 1/comparison.png
```
