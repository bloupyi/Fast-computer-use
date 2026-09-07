# Fast Computer Use Plugin

High-performance native Windows automation for Claude Code. A C# engine and MCP
server that replace slow, token-heavy ad-hoc PowerShell with hardware-level input,
sub-40ms screen capture, and self-verifying action pipelines.

## The point

The native engine dispatches a command in about 2 ms. When desktop automation
feels slow, the engine is never the bottleneck - the model round trips are. A task
issued as six tool calls with a screenshot between each takes minutes; the same
task as one verified batch takes a fraction of a second.

Measured on this repo's smoke test: launch an app, focus its text field, type into
it, and read the text back with an assertion - **one `batch_actions` call, ~190 ms
end to end**, no screenshot involved.

So the plugin is built around one rule: **plan the sequence, send it as one batch,
and let each step prove itself in text.**

## Features

- **Batch action pipelines**: `launch` -> `wait_for` -> `ui_click` -> `type` ->
  `read_text` runs atomically in one native call. Steps are numbered, timed, and
  halt at the first failure so a missed focus never sends keystrokes into the
  wrong window.
- **Text verification instead of screenshots**: `read_text` reads a control's real
  content out of UI Automation and asserts it; `wait_for` blocks until an element
  or window appears; `verify` attaches a check to any batch step. All text, no
  image tokens.
- **Verified app launch**: `app_launch` starts an app, waits for *its* window,
  focuses it, and reports the actual failure reason (read out of the error dialog)
  when the app cannot start.
- **Panoramic multi-monitor capture**: stitches every monitor into one labelled
  image with a per-monitor resolution budget, plus the coordinate map to convert a
  panorama point back to the desktop.
- **Sub-40ms screen capture**: GDI-based, with downscaling and ROI cropping.
- **Win32 input virtualization**: hardware-accurate clicks, smooth trajectories,
  drag-and-drop, Unicode typing, complex hotkeys.
- **Reliable foreground focus**: beats the Win32 foreground lock and reads back
  `GetForegroundWindow` to confirm, rather than assuming.
- **UI Automation inspection**: control names, automation ids and pre-calculated
  click coordinates, so clicking never needs visual guessing.
- **Persistent daemon**: sub-2ms command dispatch over stdio JSON lines.

## Architecture

```
User Request
    ↓
Skill / /fast-do   (doctrine: batch first, verify in text)
    ↓
MCP Server (Node.js)
    ↓ stdio JSON-RPC
Native Engine (C# .NET 4.8)
    ↓
Windows APIs (GDI, SendInput, UIAutomation)
```

## Installation

### Local installation (recommended for development)

```bash
cd %USERPROFILE%\.claude\local-plugins
git clone https://github.com/bloupyi/Fast-computer-use.git
```

Restart Claude Code; the plugin is discovered automatically.

### Via Claude CLI

```bash
claude plugin install fast-computer-use
```

Or wire it up manually in `~/.claude/.mcp.json`:

```json
{
  "mcpServers": {
    "fast-computer-use": {
      "type": "stdio",
      "command": "node",
      "args": ["path/to/server/index.mjs"],
      "env": { "ENGINE_PATH": "path/to/bin/FastEngine.exe" }
    }
  }
}
```

### Requirements

- **Windows 10/11** (native Win32 dependency)
- **.NET Framework 4.8** (for the C# daemon)
- **Node.js 18+** (for the MCP server)

## MCP Tools

### `batch_actions` - the entry point

Runs a whole sequence in one native call. Each step names its command with `cmd`
(a step carrying only `action` also works).

```json
{
  "actions": [
    {"cmd": "launch", "app": "notepad", "timeout_ms": 8000},
    {"cmd": "ui_click", "control_type": "Document"},
    {"cmd": "keyboard", "action": "type_text", "text": "hello world"},
    {"cmd": "read_text", "control_type": "Document", "expect": "hello world"}
  ]
}
```

Step types: `launch`, `window_focus`, `mouse`, `ui_click`, `keyboard`, `wait_for`,
`read_text`, `screenshot`, `wait`, plus every engine command.

The batch **stops at the first failing step** and reports which one. Pass
`continue_on_error: true` to run past failures.

Any step accepts `verify`:

| form | meaning |
|---|---|
| `"Save"` | a UI element matching this must be present afterwards |
| `true` | report the foreground window after the step |
| `{"mode":"text","filter":"104","expect":"hi"}` | read a control back and assert its content |

### `app_launch`

Start an application, document or URI and wait until its window is really up and
focused. Replaces open-Run / type / enter / sleep / screenshot with one verified
call. Reports the real failure reason as text when the app cannot start.

### `wait_for`

Block until a UI element (`filter`) or a window (`window`) appears - or, with
`present: false`, until it goes away. Use it instead of guessing a sleep duration
or screenshotting in a loop.

### `read_text`

Read a control's actual text out of UI Automation (`ValuePattern`, then
`TextPattern`). With `expect`, it asserts the content and errors when it does not
match. This is how you confirm typing or pasting worked without a screenshot.

### `screen_capture`

Monitor, active window, ROI - or **panorama**:

