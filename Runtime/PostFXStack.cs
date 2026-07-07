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

        readonly Dictionary<string, int> passIndexMap = new();

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

        /// <summary>
        /// 从 Material 的 shader 中构建 pass 名称→索引映射。
        /// 在 Settings 赋值后、渲染前调用。
        /// </summary>
        public void InitializePassMap()
        {
            passIndexMap.Clear();
            Material mat = Settings.Material;
            if (mat == null) return;
            for (int i = 0; i < mat.passCount; i++)
            {
                string name = mat.GetPassName(i);
                if (!string.IsNullOrEmpty(name))
                    passIndexMap[name] = i;
            }
        }

        /// <summary>
        /// 通过名称查找 shader pass 索引。
        /// 找不到时回退到 0 (Copy) 并警告。
        /// </summary>
        public int GetPassIndex(string passName)
        {
            if (passIndexMap.TryGetValue(passName, out int index))
                return index;
            Debug.LogWarning($"PostFX pass '{passName}' not found in shader. Falling back to pass 0.");
            return 0;
        }

        public void Draw(CommandBuffer buffer, RenderTargetIdentifier to, int passIndex)
        {
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
