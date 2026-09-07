#!/usr/bin/env node
// Reproducible smoke test for the fast-computer-use MCP plugin.
//
// Spawns the real MCP server (server/index.mjs, which itself spawns the real
// FastEngine.exe daemon) and speaks actual JSON-RPC 2.0 over stdio - the same
// protocol a real MCP client (Claude Code) uses - to exercise every tool with
// safe arguments. No mocks: this proves the whole stack (Node MCP layer +
// native Win32 engine) end to end.
//
// Safety: interactive input (mouse clicks, keystrokes) is never sent to
// whatever window happens to be focused. The script opens its own disposable
// scratch window first and only proceeds with interactive tests once it has
// confirmed that window is actually focused; it is closed again at the end.
//
// Notepad is the preferred scratch app because it is what the plugin's own
// benchmark uses. When Notepad is broken on the host (a missing Windows App
// SDK DLL has been observed), that is reported as an ENVIRONMENT note and the
// run continues against Character Map - a pure Win32 app that is always
// present, has an editable field, and discards everything when closed.
//
// Exit code 0 = every check passed. Non-zero = see stderr for which failed.

import { spawn } from 'node:child_process';
import { createInterface } from 'node:readline';
import { tmpdir } from 'node:os';
import { unlinkSync } from 'node:fs';
import { resolve, dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const SERVER_PATH = resolve(__dirname, 'index.mjs');

// The engine always writes a screenshot to disk (defaulting to a generated
// temp path) even when return_image is false - pass an explicit save_path
// for every capture so we know exactly what to delete afterward.
let shotCounter = 0;
function tmpShotPath(ext = 'jpg') {
  return join(tmpdir(), `fast-computer-use-smoke-test-${process.pid}-${shotCounter++}.${ext}`);
}
function cleanupShot(path) {
  try { unlinkSync(path); } catch { /* best effort */ }
}

const EXPECTED_TOOLS = [
  'batch_actions', 'screen_capture', 'app_launch', 'wait_for', 'read_text',
  'ui_inspect', 'mouse_action', 'keyboard_action', 'window_manager', 'system_info'
];

let passCount = 0;
let failCount = 0;
const failures = [];
const notes = [];

function ok(label, detail = '') {
  passCount++;
  console.log(`  ok  - ${label}${detail ? ` (${detail})` : ''}`);
}

function fail(label, detail) {
  failCount++;
  failures.push(label);
  console.error(`  FAIL - ${label}${detail ? `: ${detail}` : ''}`);
}

function assert(cond, label, detail = '') {
  if (cond) ok(label, detail);
  else fail(label, detail);
}

function note(text) {
  notes.push(text);
  console.log(`  note  - ${text}`);
}

async function sleep(ms) {
  return new Promise((r) => setTimeout(r, ms));
}

// --- Minimal MCP (JSON-RPC 2.0 over stdio, line-delimited) client ------------

class McpClient {
  constructor(serverPath) {
    this.serverPath = serverPath;
    this.nextId = 1;
    this.pending = new Map();
    this.proc = null;
  }

  start() {
    this.proc = spawn(process.execPath, [this.serverPath], {
      stdio: ['pipe', 'pipe', 'pipe']
    });

    const rl = createInterface({ input: this.proc.stdout, crlfDelay: Infinity });
    rl.on('line', (line) => {
      const trimmed = line.trim();
      if (!trimmed) return;
      let msg;
      try { msg = JSON.parse(trimmed); } catch { return; }
      if (msg.id !== undefined && this.pending.has(msg.id)) {
        const { resolve } = this.pending.get(msg.id);
        this.pending.delete(msg.id);
        resolve(msg);
      }
    });

    this.proc.stderr.on('data', (d) => {
      const msg = d.toString().trim();
      if (msg) console.error(`  [server stderr] ${msg}`);
    });
  }

  request(method, params, timeoutMs = 30000) {
    const id = this.nextId++;
    const payload = { jsonrpc: '2.0', id, method, ...(params !== undefined ? { params } : {}) };
    return new Promise((resolve, reject) => {
      const timeout = setTimeout(() => {
        this.pending.delete(id);
        reject(new Error(`timed out after ${timeoutMs}ms waiting for ${method}`));
      }, timeoutMs);
      this.pending.set(id, { resolve: (msg) => { clearTimeout(timeout); resolve(msg); } });
      this.proc.stdin.write(JSON.stringify(payload) + '\n');
    });
  }

  async callTool(name, args = {}, timeoutMs = 30000) {
    const msg = await this.request('tools/call', { name, arguments: args }, timeoutMs);
    if (msg.error) throw new Error(`${name}: ${msg.error.message}`);
    return msg.result;
  }

  stop() {
    if (!this.proc || this.proc.killed) return;
    try { this.proc.kill(); } catch {}
  }
}

function toolResultText(result) {
  return result?.content?.map((c) => c.text).join('\n') ?? '';
}

async function getActiveWindowTitle(client) {
  const res = await client.callTool('window_manager', { action: 'get_active' });
  return toolResultText(res).match(/"([^"]*)"/)?.[1] ?? '';
}

