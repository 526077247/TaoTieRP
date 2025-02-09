namespace TaoTie.RenderPipelines
{
    public partial class TaoTieRenderPipelineAsset
    {
#if UNITY_EDITOR

        static string[] renderingLayerNames;

        static TaoTieRenderPipelineAsset()
        {
            renderingLayerNames = new string[31];
            for (int i = 0; i < renderingLayerNames.Length; i++)
            {
                renderingLayerNames[i] = "Layer " + (i + 1);
            }
        }

        public override string[] renderingLayerMaskNames => renderingLayerNames;

#endif
    }
}