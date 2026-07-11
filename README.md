# TaoTie RP

A custom Unity Scriptable Render Pipeline (SRP) built on the Render Graph API, featuring Forward and Deferred rendering paths, Forward+ tile-based light culling with bitmask + ZBin depth culling, cascaded shadow maps, light cookies, and a modular post-processing stack.

## Requirements

- Unity 2022.3.x or later
- Render Pipelines Core 14.0.10+
- Unity Mathematics 1.2.6+
- Burst 1.8.18+

## License

[MIT](LICENSE)

---

## Features

### Rendering Paths

| Setting | Condition | Native | WebGL2 | WebGL1 |
|---------|-----------|:------:|:------:|:------:|
| **Forward** | Default; always available | ✅ | ✅ | ✅ |
| **Deferred** | Requires MRT (supportedRenderTargetCount ≥ 3); not a reflection camera; forces MSAA off. Shader uses `#pragma exclude_renderers gles`. | ✅ | ❌ (forced Forward) | ❌ (forced Forward) |
| **Forward+** | Enabled when `forwardPlus != Off` and graphics API is not OpenGLES2. Auto mode activates when visible other lights exceed 16 (hysteresis: disables below 8). Uses ComputeBuffer on native, Texture2D fallback on WebGL2. Stripped on WebGL1 builds. | ✅ | ✅ (Texture2D fallback) | ❌ |

> When Deferred is selected but the platform doesn't support it (all WebGL runtimes, or insufficient MRT on native), the pipeline automatically falls back to Forward rendering. In the Editor, Deferred is available on all platforms for testing purposes.

#### Opaque Queue

| Path | Forward+ | Shader LightMode | Lighting Method | Native | WebGL2 | WebGL1 |
|------|----------|-----------------|-----------------|:------:|:------:|:------:|
| Forward | Off | `CustomLit` | Per-pixel, up to `maxOtherLights` (default 16, max 64) other lights | ✅ | ✅ | ✅ (capped at 8) |
| Forward | On | `CustomLit` + `_TAOTIE_FORWARD_PLUS` | Per-pixel, bitmask + ZBin tile-culled, up to 256 other lights | ✅ (ComputeBuffer) | ✅ (Texture2D fallback) | ❌ (FP disabled → Off) |
| Deferred | Off | `DeferredGBuffer` | GBuffer MRT → fullscreen `DeferredLightingPass` (up to 256 lights) | ✅ | ❌ (no deferred) | ❌ |
| Deferred | On | `DeferredGBuffer` | GBuffer MRT → fullscreen `DeferredLightingPass` with bitmask + ZBin tile culling (up to 256 lights) | ✅ | ❌ | ❌ |

> In Deferred path, opaque geometry writes to GBuffer textures via `DeferredGBuffer` shader pass. Lighting is computed in a separate fullscreen `DeferredLightingPass`. When Forward+ is enabled, the `DeferredLightingPass` also uses bitmask + ZBin tile-culled light iteration via `LIGHT_LOOP_BEGIN`/`LIGHT_LOOP_END` macros — the same tile data computed by `ForwardPlusCullPass` is reused.

#### Transparent Queue

| Path | Forward+ | Shader LightMode | Lighting Method | Native | WebGL2 | WebGL1 |
|------|----------|-----------------|-----------------|:------:|:------:|:------:|
| Forward | Off | `CustomLit` | Per-pixel, up to `maxOtherLights` (default 16, max 64) | ✅ | ✅ | ✅ (capped at 8) |
| Forward | On | `CustomLit` + `_TAOTIE_FORWARD_PLUS` | Per-pixel, bitmask + ZBin tile-culled, up to 256 | ✅ | ✅ | ❌ (falls back to Off) |
| Deferred | Off | `CustomLit` | Per-pixel, up to `maxOtherLights` (default 16, max 64) | ✅ | ❌ | ❌ |
| Deferred | On | `CustomLit` + `_TAOTIE_FORWARD_PLUS` | Per-pixel, tile-culled, up to 256 | ✅ | ❌ | ❌ |

> Transparent objects are always rendered with the forward path (`CustomLit` shader tag), regardless of whether the pipeline is set to Forward or Deferred. When `forwardPlus` is not `Off`, transparent objects also benefit from Forward+ bitmask + ZBin tile-based light culling. On WebGL1 (GLES2), Forward+ is disabled and the maximum other light count is capped at 8 due to CBUFFER/array size limitations.

