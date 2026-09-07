---
name: fast-computer-use
description: Operating doctrine for driving Windows through the fast-computer-use MCP tools. Load it whenever a task means controlling the desktop - opening an app, clicking, typing, filling a form, moving files through a GUI, reading what is on screen - so the work runs as verified batches instead of one slow tool call per action.
---

# Driving Windows with fast-computer-use

The native engine dispatches a command in about 2 ms. If desktop automation feels
slow, it is never the engine: it is the number of model round trips. Opening
Notepad and typing "hello world" is roughly 200 ms of real work. Issued as six
separate tool calls with a screenshot between each, it becomes minutes.

Everything below exists to keep the work in the engine and out of the round trip.

## The rule

**Plan the whole sequence first, then send it as one `batch_actions` call.**

Do not issue `mouse_action` / `keyboard_action` one at a time to walk through a
task. Those single-shot tools are for a genuine one-off. A task is a batch.

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

One call. Launched, focused, typed, and *proved* - no screenshot, no guessing.

## Check, do not look

Every step can prove itself in text, for no image tokens:

- `read_text` with `expect` reads a control's real content back and asserts it.
- `wait_for` blocks until an element or window appears (or disappears). Use it
  instead of `wait` with a guessed duration, and instead of screenshotting in a
  loop to see whether something finished.
- `verify` on any step: a UI filter string, `true` to report the foreground
  window, or `{"mode":"text","filter":"...","expect":"..."}`.
- `ui_inspect` lists controls with exact click coordinates and automation ids.

A batch stops at the first failed step and tells you which one, so a failed focus
never sends the following keystrokes into someone else's window. Pass
`continue_on_error: true` only when a step is genuinely optional.

**Take a screenshot only when you actually need to see pixels** - an image, a
canvas, a layout question. To find out whether a button exists, where it is, or
what a field contains, `ui_inspect` and `read_text` answer in text and cost a
fraction of the tokens.

## Dragging

Two correct shapes, and one that looks right but is not:

```json
{"cmd": "mouse", "action": "drag", "x": 100, "y": 200, "to_x": 400, "to_y": 300}
```

That is the whole gesture in one step. Use it unless the drag has to pass through
specific waypoints.

When you do need to steer the path, bracket your own moves:

```json
{"cmd": "mouse", "action": "mouse_down"},
{"cmd": "mouse", "action": "move", "x": 250, "y": 250, "smooth": true},
{"cmd": "mouse", "action": "move", "x": 400, "y": 300, "smooth": true},
{"cmd": "mouse", "action": "mouse_up"}
```

The button stays held across every step in between. What does **not** work is
moving first and clicking at the end: that travels with the button up and then
clicks, which is not a drag. If a target does not register the grab or the drop
lands short, raise `grab_ms` / `drop_ms` rather than adding blind `wait` steps.

## Prefer identity over coordinates

`{"cmd":"ui_click","automation_id":"104"}` and `{"cmd":"ui_click","name":"Save"}`
survive a moved or resized window. Raw x/y does not. Reach for coordinates only
when the control is not in the accessibility tree.

`launch` beats the win+r dance: it starts the app, waits for *its* window, focuses
it, and reports the real reason as text if the app fails to start.

## Multi-monitor

Call `system_info` once to learn the monitor layout. To see everything at once,
use `screen_capture` with `panorama: true`: it stitches every monitor into one
labelled image and gives each its own resolution budget, so text stays readable
instead of being squashed into a strip. It returns a per-screen map - convert a
panorama point back to desktop coordinates with it before clicking:

```
desktop_x = screen.bounds.x + (pano_x - screen.dest.x) / screen.scale
```

## Keep quiet while working

Narration between steps costs the user the same wall-clock time as the work does.
State the plan once, run the batch, report what happened. No commentary per step,
no "now I will click the button", no screenshot posted just to show progress.

## When a task is long or branching

If the task needs many batches with decisions in between - waiting on a UI that
changes, reacting to dialogs, driving an app you have to explore first - run it
through `/fast-do`, which puts the action loop on a fast model and keeps the heavy
model for the parts that need real understanding.