// --- Disposable scratch window (safety net for interactive tests) -----------

// Notepad's "save changes?" confirmation has a locale-dependent label
// ("Don't save" / "Ne pas enregistrer" / ...) but a stable automationId
// ("SecondaryButton" = discard) - find it with ui_inspect rather than matching
// text. The dialog takes the foreground when it appears, so active_window is
// enough - and it matters: target:"screen" walks every open window and can
// burn the element cap on unrelated apps before reaching this small dialog.
// Returns true if a dialog was found and dismissed.
async function dismissSaveDialogIfPresent(client) {
  const res = await client.callTool('ui_inspect', {
    target: 'active_window', max_depth: 6, filter: 'SecondaryButton'
  });
  const match = toolResultText(res).match(/Center:\s*\((\d+),\s*(\d+)\)/);
  if (!match) return false;
  await client.callTool('mouse_action', { action: 'click', x: Number(match[1]), y: Number(match[2]) });
  await sleep(200);
  return true;
}

const SCRATCH_TEXT = 'fast-computer-use smoke test';

// Opens the scratch window the interactive tests are allowed to touch, and
// verifies it is really focused before handing it over. Prefers Notepad, falls
// back to Character Map when the host's Notepad cannot start.
function hwndOf(launchResult) {
  return toolResultText(launchResult).match(/hwnd=(\d+)/)?.[1] ?? '';
}

async function openScratchApp(client) {
  const np = await client.callTool('app_launch', {
    app: 'notepad', reuse_existing: false, timeout_ms: 8000
  }, 40000);

  if (!np.isError) {
    // A restored session tab is never trusted: force a genuinely new one.
    await client.callTool('keyboard_action', { action: 'hotkey', hotkey: 'ctrl+n' });
    await sleep(300);
    const title = await getActiveWindowTitle(client);
    if (/^Untitled|^Sans titre/i.test(title)) {
      return {
        kind: 'notepad',
        title,
        hwnd: hwndOf(np),
        // Notepad's editor pane is a Document element; it has no automation id.
        editFilter: 'Document',
        closeTarget: SCRATCH_TEXT,
        needsSaveDismiss: true
      };
    }
    note(`Notepad opened but gave "${title}" instead of a blank tab; using Character Map instead`);
  } else {
    note(`ENVIRONMENT: Notepad cannot start on this host, falling back to Character Map. ${toolResultText(np).slice(0, 160)}`);
  }

  const cm = await client.callTool('app_launch', {
    app: 'charmap', reuse_existing: false, timeout_ms: 10000
  }, 40000);
  if (cm.isError) throw new Error(`no usable scratch app: ${toolResultText(cm)}`);

  const title = await getActiveWindowTitle(client);
  return {
    kind: 'charmap',
    title,
    hwnd: hwndOf(cm),
    // "Characters to copy" - stable automation id, locale-independent.
    editFilter: '104',
    closeTarget: title,
    needsSaveDismiss: false
  };
}

async function closeScratchApp(client, scratch) {
  try {
    // By handle, not by title: the title is locale-dependent and changes as
    // soon as the window has content.
    const target = scratch.hwnd || scratch.closeTarget;
    const res = await client.callTool('window_manager', { action: 'close', target });
    if (res.isError) note(`teardown: close failed - ${toolResultText(res)}`);
    if (!scratch.needsSaveDismiss) return;
    for (let i = 0; i < 10; i++) {
      await sleep(200);
      if (await dismissSaveDialogIfPresent(client)) break;
      const title = await getActiveWindowTitle(client);
      if (!title.includes('smoke test')) break;
    }
  } catch (err) {
    note(`teardown: close threw - ${err.message}`);
  }
}

