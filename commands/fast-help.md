---
description: Display Fast Computer Use plugin help and command reference
---

# Fast Computer Use Plugin

Native Windows automation for Claude Code: hardware-level input, sub-40ms screen
capture, UI Automation inspection, and self-verifying action pipelines.

## The one thing to know

The engine dispatches a command in about 2 ms. Slowness never comes from the
machine - it comes from issuing one tool call per action with a screenshot in
between. **Plan the sequence, send it as one `batch_actions` call, and let each
step prove itself in text.**

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

One call, about 190 ms, verified - no screenshot. The same task issued step by
step takes minutes.

## Commands

### `/fast-do` - drive a whole task

```
/fast-do <what you want done on the desktop>
```

Runs a multi-step task through a dedicated two-model workflow: the repetitive
action loop goes to a fast model, while reading source, understanding an
unfamiliar app and recovering from surprises stay on the heavy model. Use it for
anything that needs more than one batch.

### `/fast-screen` - screenshot

```
/fast-screen [--panorama] [--screen N] [--max-dim N] [--active] [--roi x,y,w,h] [--format jpeg|png] [--quality N] [--save PATH]
```

- `/fast-screen --panorama` - every monitor stitched into one labelled image, each
  with its own resolution budget, plus the map to convert a panorama point back to
  desktop coordinates. This is the multi-monitor option.
- `/fast-screen --active --max-dim 1280` - active window only
- `/fast-screen --roi 0,0,800,600` - region of interest

### `/fast-inspect` - find controls without looking

```
/fast-inspect [--target active_window|cursor|screen] [--depth N] [--filter TEXT] [--all]
```

Returns control names, automation ids and exact click coordinates. Prefer this
over a screenshot whenever the question is "is it there / where is it".

### `/fast-click` - one native click

```
/fast-click <x> <y> [--double] [--triple] [--right] [--middle] [--smooth]
```

For a sequence, use `batch_actions` instead - and prefer `ui_click` with an
`automation_id` or `name` over raw coordinates, since those survive a moved window.

### `/fast-type` - keyboard input

```
/fast-type <text> [--paste] [--hotkey COMBO] [--key KEY] [--delay N]
```

`--paste` injects through the clipboard in under 10 ms; use it for anything long.

## MCP tools

| tool | use it for |
|---|---|
| `batch_actions` | **the default.** A whole sequence in one call, halting at the first failure |
| `app_launch` | start an app and wait for its real window, with the true failure reason on error |
| `wait_for` | block until an element or window appears / disappears |
| `read_text` | read a control's real content and assert it, instead of screenshotting |
| `ui_inspect` | controls with automation ids and click coordinates |
| `screen_capture` | pixels: one monitor, active window, ROI, or `panorama: true` |
| `mouse_action` | a genuine one-off click, move, drag or scroll |
| `keyboard_action` | a genuine one-off type, paste, hotkey or key press |
| `window_manager` | list, focus (verified), minimize, maximize, restore, close, move |
| `system_info` | monitor layout, cursor, active window |

## Verifying instead of looking

Attach `verify` to any batch step:

| form | meaning |
|---|---|
| `"Save"` | a UI element matching this must be present afterwards |
| `true` | report the foreground window after the step |
| `{"mode":"text","filter":"104","expect":"hi"}` | read a control back and assert its content |

A batch halts at the first failed step, so a failed focus never sends the
following keystrokes into the wrong window. `continue_on_error: true` opts out.

Screenshots are for pixels - an image, a canvas, a layout question. For facts,
`ui_inspect` and `read_text` answer in text at a fraction of the token cost.

## Multi-monitor

Call `system_info` once for the layout. Then `screen_capture` with
`panorama: true`. To click something spotted in a panorama, convert first:

```
desktop_x = screen.bounds.x + (pano_x - screen.dest.x) / screen.scale
```

## Troubleshooting

- **An app will not start**: `app_launch` returns the actual reason, read out of
  the Windows error dialog. A missing DLL there is a broken installation on the
  host, not a plugin problem.
- **Input lands in the wrong window**: never bypass the default halt-on-failure.
  Start batches with `window_focus`, which confirms the window really reached the
  foreground.
- **A batch step reports `Unknown command`**: the `cmd` is not one the engine
  knows. Check the spelling against the table above.
- **Rebuild after editing the engine**: `.\bin\build.ps1`, then
  `node server/smoke-test.mjs` (exit code 0 = everything passed).
