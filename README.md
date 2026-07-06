# TaoTie RP

A custom Unity Scriptable Render Pipeline (SRP) built on the Render Graph API, featuring Forward and Deferred rendering paths, Forward+ tile-based light culling, cascaded shadow maps, and a full post-processing stack.

## Requirements

- Unity 2022.3.53f1 or later
- Render Pipelines Core 14.0.10+
- Unity Mathematics 1.2.6+

## License

[MIT](LICENSE)

---

## Features

### Rendering Paths

TaoTie RP supports two rendering paths with optional Forward+ tile-based light culling. The combination of rendering path, Forward+ toggle, and target platform determines how opaque and transparent queues are rendered.

#### Path Selection

| Setting | Condition | Native | WebGL2 | WebGL1 |
|---------|-----------|:------:|:------:|:------:|
| **Forward** | Default; always available | ✅ | ✅ | ✅ |
| **Deferred** | Requires `supportedRenderTargetCount ≥ 3` (MRT); not a reflection camera; forces MSAA off. The `DeferredLighting` shader uses `#pragma exclude_renderers gles`, so even if the runtime check is bypassed, the shader is stripped from WebGL builds. | ✅ | ❌ (forced Forward) | ❌ (forced Forward) |
| **Forward+** | Enabled when `useForwardPlus = true` and graphics API is not OpenGLES2. Uses ComputeBuffer on native, Texture2D fallback on WebGL2. | ✅ | ✅ (Texture2D fallback) | ❌ |

> When Deferred is selected but the platform doesn't support it (all WebGL runtimes, or insufficient MRT on native), the pipeline automatically falls back to Forward rendering. In the Editor, Deferred is available on all platforms for testing purposes (`UNITY_EDITOR` bypasses the WebGL exclusion).

#### Opaque Queue

| Path | Forward+ | Shader LightMode | Lighting Method | Notes |
|------|----------|-----------------|-----------------|-------|
| Forward | Off | `CustomLit` | Per-pixel, up to 8 other lights (CPU loop) | Default on WebGL1 |
| Forward | On | `CustomLit` + `_TAOTIE_FORWARD_PLUS` | Per-pixel, tile-culled, up to 256 other lights (ComputeBuffer/Texture2D) | Not available on WebGL1 |
| Deferred | Off | `DeferredGBuffer` | GBuffer MRT (albedo/normal/emission) → fullscreen `DeferredLightingPass` | Forward+ **not used**; lighting resolved in fullscreen pass |
| Deferred | On | `DeferredGBuffer` | GBuffer MRT → fullscreen `DeferredLightingPass` | Forward+ **not used** on opaque; lighting resolved in fullscreen pass regardless of `useForwardPlus` |

> In Deferred path, opaque geometry writes to GBuffer textures via `DeferredGBuffer` shader pass. Lighting is computed in a separate fullscreen `DeferredLightingPass` using the GBuffer data and depth. Forward+ does not apply to the deferred opaque pass — the `DeferredGBuffer` shader pass does not include the `_TAOTIE_FORWARD_PLUS` keyword, and lighting is resolved entirely in the fullscreen lighting pass rather than per-pixel during geometry rendering.

#### Transparent Queue

| Path | Forward+ | Shader LightMode | Lighting Method | Notes |
|------|----------|-----------------|-----------------|-------|
| Forward | Off | `CustomLit` | Per-pixel, up to 8 other lights | Same as opaque |
| Forward | On | `CustomLit` + `_TAOTIE_FORWARD_PLUS` | Per-pixel, tile-culled, up to 256 other lights | Same as opaque |
| Deferred | Off | `CustomLit` | Per-pixel, up to 8 other lights | **Always forward** — deferred cannot handle transparency |
| Deferred | On | `CustomLit` + `_TAOTIE_FORWARD_PLUS` | Per-pixel, tile-culled, up to 256 other lights | **Always forward** — Forward+ applies to transparents even in deferred mode |

> Transparent objects are always rendered with the forward path (`CustomLit` shader tag), regardless of whether the pipeline is set to Forward or Deferred. This is because GBuffer-based deferred lighting cannot handle transparency. When `useForwardPlus` is enabled, transparent objects in the deferred path also benefit from Forward+ tile-based light culling.

#### Full Pass Sequences

**Forward path:**
```
LightingPass → SetupPass → [DepthPrePass] → GeometryPass(opaque, CustomLit) → OutLinePass → SkyboxPass
→ [ResolvePass(MSAA)] → CopyAttachmentsPass → GeometryPass(transparent, CustomLit)
→ UnsupportedShadersPass → [ResolvePass] → [TAAResolvePass] → PostFX → Final
```

