# TaoTie RP

A custom Unity Scriptable Render Pipeline (SRP) built on the Render Graph API, featuring Forward and Deferred rendering paths, Forward+ tile-based light culling with bitmask + ZBin depth culling, per-vertex lighting via Light.renderMode (ForcePixel/Auto/ForceVertex), cascaded shadow maps, light cookies, and a modular post-processing stack.

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
| **Forward+** | Enabled when `forwardPlus != Off` and graphics API is not OpenGLES2. Auto mode activates when visible other lights exceed 16 (hysteresis: disables below 8). Uses StructuredBuffer on native (SM 4.5+), Texture2D fallback on WebGL2. Stripped on WebGL1 builds. | ✅ | ✅ (Texture2D fallback) | ❌ |

> When Deferred is selected but the platform doesn't support it (all WebGL runtimes, or insufficient MRT on native), the pipeline automatically falls back to Forward rendering. In the Editor, Deferred is available on all platforms for testing purposes.

#### Opaque Queue

| Path | Forward+ | Shader LightMode | Lighting Method | Native | WebGL2 | WebGL1 |
|------|----------|-----------------|-----------------|:------:|:------:|:------:|
| Forward | Off | `CustomLit` | Per-pixel up to `maxOtherLights`; excess Auto lights demoted to per-vertex | ✅ | ✅ | ✅ (capped at 8) |
| Forward | On | `CustomLit` + `_TAOTIE_FORWARD_PLUS` | Per-pixel, bitmask + ZBin tile-culled; excess Auto lights demoted to per-vertex | ✅ (ComputeBuffer) | ✅ (Texture2D fallback) | ❌ (FP disabled → Off) |
| Deferred | Off | `DeferredGBuffer` | GBuffer MRT → fullscreen lighting (all lights per-pixel, no limit, no vertex lights) | ✅ | ❌ (no deferred) | ❌ |
| Deferred | On | `DeferredGBuffer` | GBuffer MRT → fullscreen lighting with bitmask + ZBin tile culling (all lights per-pixel) | ✅ | ❌ | ❌ |

> In Deferred path, opaque geometry writes to GBuffer textures via `DeferredGBuffer` shader pass. Lighting is computed in a separate fullscreen `DeferredLightingPass`. When Forward+ is enabled, the `DeferredLightingPass` also uses bitmask + ZBin tile-culled light iteration via `LIGHT_LOOP_BEGIN`/`LIGHT_LOOP_END` macros — the same tile data computed by `ForwardPlusCullPass` is reused.

#### Transparent Queue

| Path | Forward+ | Shader LightMode | Lighting Method | Native | WebGL2 | WebGL1 |
|------|----------|-----------------|-----------------|:------:|:------:|:------:|
| Forward | Off | `CustomLit` | Per-pixel up to `maxOtherLights`; excess demoted to per-vertex | ✅ | ✅ | ✅ (capped at 8) |
| Forward | On | `CustomLit` + `_TAOTIE_FORWARD_PLUS` | Per-pixel, tile-culled; excess demoted to per-vertex | ✅ | ✅ | ❌ (falls back to Off) |
| Deferred | Off | `CustomLit` | Per-pixel up to `maxOtherLights`; excess demoted to per-vertex | ✅ | ❌ | ❌ |
| Deferred | On | `CustomLit` + `_TAOTIE_FORWARD_PLUS` | Per-pixel, tile-culled; excess demoted to per-vertex | ✅ | ❌ | ❌ |

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
- **StructuredBuffer hysteresis** — On platforms supporting Shader Model 4.5+ (non-GLES), `_OTHER_LIGHT_BUFFER` is enabled when total other lights > 256, disabled when < 128. When active, light data is uploaded as `StructuredBuffer<OtherLightData>` (up to 1024 lights); otherwise CBUFFER arrays are used. On SM < 4.5 / GLES platforms, StructuredBuffer is never used.
- **`_SUPPORTS_STRUCTURED_BUFFER` keyword** — Static platform capability keyword set at runtime based on `supportsStructuredBuffer` (SM 4.5+ non-GLES). Controls Tile/ZBin data storage: StructuredBuffer when enabled, Texture2D fallback when disabled. Stripped on WebGL builds. Independent from `_OTHER_LIGHT_BUFFER` (which controls light data storage via hysteresis).
- **Hysteresis threshold** — Auto mode enables Forward+ when lights > 16, disables when lights < 8, preventing variant thrashing near the threshold
- **GPU compute culling** — ComputeShader (`ForwardPlusCulling.compute`) with `groupshared` memory for collaborative light bounds loading, `[numthreads(8,8,1)]` dispatch. Burst-compiled CPU Job (`TileCullJob.cs`) fallback for WebGL/GLES3
- **Deferred integration** — When Forward+ is enabled, the Deferred lighting pass also uses bitmask + ZBin tile-culled light iteration via `LIGHT_LOOP_BEGIN`/`LIGHT_LOOP_END` macros
- `LIGHT_LOOP_BEGIN` / `LIGHT_LOOP_END` macros abstract the Other Light iteration so `GetLighting()` stays clean across Forward+, GLES2, and plain Forward paths
- Supports up to 4 directional lights and 256 point/spot lights (8 on GLES2)
- StructuredBuffer light data (SM 4.5+) with Texture2D fallback for GLES/WebGL; `_SUPPORTS_STRUCTURED_BUFFER` keyword controls tile/ZBin data path, `_OTHER_LIGHT_BUFFER` controls light data path

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

