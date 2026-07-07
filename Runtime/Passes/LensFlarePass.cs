using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    public class LensFlarePass
    {
        static readonly ProfilingSampler sampler = new("Lens Flare");

        static readonly int
            flareTextureID = Shader.PropertyToID("_FlareTexture"),
            flareDataID = Shader.PropertyToID("_FlareData"),
            flareColorID = Shader.PropertyToID("_FlareColor"),
            flarePolygonSidesID = Shader.PropertyToID("_FlarePolygonSides");

        static Material flareMaterial;
        static Shader cachedShader;

        // Reusable mesh for drawing flare quads
        static Mesh flareQuad;

        struct FlareDraw
        {
            public LensFlareElement element;
            public Vector2 screenPos;       // [0,1] screen UV
            public float screenDepth;      // linear eye depth at flare source
            public Color color;
            public float intensity;
            public float scale;
            public bool onScreen;
            public float occlusionFade;
        }

        List<FlareDraw> draws = new();

        Camera camera;
        Vector2Int bufferSize;
        TextureHandle colorTarget;
        TextureHandle depthTexture;
        bool useDepthTexture;

        static Mesh GetQuad()
        {
            if (flareQuad != null) return flareQuad;
            flareQuad = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            flareQuad.vertices = new Vector3[] {
                new(-1, -1, 0), new(1, -1, 0), new(1, 1, 0), new(-1, 1, 0)
            };
            flareQuad.uv = new Vector2[] {
                new(0, 0), new(1, 0), new(1, 1), new(0, 1)
            };
            flareQuad.triangles = new int[] { 0, 1, 2, 0, 2, 3 };
            return flareQuad;
        }

        static void EnsureMaterial()
        {
            if (cachedShader == null)
                cachedShader = Shader.Find("Hidden/TaoTie RP/Lens Flare");
            if (cachedShader == null)
            {
                flareMaterial = null;
                return;
            }
            if (flareMaterial == null || flareMaterial.shader != cachedShader)
            {
                if (flareMaterial != null) CoreUtils.Destroy(flareMaterial);
                flareMaterial = new Material(cachedShader) { hideFlags = HideFlags.HideAndDontSave };
            }
        }

        void Render(RenderGraphContext context)
        {
            if (flareMaterial == null || draws.Count == 0) return;

            CommandBuffer cmd = context.cmd;
            Mesh quad = GetQuad();

            for (int i = 0; i < draws.Count; i++)
            {
                var draw = draws[i];
                if (!draw.onScreen && draw.occlusionFade <= 0f) continue;

                var elem = draw.element;
                float intensity = draw.intensity * elem.intensity * draw.occlusionFade;

                // Direction from screen center to flare position
                Vector2 center = new(0.5f, 0.5f);
                Vector2 dir = (draw.screenPos - center);
                float dirMag = dir.magnitude;
                if (dirMag > 0.001f)
                    dir /= dirMag;
                else
                    dir = Vector2.zero;

                // Position along the axis: position=0 at light, position=1 at opposite end
                Vector2 flareScreenPos = draw.screenPos + dir * (elem.position * elem.translationScale);

                // Convert to clip space [-1, 1]
                Vector2 clipPos = new(
                    flareScreenPos.x * 2f - 1f,
                    flareScreenPos.y * 2f - 1f
                );

                float size = elem.sizeScale * draw.scale * 0.1f;

                // Build matrix: scale + translate
                Matrix4x4 matrix = Matrix4x4.TRS(
                    new Vector3(clipPos.x, clipPos.y, 0),
                    Quaternion.Euler(0, 0, elem.rotation + elem.angularOffset),
                    new Vector3(size, size, 1)
                );

                // Set material properties
                if (elem.type == LensFlareElement.ElementType.Image && elem.imageTexture != null)
                    cmd.SetGlobalTexture(flareTextureID, elem.imageTexture);
                else
                    cmd.SetGlobalTexture(flareTextureID, Texture2D.whiteTexture);

                float typeIndex = elem.type switch
                {
                    LensFlareElement.ElementType.Image => 0f,
                    LensFlareElement.ElementType.Circle => 1f,
                    LensFlareElement.ElementType.Polygon => 2f,
                    _ => 1f
                };

                cmd.SetGlobalVector(flareDataID, new Vector4(
                    intensity,
                    (elem.rotation + elem.angularOffset) * Mathf.Deg2Rad,
                    (float)bufferSize.x / bufferSize.y,
                    typeIndex));
                cmd.SetGlobalColor(flareColorID, draw.color * elem.tint);
                cmd.SetGlobalFloat(flarePolygonSidesID, elem.polygonSides);

                // Set blend mode
                switch (elem.blendMode)
                {
                    case LensFlareElement.BlendMode.Additive:
                        flareMaterial.SetPass(0);
                        break;
                    default:
                        flareMaterial.SetPass(0);
                        break;
                }

                cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
                cmd.DrawMesh(quad, matrix, flareMaterial, 0, 0);
                cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix, camera.projectionMatrix);
            }

            context.renderContext.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }

        public static void Record(
            RenderGraph renderGraph,
            Camera camera,
            in CameraRendererTextures textures,
            bool useDepthTexture,
            Vector2Int bufferSize,
            bool useHDR)
        {
            // Collect all active LensFlareComponents (static registration, no FindObjectsByType)
            var components = LensFlareComponent.ActiveComponents;

            if (components == null || components.Count == 0) return;

            EnsureMaterial();
            if (flareMaterial == null) return;

            using RenderGraphBuilder builder = renderGraph.AddRenderPass(
                sampler.name, out LensFlarePass pass, sampler);

            pass.camera = camera;
            pass.bufferSize = bufferSize;
            pass.useDepthTexture = useDepthTexture;
            pass.colorTarget = builder.UseColorBuffer(textures.colorAttachment, 0);
            if (useDepthTexture)
            {
                TextureHandle depthTex = textures.depthCopy.IsValid()
                    ? textures.depthCopy
                    : textures.depthAttachment;
                pass.depthTexture = builder.ReadTexture(depthTex);
            }
            pass.draws.Clear();

            // Build draw list
            int componentCount = components.Count;
            for (int i = 0; i < componentCount; i++)
            {
                var comp = components[i];
                if (comp.flareData == null) continue;

                // World-to-screen position
                Vector3 worldPos = comp.WorldPosition;
                Vector3 screenPos = camera.WorldToViewportPoint(worldPos);
                bool onScreen = screenPos.z > 0 &&
                    screenPos.x >= 0 && screenPos.x <= 1 &&
                    screenPos.y >= 0 && screenPos.y <= 1;

                // For off-screen flares, still allow if allowOffScreen
                if (!onScreen && !comp.allowOffScreen)
                    continue;

                float depth = screenPos.z; // linear eye depth
                Color color = comp.GetColor();
                float intensity = comp.intensity;
                float scale = comp.scale;

                float occlusionFade = 1f;
                if (!onScreen)
                    occlusionFade = 0f;

                var elements = comp.flareData.elements;
                if (elements == null) continue;

                for (int j = 0; j < elements.Length; j++)
                {
                    pass.draws.Add(new FlareDraw
                    {
                        element = elements[j],
                        screenPos = new Vector2(screenPos.x, screenPos.y),
                        screenDepth = depth,
                        color = color,
                        intensity = intensity,
                        scale = scale,
                        onScreen = onScreen,
                        occlusionFade = occlusionFade,
                    });
                }
            }

            if (pass.draws.Count == 0) return;

            builder.AllowPassCulling(false);
            builder.SetRenderFunc<LensFlarePass>(
                static (pass, context) => pass.Render(context));
        }

        public static void Dispose()
        {
            if (flareMaterial != null)
            {
                CoreUtils.Destroy(flareMaterial);
                flareMaterial = null;
            }
            if (flareQuad != null)
            {
                CoreUtils.Destroy(flareQuad);
                flareQuad = null;
            }
        }
    }
}
