---
description: Execute a native Win32 mouse click at specified coordinates
argument-hint: <x> <y> [--double] [--triple] [--right] [--middle] [--smooth]
---

Execute a native Win32 hardware-accurate mouse click using SendInput.

## Arguments

Parse `$ARGUMENTS` for:
- First two positional arguments: `x` and `y` screen coordinates (required)
- `--double`: Perform a double-click
- `--triple`: Perform a triple-click
- `--right`: Right-click instead of left-click
- `--middle`: Middle-click
- `--smooth`: Use smooth interpolated cursor movement

## Execution

Call the `mouse_action` MCP tool with:
- `action`: `"click"` (default), `"double_click"`, `"triple_click"`, `"right_click"`, or `"middle_click"`
- `x` and `y` from parsed arguments
- `button`: `"left"` (default), `"right"`, or `"middle"`
- `smooth`: boolean based on `--smooth` flag

Display the action result.

## Example Usage

```
/fast-click 500 300
/fast-click 800 600 --double
/fast-click 120 80 --right
/fast-click 400 400 --smooth
```

## Pro Tip

Combine with `/fast-inspect` to get exact clickable coordinates without guessing:
1. `/fast-inspect --filter "Submit"`
2. Find the button's center coordinates (e.g., 652, 428)
3. `/fast-click 652 428`