- **Per-Vertex Lighting** — Lights with `Light.renderMode` set to **Not Important (ForceVertex)** are computed per-vertex (simplified: no shadows, no cookies, Lambert diffuse only). Lights set to **Important (ForcePixel)** are always per-pixel. **Auto** lights are per-pixel up to the pixel light limit, then demoted to per-vertex. `maxOtherLights` controls the per-pixel light limit in Forward (non-Forward+) path. Deferred ignores render mode — all lights are per-pixel with no limit. The shader reads vertex lights from the same `_OtherLight*` arrays: indices `[0.._OtherLightCount-1]` are per-pixel, `[_OtherLightCount.._VertexLightCount-1]` are per-vertex.

  | Render Mode | Behavior |
  |-------------|----------|
  | **ForcePixel** | Always per-pixel (sorted highest, never demoted) |
  | **Auto** | Per-pixel if within limit; otherwise demoted to per-vertex |
  | **ForceVertex** | Always per-vertex |

  Light sorting priority (within each category, by descending score `brightness × screenArea / distSqr`): ForcePixel > Auto > ForceVertex. Demoted Auto lights are boosted above ForceVertex in the vertex light list.

- **Light Cookies** — Directional and spot light cookie textures with per-light world-to-light projection matrices
  - Directional lights: Supported in all paths. Cookie size controls tiling repeat.
  - Spot lights: Supported in all paths including Forward+ (with index-bounds checking).
  - Point lights: Not supported (cubemap cookies not implemented).
  - On GLES2/GLES3/WebGL2, cookie textures are disabled to stay within the 16-sampler limit.
  - When no cookie is assigned, a 1×1 white texture is bound.
- Light probe interpolation and Light Probe Proxy Volumes (LPPV)
- Reflection probes
- **Rendering Layer Mask** — Per-light `renderingLayerMask` (set in the Light Inspector) controls which rendering layers each light affects. Per-camera `maskLights` + `renderingLayerMask` filters which lights are visible to a camera. The shader `RenderingLayersOverlap()` function performs a bitwise AND between the surface's `renderingLayerMask` (from `unity_RenderingLayer`) and the light's `renderingLayerMask` to determine overlap.
  - **Forward path**: Surface reads `renderingLayerMask` from `unity_RenderingLayer` per-pixel; light mask is packed in `_DirectionalLightDirectionsAndMasks[index].w` / `_OtherLightDirectionsAndMasks[index].w` via `(float)` value cast.
  - **Deferred path**: `DeferredGBufferPass` packs `renderingLayerMask` into the emission GBuffer's alpha channel (RT2.a, format `R32G32B32A32_SFloat` for full float32 precision). `DeferredLightingPass` reads it back from the GBuffer texture.
  - **"Everything" (0x7FFFFFFF)**: C# sends `0x00FFFFFF` as a sentinel value; the shader treats it as all-layers-match to avoid float overflow.
  - **24-Layer limitation**: Due to float32's 24-bit mantissa precision, the `(float)` value cast used for CBUFFER and GBuffer transmission preserves single-layer masks (powers of 2) exactly for all 31 layers, but **multi-layer combinations that mix bits 0–23 with bits 24–30 may lose precision** (e.g., Layer 1 + Layer 25 = `0x1000001` rounds to `0x1000000`). In practice, single-layer selection and "Everything" work correctly for all 31 layers; multi-layer masks are reliable for layers 1–24.
  - This parameter has no effect in WebGL1❌

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
- **Lens Flare (SRP)** — Data-driven lens flare system powered by LensFlareCommonSRP (Image/Circle/Polygon shapes, occlusion, light attenuation)
- **WebGL/Mobile Compatibility** — `_SUPPORTS_STRUCTURED_BUFFER` keyword (static, SM 4.5+ non-GLES) controls Tile/ZBin StructuredBuffer vs Texture2D; `_OTHER_LIGHT_BUFFER` (hysteresis, >256 enable/<128 disable) controls light data StructuredBuffer vs CBUFFER; GLES2/GLES3 use bare globals (no CBUFFER); no deferred on WebGL; 8 light cap on GLES2; per-vertex lighting not supported in Deferred path
- **Debug Tools** — Rendering Debugger panels accessible via **Window → Analysis → Rendering Debugger**:
  - **Forward+ Debugger** — Visualizes tile light counts as a heat map overlay
  - **Depth Debugger** — Visualizes depth buffer (Linear Eye / Linear 01 / Raw), split-screen mode, adjustable opacity
  - **Overdraw Debugger** — Visualizes pixel overdraw with an additive heat map (blue → cyan → green → yellow → red → white), reveals areas of wasted fragment shading

### Shader Stripping

Automatic stripping of unused shader variants based on build target and GraphicsAPIs:

- Debugger shaders and Meta passes always stripped
- ShadowCaster pass stripped when `directional.maxLightCount == 0`; `_SHADOW_MASK` keyword stripped when shadows disabled
- Lens Flare shader stripped when no `LensFlareDataSRP` assets exist in the project
- SMAA passes stripped when not selected; SMAA always stripped on WebGL1 builds (detected via `PlayerSettings.GetGraphicsAPIs` — no OpenGLES3 = WebGL1)
- `_TAOTIE_FORWARD_PLUS` keyword variants stripped when Forward+ is Off or WebGL1 target
- `_SUPPORTS_STRUCTURED_BUFFER` keyword variants stripped on WebGL builds (no StructuredBuffer support)
- `_OTHER_LIGHT_BUFFER` keyword variants stripped on WebGL builds
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
| `Hidden/TaoTie RP/Lens Flare` | Lens flare (LensFlareCommonSRP, SM3.5+) |
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
| `Hidden/TaoTie RP/Overdraw` | Overdraw geometry accumulation |
| `Hidden/TaoTie RP/Overdraw Resolve` | Overdraw heat map resolve |

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
│   ├── Debugger/                  # Debug passes (depth, forward+, overdraw)
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
