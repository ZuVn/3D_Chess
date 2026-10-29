# Performance

Scope: frame budget, measured results, quality presets, main-thread and allocation rules, how to measure.
Source: `App.ApplyQuality` (`Assets/Scripts/Core/App.cs`), `Assets/Scripts/Core/Benchmark.cs`, URP assets in
`Assets/Settings/`, `Hud` FPS counter.
Status: target **met on the RTX 3050, not met stably on the Intel UHD GT1 iGPU** (measurements below).

## Target

60 FPS = 16.67 ms per frame at 1920×1080 during normal play, including while the AI searches.

## Reference hardware (measured 2026-10-29)

Intel Core i5-11400H (6C/12T), 15 GB RAM, NVIDIA GeForce RTX 3050 Laptop 4 GB + Intel UHD Graphics (Tiger Lake GT1),
Ubuntu 26.04 / Linux 7.0, Wayland (XWayland). Window 1920×1080 requested (1920×1012 client area).

| GPU / API | Preset | VSync | Avg FPS | Median | p99 | 1% low |
|---|---|---|---|---|---|---|
| RTX 3050, Vulkan (default on this laptop) | Medium | on* | 125 | 7.2 ms | 15.2 ms | 51 |
| RTX 3050, Vulkan, vs Stockfish 1 s/move | Medium | on* | 132 | 7.0 ms | 16.3 ms | 45 |
| RTX 3050, OpenGL (PRIME offload) | High | off, uncapped | 200 | 3.9 ms | 11.4 ms | 76 |
| Intel GT1, OpenGL | Low (0.75 render scale) | on | 61 | 16.2 ms | 32.3 ms | 25 |
| Intel GT1, OpenGL | Medium | on | 29 | 34.4 ms | 50.3 ms | 19 |

\* Under PRIME offload the driver ignored the swap interval, so VSync did not cap the rate.
Managed heap 34–39 MB over a 60 s run with 2–3 full games; 4–7 gen-0 GCs per minute. Rare single-frame spikes
(≤ 5 frames > 33 ms per minute) coincided with game restarts; the benchmark's scripted "human" also searches on
the main thread (depth 1, ≤ 50 ms), which is benchmark overhead, not gameplay code.
These numbers are from the Linux build. Windows performance is **unmeasured** (only a Wine smoke test).

## Quality presets (`App.ApplyQuality`, runtime copy of the URP asset)

| | Low | Medium (default) | High |
|---|---|---|---|
| Render scale | 0.75 | 1 | 1 |
| MSAA | off | 4× | 8× |
| Shadows | off | soft, 2048, 1 cascade, 32 m | soft, 4096, 2 cascades |
| HDR / post (tonemap, bloom, vignette) | off | on | on |
| SSAO renderer feature | off | off | on |

First run chooses Low when `SystemInfo.graphicsMemorySize < 2048`. Linux graphics APIs: Vulkan first (selects the
discrete GPU on this hybrid laptop), OpenGLCore fallback (`-force-glcore`). Frame cap: VSync, or VSync off + limit
30–240/unlimited in Settings.

## Rules for code on the main thread

- AI search and Stockfish I/O run on worker tasks; `Update` only polls (see [ai_engine.md](ai_engine.md)).
- No `Find*`/`GetComponent` searches per frame; references are created and cached in `App.Awake`.
- HUD text is set only when the displayed value changes (clock cache key, 10 Hz thinking label, 0.5 s FPS label);
  `Hud.Refresh` runs on events, not per frame. Highlights rebuild on change.
- Search code is allocation-free per node (`stackalloc` in the evaluator, preallocated move buffers).
- Shared meshes/materials, two merged square meshes, pooled piece and marker objects, one shadow-casting light,
  reflection probe rendered only on theme change.

## How to measure

```bash
cd Builds/Linux
./RoyalGambit.x86_64 -screen-width 1920 -screen-height 1080 -screen-fullscreen 0 \
  -benchmark 60 -benchmark-out bench.txt [-quality 0|1|2] [-vsync 0|1] [-fpslimit N] [-difficulty 0|1|2] [-force-glcore]
```
Plays vs the AI with the camera orbiting and 2D/3D toggling every 8 s; writes GPU/CPU, avg/median/p95/p99/max,
1% low, frames over 16.7/33.3 ms, managed heap and GC counts. Overrides are not persisted. For hybrid laptops
force the NVIDIA GPU on OpenGL with `__NV_PRIME_RENDER_OFFLOAD=1 __GLX_VENDOR_LIBRARY_NAME=nvidia`.
For per-system CPU/GPU breakdown use a Development Build with the Unity Profiler (not done yet; pending).
In-game: Settings → Show FPS counter.

Record new results here with date, hardware, API, preset and the raw benchmark file values.

Related: [build_and_release.md](build_and_release.md), [testing.md](testing.md).
