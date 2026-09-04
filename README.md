# Fast Computer Use Plugin

High-performance native Windows automation plugin that replaces slow, token-heavy ad-hoc PowerShell scripts with a native C# automation engine and MCP server.

## Features

- **Sub-40ms Screen Capture**: GDI-based multi-monitor capture with downscaling (reduces visual tokens by up to 75%)
- **Win32 Input Virtualization**: Hardware-accurate mouse clicks, smooth trajectories, drag-and-drop, Unicode keyboard typing, complex hotkeys
- **Instant Clipboard Paste**: Inject large code blocks in <10ms via STA clipboard + synthetic Ctrl+V
- **Windows UI Automation (UIA)**: Inspect accessibility trees with pre-calculated click coordinates to eliminate visual guessing
- **Batch Action Pipelines**: Execute multi-step sequences (`focus` → `click` → `paste` → `hotkey` → `wait` → `screenshot`) atomically in one native call
- **Persistent Daemon**: Sub-2ms command dispatch latency via background stdio JSON line communication

## Architecture

```
User Request
    ↓
MCP Server (Node.js)
    ↓ stdio JSON-RPC
Native Engine (C# .NET 4.8)
    ↓
Windows APIs (GDI, SendInput, UIAutomation)
```

## Installation

### Local Installation (recommended for development)

Clone the repository and place it in your Claude Code local plugins directory:

```bash
cd %USERPROFILE%\.claude\local-plugins
git clone https://github.com/bloupyi/Fast-computer-use.git
```

Then restart Claude Code. The plugin will be automatically discovered and loaded.

### Via Claude CLI

If the plugin is published to the marketplace, install it with:

```bash
claude plugin install fast-computer-use
```

Or enable it in your `.mcp.json` configuration file at `~/.claude/.mcp.json`:

```json
{
  "mcpServers": {
    "fast-computer-use": {
      "type": "stdio",
      "command": "node",
      "args": ["path/to/server/index.mjs"],
      "env": {
        "ENGINE_PATH": "path/to/bin/FastEngine.exe"
      }
    }
  }
}
```

### Requirements

- **Windows 10/11** (native Win32 API dependency)
- **.NET Framework 4.8** (for the C# daemon)
- **Node.js 18+** (for the MCP server)

## Available MCP Tools

### `screen_capture`
Capture high-speed screenshots with optional downscaling, ROI cropping, and multi-monitor support.

**Key parameters:**
- `screen_index`: Monitor index (0 = primary, -1 = virtual desktop)
- `target_active_window`: Capture only the active window
- `roi`: Region of interest crop `{x, y, width, height}`
- `max_dimension`: Downscale to reduce token usage (e.g., 1024, 1280)
- `format`: `"jpeg"` (default) or `"png"`
- `quality`: JPEG quality 1-100 (default: 80)

### `ui_inspect`
Inspect Windows UI Automation tree to locate interactive controls with click coordinates.

**Key parameters:**
- `target`: `"active_window"` (default), `"cursor"`, or `"screen"`
- `max_depth`: Hierarchy depth (default: 7)
- `filter`: Search by control name, type, or automation ID
- `interactive_only`: Filter clickable/focusable elements (default: true)

### `mouse_action`
Execute native Win32 mouse input.

**Actions:** `click`, `double_click`, `triple_click`, `right_click`, `middle_click`, `move`, `drag`, `scroll`

**Key parameters:**
- `x`, `y`: Screen coordinates
- `button`: `"left"`, `"right"`, `"middle"`
- `scroll_amount`: Scroll delta (positive = up, negative = down)
- `to_x`, `to_y`: Drag destination
- `smooth`: Interpolate smooth movement

### `keyboard_action`
Execute native Win32 keyboard input.

**Actions:** `type_text`, `paste_text`, `hotkey`, `press_key`, `key_down`, `key_up`

**Key parameters:**
- `text`: Text to type or paste
- `hotkey`: Combination like `"ctrl+c"`, `"ctrl+v"`, `"win+r"`, `"alt+tab"`
- `key`: Single key name
- `delay_ms`: Inter-keystroke delay for `type_text` (default: 5ms)

### `batch_actions`
Execute multiple actions atomically without round-trip latency. Each item can
name its action with either `cmd` or `type` — both are recognized.

**Example:**
```json
{
  "actions": [
    {"cmd": "window_focus", "target": "notepad"},
    {"cmd": "mouse", "action": "click", "x": 100, "y": 200},
    {"cmd": "keyboard", "action": "paste_text", "text": "const x = 42;"},
    {"cmd": "wait", "ms": 200},
    {"cmd": "keyboard", "action": "hotkey", "hotkey": "ctrl+s"},
    {"cmd": "screenshot", "maxDimension": 1280}
  ]
}
```

### `window_manager`
Manage Windows applications.

**Actions:** `list`, `get_active`, `focus`, `minimize`, `maximize`, `restore`, `close`, `set_pos`

**Key parameters:**
- `target`: Window title substring, process name, or HWND
- `x`, `y`, `width`, `height`: Position/size for `set_pos`

**Note:** `restore` only un-minimizes a window (`SW_RESTORE`) — by design it
does not steal keyboard focus back (unlike `focus`, which does). Don't assume
a window has focus just because you restored it; call `focus` explicitly
first if a subsequent `mouse_action`/`keyboard_action` needs to land on it.

### `system_info`
Get display configurations, cursor coordinates, and active window info.

## Native Engine

The FastEngine C# daemon (`bin/FastEngine.exe`) is compiled from `bin/FastEngine.cs` using:

```powershell
.\bin\build.ps1
```

**Dependencies:**
- .NET Framework 4.8
- WindowsBase.dll
- UIAutomationClient.dll
- UIAutomationTypes.dll
- System.Drawing.dll
- System.Windows.Forms.dll
- System.Web.Extensions.dll

## Testing

`server/smoke-test.mjs` spawns the real MCP server (`server/index.mjs`, which
spawns the real `bin/FastEngine.exe` daemon) and speaks actual JSON-RPC over
stdio — the same protocol Claude Code uses — to exercise all 7 tools with safe
arguments. It opens its own disposable, verified-blank Notepad tab for the
interactive (mouse/keyboard) checks and closes it again afterward, so it never
touches whatever window happens to be focused when you run it.

```powershell
node server/smoke-test.mjs
```

Exit code 0 means every check passed; a non-zero exit prints which failed.
Rebuild `bin/FastEngine.exe` first (`.\bin\build.ps1`) if you've changed
`bin/FastEngine.cs`.

## Performance Benchmarks

- **Mouse move**: ~0ms
- **Screenshot (downscaled 1280px)**: ~72ms
- **UI tree inspection**: <100ms
- **Clipboard paste (large code)**: <10ms
- **Daemon command dispatch**: <2ms

## License

MIT
