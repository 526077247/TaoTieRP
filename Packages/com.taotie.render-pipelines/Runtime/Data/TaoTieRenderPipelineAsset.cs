using UnityEngine;
using UnityEngine.Rendering;

namespace TaoTie
{
    [CreateAssetMenu(menuName = "Rendering/TaoTie Pipeline")]
    public partial class TaoTieRenderPipelineAsset : RenderPipelineAsset
    {
        [SerializeField] TaoTieRenderPipelineSettings settings = new TaoTieRenderPipelineSettings();

        protected override RenderPipeline CreatePipeline()
        {
            return new TaoTieRenderPipeline(settings);
        }

    }
}