using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    public class PostFXStack
    {
        public static readonly int
            fxSourceId = Shader.PropertyToID("_PostFXSource"),
            fxSource2Id = Shader.PropertyToID("_PostFXSource2"),
            fxSourceTexelSizeId = Shader.PropertyToID("_PostFXSource_TexelSize"),
            finalSrcBlendId = Shader.PropertyToID("_FinalSrcBlend"),
            finalDstBlendId = Shader.PropertyToID("_FinalDstBlend");

        static readonly Rect fullViewRect = new(0f, 0f, 1f, 1f);

        Dictionary<string, int> passIndexMap = new();

        public CameraBufferSettings BufferSettings { get; set; }

        public Vector2Int BufferSize { get; set; }

        public Vector2Int SourceSize { get; set; }

        public Camera Camera { get; set; }

        public CameraSettings.FinalBlendMode FinalBlendMode { get; set; }

        public PostFXSettings Settings { get; set; }

        public int ColorLUTResolution { get; set; }

        /// <summary>Deferred path GBuffer normal texture (valid only when UseGBufferNormals is true).</summary>
        public TextureHandle GBufferNormalMS { get; set; }

        /// <summary>Whether the current rendering path uses G-Buffer normals (deferred).</summary>
        public bool UseGBufferNormals { get; set; }

        /// <summary>Whether HDR rendering is active (affects temp texture formats).</summary>
        public bool UseHDR { get; set; }

        /// <summary>MSAA sample count for temp textures.</summary>
        public MSAASamples MSAA { get; set; }

        /// <summary>方向光阴影图集，供需要采样阴影的后效使用 (如 Volumetric Fog)。</summary>
        public TextureHandle ShadowDirectionalAtlas { get; set; }

        /// <summary>点/聚光灯阴影图集。</summary>
        public TextureHandle ShadowOtherAtlas { get; set; }

        /// <summary>当前相机的 Volume 堆栈（由 CameraRenderer 更新），供 PostFXEffect 读取覆盖值。</summary>
        public VolumeStack VolumeStack { get; set; }

        /// <summary>当前相机的 Volume LayerMask（由 CameraRenderer 更新）。</summary>
        public LayerMask VolumeLayerMask { get; set; } = -1;

        /// <summary>
        /// 获取当前相机被 Volume 激活的 VolumeComponent。
        /// 没有任何 Volume 激活该组件时返回 null。
        /// </summary>
        public T GetActiveVolume<T>() where T : VolumeComponent
        {
            if (VolumeStack == null) return null;
            if (!VolumeManager.instance.IsComponentActiveInMask<T>(VolumeLayerMask)) return null;
            return VolumeStack.GetComponent<T>();
        }

        static readonly Dictionary<Material, Dictionary<string, int>> passMaps = new();

        Material lastMaterial;

        public void InitializePassMap()
        {
            if (Settings == null) return;
            Material mat = Settings.Material;
            if (mat == null) return;
            if (mat == lastMaterial && passIndexMap.Count > 0) return;
            lastMaterial = mat;
            if (!passMaps.TryGetValue(mat, out passIndexMap))
            {
                passIndexMap = new Dictionary<string, int>();
                for (int i = 0; i < mat.passCount; i++)
                {
                    string name = mat.GetPassName(i);
                    if (!string.IsNullOrEmpty(name))
                        passIndexMap[name] = i;
                }
                passMaps[mat] = passIndexMap;
            }
        }

        public int GetPassIndex(string passName)
        {
            if (passIndexMap.TryGetValue(passName, out int index))
                return index;
            Debug.LogWarning($"PostFX pass '{passName}' not found in shader. Falling back to pass 0.");
            return 0;
        }

        public void Draw(CommandBuffer buffer, RenderTargetIdentifier to, int passIndex)
        {
            if (Settings == null) return;
            Material mat = Settings.Material;
            if (mat == null || passIndex >= mat.passCount) return;
            buffer.SetRenderTarget(
                to, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
            buffer.Blit(null, to, mat, passIndex);
        }

        public void Draw(
            CommandBuffer buffer,
            RenderTargetIdentifier from,
            RenderTargetIdentifier to,
            int passIndex)
        {
            if (Settings == null) return;
            Material mat = Settings.Material;
            if (mat == null || passIndex >= mat.passCount) return;
            buffer.SetGlobalTexture(fxSourceId, from);
            buffer.SetGlobalVector(fxSourceTexelSizeId, new Vector4(
                1f / SourceSize.x, 1f / SourceSize.y, SourceSize.x, SourceSize.y));
            buffer.SetRenderTarget(
                to, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);

            buffer.Blit(from, to, mat, passIndex);
        }

        public void DrawFinal(
            CommandBuffer buffer,
            RenderTargetIdentifier from,
            int passIndex)
        {
            if (Settings == null) return;
            Material mat = Settings.Material;
            if (mat == null || passIndex >= mat.passCount) return;
            buffer.SetGlobalFloat(finalSrcBlendId, (float) FinalBlendMode.source);
            buffer.SetGlobalFloat(
                finalDstBlendId, (float) FinalBlendMode.destination);
            buffer.SetGlobalTexture(fxSourceId, from);
            buffer.SetGlobalVector(fxSourceTexelSizeId, new Vector4(
                1f / SourceSize.x, 1f / SourceSize.y, SourceSize.x, SourceSize.y));
            buffer.SetRenderTarget(
                BuiltinRenderTextureType.CameraTarget,
                FinalBlendMode.destination == BlendMode.Zero &&
                Camera.rect == fullViewRect
                    ? RenderBufferLoadAction.DontCare
                    : RenderBufferLoadAction.Load,
                RenderBufferStoreAction.Store);
            buffer.SetViewport(Camera.pixelRect);
            buffer.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
            buffer.DrawMesh(CameraRendererCopier.FullscreenMesh, Matrix4x4.identity, mat, 0, passIndex);
            buffer.SetViewProjectionMatrices(Camera.worldToCameraMatrix, Camera.projectionMatrix);
        }
    }
}