**Deferred path:**
```
LightingPass → SetupPass → GBufferPass(opaque, DeferredGBuffer) → DeferredLightingPass → SkyboxPass
→ OutLinePass → CopyAttachmentsPass → GeometryPass(transparent, CustomLit)
→ UnsupportedShadersPass → [TAAResolvePass] → PostFX → Final
```

> `[...]` = optional, depends on settings. DepthPrePass runs when MSAA + Copy Depth is enabled. TAAResolvePass runs when TAA is enabled.

### Lighting

- **Forward+** — Custom CPU tile-based light culling, supporting up to 4 directional lights and 256 point/spot lights
- ComputeBuffer light data with Texture2D fallback for WebGL
- Light probe interpolation and Light Probe Proxy Volumes (LPPV)
- Reflection probes

### Shadows

- Directional cascaded shadows (1–4 cascades, cascade fade, soft blend)
- Spot light shadows
- Point light 6-face cube shadows
- Shadowmask support
- 3 shadow filter quality levels (Hard / Medium / Soft)
- Configurable shadow atlas resolution (256–8192)

### Post-Processing

- **Bloom** — Pyramid down/up-sampling, scatter/additive mode, firefly filtering, bicubic upsampling
- **Tone Mapping** — ACES, Neutral, Reinhard
- **Color Grading** — Color LUT (16/32/64), color adjustments, white balance, split toning, channel mixer, shadows/midtones/highlights
- **Bicubic Rescaling** — Off / Up-only / Up-and-down
- Post-processing overrides per camera

### Anti-Aliasing

TaoTie RP provides multiple anti-aliasing strategies, organized into two layers:

**High-Quality AA** (pipeline-level, mutually exclusive):

| Mode | Description | Forward | Deferred | WebGL1 |
|------|-------------|:-------:|:-------:|:------:|
| **MSAA** | Hardware multi-sample anti-aliasing (2x/4x/8x) | ✅ | ❌ | ❌ |
| **TAA** | Temporal anti-aliasing with Halton jitter, depth-based reprojection, neighborhood clamping | ✅ | ✅ | ❌ |

- MSAA and TAA are mutually exclusive — enabling one disables the other
- MSAA is disabled in Deferred mode (MRT + MSAA not reliably supported)
- TAA is disabled on WebGL1/GLES2 (requires depth texture sampling)
- When MSAA + Copy Depth is enabled, a Depth Pre-Pass renders opaque depth to a non-MSAA texture
- TAA parameters: Jitter Scale, Anti-Flicker, Base Blend Factor, Jitter Spread

**Post-Process AA** (pipeline-level, mutually exclusive):

| Mode | Description |
|------|-------------|
| **FXAA** | Fast approximate anti-aliasing (NVIDIA FXAA 3.11 console variant) |
| **SMAA** | Subpixel Morphological Anti-Aliasing (full 3-pass: edge detection → blend weight calculation → neighborhood blending, with precomputed area/search lookup textures) |

- FXAA and SMAA are mutually exclusive
- SMAA uses the original precomputed lookup textures (areaTex 160×560, searchTex 64×16) embedded as byte arrays
- When SMAA is not selected, SMAA shader passes and lookup data are stripped from builds via `SMAA_DISABLED` define

**Per-Camera Control:**

- `allowHighQualityAA` — enables/disables MSAA or TAA for this camera
- `allowPostProcessAA` — enables/disables FXAA or SMAA for this camera

### Shaders

| Shader | Description |
|--------|-------------|
| `TaoTie RP/Lit` | Metallic-roughness PBR lit shader with normal maps, detail maps, MODS mask map, emission, alpha clipping, fresnel, outline |
| `TaoTie RP/Unlit` | Unlit shader |
| `TaoTie RP/Unlit Particles` | Particle shader with near fade, soft particles, distortion, vertex colors, flipbook blending |
| `TaoTie RP/UI TaoTie Blending` | UI shader with stencil and custom blending |
| `Hidden/DeferredLighting` | Fullscreen deferred lighting pass |
| `Hidden/PostFXStack` | All post-processing effects |
| `Hidden/CameraRenderer` | Internal blit/copy operations |
| `Hidden/ForwardPlusDebugger` | Debug overlay |
| `Hidden/DepthDebugger` | Depth visualization (Linear Eye / 01 / Raw, split-screen, opacity) |

### Other Features

