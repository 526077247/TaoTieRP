# TaoTie RP

A custom Unity Scriptable Render Pipeline (SRP) built on the Render Graph API, featuring Forward and Deferred rendering paths, Forward+ tile-based light culling, cascaded shadow maps, light cookies, and a full post-processing stack.

## Requirements

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

| Path | Forward+ | Shader LightMode | Lighting Method | Native | WebGL2 | WebGL1 |
|------|----------|-----------------|-----------------|:------:|:------:|:------:|
| Forward | Off | `CustomLit` | Per-pixel, up to `maxOtherLights` (default 64, max 64) other lights | ✅ | ✅ | ✅ (capped at 8) |
| Forward | On | `CustomLit` + `_TAOTIE_FORWARD_PLUS` | Per-pixel, tile-culled, up to 256 other lights | ✅ (ComputeBuffer) | ✅ (Texture2D fallback) | ❌ (FP disabled → Off) |
| Deferred | Off | `DeferredGBuffer` | GBuffer MRT → fullscreen `DeferredLightingPass` (up to 256 lights) | ✅ | ❌ (no deferred) | ❌ |
| Deferred | On | `DeferredGBuffer` | GBuffer MRT → fullscreen `DeferredLightingPass` (up to 256 lights) | ✅ | ❌ | ❌ |

> In Deferred path, opaque geometry writes to GBuffer textures via `DeferredGBuffer` shader pass. Lighting is computed in a separate fullscreen `DeferredLightingPass` using the GBuffer data and depth. Forward+ does not apply to the deferred opaque pass — the `DeferredGBuffer` shader pass does not include the `_TAOTIE_FORWARD_PLUS` keyword, and lighting is resolved entirely in the fullscreen lighting pass rather than per-pixel during geometry rendering.

#### Transparent Queue

| Path | Forward+ | Shader LightMode | Lighting Method | Native | WebGL2 | WebGL1 |
|------|----------|-----------------|-----------------|:------:|:------:|:------:|
| Forward | Off | `CustomLit` | Per-pixel, up to `maxOtherLights` (default 64) | ✅ | ✅ | ✅ (capped at 8) |
| Forward | On | `CustomLit` + `_TAOTIE_FORWARD_PLUS` | Per-pixel, tile-culled, up to 256 | ✅ | ✅ | ❌ (falls back to Off) |
| Deferred | Off | `CustomLit` | Per-pixel, up to `maxOtherLights` (default 64) | ✅ | ❌ | ❌ |
| Deferred | On | `CustomLit` + `_TAOTIE_FORWARD_PLUS` | Per-pixel, tile-culled, up to 256 | ✅ | ❌ | ❌ |

> Transparent objects are always rendered with the forward path (`CustomLit` shader tag), regardless of whether the pipeline is set to Forward or Deferred. When `useForwardPlus` is enabled, transparent objects in the deferred path also benefit from Forward+ tile-based light culling. On WebGL1 (GLES2), Forward+ is disabled and the maximum other light count is capped at 8 due to CBUFFER/array size limitations.

#### Full Pass Sequences

**Forward path:**
```
LightingPass → SetupPass → [DepthPrePass] → GeometryPass(opaque, CustomLit) → SkyboxPass
→ [ResolvePass(MSAA)] → CopyAttachmentsPass → GeometryPass(transparent, CustomLit)
→ UnsupportedShadersPass → [ResolvePass] → [TAAResolvePass] → PostFX → Final
```

**Deferred path:**
```
LightingPass → SetupPass → GBufferPass(opaque, DeferredGBuffer) → DeferredLightingPass
→ SkyboxPass → CopyAttachmentsPass → GeometryPass(transparent, CustomLit)
→ UnsupportedShadersPass → [TAAResolvePass] → PostFX → Final
```

> `[...]` = optional, depends on settings. DepthPrePass runs when MSAA + Copy Depth is enabled. TAAResolvePass runs when TAA is enabled.

### Lighting

- **Forward+** — Custom CPU tile-based light culling, supporting up to 4 directional lights and 256 point/spot lights
- ComputeBuffer light data with Texture2D fallback for WebGL
- Light probe interpolation and Light Probe Proxy Volumes (LPPV)
- Reflection probes
- **Light Cookies** — Directional and spot light cookie textures with per-light world-to-light projection matrices
  - **Directional lights**: Supported in both Forward and Deferred paths. Cookie size controls tiling repeat.
  - **Spot lights**: Supported in Forward path only (non-Forward+). Cookie is perspective-projected through the cone using the spot angle and range.
  - **Point lights**: Not supported (cubemap cookies not implemented).
  - **Forward+ path**: Spot light cookies are not available due to CBUFFER size and shader loop unroll constraints.
  - When no cookie is assigned, a 1×1 white texture is bound (no performance overhead — shader early-outs via enable flag).