#### Full Pass Sequences

**Forward path:**
```
LightingPass → SetupPass → [DepthPrePass] → [ForwardPlusCullPass] → GeometryPass(opaque, CustomLit) → SkyboxPass
→ [ResolvePass(MSAA)] → CopyAttachmentsPass → [SSAOPass] → GeometryPass(transparent, CustomLit)
→ UnsupportedShadersPass → [ResolvePass] → [TAAResolvePass] → LensFlarePass
→ PostFXPass / FinalPass → DepthDebuggerPass → ForwardPlusDebuggerPass → GizmosPass
```

**Deferred path:**
```
LightingPass → SetupPass → [DepthPrePass] → [ForwardPlusCullPass] → GBufferPass(opaque, DeferredGBuffer) → DeferredLightingPass
→ SkyboxPass → CopyAttachmentsPass → [SSAOPass] → GeometryPass(transparent, CustomLit)
→ UnsupportedShadersPass → [TAAResolvePass] → LensFlarePass
→ PostFXPass / FinalPass → DepthDebuggerPass → ForwardPlusDebuggerPass → GizmosPass
```

> `[...]` = optional, depends on settings. DepthPrePass runs before both Forward and Deferred paths when `depthPrimingMode` is Forced, or in Forward when MSAA + Copy Depth is enabled (Auto mode). In Deferred, `depthPrimingMode = Auto` never triggers DepthPrePass (MSAA is always off). ForwardPlusCullPass runs when Forward+ is active, after DepthPrePass (if any) and before geometry/GBuffer rendering. 2.5D depth culling only activates when DepthPrePass is running. SSAOPass runs when SSAO is enabled and depth texture is available. TAAResolvePass runs when TAA is enabled. PostFXPass runs when active post-processing effects exist; otherwise FinalPass blits directly.

> **Reflection cameras** use a simplified path: `LightingPass → SetupPass → [DepthPrePass] → [ForwardPlusCullPass] → GeometryPass(opaque) → [CopyAttachmentsPass] → FinalPass`. They skip deferred, skybox, SSAO, TAA, and post-processing entirely.

### Forward+ Tile-Based Light Culling

- **Bitmask tile data** — Each tile stores a fixed-size uint32 bitmask (8 words for 256 lights). On SM5.0+ platforms, `firstbitlow` iterates only set bits; GLES3/WebGL2 uses 32-bit for-loop fallback
- **ZBin depth culling** — Lights are binned by camera-space depth into `zBinCount` (default 32, configurable 8–64) depth slices. In the pixel shader, the tile bitmask is ANDed with the ZBin bitmask for the current pixel's depth, reducing per-pixel light iterations
- **2.5D tile depth culling** — When DepthPrePass is active (Forced mode, or in Forward path when SSAO/TAA/MSAA depth priming is active), the compute shader samples the depth texture at each tile's center and skips lights whose Z range doesn't overlap the tile's depth. When DepthPrePass is not running, Forward+ falls back to pure 2D tile culling (ZBin still applies in pixel shader)
- **Light priority selection** — When visible lights exceed the platform limit (8 on GLES2, 256 on other platforms), lights are scored by `brightness × screenArea / distSqr` and the top `max` are selected via partial selection sort. Static arrays avoid per-frame allocation
- **Hysteresis threshold** — Auto mode enables Forward+ when lights > 16, disables when lights < 8, preventing variant thrashing near the threshold
- **GPU compute culling** — ComputeShader (`ForwardPlusCulling.compute`) with `groupshared` memory for collaborative light bounds loading, `[numthreads(8,8,1)]` dispatch. Burst-compiled CPU Job (`TileCullJob.cs`) fallback for WebGL/GLES3
- **Deferred integration** — When Forward+ is enabled, the Deferred lighting pass also uses bitmask + ZBin tile-culled light iteration via `LIGHT_LOOP_BEGIN`/`LIGHT_LOOP_END` macros
- `LIGHT_LOOP_BEGIN` / `LIGHT_LOOP_END` macros abstract the Other Light iteration so `GetLighting()` stays clean across Forward+, GLES2, and plain Forward paths
- Supports up to 4 directional lights and 256 point/spot lights (8 on GLES2)
- ComputeBuffer light data with Texture2D fallback for WebGL

### Shadows

