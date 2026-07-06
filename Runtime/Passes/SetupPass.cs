using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace TaoTie.RenderPipelines
{
    public class SetupPass
    {
        static readonly ProfilingSampler sampler = new("Setup");

        static readonly int attachmentSizeID = Shader.PropertyToID("_CameraBufferSize");
        
        TextureHandle colorAttachment, depthAttachment;
        Vector2Int attachmentSize;
        Camera camera;
        CameraClearFlags clearFlags;
        Vector2 jitter;
        bool useJitter;

        void Render(RenderGraphContext context)
        {
            context.renderContext.SetupCameraProperties(camera);
            CommandBuffer cmd = context.cmd;

            // Apply TAA jitter after SetupCameraProperties
            if (useJitter)
            {
                Matrix4x4 jitteredProj = camera.projectionMatrix;
                jitteredProj.m02 += jitter.x * 2f / attachmentSize.x;
                jitteredProj.m12 += jitter.y * 2f / attachmentSize.y;
                cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix, jitteredProj);
            }

            cmd.SetRenderTarget(
                colorAttachment,
                RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store,
                depthAttachment,
                RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
            cmd.SetViewport(new Rect(0, 0, attachmentSize.x, attachmentSize.y));
            
            cmd.ClearRenderTarget(
                clearFlags <= CameraClearFlags.Depth,
                clearFlags <= CameraClearFlags.Color,
                clearFlags == CameraClearFlags.Color ? camera.backgroundColor.linear : Color.clear
            );
            cmd.SetGlobalVector(attachmentSizeID, new Vector4(
                1f / attachmentSize.x, 1f / attachmentSize.y,
                attachmentSize.x, attachmentSize.y
            ));
            context.renderContext.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }

        public static CameraRendererTextures Record(RenderGraph renderGraph, bool copyColor, bool copyDepth,
            bool useHDR, Vector2Int attachmentSize, Camera camera, MSAASamples msaaSamples,
            Vector2 taaJitter = default, bool useTAA = false)
        {
            using RenderGraphBuilder builder =
                renderGraph.AddRenderPass(sampler.name, out SetupPass pass, sampler);
            pass.attachmentSize = attachmentSize;
            pass.camera = camera;
            pass.clearFlags = camera.clearFlags;
            pass.jitter = taaJitter;
            pass.useJitter = useTAA;

            TextureHandle colorCopy = default, depthCopy = default;

            if (pass.clearFlags > CameraClearFlags.Color)
            {
                pass.clearFlags = CameraClearFlags.Color;
            }

            var desc = new TextureDesc(attachmentSize.x, attachmentSize.y)
            {
                colorFormat = SystemInfo.GetGraphicsFormat(
                    useHDR ? DefaultFormat.HDR : DefaultFormat.LDR),
                msaaSamples = msaaSamples,
                name = "Color Attachment"
            };
            TextureHandle colorAttachment =
                pass.colorAttachment = builder.WriteTexture(renderGraph.CreateTexture(desc));

            TextureHandle resolvedColorAttachment;
            if (msaaSamples != MSAASamples.None)
            {
                desc.msaaSamples = MSAASamples.None;
                desc.name = "Resolved Color Attachment";
                resolvedColorAttachment = renderGraph.CreateTexture(desc);
            }
            else
            {
                resolvedColorAttachment = colorAttachment;
            }

            if (copyColor)
            {
                desc.msaaSamples = MSAASamples.None;
                desc.name = "Color Copy";
                colorCopy = renderGraph.CreateTexture(desc);
            }

            desc.msaaSamples = msaaSamples;
            desc.depthBufferBits = DepthBits.Depth32;
            desc.name = "Depth Attachment";
            TextureHandle depthAttachment =
                pass.depthAttachment = builder.WriteTexture(renderGraph.CreateTexture(desc));
            if (copyDepth)
            {
                desc.msaaSamples = MSAASamples.None;
                desc.name = "Depth Copy";
                depthCopy = renderGraph.CreateTexture(desc);
            }
            
            builder.AllowPassCulling(false);
            builder.SetRenderFunc<SetupPass>(static (pass, context) => pass.Render(context));
            return new CameraRendererTextures(colorAttachment, depthAttachment, colorCopy, depthCopy, resolvedColorAttachment);
        }

        public static GBufferTextures RecordGBuffer(
            RenderGraph renderGraph, bool useHDR, Vector2Int bufferSize,
            TextureHandle depthAttachment)
        {
            using RenderGraphBuilder builder =
                renderGraph.AddRenderPass("G-Buffer Setup", out SetupPass pass, sampler);
            pass.attachmentSize = bufferSize;

            // Use UNorm for normals since octahedral encoding is remapped to [0,1].
            GraphicsFormat albedoFormat = useHDR &&
                SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16A16_SFloat, FormatUsage.Render)
                ? GraphicsFormat.R16G16B16A16_SFloat
                : GraphicsFormat.R8G8B8A8_UNorm;
            GraphicsFormat normalFormat = albedoFormat;

            var desc = new TextureDesc(bufferSize.x, bufferSize.y)
            {
                colorFormat = albedoFormat,
                name = "G-Buffer Albedo AO"
            };
            TextureHandle albedoAO = renderGraph.CreateTexture(desc);

            desc.colorFormat = normalFormat;
            desc.name = "G-Buffer Normal Metallic";
            TextureHandle normalMS = renderGraph.CreateTexture(desc);

            desc.colorFormat = albedoFormat;
            desc.name = "G-Buffer Emission";
            TextureHandle emission = renderGraph.CreateTexture(desc);

            builder.SetRenderFunc<SetupPass>(static (pass, context) => {});
            return new GBufferTextures(albedoAO, normalMS, emission, depthAttachment);
        }
    }
}