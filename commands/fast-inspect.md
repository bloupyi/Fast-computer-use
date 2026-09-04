---
description: Inspect Windows UI Automation tree for interactive controls with click coordinates
argument-hint: [--target active_window|cursor|screen] [--depth N] [--filter TEXT]
---

Inspect the Windows UI Automation (UIA) accessibility tree to discover interactive buttons, input fields, tabs, menus, and controls with exact pre-calculated center coordinates (x, y) for instant, accurate clicking without coordinate guessing.

## Arguments

Parse `$ARGUMENTS` for:
- `--target active_window|cursor|screen`: Scope to inspect (default: active_window)
- `--depth N`: Maximum hierarchy depth to traverse (default: 7)
- `--filter TEXT`: Optional search filter by control name, type, or automation ID
- `--interactive`: Only show clickable/focusable elements (default: true)
- `--all`: Include non-interactive elements

## Execution

Call the `ui_inspect` MCP tool with the parsed parameters.

Display the discovered UI elements in a clear, numbered list showing:
1. Control type (e.g., [Button], [Edit], [TabItem])
2. Control name or text
3. Automation ID (if present)
4. Pre-calculated center coordinates for clicking
5. Bounding rectangle

## Example Usage

```
/fast-inspect
/fast-inspect --target cursor
/fast-inspect --filter "Save" --depth 10
/fast-inspect --target screen --all
```

## Use Case

This command eliminates the need to visually guess screen coordinates. Instead of taking a screenshot and estimating pixel positions, you can directly inspect the accessibility tree to get exact clickable coordinates for UI elements.
