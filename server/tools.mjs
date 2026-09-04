export const TOOL_DEFINITIONS = [
  {
    name: 'screen_capture',
    description: 'Capture a high-speed screenshot of a monitor, active window, or region of interest (ROI). Supports downscaling (max_dimension) to drastically reduce visual tokens while maintaining full legibility, and configurable JPEG/PNG compression.',
    inputSchema: {
      type: 'object',
      properties: {
        screen_index: {
          type: 'integer',
          description: 'Display monitor index (0 for primary, 1, 2... or -1 for combined virtual desktop canvas across all screens).'
        },
        target_active_window: {
          type: 'boolean',
          description: 'If true, automatically captures and crops directly to the active foreground window.'
        },
        roi: {
          type: 'object',
          description: 'Region of Interest (crop rectangle) in screen coordinates.',
          properties: {
            x: { type: 'integer' },
            y: { type: 'integer' },
            width: { type: 'integer' },
            height: { type: 'integer' }
          },
          required: ['x', 'y', 'width', 'height']
        },
        max_dimension: {
          type: 'integer',
          description: 'Maximum width or height in pixels (e.g. 1024 or 1280). Scales image proportionally to reduce token consumption by up to 75% while maintaining sharpness.'
        },
        format: {
          type: 'string',
          enum: ['jpeg', 'png'],
          description: 'Image format (default: jpeg for compact size).'
        },
        quality: {
          type: 'integer',
          description: 'JPEG compression quality 1-100 (default: 80).'
        },
        save_path: {
          type: 'string',
          description: 'Optional file path on disk to save the image.'
        },
        return_image: {
          type: 'boolean',
          description: 'Whether to return the base64 image data directly in the response (default: true).'
        }
      }
    }
  },
  {
    name: 'ui_inspect',
    description: 'Inspect the Windows UI Automation (UIA) accessibility tree to discover interactive buttons, input fields, tabs, menus, and controls with exact pre-calculated center coordinates (x, y) for instant, accurate clicking without coordinate guessing.',
    inputSchema: {
      type: 'object',
      properties: {
        target: {
          type: 'string',
          enum: ['active_window', 'cursor', 'screen'],
          description: 'Scope to inspect (default: active_window).'
        },
        max_depth: {
          type: 'integer',
          description: 'Maximum hierarchy depth to traverse (default: 7).'
        },
        filter: {
          type: 'string',
          description: 'Optional search filter by control name, type, or automation ID.'
        },
        interactive_only: {
          type: 'boolean',
          description: 'Filter only clickable/focusable interactive elements (default: true).'
        }
      }
    }
  },
  {
    name: 'mouse_action',
    description: 'Execute native Win32 hardware-accurate mouse input (click, double click, right click, middle click, move, drag-and-drop, scroll).',
    inputSchema: {
      type: 'object',
      properties: {
        action: {
          type: 'string',
          enum: ['click', 'double_click', 'triple_click', 'right_click', 'middle_click', 'move', 'mouse_down', 'mouse_up', 'drag', 'scroll'],
          description: 'Mouse action to execute.'
        },
        x: {
          type: 'integer',
          description: 'Target X screen coordinate.'
        },
        y: {
          type: 'integer',
          description: 'Target Y screen coordinate.'
        },
        button: {
          type: 'string',
          enum: ['left', 'right', 'middle'],
          description: 'Mouse button (default: left).'
        },
        scroll_amount: {
          type: 'integer',
          description: 'Scroll amount for "scroll" (positive = up, negative = down. Default 120 or -120).'
        },
        to_x: {
          type: 'integer',
          description: 'Destination X coordinate for "drag".'
        },
        to_y: {
          type: 'integer',
          description: 'Destination Y coordinate for "drag".'
        },
        smooth: {
          type: 'boolean',
          description: 'Interpolate smooth movement trajectory.'
        }
      },
      required: ['action']
    }
  },
  {
    name: 'keyboard_action',
    description: 'Execute native Win32 hardware keyboard input: fast typing, instant clipboard pasting (injects multiline text/code in <10ms), hotkeys (ctrl+c, ctrl+v, win+r, alt+tab), or individual key presses.',
    inputSchema: {
      type: 'object',
      properties: {
        action: {
          type: 'string',
          enum: ['type_text', 'paste_text', 'hotkey', 'press_key', 'key_down', 'key_up'],
          description: 'Keyboard action to execute.'
        },
        text: {
          type: 'string',
          description: 'Text string for "type_text" or "paste_text".'
        },
        hotkey: {
          type: 'string',
          description: 'Hotkey combination (e.g. "ctrl+c", "ctrl+v", "ctrl+a", "win+r", "alt+tab", "ctrl+shift+esc", "enter", "escape", "tab", "backspace").'
        },
        key: {
          type: 'string',
          description: 'Single key name for "press_key", "key_down", or "key_up".'
        },
        delay_ms: {
          type: 'integer',
          description: 'Inter-keystroke delay for "type_text" in ms (default: 5).'
        }
      },
      required: ['action']
    }
  },
  {
    name: 'batch_actions',
    description: 'Execute a sequence of multiple automation actions (focus, click, paste, hotkey, wait, screenshot) in a single atomic native call without roundtrip latency.',
    inputSchema: {
      type: 'object',
      properties: {
        actions: {
          type: 'array',
          description: 'Ordered list of action objects to execute consecutively.',
          items: {
            type: 'object'
          }
        }
      },
      required: ['actions']
    }
  },
  {
    name: 'window_manager',
    description: 'List, focus, minimize, maximize, restore, move/resize, or close Windows applications.',
    inputSchema: {
      type: 'object',
      properties: {
        action: {
          type: 'string',
          enum: ['list', 'get_active', 'focus', 'minimize', 'maximize', 'restore', 'close', 'set_pos'],
          description: 'Window management action.'
        },
        target: {
          type: 'string',
          description: 'Window title substring, process name, or HWND handle.'
        },
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
    description: 'Get display monitor configurations, virtual desktop bounds, current cursor coordinates, and active foreground window info.',
    inputSchema: {
      type: 'object',
      properties: {}
    }
  }
];

export async function handleToolCall(name, args, engineClient) {
  try {
    switch (name) {
      case 'screen_capture': {
        const payload = {
          cmd: 'screenshot',
          screenIndex: args.screen_index !== undefined ? args.screen_index : 0,
          targetActiveWindow: Boolean(args.target_active_window),
          maxDimension: args.max_dimension || 1280,
          format: args.format || 'jpeg',
          quality: args.quality || 80,
          returnBase64: args.return_image !== false,
          savePath: args.save_path || ''
        };

        if (args.roi) {
          payload.roi = args.roi;
        }

        const res = await engineClient.send(payload);

        if (res.status === 'error') {
          return {
            content: [{ type: 'text', text: `Screenshot error: ${res.message}` }],
            isError: true
          };
        }

        const content = [];
        const metaText = `Screenshot captured: ${res.width}x${res.height} (source: ${res.sourceRect.width}x${res.sourceRect.height}) in ${res.elapsedMs}ms${res.path ? ` | Saved to: ${res.path}` : ''}`;
        content.push({ type: 'text', text: metaText });

        if (res.base64 && args.return_image !== false) {
          const mimeType = (args.format === 'png') ? 'image/png' : 'image/jpeg';
          content.push({
            type: 'image',
            data: res.base64,
            mimeType
          });
        }

        return { content, isError: false };
      }

      case 'ui_inspect': {
        const payload = {
          cmd: 'ui_inspect',
          target: args.target || 'active_window',
          maxDepth: args.max_depth || 7,
          filter: args.filter || '',
          interactiveOnly: args.interactive_only !== false
        };

        const res = await engineClient.send(payload);

        if (res.status === 'error') {
          return {
            content: [{ type: 'text', text: `UI inspect error: ${res.message}` }],
            isError: true
          };
        }

        let output = `UI Elements (${res.count} found in ${res.elapsedMs}ms):\n`;
        if (res.elements && res.elements.length > 0) {
          res.elements.forEach((el, idx) => {
            const idStr = el.automationId ? ` [id=${el.automationId}]` : '';
            output += `${idx + 1}. [${el.type}] "${el.name}"${idStr} -> Center: (${el.center.x}, ${el.center.y}) | Bounds: [${el.rect.x}, ${el.rect.y}, ${el.rect.width}x${el.rect.height}]\n`;
          });
        } else {
          output += `No matching interactive elements found.`;
        }

        return {
          content: [{ type: 'text', text: output }],
          isError: false
        };
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

        const res = await engineClient.send(payload);
        return {
          content: [{ type: 'text', text: JSON.stringify(res, null, 2) }],
          isError: res.status === 'error'
        };
      }

      case 'keyboard_action': {
        const payload = {
          cmd: 'keyboard',
          action: args.action,
          text: args.text || '',
          hotkey: args.hotkey || '',
          key: args.key || '',
          delayMs: args.delay_ms || 5
        };

        const res = await engineClient.send(payload);
        return {
          content: [{ type: 'text', text: JSON.stringify(res, null, 2) }],
          isError: res.status === 'error'
        };
      }

      case 'batch_actions': {
        const payload = {
          cmd: 'batch',
          actions: args.actions || []
        };

        const res = await engineClient.send(payload);

        const content = [];
        let summary = `Executed ${res.count || 0} actions in ${res.elapsedMs || 0}ms:\n`;
        if (res.results) {
          res.results.forEach((r, idx) => {
            summary += `Step ${idx + 1}: ${r.status === 'ok' ? 'OK' : 'FAIL'} (${JSON.stringify(r)})\n`;
          });
        }
        content.push({ type: 'text', text: summary });

        // If any step was a screenshot with base64, also attach it
        if (res.results) {
          for (const step of res.results) {
            if (step.base64) {
              content.push({
                type: 'image',
                data: step.base64,
                mimeType: step.format === 'png' ? 'image/png' : 'image/jpeg'
              });
            }
          }
        }

        return {
          content,
          isError: res.status === 'error'
        };
      }

      case 'window_manager': {
        const act = args.action;
        let payload;

        if (act === 'list') {
          payload = { cmd: 'window_list' };
        } else if (act === 'get_active') {
          payload = { cmd: 'info' };
          const infoRes = await engineClient.send(payload);
          return {
            content: [{ type: 'text', text: JSON.stringify(infoRes.activeWindow, null, 2) }],
            isError: false
          };
        } else if (act === 'focus') {
          payload = { cmd: 'window_focus', target: args.target, action: 'focus' };
        } else if (act === 'minimize' || act === 'maximize' || act === 'restore') {
          payload = { cmd: 'window_state', target: args.target, state: act };
        } else if (act === 'close') {
          payload = { cmd: 'window_close', target: args.target };
        } else if (act === 'set_pos') {
          payload = { cmd: 'window_pos', target: args.target, x: args.x, y: args.y, width: args.width, height: args.height };
        } else {
          return {
            content: [{ type: 'text', text: `Unknown window_manager action: ${act}` }],
            isError: true
          };
        }

        const res = await engineClient.send(payload);
        return {
          content: [{ type: 'text', text: JSON.stringify(res, null, 2) }],
          isError: res.status === 'error'
        };
      }

      case 'system_info': {
        const res = await engineClient.send({ cmd: 'info' });
        return {
          content: [{ type: 'text', text: JSON.stringify(res, null, 2) }],
          isError: res.status === 'error'
        };
      }

      default:
        return {
          content: [{ type: 'text', text: `Unknown tool: ${name}` }],
          isError: true
        };
    }
  } catch (err) {
    return {
      content: [{ type: 'text', text: `Error executing ${name}: ${err.message}` }],
      isError: true
    };
  }
}
