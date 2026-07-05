using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    public class CopyAttachmentsPass
    {
        static readonly ProfilingSampler sampler = new("Copy Attachments");

        static readonly int
            colorCopyID = Shader.PropertyToID("_CameraColorTexture"),
            depthCopyID = Shader.PropertyToID("_CameraDepthTexture");

        bool copyColor, copyDepth, useMSAA;

        CameraRendererCopier copier;

        TextureHandle colorAttachment, depthAttachment, colorCopy, depthCopy;
        TextureHandle rtColor, rtDepth;

        void Render(RenderGraphContext context)
        {
            CommandBuffer buffer = context.cmd;
            bool usedCopyByDrawing = false;
            if (copyColor)
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                buffer.Blit(colorAttachment, colorCopy);
                usedCopyByDrawing = true;
#else
                copier.Copy(buffer, colorAttachment, colorCopy, false);
#endif
                buffer.SetGlobalTexture(colorCopyID, colorCopy);
            }

            if (copyDepth)
            {
                if (useMSAA
#if UNITY_WEBGL && !UNITY_EDITOR
                    || true
#endif
                   )
                {
                    copier.CopyByDrawing(buffer, depthAttachment, depthCopy, true,
                        new Rect(0, 0, copier.Camera.pixelWidth, copier.Camera.pixelHeight));
                    usedCopyByDrawing = true;
                }
                else
                {
                    copier.Copy(buffer, depthAttachment, depthCopy, true);
                }
                buffer.SetGlobalTexture(depthCopyID, depthCopy);
            }

            if (CameraRendererCopier.RequiresRenderTargetResetAfterCopy || usedCopyByDrawing)
            {
                buffer.SetRenderTarget(
                    rtColor,
                    RenderBufferLoadAction.Load, RenderBufferStoreAction.Store,
                    rtDepth,
                    RenderBufferLoadAction.Load, RenderBufferStoreAction.Store
                );
            }

            context.renderContext.ExecuteCommandBuffer(buffer);
            buffer.Clear();
        }

        public static void Record(RenderGraph renderGraph, bool copyColor,
            bool copyDepth,
            CameraRendererCopier copier,
            in CameraRendererTextures textures,
            bool useMSAA = false)
        {
            if (copyColor || copyDepth)
            {
                using RenderGraphBuilder builder = renderGraph.AddRenderPass(
                    sampler.name, out CopyAttachmentsPass pass, sampler);
                pass.copyColor = copyColor;
                pass.copyDepth = copyDepth;
                pass.useMSAA = useMSAA;
                pass.copier = copier;

                pass.colorAttachment = builder.ReadTexture(
                    useMSAA ? textures.resolvedColorAttachment : textures.colorAttachment);
                pass.depthAttachment = builder.ReadTexture(textures.depthAttachment);
                pass.rtColor = textures.colorAttachment;
                pass.rtDepth = textures.depthAttachment;
                if (copyColor)
                {
                    pass.colorCopy = builder.WriteTexture(textures.colorCopy);
                }

                if (copyDepth)
                {
                    pass.depthCopy = builder.WriteTexture(textures.depthCopy);
                }

                builder.AllowPassCulling(true);
                builder.SetRenderFunc<CopyAttachmentsPass>(
                    static (pass, context) => pass.Render(context));
            }
        }
    }
}