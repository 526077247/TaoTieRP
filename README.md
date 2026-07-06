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

| Path | Description |
|------|-------------|
| **Forward** | Default path with MSAA support, suitable for most platforms |
| **Deferred** | GBuffer-based path (requires MRT ≥ 3), disabled on WebGL/GLES2 |

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
- **FXAA** — Fast approximate anti-aliasing
- **Bicubic Rescaling** — Off / Up-only / Up-and-down
- Post-processing overrides per camera

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
- **WebGL/Mobile Compatibility** — ComputeBuffer→Texture2D fallback, no deferred on GLES2, graphics format fallbacks

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
