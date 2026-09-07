// Batch items are written by hand in tool arguments, so a step like
// {cmd:"screenshot", max_dimension:400} would otherwise silently capture full size.
const FIELD_ALIASES = {
  max_dimension: 'maxDimension',
  screen_index: 'screenIndex',
  monitor_index: 'monitorIndex',
  target_active_window: 'targetActiveWindow',
  return_image: 'returnBase64',
  return_base64: 'returnBase64',
  save_path: 'savePath',
  scroll_amount: 'scrollAmount',
  to_x: 'toX',
  to_y: 'toY',
  delay_ms: 'delayMs',
  max_depth: 'maxDepth',
  interactive_only: 'interactiveOnly',
  max_chars: 'maxChars'
};

function normalizeItem(item) {
  if (!item || typeof item !== 'object' || Array.isArray(item)) return item;
  const out = {};
  for (const [key, value] of Object.entries(item)) {
    const mapped = FIELD_ALIASES[key] || key;
    out[mapped] = value;
  }
  return out;
}

// A batch step is one line of text, not a JSON dump: a pipeline of ten steps has to
// stay readable and cheap to read back.
function describeStep(step) {
  const idx = step.step !== undefined ? `${step.step}.` : '-';
  const state = step.status === 'ok' ? 'ok  ' : 'FAIL';
  const what = step.action || step.cmd || 'step';
  const ms = step.elapsedMs !== undefined ? `${step.elapsedMs}ms` : '';

  const bits = [];
  if (step.message) bits.push(step.message);
  if (step.title) bits.push(`window="${step.title}"`);
  if (step.text !== undefined) {
    const preview = String(step.text).replace(/\r?\n/g, '\\n').slice(0, 120);
    bits.push(`text="${preview}"`);
    if (step.matched !== undefined) bits.push(`matched=${step.matched}`);
  }
  if (step.x !== undefined && step.y !== undefined && step.text === undefined) bits.push(`at (${step.x},${step.y})`);
  if (step.count !== undefined && step.text === undefined) bits.push(`count=${step.count}`);
  if (step.waitedMs !== undefined) bits.push(`waited=${step.waitedMs}ms`);
  if (step.verify) {
    const v = step.verify;
    const vd = v.matched !== undefined ? `matched=${v.matched}` : (v.foreground !== undefined ? `foreground="${v.foreground}"` : v.status);
    bits.push(`verify(${v.mode || '?'}): ${vd}`);
  }
  if (step.path && !step.base64) bits.push(step.path);

  return `  ${idx} ${state} ${what} ${ms}${bits.length ? '  | ' + bits.join(' | ') : ''}`;
}

function panoramaSummary(res) {
  const lines = [`Panorama: ${res.width}x${res.height} across ${(res.screens || []).length} monitor(s) in ${res.elapsedMs}ms${res.path ? ` | ${res.path}` : ''}`];
  for (const s of res.screens || []) {
    lines.push(`  screen ${s.index}${s.primary ? ' (primary)' : ''}: desktop ${s.bounds.width}x${s.bounds.height} at (${s.bounds.x},${s.bounds.y}) -> panorama rect (${s.dest.x},${s.dest.y}) ${s.dest.width}x${s.dest.height}, scale ${s.scale}`);
  }
  lines.push(`  To click something you spotted in the panorama, convert first: ${res.mapping}`);
  return lines.join('\n');
}