### Shadows

- Directional cascaded shadows (1–4 cascades, cascade fade, soft blend)
- Spot light shadows
- Point light 6-face cube shadows
- Shadowmask support
- 3 shadow filter quality levels (Hard / Medium / Soft)
- Configurable shadow atlas resolution (256–8192)

### SSAO (Screen Space Ambient Occlusion)

- Alchemy/Horizon-based AO algorithm with depth-reconstructed normals
- 3 quality presets: Low (4 samples), Medium (8 samples), High (12 samples)
- Bilateral blur (horizontal + vertical, 5-tap each) to reduce noise while preserving edges
- Configurable radius, intensity, and distance falloff
- Configurable resolution downsample (0.25×–1×) for performance scaling
- Applied to ambient/indirect lighting only (multiplies `surface.occlusion`), does not affect direct lighting
- Works in both Forward and Deferred paths (applied after opaque queue, before transparent queue)
- Configured under **Shadows > SSAO** in the pipeline asset

### Outline (Post-Process Effect)

Outline is now a modular PostFX effect (`OutlineEffect`) integrated into the Post FX Settings effects list, alongside Bloom and Color Grading. It can be individually enabled/disabled, reordered, and added via the Inspector's `+` dropdown.

- Roberts Cross depth edge detection
- Optional G-Buffer normal edge detection (Deferred path)
- Configurable color, depth sensitivity, normal sensitivity, and width
- Supports both Forward (with MSAA) and Deferred paths
- Skipped for SceneView and Preview cameras

### Post-Processing

The post-processing stack uses a modular effect architecture. Each effect is an independent `[Serializable]` class inheriting from `PostFXEffect`, registered in the Post FX Settings asset's effects list. Effects can be individually enabled/disabled, reordered to change execution order, and added via the Inspector's `+` dropdown menu (which auto-discovers all `PostFXEffect` subclasses via reflection).

Shader pass indices are resolved dynamically at runtime by name (via `Material.GetPassName`), eliminating the fragile hardcoded enum-to-shader-pass-index coupling.

Built-in effects:

| Effect | Description |
|--------|-------------|
| **Bloom** | Pyramid down/up-sampling, scatter/additive mode, firefly filtering, bicubic upsampling |
| **Color Grading** | Color LUT (16/32/64), color adjustments, white balance, split toning, channel mixer, shadows/midtones/highlights, tone mapping (ACES, Neutral, Reinhard) |
| **Outline** | Roberts Cross depth + optional G-Buffer normal edge detection, configurable color/sensitivity/width |

> Extensibility: to add a new post-processing effect, create a class inheriting from `PostFXEffect`, override `DisplayName` and `Execute()`, and it will automatically appear in the Inspector's `+` dropdown. No changes to PostFXStack, PostFXPass, or shader pass enums are needed.

**Additional post-processing features:**
- **Bicubic Rescaling** — Off / Up-only / Up-and-down
- Post-processing overrides per camera

### Anti-Aliasing

TaoTie RP provides multiple anti-aliasing strategies, organized into two layers:

**High-Quality AA** (pipeline-level, mutually exclusive):

| Mode | Description | Forward | Deferred | WebGL1 |
|------|-------------|:-------:|:-------:|:------:|
| **MSAA** | Hardware multi-sample anti-aliasing (2x/4x/8x) | ✅ | ❌ | ❌ |
| **TAA** | Temporal anti-aliasing with Halton jitter, depth-based reprojection, YCoCg variance clamping, AABB clip-to-center | ✅ | ✅ | ❌ |

- MSAA and TAA are mutually exclusive — enabling one disables the other
- MSAA is disabled in Deferred mode (MRT + MSAA not reliably supported)
- TAA is disabled on WebGL1/GLES2 (requires depth texture sampling)
- When MSAA + Copy Depth is enabled, a Depth Pre-Pass renders opaque depth to a non-MSAA texture
- TAA parameters: Jitter Scale, Base Blend Factor, Variance Clamp Scale

**Post-Process AA** (pipeline-level, mutually exclusive):

| Mode | Description |
|------|-------------|
| **FXAA** | Fast approximate anti-aliasing (NVIDIA FXAA 3.11 console variant) |
| **SMAA** | Subpixel Morphological Anti-Aliasing (full 3-pass: edge detection → blend weight calculation → neighborhood blending, with precomputed area/search lookup textures) |

- FXAA and SMAA are mutually exclusive
- SMAA uses the original precomputed lookup textures (areaTex 160×560, searchTex 64×16) embedded as byte arrays
- SMAA edge texture uses optimized `R8G8_UNorm` format for reduced memory
- When SMAA is not selected, SMAA shader passes and lookup data are stripped from builds via `SMAA_DISABLED` define

