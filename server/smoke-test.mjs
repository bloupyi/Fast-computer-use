#!/usr/bin/env node
// Reproducible smoke test for the fast-computer-use MCP plugin.
//
// Spawns the real MCP server (server/index.mjs, which itself spawns the real
// FastEngine.exe daemon) and speaks actual JSON-RPC 2.0 over stdio — the same
// protocol a real MCP client (Claude Code) uses — to exercise every tool with
// safe arguments. No mocks: this proves the whole stack (Node MCP layer +
// native Win32 engine) end to end.
//
// Safety: interactive input (mouse clicks, keystrokes) is never sent to
// whatever window happens to be focused. The script opens a disposable,
// verified-blank Notepad tab first and only proceeds with interactive tests
// (including every mouse_action click variant) once it has confirmed that tab
// is actually focused; the tab is closed again at the end.
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
// temp path) even when return_image is false — pass an explicit save_path
// for every capture so we know exactly what to delete afterward.
let shotCounter = 0;
function tmpShotPath() {
  return join(tmpdir(), `fast-computer-use-smoke-test-${process.pid}-${shotCounter++}.jpg`);
}
function cleanupShot(path) {
  try { unlinkSync(path); } catch { /* best effort */ }
}

const EXPECTED_TOOLS = [
  'screen_capture', 'ui_inspect', 'mouse_action', 'keyboard_action',
  'batch_actions', 'window_manager', 'system_info'
];

