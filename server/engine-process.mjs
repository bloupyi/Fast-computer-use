import { spawn } from 'node:child_process';
import { createInterface } from 'node:readline';
import { existsSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = dirname(__filename);

export class FastEngineClient {
  constructor(enginePath = null) {
    this.enginePath = enginePath || process.env.ENGINE_PATH || resolve(__dirname, '../bin/FastEngine.exe');
    this.process = null;
    this.readline = null;
    this.queue = [];
    this.currentTask = null;
    this.ready = false;
    this.initPromise = null;
    this.restarting = false;
    // The engine answers one line per command, in order. A timed-out command's reply
    // is still coming: count it so it gets dropped instead of served to the next caller.
    this.orphanResponses = 0;
  }

  async start() {
    if (this.initPromise) return this.initPromise;
    if (this.ready && this.process && !this.process.killed) return true;

    this.initPromise = new Promise((resolveInit, rejectInit) => {
      if (!existsSync(this.enginePath)) {
        return rejectInit(new Error(`FastEngine binary not found at: ${this.enginePath}`));
      }

      try {
        this.process = spawn(this.enginePath, ['--daemon'], {
          stdio: ['pipe', 'pipe', 'pipe'],
          windowsHide: true
        });

        this.readline = createInterface({
          input: this.process.stdout,
          crlfDelay: Infinity
        });

        let isFirstLine = true;

        this.readline.on('line', (line) => {
          const trimmed = line.trim();
          if (!trimmed) return;

          if (isFirstLine) {
            isFirstLine = false;
            try {
              const initData = JSON.parse(trimmed);
              if (initData.ready) {
                this.ready = true;
                resolveInit(true);
                return;
              }
            } catch (err) {
              // Ignore if not json
            }
          }

          if (this.orphanResponses > 0) {
            this.orphanResponses--;
            return;
          }

          if (this.currentTask) {
            const task = this.currentTask;
            this.currentTask = null;
            clearTimeout(task.timeout);
            try {
              const parsed = JSON.parse(trimmed);
              task.resolve(parsed);
            } catch (e) {
              task.resolve({ status: 'error', message: `Invalid JSON from engine: ${trimmed}` });
            }
            this._processNext();
          }
        });

        this.process.stderr.on('data', (data) => {
          const msg = data.toString().trim();
          if (msg) console.error(`[FastEngine STDERR] ${msg}`);
        });

        this.process.on('error', (err) => {
          console.error(`[FastEngine Error] ${err.message}`);
          if (!this.ready) rejectInit(err);
          this._handleProcessExit();
        });

        this.process.on('close', (code) => {
          this._handleProcessExit();
        });

      } catch (err) {
        rejectInit(err);
      }
    });

    return this.initPromise;
  }

  _handleProcessExit() {
    this.ready = false;
    this.initPromise = null;
    this.process = null;
    this.readline = null;

    if (this.currentTask) {
      clearTimeout(this.currentTask.timeout);
      this.currentTask.reject(new Error('FastEngine process terminated unexpectedly'));
      this.currentTask = null;
    }

    while (this.queue.length > 0) {
      const task = this.queue.shift();
      clearTimeout(task.timeout);
      task.reject(new Error('FastEngine process terminated unexpectedly'));
    }

    this.orphanResponses = 0;
  }

  async send(command, timeoutMs = 15000) {
    await this.start();

    return new Promise((resolveTask, rejectTask) => {
      const timeout = setTimeout(() => {
        if (this.currentTask && this.currentTask.timeout === timeout) {
          this.currentTask = null;
          this.orphanResponses++;
          rejectTask(new Error(`Command timed out after ${timeoutMs}ms: ${JSON.stringify(command)}`));
          this._processNext();
        }
      }, timeoutMs);

      const task = {
        command,
        resolve: resolveTask,
        reject: rejectTask,
        timeout
      };

      this.queue.push(task);
      this._processNext();
    });
  }

  _processNext() {
    if (this.currentTask || this.queue.length === 0 || !this.process || this.process.killed) {
      return;
    }

    this.currentTask = this.queue.shift();
    try {
      const jsonStr = JSON.stringify(this.currentTask.command);
      this.process.stdin.write(jsonStr + '\n');
    } catch (err) {
      clearTimeout(this.currentTask.timeout);
      this.currentTask.reject(err);
      this.currentTask = null;
      this._processNext();
    }
  }

  async stop() {
    if (!this.process || this.process.killed) return;
    try {
      this.process.stdin.write('exit\n');
      await new Promise(r => setTimeout(r, 100));
      this.process.kill();
    } catch {}
    this._handleProcessExit();
  }
}