**Per-Camera Control:**

- `allowHighQualityAA` — enables/disables MSAA or TAA for this camera
- `allowPostProcessAA` — enables/disables FXAA or SMAA for this camera

### Shaders

| Shader | Description |
|--------|-------------|
| `TaoTie RP/Lit` | Metallic-roughness PBR lit shader with normal maps, detail maps, MODS mask map, emission, alpha clipping, fresnel |
| `TaoTie RP/Unlit` | Unlit shader |
| `TaoTie RP/Unlit Particles` | Particle shader with near fade, soft particles, distortion, vertex colors, flipbook blending |
| `TaoTie RP/UI TaoTie Blending` | UI shader with stencil and custom blending |
| `Hidden/TaoTie RP/Deferred Lighting` | Fullscreen deferred lighting pass |
| `Hidden/TaoTie RP/Post FX Stack` | All post-processing effects |
| `Hidden/TaoTie RP/Camera Renderer` | Internal blit/copy operations |
| `Hidden/TaoTie RP/TAA` | Temporal anti-aliasing resolve |
| `Hidden/TaoTie RP/Outline` | Post-process outline (depth + normal edge detection) |
| `Hidden/TaoTie RP/SSAO` | Screen Space Ambient Occlusion (generate + horizontal/vertical bilateral blur) |
| `Hidden/ForwardPlus Debugger` | Debug overlay |
| `Hidden/Depth Debugger` | Depth visualization (Linear Eye / 01 / Raw, split-screen, opacity) |

### Other Features

