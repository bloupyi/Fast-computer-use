---
description: Display Fast Computer Use plugin help and command reference
---

# Fast Computer Use Plugin

High-performance native Windows automation plugin for computer use. Replaces slow, token-heavy ad-hoc PowerShell scripts with a native C# automation engine delivering sub-40ms screen capture, instant UI element inspection, hardware-level input, and batch action pipelines.

## Available Commands

### `/fast-screen` - High-Speed Screenshot
Capture screenshots with sub-40ms performance, optional downscaling to reduce visual tokens by up to 75%, multi-monitor support, and ROI cropping.

**Usage:**
```
/fast-screen [--screen N] [--max-dim N] [--active] [--roi x,y,w,h] [--format jpeg|png] [--quality N] [--save PATH]
```

**Examples:**
- `/fast-screen` - Capture primary monitor
- `/fast-screen --screen 1 --max-dim 1024` - Capture monitor 1, downscaled to 1024px
- `/fast-screen --active --max-dim 1280` - Capture active window only
- `/fast-screen --roi 0,0,800,600` - Capture region of interest

---

### `/fast-inspect` - UI Automation Tree Inspector
Inspect Windows UI Automation accessibility tree to discover interactive controls with pre-calculated click coordinates. Eliminates coordinate guessing.

**Usage:**
```
/fast-inspect [--target active_window|cursor|screen] [--depth N] [--filter TEXT] [--all]
```

**Examples:**
- `/fast-inspect` - Inspect active window
- `/fast-inspect --filter "Submit"` - Find Submit button coordinates
- `/fast-inspect --target cursor` - Inspect element under cursor
- `/fast-inspect --target screen --all` - Full screen scan including non-interactive

---

### `/fast-click` - Native Mouse Click
Execute hardware-accurate Win32 mouse clicks at specified screen coordinates.

**Usage:**
```
/fast-click <x> <y> [--double] [--triple] [--right] [--middle] [--smooth]
```

**Examples:**
- `/fast-click 500 300` - Left-click at (500, 300)
- `/fast-click 800 600 --double` - Double-click
- `/fast-click 120 80 --right` - Right-click
- `/fast-click 400 400 --smooth` - Smooth cursor movement

---

### `/fast-type` - Native Keyboard Input
Type text, execute hotkeys, or paste large blocks instantly via clipboard.

**Usage:**
```
/fast-type <text_or_hotkey> [--paste] [--delay N]
```

**Examples:**
- `/fast-type "Hello, world!"` - Type text
- `/fast-type ctrl+c` - Execute Copy hotkey
- `/fast-type "const x = 42;\nconsole.log(x);" --paste` - Instant paste (<10ms)
- `/fast-type win+r` - Open Run dialog

---

## MCP Tools (for direct invocation)

When computer use is enabled, you also have direct access to these MCP tools:

- **`screen_capture`** - Screenshot with downscaling, ROI, multi-monitor
- **`ui_inspect`** - UI Automation tree with clickable coordinates
- **`mouse_action`** - Click, double-click, drag, scroll, move
- **`keyboard_action`** - Type, paste, hotkeys, key presses
- **`batch_actions`** - Execute multi-step sequences atomically
- **`window_manager`** - List, focus, move, resize, close windows
- **`system_info`** - Display configs, cursor position, active window

## Typical Workflow

1. **Take a screenshot** to see the current state:
   ```
   /fast-screen --max-dim 1280
   ```

2. **Inspect UI elements** to find exact click coordinates:
   ```
   /fast-inspect --filter "Submit"
   ```

3. **Click the button** using the discovered coordinates:
   ```
   /fast-click 652 428
   ```

4. **Type or paste text** into input fields:
   ```
   /fast-type "user@example.com" --paste
   ```

5. **Execute hotkeys** for keyboard shortcuts:
   ```
   /fast-type ctrl+s
   ```

## Performance

- **Screen capture**: Sub-40ms (downscaled), ~72ms typical
- **UI inspection**: <100ms
- **Mouse click**: ~0ms dispatch
- **Clipboard paste**: <10ms for large blocks
- **Daemon command**: Sub-2ms latency

## Requirements

- Windows 10/11
- .NET Framework 4.8
- Node.js 18+ (for MCP server)

## Architecture

```
User Request / Computer Use
    ↓
MCP Server (Node.js)
    ↓ stdio JSON-RPC
Native Engine (C# .NET 4.8)
    ↓
Windows APIs (GDI, SendInput, UIAutomation)
```

## Tips

- Use `--max-dim 1024` or `1280` to reduce visual token usage by up to 75% while maintaining legibility
- Combine `/fast-inspect` + `/fast-click` to eliminate coordinate guessing
- Use `--paste` mode for large code blocks or multiline text (10x faster than typing)
- Batch multiple actions together for atomic execution without round-trip latency (a `{"cmd": "wait", "ms": N}` item pauses between steps)
- `window_manager`'s `restore` un-minimizes a window but does not take keyboard focus back — call `focus` explicitly before typing/clicking into a window you just restored

## Testing

Run `node server/smoke-test.mjs` from the plugin root to exercise all 7 MCP
tools against the real server and native engine end to end. Exit code 0 means
every check passed.

---

For more details, see the README at:
`C:\Users\bloup\.claude\local-plugins\fast-computer-use\README.md`
