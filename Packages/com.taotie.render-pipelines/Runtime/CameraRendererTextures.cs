using UnityEngine.Experimental.Rendering.RenderGraphModule;

namespace TaoTie.RenderPipelines
{
    public readonly ref struct CameraRendererTextures
    {
        public readonly TextureHandle
            colorAttachment,
            depthAttachment,
            colorCopy,
            depthCopy,
            resolvedColorAttachment;

        public CameraRendererTextures(
            TextureHandle colorAttachment,
            TextureHandle depthAttachment,
            TextureHandle colorCopy,
            TextureHandle depthCopy,
            TextureHandle resolvedColorAttachment)
        {
            this.colorAttachment = colorAttachment;
            this.depthAttachment = depthAttachment;
            this.colorCopy = colorCopy;
            this.depthCopy = depthCopy;
            this.resolvedColorAttachment = resolvedColorAttachment;
        }
    }
}