export const TOOL_DEFINITIONS = [
  {
    name: 'batch_actions',
    description:
      'PREFERRED ENTRY POINT. Run a whole sequence of automation steps in ONE native call: launch an app, wait for its window, click, type, paste, hotkey, screenshot, and verify the result. Every other tool here does one step and costs a full model round trip, so a task that takes seconds as a batch takes minutes issued step by step. Plan the entire sequence up front and send it as one batch. Steps run in order and STOP at the first failure (set continue_on_error to run past it). Attach "verify" to a step to confirm it actually landed - verification is text only and costs no image tokens.',
    inputSchema: {
      type: 'object',
      properties: {
        actions: {
          type: 'array',
          description:
            'Ordered steps. Each step names its command with "cmd". Supported steps:\n' +
            '  {"cmd":"launch","app":"notepad","timeout_ms":8000} - start an app (or a document/URI) and wait for its real window; reports an error if the app fails to start\n' +
            '  {"cmd":"window_focus","target":"notepad"} - bring a window to the foreground and confirm it got there\n' +
            '  {"cmd":"mouse","action":"click","x":100,"y":200} - also double_click, right_click, middle_click, move, drag (to_x/to_y), scroll (scroll_amount)\n' +
            '  {"cmd":"ui_click","automation_id":"104"} - click an element by automation id or {"name":"Save"}, no coordinates needed\n' +
            '  {"cmd":"keyboard","action":"type_text","text":"hello"} - also paste_text (instant, use it for anything long), hotkey ("ctrl+s"), press_key\n' +
            '  {"cmd":"wait_for","filter":"Save","timeout_ms":5000} - wait until a UI element appears; or {"window":"Notepad"} for a window. Always prefer this over a blind wait\n' +
            '  {"cmd":"read_text","filter":"104","expect":"hello"} - read a control\'s real content back and assert what it contains\n' +
            '  {"cmd":"wait","ms":200} - fixed pause, last resort\n' +
            '  {"cmd":"screenshot","max_dimension":800} - only when you genuinely need to look\n' +
            'Any step also accepts "verify": a UI filter string to look for after it runs, true to report the foreground window, or {"mode":"text","filter":"104","expect":"hello"} to read a value back.',
          items: { type: 'object' }
        },
        continue_on_error: {
          type: 'boolean',
          description: 'Keep running after a step fails (default false: a failed focus or click would otherwise send the following keystrokes into the wrong window).'
        }
      },
      required: ['actions']
    }
  },
  {
    name: 'screen_capture',
    description:
      'Capture a screenshot: one monitor, the active window, a region, or a PANORAMA stitching every monitor into one image (set panorama:true - built for multi-screen setups, since it gives each monitor its own resolution budget instead of squashing the whole desktop into one strip). Prefer ui_inspect or read_text when you need facts rather than pixels: they answer in text, for a fraction of the tokens.',
    inputSchema: {
      type: 'object',
      properties: {
        panorama: {
          type: 'boolean',
          description: 'Stitch every monitor side by side into a single labelled image, with a per-screen coordinate map. Use this on multi-monitor setups instead of screen_index:-1.'
        },
        screens: {
          type: 'array',
          items: { type: 'integer' },
          description: 'Panorama only: restrict to these monitor indices (default: all).'
        },
        screen_index: {
          type: 'integer',
          description: 'Monitor index (0 = primary). -1 grabs the raw virtual desktop bounding box; panorama:true is usually the better multi-screen option.'
        },
        target_active_window: { type: 'boolean', description: 'Capture and crop to the active foreground window.' },
        roi: {
          type: 'object',
          description: 'Region of interest in screen coordinates.',
          properties: { x: { type: 'integer' }, y: { type: 'integer' }, width: { type: 'integer' }, height: { type: 'integer' } },
          required: ['x', 'y', 'width', 'height']
        },
        max_dimension: {
          type: 'integer',
          description: 'Max width/height in pixels (default 1280). In panorama mode this budget applies PER MONITOR, so text stays legible.'
        },
        format: { type: 'string', enum: ['jpeg', 'png'], description: 'Image format (default jpeg).' },
        quality: { type: 'integer', description: 'JPEG quality 1-100 (default 80).' },
        save_path: { type: 'string', description: 'Optional path to save the image.' },
        return_image: { type: 'boolean', description: 'Return the image itself (default true). Set false to only get the metadata and path, at no visual token cost.' }
      }
    }
  },
  {
    name: 'app_launch',
    description:
      'Start an application, document or URI and wait until its window is really up and focused. Replaces the win+r dance (open Run, type a name, press enter, sleep, screenshot to check) with one verified call. If the app fails to start, the actual reason is read out of the error dialog and returned as text.',
    inputSchema: {
      type: 'object',
      properties: {
        app: { type: 'string', description: 'Executable name ("notepad"), full path, document, or URI ("https://...").' },
        args: { type: 'string', description: 'Command-line arguments.' },
        expect_window: { type: 'string', description: 'Window title or process to wait for, if it differs from the executable name.' },
        reuse_existing: { type: 'boolean', description: 'Focus an already-open window instead of starting another instance (default true).' },
        focus: { type: 'boolean', description: 'Bring the window to the foreground once it appears (default true).' },
        timeout_ms: { type: 'integer', description: 'How long to wait for the window (default 10000).' }
      },
      required: ['app']
    }
  },
  {
    name: 'wait_for',
    description:
      'Block until a UI element or a window appears (or disappears), then continue. This is how you synchronise with an app instead of guessing a sleep duration or screenshotting in a loop: it polls natively and returns as soon as the condition holds.',
    inputSchema: {
      type: 'object',
      properties: {
        filter: { type: 'string', description: 'UI element to wait for, matched on name, control type or automation id, inside the active window.' },
        window: { type: 'string', description: 'Window title substring or process name to wait for. Use instead of filter.' },
        present: { type: 'boolean', description: 'true (default) waits for it to appear; false waits for it to go away (a progress dialog closing, for instance).' },
        foreground: { type: 'boolean', description: 'Window mode: also require it to be the foreground window.' },
        timeout_ms: { type: 'integer', description: 'Give up after this long (default 5000).' },
        poll_ms: { type: 'integer', description: 'Polling interval (default 80).' }
      }
    }
  },
  {
    name: 'read_text',
    description:
      'Read the actual text content of a control (text box, document, label) straight out of UI Automation. This is how you confirm typing or pasting worked, or read what an app is showing, without a screenshot: it answers in text and can assert the content with "expect".',
    inputSchema: {
      type: 'object',
      properties: {
        filter: { type: 'string', description: 'Which element to read, by name, automation id or control type. Omit to read the focused element.' },
        target: { type: 'string', enum: ['focused', 'active_window'], description: 'Where to look (default focused).' },
        expect: { type: 'string', description: 'Assert the text contains this. The call reports an error when it does not.' },
        max_chars: { type: 'integer', description: 'Truncate the returned text (default 20000).' }
      }
    }
  },
  {
    name: 'ui_inspect',
    description:
      'List the interactive controls of a window from the UI Automation tree, each with its exact click coordinates and automation id. Use this instead of screenshotting and guessing where to click: it is text, it is precise, and the coordinates it returns are ready to click.',
    inputSchema: {
      type: 'object',
      properties: {
        target: { type: 'string', enum: ['active_window', 'cursor', 'screen'], description: 'Scope to inspect (default active_window).' },
        max_depth: { type: 'integer', description: 'Maximum hierarchy depth (default 7).' },
        filter: { type: 'string', description: 'Match on control name, type or automation id.' },
        interactive_only: { type: 'boolean', description: 'Only clickable/focusable elements (default true).' }
      }
    }
  },
  {
    name: 'mouse_action',
    description: 'One native mouse action. For a sequence of steps use batch_actions instead: one call, no round trip per click.',
    inputSchema: {
      type: 'object',
      properties: {
        action: {
          type: 'string',
          enum: ['click', 'double_click', 'triple_click', 'right_click', 'middle_click', 'move', 'mouse_down', 'mouse_up', 'drag', 'scroll'],
          description: 'Mouse action to execute.'
        },
        x: { type: 'integer', description: 'Target X screen coordinate.' },
        y: { type: 'integer', description: 'Target Y screen coordinate.' },
        button: { type: 'string', enum: ['left', 'right', 'middle'], description: 'Mouse button (default left).' },
        scroll_amount: { type: 'integer', description: 'Scroll delta (positive = up, negative = down).' },
        to_x: { type: 'integer', description: 'Destination X for drag.' },
        to_y: { type: 'integer', description: 'Destination Y for drag.' },
        smooth: { type: 'boolean', description: 'Interpolate the movement instead of teleporting the cursor.' },
        duration_ms: { type: 'integer', description: 'Total travel time for a smooth move (default 40).' }
      },
      required: ['action']
    }
  },
  {
    name: 'keyboard_action',
    description: 'One native keyboard action: type, paste from clipboard (instant, use it for anything long), hotkey, or a single key. For a sequence use batch_actions.',
    inputSchema: {
      type: 'object',
      properties: {
        action: {
          type: 'string',
          enum: ['type_text', 'paste_text', 'hotkey', 'press_key', 'key_down', 'key_up'],
          description: 'Keyboard action to execute.'
        },
        text: { type: 'string', description: 'Text for type_text or paste_text.' },
        hotkey: { type: 'string', description: 'Combination such as "ctrl+c", "ctrl+v", "win+r", "alt+tab", "ctrl+shift+esc".' },
        key: { type: 'string', description: 'Single key name for press_key, key_down or key_up.' },
        delay_ms: { type: 'integer', description: 'Inter-keystroke delay for type_text (default 2).' }
      },
      required: ['action']
    }
  },
  {
    name: 'window_manager',
    description: 'List, focus, minimize, maximize, restore, move/resize or close windows. focus verifies the window actually reached the foreground and reports it.',
    inputSchema: {
      type: 'object',
      properties: {
        action: {
          type: 'string',
          enum: ['list', 'get_active', 'focus', 'minimize', 'maximize', 'restore', 'close', 'set_pos'],
          description: 'Window management action.'
        },
        target: { type: 'string', description: 'Window title substring, process name, or HWND.' },
        x: { type: 'integer', description: 'X coordinate for set_pos' },
        y: { type: 'integer', description: 'Y coordinate for set_pos' },
        width: { type: 'integer', description: 'Width for set_pos' },
        height: { type: 'integer', description: 'Height for set_pos' }
      },
      required: ['action']
    }
  },
  {
    name: 'system_info',
    description: 'Monitor layout and virtual desktop bounds, cursor position, and active window. Call it once before working on a multi-monitor machine so you know which coordinates belong to which screen.',
    inputSchema: { type: 'object', properties: {} }
  }
];

