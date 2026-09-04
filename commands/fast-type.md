---
description: Type text or execute keyboard hotkeys using native Win32 input
argument-hint: <text_or_hotkey> [--paste] [--delay N]
---

Execute native Win32 keyboard input - fast typing, instant clipboard pasting, or hotkey combinations.

## Arguments

Parse `$ARGUMENTS` for:
- First positional argument: text to type OR hotkey combination
- `--paste`: Use instant clipboard paste mode (<10ms for large blocks)
- `--delay N`: Inter-keystroke delay in milliseconds for typing (default: 5)

## Hotkey Detection

If the argument looks like a hotkey pattern (contains `+` like `ctrl+c`, `win+r`, `alt+tab`), treat it as a hotkey. Otherwise, treat it as text to type.

**Common hotkeys:**
- `ctrl+c`, `ctrl+v`, `ctrl+x`, `ctrl+a`, `ctrl+z`
- `win+r`, `win+e`, `win+d`
- `alt+tab`, `alt+f4`
- `ctrl+shift+esc`
- `enter`, `escape`, `tab`, `backspace`

## Execution

### For Hotkeys
Call `keyboard_action` MCP tool with:
- `action`: `"hotkey"`
- `hotkey`: the parsed hotkey string

### For Text (normal typing)
Call `keyboard_action` MCP tool with:
- `action`: `"type_text"`
- `text`: the input text
- `delay_ms`: parsed delay value

### For Text (paste mode)
Call `keyboard_action` MCP tool with:
- `action`: `"paste_text"`
- `text`: the input text

Display the action result.

## Example Usage

```
/fast-type "Hello, world!"
/fast-type "const x = 42;\nconsole.log(x);" --paste
/fast-type ctrl+c
/fast-type win+r
/fast-type ctrl+shift+esc
/fast-type "some slow text" --delay 50
```

## Use Case

- **Typing**: Normal text input with configurable inter-keystroke delay
- **Paste**: Inject large code blocks or multiline text in <10ms via clipboard
- **Hotkeys**: Execute keyboard shortcuts (copy, paste, window management, etc.)