- **GPU Instancing** — `MeshBall` example demonstrates 1023-instance GPU instancing with `MaterialPropertyBlock`
- **Per-Object Material Properties** — Override material properties per object via `MaterialPropertyBlock`
- **LOD Cross-Fade** — `LOD_FADE_CROSSFADE` support
- **SRP Batcher** — Enabled by default for reduced draw call overhead
- **Render Scaling** — Per-camera render scale (Inherit / Multiply / Override)
- **HDR** — Per-camera HDR support
- **Shader Stripping** — Automatic stripping of unused shader variants (debug shaders, Meta passes, WebGL compute buffer variants)
- **WebGL/Mobile Compatibility** — ComputeBuffer→Texture2D fallback, no deferred on WebGL1/GLES2 (`supportedRenderTargetCount == 1`), graphics format fallbacks

### Depth Texture (Copy Depth)

When `copyDepth` is enabled, the opaque depth buffer is copied to `_CameraDepthTexture` before the transparent render queue, enabling soft particles, depth-based transparency, and other depth-dependent effects.

| Condition | Method | Description |
|-----------|--------|-------------|
| No MSAA | `CopyTexture` / `CopyByDrawing` | Direct copy from depth attachment to non-MSAA depth texture |
| MSAA + any platform | Depth Pre-Pass | Opaque objects are rendered with a `DepthOnly` shader pass into a non-MSAA depth texture before the main geometry pass |

**Forward path with MSAA + Copy Depth:**
```
SetupPass → DepthPrePass → GeometryPass(opaque) → OutLine → Skybox → ResolvePass
→ CopyAttachmentsPass (color copy only)
→ GeometryPass(transparent) → ...
```

> **Note:** Deferred path always forces MSAA off, so depth copy always uses the direct `CopyTexture` method regardless of platform.

---

## Project Structure

```
TaoTieRP/
├── Assets/
│   ├── Examples/                  # Example scripts (MeshBall, PerObjectMaterialProperties)
│   ├── Scenes/                    # Example scenes
│   │   ├── Baked Light/
│   │   ├── Circuitry/
│   │   ├── Common Materials/
│   │   ├── LOD/
│   │   ├── Many Lights/
│   │   ├── Multiple Cameras/
│   │   ├── Particles/
│   │   └── Tone Mapping/
│   ├── Post FX *.asset            # Post-processing preset assets
│   └── Tao Tie RP.asset           # Render pipeline asset
├── Packages/
│   └── com.taotie.render-pipelines/
│       ├── Runtime/
│       │   ├── Data/               # Pipeline settings (camera, shadow, post-FX, etc.)
│       │   ├── Passes/             # Render graph passes (18 passes)
│       │   ├── Attribute/          # Custom inspector attributes
│       │   └── Materials/          # Internal materials
│       ├── Editor/                 # Editor tools, property drawers, shader stripper
│       ├── Shaders/
│       │   ├── ShaderLibrary/     # HLSL include files
│       │   ├── Lit.shader
│       │   ├── Unlit.shader
│       │   ├── UnlitParticles.shader
│       │   └── ...
│       └── LWGUI/                 # Material inspector (Light Weight Shader GUI)
└── ProjectSettings/
```

### Render Pass Sequence

**Forward Path:**
```
LightingPass → SetupPass → GeometryPass(opaque) → OutLinePass → SkyboxPass
→ ResolvePass(MSAA) → CopyAttachments → GeometryPass(transparent)
→ UnsupportedShaders → ResolvePass → PostFX → Final → DepthDebug → Debug → Gizmos
```

**Deferred Path:**
```
LightingPass → SetupPass → GBufferPass → DeferredLightingPass → SkyboxPass
→ OutLinePass → CopyAttachments → GeometryPass(transparent)
→ PostFX → Final → DepthDebug → Debug → Gizmos
```

---

## Getting Started

1. Open the project in Unity 2022.3.53f1 or later
2. The pipeline asset (`Tao Tie RP.asset`) is assigned in **Project Settings > Graphics > Scriptable Render Pipeline Asset**
3. Open any scene under `Assets/Scenes/` to explore features

### Example Scenes

| Scene | Showcases |
|-------|-----------|
| Baked Light | Baked lighting, lightmaps, shadowmask |
| Circuitry | Complex materials and geometry |
| Common Materials | Lit/Unlit material presets |
| LOD | LOD group and cross-fade |
| Many Lights | Forward+ tile-based light culling |
| Multiple Cameras | Per-camera overrides (render scale, post-FX, blend mode) |
| Particles | Particle system with custom shader |
| Tone Mapping | ACES / Neutral / Reinhard tone mapping |

---