function textResult(text, isError = false) {
  return { content: [{ type: 'text', text }], isError };
}

export async function handleToolCall(name, args, engineClient) {
  try {
    switch (name) {
      case 'screen_capture': {
        const wantsPanorama = Boolean(args.panorama);
        const payload = wantsPanorama
          ? {
              cmd: 'panorama',
              maxDimension: args.max_dimension || 1280,
              format: args.format || 'jpeg',
              quality: args.quality || 80,
              returnBase64: args.return_image !== false,
              savePath: args.save_path || '',
              labels: args.labels !== false
            }
          : {
              cmd: 'screenshot',
              screenIndex: args.screen_index !== undefined ? args.screen_index : 0,
              targetActiveWindow: Boolean(args.target_active_window),
              maxDimension: args.max_dimension || 1280,
              format: args.format || 'jpeg',
              quality: args.quality || 80,
              returnBase64: args.return_image !== false,
              savePath: args.save_path || ''
            };

        if (wantsPanorama && Array.isArray(args.screens)) payload.screens = args.screens;
        if (!wantsPanorama && args.roi) payload.roi = args.roi;

        const res = await engineClient.send(payload, 30000);
        if (res.status === 'error') return textResult(`Screenshot error: ${res.message}`, true);

        const content = [
          {
            type: 'text',
            text: wantsPanorama
              ? panoramaSummary(res)
              : `Screenshot captured: ${res.width}x${res.height} (source: ${res.sourceRect.width}x${res.sourceRect.height}) in ${res.elapsedMs}ms${res.path ? ` | Saved to: ${res.path}` : ''}`
          }
        ];

        if (res.base64 && args.return_image !== false) {
          content.push({ type: 'image', data: res.base64, mimeType: args.format === 'png' ? 'image/png' : 'image/jpeg' });
        }
        return { content, isError: false };
      }

      case 'ui_inspect': {
        const res = await engineClient.send({
          cmd: 'ui_inspect',
          target: args.target || 'active_window',
          maxDepth: args.max_depth || 7,
          filter: args.filter || '',
          interactiveOnly: args.interactive_only !== false
        });

        if (res.status === 'error') return textResult(`UI inspect error: ${res.message}`, true);

        let output = `UI Elements (${res.count} found in ${res.elapsedMs}ms):\n`;
        if (res.elements && res.elements.length > 0) {
          res.elements.forEach((el, idx) => {
            const idStr = el.automationId ? ` [id=${el.automationId}]` : '';
            output += `${idx + 1}. [${el.type}] "${el.name}"${idStr} -> Center: (${el.center.x}, ${el.center.y}) | Bounds: [${el.rect.x}, ${el.rect.y}, ${el.rect.width}x${el.rect.height}]\n`;
          });
        } else {
          output += 'No matching interactive elements found.';
        }
        return textResult(output);
      }

      case 'app_launch': {
        const res = await engineClient.send({
          cmd: 'launch',
          app: args.app,
          arguments: args.args || '',
          expect_window: args.expect_window || '',
          reuse_existing: args.reuse_existing !== false,
          focus: args.focus !== false,
          timeout_ms: args.timeout_ms || 10000
        }, 40000);

        if (res.status === 'error') {
          const why = res.errorText ? `${res.message} - ${res.errorText}` : res.message;
          return textResult(`Launch failed: ${why}`, true);
        }
        return textResult(
          `Launched "${args.app}" in ${res.waitedMs}ms${res.reused ? ' (reused an existing window)' : ''} | window: "${res.title}" hwnd=${res.hwnd} | focused: ${res.focused !== false}`
        );
      }

      case 'wait_for': {
        const payload = {
          cmd: 'wait_for',
          present: args.present !== false,
          timeout_ms: args.timeout_ms || 5000,
          poll_ms: args.poll_ms || 80
        };
        if (args.filter) payload.filter = args.filter;
        if (args.window) payload.window = args.window;
        if (args.foreground) payload.foreground = true;

        const timeout = (args.timeout_ms || 5000) + 10000;
        const res = await engineClient.send(payload, timeout);
        if (res.status === 'error') return textResult(`wait_for: ${res.message} (waited ${res.waitedMs}ms)`, true);
        return textResult(
          `wait_for satisfied after ${res.waitedMs}ms: ${res.window ? `window "${res.title || res.window}"` : `${res.count} element(s) matching "${res.filter}"`}`
        );
      }

      case 'read_text': {
        const payload = { cmd: 'read_text', target: args.target || 'focused' };
        if (args.filter) payload.filter = args.filter;
        if (args.expect !== undefined) payload.expect = args.expect;
        if (args.max_chars) payload.max_chars = args.max_chars;

        const res = await engineClient.send(payload);
        if (res.status === 'error') {
          return textResult(`read_text: ${res.message}${res.text !== undefined ? `\nActual text: ${JSON.stringify(res.text)}` : ''}`, true);
        }
        const where = res.element ? ` from [${res.type}] "${res.element}"${res.automationId ? ` [id=${res.automationId}]` : ''}` : '';
        const assertion = res.expect !== undefined ? `\nContains ${JSON.stringify(res.expect)}: ${res.matched}` : '';
        return textResult(`Read ${res.length} chars${where}:\n${res.text}${assertion}`);
      }

      case 'mouse_action': {
        const payload = {
          cmd: 'mouse',
          action: args.action,
          x: args.x,
          y: args.y,
          button: args.button || 'left',
          scrollAmount: args.scroll_amount || (args.action === 'scroll' ? -120 : 0),
          toX: args.to_x,
          toY: args.to_y,
          smooth: Boolean(args.smooth)
        };
        if (args.duration_ms) payload.duration_ms = args.duration_ms;

        const res = await engineClient.send(payload);
        if (res.status === 'error') return textResult(`mouse_action ${args.action} failed: ${res.message}`, true);
        const at = res.x !== undefined ? ` at (${res.x}, ${res.y})` : '';
        return textResult(`mouse ${args.action}${at} in ${res.elapsedMs}ms`);
      }

      case 'keyboard_action': {
        const res = await engineClient.send({
          cmd: 'keyboard',
          action: args.action,
          text: args.text || '',
          hotkey: args.hotkey || '',
          key: args.key || '',
          delayMs: args.delay_ms !== undefined ? args.delay_ms : 2
        });
        if (res.status === 'error') return textResult(`keyboard_action ${args.action} failed: ${res.message}`, true);
        const what = res.keys ? `"${res.keys}"` : res.length !== undefined ? `${res.length} chars (${res.mode})` : res.key || '';
        return textResult(`keyboard ${args.action} ${what} in ${res.elapsedMs}ms`);
      }

      case 'batch_actions': {
        const actions = (args.actions || []).map(normalizeItem);
        const payload = { cmd: 'batch', actions };
        if (args.continue_on_error) payload.continue_on_error = true;

        // A batch can legitimately run for a while (an app launch plus several waits),
        // so its ceiling is the sum of what its steps are allowed to take.
        const budget = actions.reduce((acc, a) => acc + (Number(a.timeout_ms || a.timeoutMs || 0) || 0) + (Number(a.ms || 0) || 0), 0);
        const res = await engineClient.send(payload, Math.max(60000, budget + 30000));

        const lines = [];
        const failed = res.failed || 0;
        lines.push(
          `Batch ${res.status === 'ok' ? 'completed' : 'INCOMPLETE'}: ${res.count || 0} step(s), ${failed} failed, ${res.elapsedMs || 0}ms total`
        );
        if (res.stoppedAtStep) {
          lines.push(`Halted at step ${res.stoppedAtStep}; the remaining steps did not run. ${res.message || ''}`);
        }
        for (const step of res.results || []) lines.push(describeStep(step));

        const content = [{ type: 'text', text: lines.join('\n') }];
        for (const step of res.results || []) {
          if (step.base64) {
            content.push({ type: 'image', data: step.base64, mimeType: step.mimeType === 'image/png' ? 'image/png' : 'image/jpeg' });
          }
        }
        return { content, isError: failed > 0 };
      }

      case 'window_manager': {
        const act = args.action;
        let payload;

        if (act === 'list') {
          payload = { cmd: 'window_list' };
          const res = await engineClient.send(payload);
          if (res.status === 'error') return textResult(`window_manager list failed: ${res.message}`, true);
          const lines = [`Windows (${(res.windows || []).length}):`];
          for (const w of res.windows || []) {
            lines.push(
              `  [${w.processName}] "${w.title}" hwnd=${w.hwnd} ${w.rect.width}x${w.rect.height} at (${w.rect.x},${w.rect.y})${w.isForeground ? ' FOREGROUND' : ''}${w.isMinimized ? ' minimized' : ''}`
            );
          }
          return textResult(lines.join('\n'));
        }

        if (act === 'get_active') {
          const infoRes = await engineClient.send({ cmd: 'info' });
          const a = infoRes.activeWindow || {};
          return textResult(
            `Active window: [${a.processName}] "${a.title}" hwnd=${a.hwnd} pid=${a.pid} ${a.rect ? `${a.rect.width}x${a.rect.height} at (${a.rect.x},${a.rect.y})` : ''}`
          );
        }

        if (act === 'focus') payload = { cmd: 'window_focus', target: args.target, action: 'focus' };
        else if (act === 'minimize' || act === 'maximize' || act === 'restore') payload = { cmd: 'window_state', target: args.target, state: act };
        else if (act === 'close') payload = { cmd: 'window_close', target: args.target };
        else if (act === 'set_pos') payload = { cmd: 'window_pos', target: args.target, x: args.x, y: args.y, width: args.width, height: args.height };
        else return textResult(`Unknown window_manager action: ${act}`, true);

        const res = await engineClient.send(payload);
        if (res.status === 'error') return textResult(`window_manager ${act} failed: ${res.message}`, true);
        const fg = res.foreground !== undefined ? ` | foreground: ${res.foreground}` : '';
        return textResult(`window ${act}: "${res.title}" hwnd=${res.hwnd}${fg} in ${res.elapsedMs}ms`);
      }

      case 'system_info': {
        const res = await engineClient.send({ cmd: 'info' });
        if (res.status === 'error') return textResult(`system_info failed: ${res.message}`, true);
        const lines = ['Monitors:'];
        for (const m of res.monitors || []) {
          lines.push(`  ${m.index}${m.primary ? ' (primary)' : ''}: ${m.bounds.width}x${m.bounds.height} at (${m.bounds.x},${m.bounds.y}) [${m.device}]`);
        }
        const v = res.virtualScreen;
        if (v) lines.push(`Virtual desktop: ${v.width}x${v.height} at (${v.x},${v.y})`);
        if (res.cursor) lines.push(`Cursor: (${res.cursor.x}, ${res.cursor.y})`);
        const a = res.activeWindow;
        if (a) lines.push(`Active window: [${a.processName}] "${a.title}" hwnd=${a.hwnd}`);
        return textResult(lines.join('\n'));
      }

      default:
        return textResult(`Unknown tool: ${name}`, true);
    }
  } catch (err) {
    return textResult(`Error executing ${name}: ${err.message}`, true);
  }
}