- Directional cascaded shadows (1–4 cascades, cascade fade, soft blend)
- Spot light shadows
- Point light 6-face cube shadows
- Shadowmask support
- 3 shadow filter quality levels (Low / Medium / High)
- Configurable shadow atlas resolution (256–8192)
- Configurable shadow max distance and distance fade
- **Forward+ settings**: `maxLightsPerTile`, `tileSize` (8–256px, adaptive), `zBinCount` (8–64 depth bins)

### SSAO (Screen Space Ambient Occlusion)

- Alchemy/Horizon-based AO algorithm with depth-reconstructed normals
- 3 quality presets: Low (4 samples), Medium (8 samples), High (12 samples)
- Bilateral blur (horizontal + vertical, 5-tap each)
- Configurable radius, intensity, distance falloff, and resolution downsample (0.25×–1×)
- Applied to ambient/indirect lighting only
- Works in both Forward and Deferred paths
- `_SSAO_ENABLED` shader keyword toggled at runtime; variants stripped at build time when SSAO is disabled

### Post-Processing

The post-processing stack uses a modular effect architecture with **Unity Volume system integration**. All post-processing parameters are controlled by `VolumeComponent` overrides in the scene.

#### Volume System

Each post-processing effect has a corresponding `VolumeComponent` subclass in `Runtime/PostFX/Volume/`. These appear in the Volume Profile Inspector under **Add Override → TaoTie RP**. At runtime, effects read their parameters from the active `VolumeStack` via `PostFXStack.GetActiveVolume<T>()`.

**Per-camera Volume filtering** is supported via the `volumeLayerMask` field on `CameraSettings` (on the `TaoTieRenderPipelineCamera` component).

**`PostFXSettings`** (ScriptableObject) manages:
- The effects list (which effects are enabled/disabled, execution order)
- Shader references (shared PostFXStack shader + per-effect dedicated shaders)
- Shader stripping information

#### Built-in Effects

| Effect | Description | Shader |
|--------|-------------|--------|
| **Bloom** | Pyramid down/up-sampling, scatter/additive mode, firefly filtering, bicubic upsampling | PostFXStack.shader |
| **Color Grading** | Color LUT (16/32/64), color adjustments, white balance, split toning, channel mixer, SMH, tone mapping (ACES, Neutral, Reinhard) | PostFXStack.shader |
| **Depth Of Field** | CoC-based depth blur, 13-tap Poisson disc, foreground/background blur | DepthOfField.shader |
| **Outline** | Roberts Cross depth + optional G-Buffer normal edge detection | Outline.shader |
| **Volumetric Fog** | Raymarched volumetric fog with exponential extinction, Mie scattering | VolumetricFog.shader |
| **Motion Blur** | Camera-motion-based blur via depth reconstruction + previous VP matrix | MotionBlur.shader |
| **Vignette** | Radial darkening toward screen edges | Vignette.shader |
| **Chromatic Aberration** | Radial RGB channel offset | ChromaticAberration.shader |
| **Film Grain** | Animated procedural noise, luma-weighted response | FilmGrain.shader |
| **Lens Distortion** | Barrel/pincushion distortion with scale compensation | LensDistortion.shader |
| **Sharpen** | Unsharp Mask edge enhancement | Sharpen.shader |
| **Posterize** | Color quantization for stylized look | Posterize.shader |
| **Pixelate** | Grid-snap UV sampling for pixel art / retro effect | Pixelate.shader |
| **Color Curves** | 8-channel AnimationCurve-based grading, baked to 1D LUT textures | ColorCurves.shader |
| **Panini Projection** | Cylindrical stereographic projection for wide-FOV scenes | PaniniProjection.shader |

> **Extensibility**: to add a new post-processing effect, create a `VolumeComponent` subclass and a `PostFXEffect` subclass. The effect automatically appears in PostFXSettings's `+` dropdown and in Volume Profile's Add Override menu via reflection-based discovery.

### Anti-Aliasing

**High-Quality AA** (pipeline-level, mutually exclusive):

| Mode | Description | Forward | Deferred | WebGL1 |
|------|-------------|:-------:|:-------:|:------:|
| **MSAA** | Hardware multi-sample anti-aliasing (2x/4x/8x) | ✅ | ❌ | ❌ |
| **TAA** | Temporal anti-aliasing with Halton jitter, depth-based reprojection, YCoCg variance clamping | ✅ | ✅ | ❌ |

**Post-Process AA** (pipeline-level, mutually exclusive):

