using UnityEngine.Experimental.Rendering.RenderGraphModule;

namespace TaoTie.RenderPipelines
{
    public readonly ref struct ShadowTextures
    {
        public readonly TextureHandle directionalAtlas, otherAtlas;

        public ShadowTextures(
            TextureHandle directionalAtlas,
            TextureHandle otherAtlas)
        {
            this.directionalAtlas = directionalAtlas;
            this.otherAtlas = otherAtlas;
        }
    }
}