# ImageToUI

[中文](README.zh-CN.md)

ImageToUI provides the `image-to-ui` Codex skill. It takes a target UI effect
image and a sliced sprite directory, then generates a structured
`ui_structure.json`.

## Example

Example input:

- Effect image: `test/Design/0_Tutorial_1 1.png`
- Sliced sprite directory: `test/Sprite/`
- JSON output: `output/0_Tutorial_1 1/ui_structure.json`

Example Codex request:

```text
Use skills/image-to-ui with effect image test/Design/0_Tutorial_1 1.png and sliced sprites from test/Sprite to generate output/0_Tutorial_1 1/ui_structure.json.
```

## Result

The main result is:

```text
output/0_Tutorial_1 1/ui_structure.json
```

The visual comparison result is:

```text
output/0_Tutorial_1 1/comparison.png
```