| Mode | Description |
|------|-------------|
| **FXAA** | Fast approximate anti-aliasing (NVIDIA FXAA 3.11 console variant) |
| **SMAA** | Subpixel Morphological Anti-Aliasing (full 3-pass with precomputed lookup textures). Stripped on WebGL1 builds (`SMAA_DISABLED`); when SMAA is selected but stripped, automatically falls back to FXAA |

- SMAA stripping controlled by `stripSMAAWhenUnused` toggle (reduces build size by ~180KB); when SMAA is stripped but selected, falls back to FXAA automatically
- Per-camera control: `allowHighQualityAA` and `allowPostProcessAA`

### Lighting

- **Light Cookies** — Directional and spot light cookie textures with per-light world-to-light projection matrices
  - Directional lights: Supported in all paths. Cookie size controls tiling repeat.
  - Spot lights: Supported in all paths including Forward+ (with index-bounds checking).
  - Point lights: Not supported (cubemap cookies not implemented).
  - On GLES2/GLES3/WebGL2, cookie textures are disabled to stay within the 16-sampler limit.
  - When no cookie is assigned, a 1×1 white texture is bound.
- Light probe interpolation and Light Probe Proxy Volumes (LPPV)
- Reflection probes
- **Rendering Layer Mask** — Per-camera `maskLights` + `renderingLayerMask` to filter which lights affect a camera

### Other Features

- **GPU Instancing** — `MeshBall` example demonstrates 1023-instance GPU instancing
- **LOD Cross-Fade** — `LOD_FADE_CROSSFADE` support
- **SRP Batcher** — Enabled by default for reduced draw call overhead
- **Render Scaling** — Per-camera render scale (Inherit / Multiply / Override)
- **HDR** — Per-camera HDR support
- **Copy Color & Copy Depth** — Per-camera toggles to copy opaque color and depth before transparent queue
- **Depth Priming Mode** — Auto (depth pre-pass only when needed) or Forced (always)
- **Bicubic Rescaling** — Off / Up-only / Up-and-down
- **Per-Camera Final Blend Mode** — Configurable source/destination blend mode
- **Lens Flare (SRP)** — Data-driven lens flare system with Image/Circle/Polygon shapes
- **WebGL/Mobile Compatibility** — ComputeBuffer → Texture2D fallback, no deferred on WebGL, 8 light cap on GLES2

### Shader Stripping

Automatic stripping of unused shader variants based on build target and GraphicsAPIs:

- Debugger shaders and Meta passes always stripped
- SMAA passes stripped when not selected; SMAA always stripped on WebGL1 builds (detected via `PlayerSettings.GetGraphicsAPIs` — no OpenGLES3 = WebGL1)
- `_TAOTIE_FORWARD_PLUS` keyword variants stripped when Forward+ is Off or WebGL1 target
- Dedicated PostFX shaders (DOF, Outline, Vignette, etc.) stripped when their effect type is not present in any `PostFXSettings` in the project
- Bloom/ColorGrading passes stripped when those effects are absent from all `PostFXSettings` queues
- `_SSAO_ENABLED` keyword variants stripped when SSAO is disabled
- Deferred lighting shader and `DeferredGBuffer` pass stripped in Forward mode

### Shaders

| Shader | Description |
|--------|-------------|
| `TaoTie RP/Lit` | Metallic-roughness PBR lit shader with normal maps, detail maps, MODS mask map, emission, alpha clipping, fresnel |
| `TaoTie RP/Unlit` | Unlit shader |
| `TaoTie RP/Unlit Particles` | Particle shader with near fade, soft particles, distortion, vertex colors, flipbook blending |
| `TaoTie RP/UI TaoTie Blending` | UI shader with stencil and custom blending |
| `Hidden/TaoTie RP/Deferred Lighting` | Fullscreen deferred lighting pass |
| `Hidden/TaoTie RP/Post FX Stack` | Post-processing (bloom, color grading, FXAA, SMAA, rescale) |
| `Hidden/TaoTie RP/Camera Renderer` | Internal blit/copy operations |
| `Hidden/TaoTie RP/TAA` | Temporal anti-aliasing resolve |
| `Hidden/TaoTie RP/SSAO` | Screen Space Ambient Occlusion |
| `Hidden/TaoTie RP/Lens Flare` | Lens flare |
| `Hidden/TaoTie RP/Outline` | Post-process outline |
| `Hidden/TaoTie RP/Depth Of Field` | Depth of field |
| `Hidden/TaoTie RP/Volumetric Fog` | Raymarched volumetric fog |
| `Hidden/TaoTie RP/Motion Blur` | Camera motion blur |
| `Hidden/TaoTie RP/Vignette` | Vignette |
| `Hidden/TaoTie RP/Chromatic Aberration` | Chromatic aberration |
| `Hidden/TaoTie RP/Film Grain` | Film grain |
| `Hidden/TaoTie RP/Lens Distortion` | Lens distortion |
| `Hidden/TaoTie RP/Sharpen` | Sharpen |
| `Hidden/TaoTie RP/Posterize` | Posterize |
| `Hidden/TaoTie RP/Pixelate` | Pixelate |
| `Hidden/TaoTie RP/Color Curves` | Color curves |
| `Hidden/TaoTie RP/Panini Projection` | Panini projection |
| `Hidden/ForwardPlus Debugger` | Forward+ tile debug overlay |
| `Hidden/Depth Debugger` | Depth visualization |

