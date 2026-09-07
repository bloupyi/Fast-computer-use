#!/usr/bin/env node
import { createInterface } from 'node:readline';
import { FastEngineClient } from './engine-process.mjs';
import { TOOL_DEFINITIONS, handleToolCall } from './tools.mjs';

const engineClient = new FastEngineClient();

function sendResponse(response) {
  const jsonStr = JSON.stringify(response);
  process.stdout.write(jsonStr + '\n');
}

function sendError(id, code, message, data = null) {
  sendResponse({
    jsonrpc: '2.0',
    id,
    error: {
      code,
      message,
      ...(data ? { data } : {})
    }
  });
}

async function handleMessage(message) {
  if (!message || typeof message !== 'object') return;

  const { id, method, params } = message;

  // Notification (no ID)
  if (id === undefined || id === null) {
    if (method === 'notifications/initialized') {
      // Client ready notification
    }
    return;
  }

  try {
    switch (method) {
      case 'initialize': {
        // Pre-warm the native FastEngine daemon in the background
        engineClient.start().catch((err) => {
          console.error('[FastEngine Init Error]', err.message);
        });

        sendResponse({
          jsonrpc: '2.0',
          id,
          result: {
            protocolVersion: '2024-11-05',
            capabilities: {
              tools: {}
            },
            serverInfo: {
              name: 'fast-computer-use',
              version: '1.1.1'
            }
          }
        });
        break;
      }

      case 'ping': {
        sendResponse({
          jsonrpc: '2.0',
          id,
          result: {}
        });
        break;
      }

      case 'tools/list': {
        sendResponse({
          jsonrpc: '2.0',
          id,
          result: {
            tools: TOOL_DEFINITIONS
          }
        });
        break;
      }

      case 'tools/call': {
        const toolName = params?.name;
        const toolArgs = params?.arguments || {};

        const toolResult = await handleToolCall(toolName, toolArgs, engineClient);

        sendResponse({
          jsonrpc: '2.0',
          id,
          result: toolResult
        });
        break;
      }

      default: {
        sendError(id, -32601, `Method not found: ${method}`);
        break;
      }
    }
  } catch (err) {
    sendError(id, -32603, `Internal error: ${err.message}`);
  }
}

async function main() {
  const rl = createInterface({
    input: process.stdin,
    output: process.stdout,
    terminal: false
  });

  rl.on('line', (line) => {
    const trimmed = line.trim();
    if (!trimmed) return;

    try {
      const message = JSON.parse(trimmed);
      handleMessage(message);
    } catch (err) {
      console.error('[MCP JSON Parse Error]', err.message, 'Line:', trimmed);
    }
  });

  process.on('SIGINT', async () => {
    await engineClient.stop();
    process.exit(0);
  });

  process.on('SIGTERM', async () => {
    await engineClient.stop();
    process.exit(0);
  });
}

main().catch((err) => {
  console.error('[MCP Fatal Error]', err);
  process.exit(1);
});
