using UnityEngine.Experimental.Rendering.RenderGraphModule;

namespace TaoTie.RenderPipelines
{
    public readonly ref struct GBufferTextures
    {
        public readonly TextureHandle
            albedoAO,
            normalMetallicSmoothness,
            emission,
            depthAttachment;

        public GBufferTextures(
            TextureHandle albedoAO,
            TextureHandle normalMetallicSmoothness,
            TextureHandle emission,
            TextureHandle depthAttachment)
        {
            this.albedoAO = albedoAO;
            this.normalMetallicSmoothness = normalMetallicSmoothness;
            this.emission = emission;
            this.depthAttachment = depthAttachment;
        }
    }
}