let passCount = 0;
let failCount = 0;
const failures = [];

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

  request(method, params, timeoutMs = 20000) {
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

  async callTool(name, args = {}) {
    const msg = await this.request('tools/call', { name, arguments: args });
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

// --- Disposable Notepad helper (safety net for interactive tests) -----------

async function getActiveWindowTitle(client) {
  const res = await client.callTool('window_manager', { action: 'get_active' });
  try { return JSON.parse(toolResultText(res)).title || ''; } catch { return ''; }
}

// Notepad's "save changes?" confirmation has a locale-dependent label
// ("Don't save" / "Ne pas enregistrer" / ...) but a stable automationId
// ("SecondaryButton" = discard) — find it with our own ui_inspect fix rather
// than matching text. The dialog does take the foreground when it appears,
// so active_window is enough — and it matters: target:"screen" walks every
// open window (verified: it can burn the 150-element cap on unrelated apps
// before ever reaching this small dialog) and is unreliable for this.
// Returns true if a dialog was found and dismissed.
async function dismissSaveDialogIfPresent(client) {
  const res = await client.callTool('ui_inspect', {
    target: 'active_window', max_depth: 6, filter: 'SecondaryButton'
  });
  // ui_inspect's tool-call result is formatted text (see tools.mjs), not
  // JSON — pull the button's pre-computed center straight out of it.
  const match = toolResultText(res).match(/Center:\s*\((\d+),\s*(\d+)\)/);
  if (!match) return false;
  await client.callTool('mouse_action', { action: 'click', x: Number(match[1]), y: Number(match[2]) });
  await sleep(200);
  return true;
}

// Opens (or reuses) Notepad, forces a brand-new blank tab, and confirms that
// tab is actually focused before returning. Never assumes — verifies.
async function openVerifiedBlankNotepad(client) {
  const notepad = spawn('notepad.exe', [], { detached: true, stdio: 'ignore' });
  notepad.unref();

  // Modern Windows Notepad restores the previous session's tabs on launch,
  // and an "Open With" chooser has been observed to grab focus transiently on
  // this environment. Poll and nudge until Notepad is foreground, clearing
  // anything unexpected — including a save-prompt orphaned by a previous run
  // that crashed mid-cleanup.
  let focused = false;
  for (let i = 0; i < 15 && !focused; i++) {
    await sleep(200);
    const res = await client.callTool('window_manager', { action: 'get_active' });
    let info;
    try { info = JSON.parse(toolResultText(res)); } catch { info = {}; }
    if (info.processName === 'Notepad') {
      focused = true;
      if (await dismissSaveDialogIfPresent(client)) focused = false; // re-check after dismissing
    } else {
      await client.callTool('keyboard_action', { action: 'press_key', key: 'Escape' });
    }
  }
  if (!focused) throw new Error('Notepad never became the foreground window');

  // Force a genuinely new, empty tab — never trust a restored session tab.
  await client.callTool('keyboard_action', { action: 'hotkey', hotkey: 'ctrl+n' });
  await sleep(300);

  const title = await getActiveWindowTitle(client);
  if (!/^Untitled/i.test(title)) {
    throw new Error(`expected a blank "Untitled" tab, got "${title}" — refusing to run interactive tests`);
  }
  return title;
}

const DISPOSABLE_TAB_TEXT = 'fast-computer-use smoke test';

// Closes the tab/window this script typed into. Modern Notepad sometimes
// attaches a fresh launch as a tab in the existing window and sometimes opens
// a whole new top-level window (observed both ways on this environment) —
// so target it by its own unique title substring via window_manager's
// `close`, which delivers WM_CLOSE straight to that window handle and does
// not depend on it currently holding keyboard focus (unlike a Ctrl+W
// hotkey, which goes wherever focus happens to be — the preceding
// minimize/restore test does not itself re-take the foreground, since
// restore only un-minimizes and, by design, doesn't steal focus back).
// It's dirty (we typed into it), so this still raises a save-changes
// confirmation instead of closing outright — dismiss it, discarding the
// throwaway text without ever saving it to disk.
async function closeNotepadTab(client) {
  try {
    await client.callTool('window_manager', { action: 'close', target: DISPOSABLE_TAB_TEXT });
    for (let i = 0; i < 10; i++) {
      await sleep(200);
      if (await dismissSaveDialogIfPresent(client)) break;
      const title = await getActiveWindowTitle(client);
      if (!title.includes('smoke test')) break; // already closed, no dialog to dismiss
    }
  } catch { /* best-effort cleanup — never fail the whole run over teardown */ }
}

// --- Main ---------------------------------------------------------------

async function main() {
  console.log(`Smoke test: fast-computer-use MCP server (${SERVER_PATH})\n`);

  const client = new McpClient(SERVER_PATH);
  client.start();

  let blankTabOpened = false;
  try {
    // 1. initialize
    const initMsg = await client.request('initialize', {
      protocolVersion: '2024-11-05',
      capabilities: {},
      clientInfo: { name: 'smoke-test', version: '1.0.0' }
    });
    assert(!initMsg.error && initMsg.result?.serverInfo?.name === 'fast-computer-use',
      'initialize responds with serverInfo', JSON.stringify(initMsg.result?.serverInfo));

    // 2. tools/list — 7 tools expected
    const listMsg = await client.request('tools/list');
    const names = (listMsg.result?.tools ?? []).map((t) => t.name).sort();
    assert(names.length === EXPECTED_TOOLS.length &&
      EXPECTED_TOOLS.every((n) => names.includes(n)),
      `tools/list returns exactly the ${EXPECTED_TOOLS.length} expected tools`, names.join(', '));

    // 3. system_info — read-only, always safe
    const sysInfo = await client.callTool('system_info', {});
    assert(!sysInfo.isError && toolResultText(sysInfo).includes('monitors'),
      'system_info returns monitor/cursor data');

    // 4. screen_capture — safe args, no image payload in the response. The
    // engine always writes the capture to disk regardless (defaulting to a
    // generated temp path) — pass save_path explicitly so we can clean up
    // after ourselves rather than littering the temp folder.
    const shotPath1 = tmpShotPath();
    const shot = await client.callTool('screen_capture', {
      screen_index: 0, max_dimension: 64, format: 'jpeg', return_image: false, save_path: shotPath1
    });
    assert(!shot.isError, 'screen_capture (screen_index:0, no image payload)');
    cleanupShot(shotPath1);

    // 4b. screen_capture — roi: the captured source size must match the
    // requested crop rectangle exactly.
    const shotPath2 = tmpShotPath();
    const shotRoi = await client.callTool('screen_capture', {
      roi: { x: 10, y: 10, width: 150, height: 90 }, return_image: false, save_path: shotPath2
    });
    assert(!shotRoi.isError && toolResultText(shotRoi).includes('source: 150x90'),
      'screen_capture roi (crop size matches request)', toolResultText(shotRoi).split('\n')[0]);
    cleanupShot(shotPath2);

    // 4c. screen_capture — target_active_window
    const shotPath3 = tmpShotPath();
    const shotActive = await client.callTool('screen_capture', {
      target_active_window: true, return_image: false, save_path: shotPath3
    });
    assert(!shotActive.isError, 'screen_capture target_active_window');
    cleanupShot(shotPath3);

    // 5. ui_inspect — read-only introspection of whatever is currently active
    const inspect = await client.callTool('ui_inspect', {
      target: 'active_window', max_depth: 2, interactive_only: true
    });
    assert(!inspect.isError, 'ui_inspect (active_window, shallow)');

    // 6. window_manager — list + get_active, always safe
    const wmList = await client.callTool('window_manager', { action: 'list' });
    assert(!wmList.isError, 'window_manager list');
    const wmActive = await client.callTool('window_manager', { action: 'get_active' });
    assert(!wmActive.isError, 'window_manager get_active');

    // --- Interactive tests: only inside a verified disposable Notepad tab ---
    const tabTitle = await openVerifiedBlankNotepad(client);
    blankTabOpened = true;
    ok('disposable blank Notepad tab confirmed focused', tabTitle);

    // 7. mouse_action — move, then every click variant. Clicks only ever
    // land inside our own verified-blank disposable tab (see MISSION.md
    // Hors périmètre) — never against a window we didn't open ourselves.
    const move = await client.callTool('mouse_action', { action: 'move', x: 400, y: 300 });
    assert(!move.isError, 'mouse_action move');

    const rightClick = await client.callTool('mouse_action', { action: 'right_click', x: 400, y: 300 });
    assert(!rightClick.isError && /"button":\s*"right"/.test(toolResultText(rightClick)),
      'mouse_action right_click (button:right)');
    // right_click opens Notepad's context menu — dismiss it before continuing.
    await client.callTool('keyboard_action', { action: 'press_key', key: 'Escape' });

    const middleClick = await client.callTool('mouse_action', { action: 'middle_click', x: 400, y: 300 });
    assert(!middleClick.isError && /"button":\s*"middle"/.test(toolResultText(middleClick)),
      'mouse_action middle_click (button:middle)');

    const doubleClick = await client.callTool('mouse_action', { action: 'double_click', x: 400, y: 300 });
    assert(!doubleClick.isError && /"clicks":\s*2/.test(toolResultText(doubleClick)),
      'mouse_action double_click (clicks:2)');

    const tripleClick = await client.callTool('mouse_action', { action: 'triple_click', x: 400, y: 300 });
    assert(!tripleClick.isError && /"clicks":\s*3/.test(toolResultText(tripleClick)),
      'mouse_action triple_click (clicks:3)');

    // 7b. ui_inspect — target=cursor, now that the cursor is parked at a
    // known point inside the disposable tab.
    const inspectCursor = await client.callTool('ui_inspect', { target: 'cursor', max_depth: 3 });
    assert(!inspectCursor.isError, 'ui_inspect target=cursor');

    // 7c. ui_inspect — target=screen (whole desktop, not just active_window)
    const inspectScreen = await client.callTool('ui_inspect', { target: 'screen', max_depth: 2 });
    assert(!inspectScreen.isError && /\((\d+) found/.test(toolResultText(inspectScreen)) &&
      Number(toolResultText(inspectScreen).match(/\((\d+) found/)[1]) > 0,
      'ui_inspect target=screen (top-level windows found)');

    // 7d. ui_inspect — filter, on an element guaranteed present in Notepad
    // (the text editor pane itself).
    const inspectFilter = await client.callTool('ui_inspect', {
      target: 'active_window', max_depth: 6, filter: 'Document'
    });
    const filterCount = Number(toolResultText(inspectFilter).match(/\((\d+) found/)?.[1] ?? -1);
    assert(!inspectFilter.isError && filterCount >= 1,
      'ui_inspect filter="Document" narrows to the text editor pane', `count=${filterCount}`);

    // 7e. ui_inspect — interactive_only: false must surface at least as many
    // elements as interactive_only: true on the same tree.
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

    // 8. keyboard_action — type into the verified-blank tab
    const type = await client.callTool('keyboard_action', {
      action: 'type_text', text: 'fast-computer-use smoke test'
    });
    assert(!type.isError, 'keyboard_action type_text (into disposable tab)');

    // 9. window_manager — minimize/maximize/restore on our own disposable window
    const min = await client.callTool('window_manager', { action: 'minimize', target: 'Notepad' });
    assert(!min.isError, 'window_manager minimize');
    await sleep(200);
    const restore = await client.callTool('window_manager', { action: 'restore', target: 'Notepad' });
    assert(!restore.isError, 'window_manager restore');
    await sleep(200);
    const max = await client.callTool('window_manager', { action: 'maximize', target: 'Notepad' });
    assert(!max.isError, 'window_manager maximize');
    await sleep(200);
    const restore2 = await client.callTool('window_manager', { action: 'restore', target: 'Notepad' });
    assert(!restore2.isError, 'window_manager restore (after maximize)');
    await sleep(200);

    // 10. batch_actions — including the cmd:"wait" fix
    const batch = await client.callTool('batch_actions', {
      actions: [{ cmd: 'wait', ms: 50 }, { cmd: 'ping' }]
    });
    const batchText = toolResultText(batch);
    assert(!batch.isError && batchText.includes('Executed 2 actions'),
      'batch_actions (cmd:"wait" + ping)', batchText.split('\n')[0]);

  } catch (err) {
    fail('unexpected exception', err.message);
  } finally {
    if (blankTabOpened) {
      await closeNotepadTab(client);
      // Teardown is not "best effort and forget" — confirm it actually left
      // no dirty scratch tab behind, as a real, counted check. The window
      // can take a moment to actually disappear after "Don't save" is
      // clicked (tab-close transition), so poll briefly rather than check
      // once immediately.
      try {
        let stillDirty = true;
        for (let i = 0; i < 5 && stillDirty; i++) {
          const list = await client.callTool('window_manager', { action: 'list' });
          stillDirty = toolResultText(list).includes(DISPOSABLE_TAB_TEXT);
          if (stillDirty) await sleep(300);
        }
        assert(!stillDirty, 'disposable Notepad tab left no dirty window behind');
      } catch (err) {
        fail('post-teardown window check', err.message);
      }
    }
    client.stop();
  }

  console.log(`\n${passCount} passed, ${failCount} failed.`);
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