---

## Project Structure

```
com.taotie.render-pipelines/
├── package.json
├── README.md
├── LICENSE
├── Runtime/
│   ├── Data/                      # Pipeline settings (camera, shadow, post-FX, SSAO, etc.)
│   ├── Passes/                    # Render graph passes (Lighting, Geometry, GBuffer, TAA, PostFX, ForwardPlusCull, etc.)
│   ├── Debugger/                  # Debug passes (depth, forward+)
│   ├── Attribute/                 # Custom inspector attributes
│   ├── PostFX/                    # Modular post-processing effect system
│   │   ├── PostFXEffect.cs        # Abstract base class
│   │   ├── PostFXEffectRegistry.cs # Reflection-based discovery
│   │   ├── Volume/                # VolumeComponent subclasses
│   │   └── *Effect.cs             # 15 effect implementations
│   ├── CameraRenderer.cs          # Main camera renderer
│   ├── Shadows.cs                 # Shadow rendering
│   └── TaoTieRenderPipeline.cs    # Pipeline asset & entry point
├── Editor/
│   ├── ShaderStripper.cs          # Build-time shader stripping
│   └── ...                        # Property drawers, asset creators
├── Shaders/
│   ├── ShaderLibrary/             # HLSL includes (Common, Lighting, BRDF, GI, ForwardPlus, etc.)
│   ├── ForwardPlusCulling.compute # GPU tile light culling
│   ├── Lit.shader                 # PBR lit shader
│   ├── PostFXStack.shader         # Post-processing stack
│   └── ...                        # 25 shader files total
├── LWGUI/                         # Light Weight Shader GUI
└── Samples~/                      # Sample content
    ├── Examples/                  # Scripts, pipeline asset, post-FX presets
    └── Scenes/                    # 8 example scenes
```

---

## Getting Started

1. Clone this repository into your Unity project's `Packages/` directory:
   ```bash
   cd YourUnityProject/Packages
   git clone https://github.com/526077247/TaoTieRP.git
   ```
   Or add it via `manifest.json`:
   ```json
   "com.taotie.render-pipelines": "https://github.com/526077247/TaoTieRP.git"
   ```
2. Open the project in Unity 2022.3.x or later
3. Import samples via **Window → Package Manager → TaoTie RP → Samples → Import**
4. Assign the pipeline asset in **Project Settings → Graphics → Scriptable Render Pipeline Asset**
   - Use `Assets/Samples/TaoTie RP/1.0.0/TaoTie RP Samples/Examples/Tao Tie RP.asset`
   - Or create a new one via **Assets → Create → Rendering/TaoTie Pipeline**
5. Open any scene under `Assets/Samples/TaoTie RP/1.0.0/TaoTie RP Samples/Scenes/`

### Example Scenes

| Scene | Showcases |
|-------|-----------|
| Baked Light | Baked lighting, lightmaps, shadowmask |
| Circuitry | Complex materials and geometry |
| Common Materials | Lit/Unlit material presets |
| LOD | LOD group and cross-fade |
| Many Lights | Forward+ tile-based light culling |
| Multiple Cameras | Per-camera Volume filtering via Layer Mask |
| Particles | Particle system with custom shader, Bloom + Color Grading |
| Tone Mapping | ACES / Neutral / Reinhard tone mapping via Volume overrides |
