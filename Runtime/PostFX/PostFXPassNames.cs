namespace TaoTie.RenderPipelines
{
    /// <summary>
    /// Shader pass 名称常量。与 PostFXStack.shader 中 Pass { Name "..." } 对应。
    /// 不再使用整数枚举 — 索引在运行时从 Material 通过名称查找。
    /// </summary>
    public static class PostFXPassNames
    {
        public const string Copy = "Copy";
        public const string BloomHorizontal = "Bloom Horizontal";
        public const string BloomVertical = "Bloom Vertical";
        public const string BloomAdd = "Bloom Add";
        public const string BloomPrefilter = "Bloom Prefilter";
        public const string BloomPrefilterFireflies = "Bloom PrefilterFireflies";
        public const string BloomScatter = "Bloom Scatter";
        public const string BloomScatterFinal = "Bloom ScatterFinal";
        public const string ColorGradingNone = "ColorGrading None";
        public const string ColorGradingACES = "ColorGrading ACES";
        public const string ColorGradingNeutral = "ColorGrading Neutral";
        public const string ColorGradingReinhard = "ColorGrading Reinhard";
        public const string ApplyColorGrading = "Apply Color Grading";
        public const string FinalRescale = "Final Rescale";
        public const string FXAA = "FXAA";
        public const string SMAAEdgeDetection = "SMAA Edge Detection";
        public const string SMAABlendWeightCalculation = "SMAA Blend Weight";
        public const string SMAANeighborhoodBlending = "SMAA Neighborhood Blending";
    }
}
