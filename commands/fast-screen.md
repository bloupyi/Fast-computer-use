---
description: Capture a high-speed screenshot with optional downscaling and ROI
argument-hint: [--screen N] [--max-dim N] [--active] [--roi x,y,w,h]
---

Capture a screenshot using the native FastEngine with sub-40ms performance.

## Arguments

Parse `$ARGUMENTS` for:
- `--screen N`: Monitor index (0 = primary, -1 = all displays virtual desktop)
- `--max-dim N`: Downscale to maximum dimension (e.g., 1024, 1280) to reduce visual tokens by up to 75%
- `--active`: Capture only the active foreground window
- `--roi x,y,w,h`: Region of Interest crop (comma-separated: x,y,width,height)
- `--format jpeg|png`: Image format (default: jpeg)
- `--quality N`: JPEG quality 1-100 (default: 80)
- `--save PATH`: Save to disk at this path

## Execution

Call the `screen_capture` MCP tool with appropriate parameters based on the parsed arguments.

Display the result metadata and embed the returned image inline in the response.

## Example Usage

```
/fast-screen
/fast-screen --screen 1 --max-dim 1024
/fast-screen --active --max-dim 1280
/fast-screen --roi 0,0,800,600 --format png
```