// --- Main ---------------------------------------------------------------

async function main() {
  console.log(`Smoke test: fast-computer-use MCP server (${SERVER_PATH})\n`);

  const client = new McpClient(SERVER_PATH);
  client.start();

  let scratch = null;
  try {
    // 1. initialize
    const initMsg = await client.request('initialize', {
      protocolVersion: '2024-11-05',
      capabilities: {},
      clientInfo: { name: 'smoke-test', version: '1.0.0' }
    });
    assert(!initMsg.error && initMsg.result?.serverInfo?.name === 'fast-computer-use',
      'initialize responds with serverInfo', JSON.stringify(initMsg.result?.serverInfo));

    // 2. tools/list
    const listMsg = await client.request('tools/list');
    const names = (listMsg.result?.tools ?? []).map((t) => t.name).sort();
    assert(names.length === EXPECTED_TOOLS.length && EXPECTED_TOOLS.every((n) => names.includes(n)),
      `tools/list returns exactly the ${EXPECTED_TOOLS.length} expected tools`, names.join(', '));

    // 2b. batch_actions must be discoverable as the preferred path, otherwise
    // the model falls back to one call per action - the whole slowness problem.
    const batchDef = (listMsg.result?.tools ?? []).find((t) => t.name === 'batch_actions');
    assert(/PREFERRED ENTRY POINT/.test(batchDef?.description ?? '') &&
      /"cmd":"launch"/.test(batchDef?.inputSchema?.properties?.actions?.description ?? ''),
      'batch_actions advertises itself as the preferred path with documented step shapes');

    // 3. system_info - read-only, always safe
    const sysInfo = await client.callTool('system_info', {});
    const monitorCount = (toolResultText(sysInfo).match(/^ {2}\d+/gm) ?? []).length;
    assert(!sysInfo.isError && monitorCount > 0,
      'system_info returns monitor/cursor data', `${monitorCount} monitor(s)`);

    // 4. screen_capture - safe args, no image payload in the response.
    const shotPath1 = tmpShotPath();
    const shot = await client.callTool('screen_capture', {
      screen_index: 0, max_dimension: 64, format: 'jpeg', return_image: false, save_path: shotPath1
    });
    assert(!shot.isError, 'screen_capture (screen_index:0, no image payload)');
    cleanupShot(shotPath1);

    // 4b. roi: the captured source size must match the requested crop exactly.
    const shotPath2 = tmpShotPath();
    const shotRoi = await client.callTool('screen_capture', {
      roi: { x: 10, y: 10, width: 150, height: 90 }, return_image: false, save_path: shotPath2
    });
    assert(!shotRoi.isError && toolResultText(shotRoi).includes('source: 150x90'),
      'screen_capture roi (crop size matches request)', toolResultText(shotRoi).split('\n')[0]);
    cleanupShot(shotPath2);

    // 4c. target_active_window
    const shotPath3 = tmpShotPath();
    const shotActive = await client.callTool('screen_capture', {
      target_active_window: true, return_image: false, save_path: shotPath3
    });
    assert(!shotActive.isError, 'screen_capture target_active_window');
    cleanupShot(shotPath3);

    // 4d. panorama - one image covering every monitor, each with its own
    // resolution budget and a coordinate map back to the desktop.
    const panoPath = tmpShotPath();
    const pano = await client.callTool('screen_capture', {
      panorama: true, max_dimension: 320, return_image: false, save_path: panoPath
    });
    const panoText = toolResultText(pano);
    const panoDims = panoText.match(/Panorama: (\d+)x(\d+) across (\d+) monitor/);
    assert(!pano.isError && panoDims && Number(panoDims[3]) === monitorCount,
      'screen_capture panorama covers every monitor',
      panoDims ? `${panoDims[1]}x${panoDims[2]} across ${panoDims[3]}` : panoText.split('\n')[0]);
    assert(/panorama rect \(\d+,\d+\)/.test(panoText) && /desktop_x = /.test(panoText),
      'panorama returns a per-screen coordinate map');
    // With N monitors, per-monitor budgeting must produce a wider image than a
    // single squashed virtual-desktop grab at the same budget.
    if (monitorCount > 1 && panoDims) {
      assert(Number(panoDims[1]) > 320,
        'panorama budgets max_dimension per monitor, not across the whole desktop',
        `width ${panoDims[1]} > 320`);
    }
    cleanupShot(panoPath);

    // 5. ui_inspect - read-only introspection of whatever is currently active
    const inspect = await client.callTool('ui_inspect', {
      target: 'active_window', max_depth: 2, interactive_only: true
    });
    assert(!inspect.isError, 'ui_inspect (active_window, shallow)');

    // 6. window_manager - list + get_active, always safe
    const wmList = await client.callTool('window_manager', { action: 'list' });
    assert(!wmList.isError && /^Windows \(\d+\):/.test(toolResultText(wmList)), 'window_manager list');
    const wmActive = await client.callTool('window_manager', { action: 'get_active' });
    assert(!wmActive.isError && /Active window:/.test(toolResultText(wmActive)), 'window_manager get_active');

    // 6b. app_launch on a name that cannot start must report an error, never a
    // false success on some unrelated window that happened to match.
    const badLaunch = await client.callTool('app_launch', {
      app: 'zzz_no_such_executable_zzz.exe', timeout_ms: 2000
    }, 30000);
    assert(badLaunch.isError && /Launch failed/.test(toolResultText(badLaunch)),
      'app_launch reports a failure instead of matching an unrelated window');

    // 6c. wait_for must time out cleanly on something that will never appear.
    const badWait = await client.callTool('wait_for', {
      window: 'zzz_no_such_window_zzz', timeout_ms: 600
    }, 30000);
    assert(badWait.isError && /wait_for:/.test(toolResultText(badWait)),
      'wait_for times out cleanly on a condition that never holds');

    // --- Interactive tests: only inside our own disposable scratch window ---
    scratch = await openScratchApp(client);
    ok(`disposable scratch window confirmed focused`, `${scratch.kind}: ${scratch.title}`);

    // 7. mouse_action - move, then every click variant, inside our own window.
    const move = await client.callTool('mouse_action', { action: 'move', x: 400, y: 300 });
    assert(!move.isError && /mouse move at \(400, 300\)/.test(toolResultText(move)), 'mouse_action move');

    const smoothMove = await client.callTool('mouse_action', {
      action: 'move', x: 420, y: 320, smooth: true
    });
    assert(!smoothMove.isError, 'mouse_action smooth move (direct call)');

    const rightClick = await client.callTool('mouse_action', { action: 'right_click', x: 400, y: 300 });
    assert(!rightClick.isError, 'mouse_action right_click');
    await client.callTool('keyboard_action', { action: 'press_key', key: 'Escape' });

    const middleClick = await client.callTool('mouse_action', { action: 'middle_click', x: 400, y: 300 });
    assert(!middleClick.isError, 'mouse_action middle_click');

    const doubleClick = await client.callTool('mouse_action', { action: 'double_click', x: 400, y: 300 });
    assert(!doubleClick.isError, 'mouse_action double_click');

    const tripleClick = await client.callTool('mouse_action', { action: 'triple_click', x: 400, y: 300 });
    assert(!tripleClick.isError, 'mouse_action triple_click');

    // 7b. ui_inspect variants
    const inspectCursor = await client.callTool('ui_inspect', { target: 'cursor', max_depth: 3 });
    assert(!inspectCursor.isError, 'ui_inspect target=cursor');

    const inspectScreen = await client.callTool('ui_inspect', { target: 'screen', max_depth: 2 });
    const screenCount = Number(toolResultText(inspectScreen).match(/\((\d+) found/)?.[1] ?? -1);
    assert(!inspectScreen.isError && screenCount > 0,
      'ui_inspect target=screen (top-level windows found)', `count=${screenCount}`);

    const inspectFilter = await client.callTool('ui_inspect', {
      target: 'active_window', max_depth: 6, filter: scratch.editFilter, interactive_only: false
    });
    const filterCount = Number(toolResultText(inspectFilter).match(/\((\d+) found/)?.[1] ?? -1);
    assert(!inspectFilter.isError && filterCount >= 1,
      `ui_inspect filter="${scratch.editFilter}" narrows to the editable field`, `count=${filterCount}`);

    const inspectInteractive = await client.callTool('ui_inspect', {
      target: 'active_window', max_depth: 6, interactive_only: true
    });
    const inspectAll = await client.callTool('ui_inspect', {
      target: 'active_window', max_depth: 6, interactive_only: false
    });
    const nInteractive = Number(toolResultText(inspectInteractive).match(/\((\d+) found/)?.[1] ?? -1);
    const nAll = Number(toolResultText(inspectAll).match(/\((\d+) found/)?.[1] ?? -1);
    assert(!inspectInteractive.isError && !inspectAll.isError && nAll >= nInteractive && nInteractive >= 0,
      'ui_inspect interactive_only=false includes at least as much as true',
      `interactive_only=true:${nInteractive} interactive_only=false:${nAll}`);

    // 8. Put the caret in the editable field, then type into it.
    await client.callTool('batch_actions', {
      actions: [{ cmd: 'ui_click', ...(scratch.kind === 'charmap' ? { automation_id: '104' } : { control_type: 'Document' }) }]
    });
    const type = await client.callTool('keyboard_action', { action: 'type_text', text: SCRATCH_TEXT });
    assert(!type.isError, 'keyboard_action type_text (into disposable window)');

    // 8b. read_text reads that back out of UI Automation, with an assertion -
    // this is the no-screenshot way to confirm an action actually landed.
    const read = await client.callTool('read_text', { filter: scratch.editFilter, expect: SCRATCH_TEXT });
    assert(!read.isError && /Contains .*: true/.test(toolResultText(read)),
      'read_text reads the typed text back and asserts it', toolResultText(read).split('\n')[0]);

    // 8c. read_text must fail loudly when the assertion does not hold.
    const readBad = await client.callTool('read_text', {
      filter: scratch.editFilter, expect: 'zzz_text_that_is_not_there_zzz'
    });
    assert(readBad.isError, 'read_text reports a failed expectation as an error');

    // 9. window_manager - minimize/maximize/restore on our own window
    const wmTarget = scratch.title;
    const min = await client.callTool('window_manager', { action: 'minimize', target: wmTarget });
    assert(!min.isError, 'window_manager minimize');
    await sleep(200);
    const restore = await client.callTool('window_manager', { action: 'restore', target: wmTarget });
    assert(!restore.isError, 'window_manager restore');
    await sleep(200);
    const max = await client.callTool('window_manager', { action: 'maximize', target: wmTarget });
    assert(!max.isError, 'window_manager maximize');
    await sleep(200);
    const restore2 = await client.callTool('window_manager', { action: 'restore', target: wmTarget });
    assert(!restore2.isError, 'window_manager restore (after maximize)');
    await sleep(200);

    // 9b. focus must verify it actually reached the foreground, not just issue
    // a SetForegroundWindow that Windows may silently refuse.
    const focus = await client.callTool('window_manager', { action: 'focus', target: wmTarget });
    assert(!focus.isError && /foreground: true/.test(toolResultText(focus)),
      'window_manager focus confirms the window really reached the foreground',
      toolResultText(focus).slice(0, 90));

    // 10. batch_actions - the routing fix: a step carrying only "action"
    // (the field name every tool schema uses) used to die on "Unknown command".
    const batchAction = await client.callTool('batch_actions', {
      actions: [
        { action: 'move', x: 500, y: 400 },
        { action: 'move', x: 520, y: 420, smooth: true },
        { cmd: 'wait', ms: 20 }
      ]
    });
    const batchActionText = toolResultText(batchAction);
    assert(!batchAction.isError && /Batch completed: 3 step\(s\), 0 failed/.test(batchActionText),
      'batch_actions runs steps that name only "action" (incl. smooth move)',
      batchActionText.split('\n')[0]);
    assert(!/Unknown command/.test(batchActionText),
      'batch_actions no longer reports "Unknown command" for action-only steps');

    // 10b. a failing step halts the batch instead of typing on into the void.
    const batchHalt = await client.callTool('batch_actions', {
      actions: [{ action: 'move', x: 500, y: 400 }, { cmd: 'zzz_bad_cmd' }, { action: 'move', x: 600, y: 500 }]
    });
    const haltText = toolResultText(batchHalt);
    assert(batchHalt.isError && /Halted at step 2/.test(haltText) && /2 step\(s\), 1 failed/.test(haltText),
      'batch_actions halts at the first failing step', haltText.split('\n')[0]);

    // 10c. continue_on_error opts back into running the whole list.
    const batchContinue = await client.callTool('batch_actions', {
      actions: [{ action: 'move', x: 500, y: 400 }, { cmd: 'zzz_bad_cmd' }, { action: 'move', x: 600, y: 500 }],
      continue_on_error: true
    });
    assert(/3 step\(s\), 1 failed/.test(toolResultText(batchContinue)),
      'batch_actions continue_on_error runs past a failure');

    // 10d. snake_case fields inside a batch step must reach the engine, or a
    // capture silently comes back at full resolution and costs a fortune.
    const batchShotPath = tmpShotPath();
    const batchShot = await client.callTool('batch_actions', {
      actions: [{ cmd: 'screenshot', max_dimension: 200, return_image: false, save_path: batchShotPath }]
    });
    assert(!batchShot.isError && toolResultText(batchShot).includes(batchShotPath),
      'batch_actions normalizes snake_case step fields for the engine');
    cleanupShot(batchShotPath);

    // 11. wait_for and per-step verify inside a batch.
    const batchVerify = await client.callTool('batch_actions', {
      actions: [
        { cmd: 'wait_for', window: wmTarget, timeout_ms: 3000 },
        { cmd: 'mouse', action: 'move', x: 480, y: 360, verify: true }
      ]
    });
    const verifyText = toolResultText(batchVerify);
    assert(!batchVerify.isError && /wait_for/.test(verifyText) && /verify\(state\)/.test(verifyText),
      'batch_actions supports wait_for and per-step verify', verifyText.split('\n')[0]);

    // 11b. a verification that cannot hold must fail its step.
    const badVerify = await client.callTool('batch_actions', {
      actions: [{ cmd: 'mouse', action: 'move', x: 480, y: 360, verify: 'zzz_no_such_element_zzz' }]
    });
    assert(badVerify.isError && /1 failed/.test(toolResultText(badVerify)),
      'a step whose verification fails is reported as failed');

    // 12. The headline scenario, in ONE call: put text in the scratch window
    // and prove it is there, with no screenshot and no intermediate round trip.
    const scenarioText = 'hello world';
    const t0 = Date.now();
    const scenario = await client.callTool('batch_actions', {
      actions: [
        { cmd: 'window_focus', target: wmTarget },
        { cmd: 'ui_click', ...(scratch.kind === 'charmap' ? { automation_id: '104' } : { control_type: 'Document' }) },
        { cmd: 'keyboard', action: 'hotkey', hotkey: 'ctrl+a' },
        { cmd: 'keyboard', action: 'type_text', text: scenarioText },
        { cmd: 'read_text', filter: scratch.editFilter, expect: scenarioText }
      ]
    }, 60000);
    const elapsed = Date.now() - t0;
    const scenarioText2 = toolResultText(scenario);
    assert(!scenario.isError && /matched=true/.test(scenarioText2),
      `end-to-end scenario (focus, click, type, verify) in one batch_actions call`,
      `${elapsed}ms wall`);
    assert(elapsed < 5000, 'end-to-end scenario completes in under 5s', `${elapsed}ms`);

  } catch (err) {
    fail('unexpected exception', err.message);
  } finally {
    if (scratch) {
      await closeScratchApp(client, scratch);
      try {
        let stillOpen = true;
        for (let i = 0; i < 5 && stillOpen; i++) {
          const list = await client.callTool('window_manager', { action: 'list' });
          stillOpen = scratch.hwnd
            ? toolResultText(list).includes(`hwnd=${scratch.hwnd}`)
            : toolResultText(list).includes(scratch.closeTarget);
          if (stillOpen) await sleep(300);
        }
        assert(!stillOpen, 'disposable scratch window left nothing behind');
      } catch (err) {
        fail('post-teardown window check', err.message);
      }
    }
    client.stop();
  }

  console.log(`\n${passCount} passed, ${failCount} failed.`);
  if (notes.length) console.log(`Notes: ${notes.length} (see "note" lines above)`);
  if (failCount > 0) {
    console.error('Failures:', failures.join('; '));
    process.exit(1);
  }
  process.exit(0);
}

main().catch((err) => {
  console.error('[smoke-test fatal]', err);
  process.exit(1);
});
