---
description: Run a multi-step desktop task through a dedicated two-model workflow (fast model drives the actions, heavy model handles understanding)
argument-hint: <what you want done on the desktop>
---

Drive the desktop to accomplish: **$ARGUMENTS**

This command exists because desktop automation is bottlenecked by model round
trips, not by the machine. It splits the work so that the part which repeats
(deciding and firing the next batch of actions) runs on a fast model, and the
part which needs judgement (reading source, understanding an unfamiliar app,
recovering from an unexpected state) runs on the heavy model.

## 1. Survey once, on this model

Before delegating anything, establish the ground truth yourself:

- `system_info` for the monitor layout.
- `window_manager list` to see what is already open.
- `ui_inspect` on the relevant window, or `screen_capture` with `panorama: true`
  if you genuinely do not know what is on screen.

Then write the plan as an explicit list of **batches** - not individual actions.
Each batch should end in a verifiable state (`read_text` with `expect`, or
`wait_for`), so the next step knows whether it can proceed.

If the task requires understanding code, a config file, or a document, read that
now, on this model. Do not delegate comprehension to the fast model.

## 2. Delegate the action loop to a fast model

For each batch, or for a run of batches that needs no judgement between them,
spawn a subagent with `model: "haiku"` and give it:

- the exact `batch_actions` payload to send, or a precise enough description that
  it can only build one payload;
- the verification that must hold afterwards;
- an instruction to report back only: which step failed (if any), and the text
  that `read_text` / `verify` returned.

The fast model executes and checks. It does not explore, redesign the plan, or
decide what the task means.

If the task is large enough to warrant real orchestration - many independent
batches, or a fan-out across several windows or files - use the `Workflow` tool
instead of individual subagents. Load the `workflow-authoring` skill first, then
write a script whose action agents run on the fast model and whose analysis
agents run on the heavy model. Do not guess the workflow script API; read the
skill.

## 3. Escalate on surprise, do not thrash

When a batch comes back failed, do not retry it verbatim on the fast model. Pull
the state back to this model:

- `ui_inspect` the current window to see what is actually there;
- `read_text` to see what a field really contains;
- take a screenshot only if the answer is genuinely visual.

Diagnose here, produce a corrected batch, and hand that back to the fast model.
Two identical failures means the plan is wrong, not the execution.

## 4. Report

One report at the end: what was accomplished, verified how, and anything that had
to be worked around. No step-by-step narration while the work is running - it
costs the user the same wall-clock time as the automation itself.

## Rules that always hold

- One `batch_actions` call per batch. Never walk a task with single actions.
- Every batch ends verified, in text. Screenshots are for pixels, not for facts.
- Target controls by `automation_id` or `name`, not raw coordinates, whenever the
  accessibility tree exposes them.
- Never send input to a window you did not verify is focused. A failed focus step
  halts the batch by default - leave that default alone.
