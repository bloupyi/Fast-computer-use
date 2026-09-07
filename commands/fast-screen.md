---
description: Capture a high-speed screenshot with optional downscaling and ROI
argument-hint: [--panorama] [--screen N] [--max-dim N] [--active] [--roi x,y,w,h]
---

Capture a screenshot using the native FastEngine with sub-40ms performance.

## Arguments

Parse `$ARGUMENTS` for:
- `--panorama`: Stitch every monitor into one labelled image, each with its own resolution budget, plus the map to convert a panorama point back to desktop coordinates. This is the multi-monitor option; prefer it over `--screen -1`, which squashes the whole desktop into a single strip.
- `--screens a,b`: Panorama only, restrict to these monitor indices
- `--screen N`: Monitor index (0 = primary, -1 = raw virtual desktop bounding box)
- `--max-dim N`: Downscale to maximum dimension (e.g., 1024, 1280) to reduce visual tokens by up to 75%
- `--active`: Capture only the active foreground window
- `--roi x,y,w,h`: Region of Interest crop (comma-separated: x,y,width,height)
- `--format jpeg|png`: Image format (default: jpeg)
- `--quality N`: JPEG quality 1-100 (default: 80)
- `--save PATH`: Save to disk at this path

## Execution

Call the `screen_capture` MCP tool with the parsed parameters (`--panorama` maps to `panorama: true`).

Display the result metadata and embed the returned image inline in the response.
For a panorama, also surface the per-screen map, since a point read off the
panorama must be converted before it can be clicked:

```
desktop_x = screen.bounds.x + (pano_x - screen.dest.x) / screen.scale
```

If the question is "is this control there" or "what does this field say" rather
than an actual visual one, `ui_inspect` and `read_text` answer in text for a
fraction of the tokens.

## Example Usage

```
/fast-screen
/fast-screen --panorama
/fast-screen --screen 1 --max-dim 1024
/fast-screen --active --max-dim 1280
/fast-screen --roi 0,0,800,600 --format png
```