- **GPU Instancing** — `MeshBall` example demonstrates 1023-instance GPU instancing with `MaterialPropertyBlock`
- **Per-Object Material Properties** — Override material properties per object via `MaterialPropertyBlock`
- **LOD Cross-Fade** — `LOD_FADE_CROSSFADE` support
- **SRP Batcher** — Enabled by default for reduced draw call overhead
- **Render Scaling** — Per-camera render scale (Inherit / Multiply / Override)
- **HDR** — Per-camera HDR support
- **Shader Stripping** — Automatic stripping of unused shader variants (debug shaders, Meta passes, WebGL compute buffer variants, SMAA passes)
- **SSAO** — Screen Space Ambient Occlusion with Alchemy AO algorithm, depth-reconstructed normals, bilateral blur, configurable quality/radius/intensity/falloff/downsample
- **WebGL/Mobile Compatibility** — ComputeBuffer→Texture2D fallback, no deferred on WebGL1/GLES2 (`supportedRenderTargetCount == 1`), graphics format fallbacks
- **Rendering Layer Mask** — Per-light rendering layer mask for selective lighting (custom SRP layer names, works alongside Unity's built-in Culling Mask)
- **Rendering Layer Mask Names** — 31 custom rendering layers exposed via `TaoTieRenderPipelineAsset.renderingLayerMaskNames`

### Depth Texture (Copy Depth)

When `copyDepth` is enabled, the opaque depth buffer is copied to `_CameraDepthTexture` before the transparent render queue, enabling soft particles, depth-based transparency, and other depth-dependent effects.

| Condition | Method | Description |
|-----------|--------|-------------|
| No MSAA | `CopyTexture` / `CopyByDrawing` | Direct copy from depth attachment to non-MSAA depth texture |
| MSAA + any platform | Depth Pre-Pass | Opaque objects are rendered with a `DepthOnly` shader pass into a non-MSAA depth texture before the main geometry pass |

**Forward path with MSAA + Copy Depth:**
```
SetupPass → DepthPrePass → GeometryPass(opaque) → Skybox → ResolvePass
→ CopyAttachmentsPass (color copy only)
→ GeometryPass(transparent) → ...
```

> **Note:** Deferred path always forces MSAA off, so depth copy always uses the direct `CopyTexture` method regardless of platform.

---

## Project Structure

```
com.taotie.render-pipelines/
├── package.json                   # Package manifest (samples, description, dependencies)
├── README.md
├── LICENSE
├── Runtime/
│   ├── Data/                      # Pipeline settings (camera, shadow, post-FX, AA, SSAO, etc.)
│   ├── Passes/                    # Render graph passes (Lighting, Geometry, GBuffer, TAA, SMAA, PostFX, etc.)
│   ├── Debugger/                 # Debug passes (depth, forward+)
│   ├── Attribute/                 # Custom inspector attributes (ShowIf, MSAAField, EnumLabel, etc.)
│   ├── Materials/                 # Internal materials
│   ├── CameraRenderer.cs          # Main camera renderer (pass orchestration, AA/depth/TAA integration)
│   ├── PostFXStack.cs             # Post-processing stack (dynamic pass lookup, draw helpers)
│   ├── PostFX/                    # Modular post-processing effect system
│   │   ├── PostFXEffect.cs        # Abstract base class for all post-FX effects
│   │   ├── PostFXPassNames.cs     # Shader pass name constants (replaces hardcoded enum)
│   │   ├── PostFXEffectRegistry.cs# Reflection-based discovery of all PostFXEffect subclasses
│   │   ├── BloomEffect.cs         # Bloom effect (pyramid down/up-sampling)
│   │   ├── ColorGradingEffect.cs  # Color grading + tone mapping (LUT generation)
│   │   └── OutlineEffect.cs       # Outline effect (depth + normal edge detection)
│   ├── Shadows.cs                 # Shadow rendering
│   ├── TAAData.cs                 # TAA per-camera history & jitter management
│   ├── SMAATextures.cs            # SMAA precomputed lookup textures (embedded byte arrays)
│   ├── SSAOPass.cs               # SSAO render graph pass (generate + bilateral blur)
│   └── TaoTieRenderPipeline.cs    # Pipeline asset & render entry point
├── Editor/
│   ├── ShaderStripper.cs          # Build-time shader/SMAA stripping
│   ├── HighQualityAAModeDrawer.cs # AA mode dropdown (MSAA disabled in Deferred)
│   ├── TaoTieAssetCreator.cs      # One-click pipeline + Post FX asset creation
│   └── ...                        # Property drawers (ShowIf, EnumLabel, RenderingMode, etc.)
├── Shaders/
│   ├── ShaderLibrary/             # HLSL includes (Common, Lighting, BRDF, GI, ForwardPlus, etc.)
│   ├── Lit.shader                 # PBR lit shader (CustomLit, DeferredGBuffer, ShadowCaster, DepthOnly, Outline, Meta)
│   ├── Unlit.shader               # Unlit shader
│   ├── UnlitParticles.shader      # Particle shader (flipbook, soft particles, distortion)
│   ├── UIBlending.shader          # UI shader with custom blending
│   ├── DeferredLighting.shader   # Fullscreen deferred lighting pass
│   ├── PostFXStack.shader         # Post-processing (bloom, color grading, FXAA, SMAA)
│   ├── CameraRenderer.shader      # Internal blit/copy/depth operations
│   ├── TAA.shader                 # Temporal AA resolve
│   ├── Outline.shader             # Outline effect
│   ├── SSAO.shader               # SSAO (generate + horizontal/vertical bilateral blur)
│   ├── FXAAPass.hlsl              # FXAA fragment
│   ├── SMAAPass.hlsl              # SMAA 3-pass fragments
│   └── DepthOnlyPass.hlsl         # Depth-only pass for pre-pass
├── LWGUI/                         # Light Weight Shader GUI (material inspector)
└── Samples~/                      # Sample content (hidden from AssetDatabase, imported via Package Manager)
    ├── Examples/                  # Example scripts, pipeline asset, post-FX presets
    └── Scenes/                    # Example scenes (8 scenes)
```

### Render Pass Sequence

**Forward Path:**
```
LightingPass → SetupPass → [DepthPrePass] → GeometryPass(opaque) → SkyboxPass
→ ResolvePass(MSAA) → CopyAttachments → [SSAOPass] → GeometryPass(transparent)
→ UnsupportedShaders → ResolvePass → TAAResolvePass → PostFX → Final → Debug → Gizmos
```

**Deferred Path:**
```
LightingPass → SetupPass → GBufferPass → DeferredLightingPass
→ SkyboxPass → CopyAttachments → [SSAOPass] → GeometryPass(transparent)
→ PostFX → Final → Debug → Gizmos
```

---

## Getting Started

1. Clone this repository (or add as a submodule) into your Unity project's `Packages/` directory:
   ```bash
   cd YourUnityProject/Packages
   git clone https://github.com/526077247/TaoTieRP.git
   ```
   Or add it via `manifest.json`:
   ```json
   "com.taotie.render-pipelines": "https://github.com/526077247/TaoTieRP.git"
   ```
2. Open the project in Unity 2022.3.x or later
3. Import samples via **Window > Package Manager > TaoTie RP > Samples > Import** (or menu **TaoTie RP > Samples Importer...**)
4. Assign the pipeline asset (`Assets/Samples/TaoTie RP/1.0.0/TaoTie RP Samples/Examples/Tao Tie RP.asset`) in **Project Settings > Graphics > Scriptable Render Pipeline Asset**
   - Alternatively, use **Assets > Create > Rendering/TaoTie Pipeline** to create a new pipeline asset with default Post FX Settings in one click
5. Open any scene under `Assets/Samples/TaoTie RP/1.0.0/TaoTie RP Samples/Scenes/` to explore features

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