```json
{"panorama": true, "max_dimension": 1280}
```

`max_dimension` applies **per monitor** in panorama mode, so a dual 1920px setup
comes back as 2568x720 of legible pixels instead of one 1280x360 strip where
nothing is readable. The result carries each screen's `bounds`, `dest` rect and
`scale`, plus the conversion:

```
desktop_x = screen.bounds.x + (pano_x - screen.dest.x) / screen.scale
```

Other parameters: `screen_index` (`-1` = raw virtual desktop), `screens` (restrict
the panorama), `target_active_window`, `roi`, `format`, `quality`, `save_path`,
`return_image` (set `false` for metadata only, at no visual token cost).

### `ui_inspect`

Interactive controls of a window with names, automation ids and exact click
coordinates. Parameters: `target` (`active_window` / `cursor` / `screen`),
`max_depth`, `filter`, `interactive_only`.

### `mouse_action`

`click`, `double_click`, `triple_click`, `right_click`, `middle_click`, `move`,
`drag` (`to_x`/`to_y`), `scroll` (`scroll_amount`), `mouse_down`, `mouse_up`.
`smooth: true` interpolates the trajectory; `duration_ms` tunes it (40 for a move,
200 for a drag).

Movement goes through `SendInput` as absolute virtual-desktop coordinates, not
`SetCursorPos`, so an app receiving a drag sees a real gesture. Two ways to drag:
`action: "drag"` for the whole thing in one step, or `mouse_down` / moves /
`mouse_up` when you need to steer the path - the button genuinely stays held
between them. Tune `grab_ms` and `drop_ms` if a target misses the grab or the drop
lands short.

### `keyboard_action`

`type_text`, `paste_text` (clipboard injection, instant - use it for anything
long), `hotkey` (`"ctrl+s"`, `"win+r"`, `"alt+tab"`), `press_key`, `key_down`,
`key_up`.

### `window_manager`

`list`, `get_active`, `focus`, `minimize`, `maximize`, `restore`, `close`,
`set_pos`. `focus` confirms the window actually reached the foreground and reports
it; `restore` only un-minimizes (`SW_RESTORE`) and deliberately does not steal
focus, so call `focus` explicitly if input must land there next.

### `system_info`

Monitor layout, virtual desktop bounds, cursor position, active window. Worth one
call before working on a multi-monitor machine.

## Skill and command

- **`fast-computer-use` skill** - the operating doctrine: plan the sequence, send
  one batch, verify in text, keep narration out of the loop. Loads automatically
  when a task means driving the desktop.
- **`/fast-do <task>`** - runs a multi-step task through a dedicated two-model
  workflow: the repetitive action loop goes to a fast model, while reading source,
  understanding an unfamiliar app and recovering from surprises stay on the heavy
  model.

Single-purpose commands remain available: `/fast-screen`, `/fast-inspect`,
`/fast-click`, `/fast-type`, `/fast-help`.

## Native engine

`bin/FastEngine.exe` is compiled from `bin/FastEngine.cs` with:

```powershell
.\bin\build.ps1
```

**Dependencies:** .NET Framework 4.8, WindowsBase.dll, UIAutomationClient.dll,
UIAutomationTypes.dll, System.Drawing.dll, System.Windows.Forms.dll,
System.Web.Extensions.dll.

The daemon forces UTF-8 on stdout (window titles on a non-English Windows would
otherwise come back mangled and be unusable as lookup keys) and raises the system
timer resolution to 1 ms for the duration of the process, because Windows' default
~15.6 ms granularity turns every `Sleep(5)` in an input sequence into 15.6 ms of
real waiting.

## Testing

`server/smoke-test.mjs` spawns the real MCP server (which spawns the real
`FastEngine.exe` daemon) and speaks actual JSON-RPC over stdio - the same protocol
Claude Code uses - to exercise every tool with safe arguments.

Interactive checks only ever touch a disposable scratch window the test opens
itself and verifies is focused, and it closes that window by handle afterwards. It
prefers Notepad; if the host's Notepad cannot start, it says so as an ENVIRONMENT
note and continues against Character Map instead of failing.

```powershell
node server/smoke-test.mjs
```

Exit code 0 means every check passed. Rebuild `bin/FastEngine.exe` first
(`.\bin\build.ps1`) if you changed `bin/FastEngine.cs`.

## Measured performance

Taken from the smoke test and the engine's own per-step timings on a dual
1920x1080 setup:

| operation | time |
|---|---|
| Daemon command dispatch | < 2 ms |
| Mouse move | ~0 ms |
| 3 smooth moves, incl. cross-screen | 92 ms |
| Type "hello world" | 42 ms |
| Screenshot, ROI 150x90 | 3-5 ms |
| Screenshot, downscaled 1280px | ~70 ms |
| Panorama, 2 monitors at 1280px each | 432 ms |
| UI tree inspection | < 100 ms |
| `read_text` verification | ~50 ms |
| Clipboard paste (large block) | < 10 ms |
| App launch, window up and focused | ~690 ms |
| **Full scenario: launch, click, type, verify** | **~190 ms** (app already open) |

## License

MIT
