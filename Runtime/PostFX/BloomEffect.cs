using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    [System.Serializable]
    public class BloomEffect : PostFXEffect
    {
        const int maxBloomPyramidLevels = 16;

        static readonly int
            bicubicUpsamplingId = Shader.PropertyToID("_BloomBicubicUpsampling"),
            intensityId = Shader.PropertyToID("_BloomIntensity"),
            thresholdId = Shader.PropertyToID("_BloomThreshold");

        static readonly ProfilingSampler sampler = new("Bloom");

        static readonly string[] requiredPasses =
        {
            PostFXPassNames.BloomPrefilter,
            PostFXPassNames.BloomPrefilterFireflies,
            PostFXPassNames.BloomHorizontal,
            PostFXPassNames.BloomVertical,
            PostFXPassNames.BloomAdd,
            PostFXPassNames.BloomScatter,
            PostFXPassNames.BloomScatterFinal
        };

        [System.Serializable]
        public struct BloomSettings
        {
            [Range(0f, 16f)] public int maxIterations;
            [ShowIf(nameof(maxIterations), ShowIfOperator.NotEqual, 0)]
            public bool ignoreRenderScale;
            [Min(1f)]
            [ShowIf(nameof(maxIterations), ShowIfOperator.NotEqual, 0)]
            public int downscaleLimit;
            [ShowIf(nameof(maxIterations), ShowIfOperator.NotEqual, 0)]
            public bool bicubicUpsampling;

            [Min(0f)]
            [ShowIf(nameof(maxIterations), ShowIfOperator.NotEqual, 0)]
            public float threshold;
            [ShowIf(nameof(maxIterations), ShowIfOperator.NotEqual, 0)]
            [Range(0f, 1f)] public float thresholdKnee;
            [Min(0f)]
            [ShowIf(nameof(maxIterations), ShowIfOperator.NotEqual, 0)]
            public float intensity;
            [ShowIf(nameof(maxIterations), ShowIfOperator.NotEqual, 0)]
            public bool fadeFireflies;

            public enum Mode
            {
                Additive,
                Scattering
            }
            [ShowIf(nameof(maxIterations), ShowIfOperator.NotEqual, 0)]
            public Mode mode;
            [ShowIf(nameof(maxIterations), ShowIfOperator.NotEqual, 0)]
            [Range(0.05f, 0.95f)] public float scatter;
        }

        [SerializeField] public BloomSettings settings = new BloomSettings
        {
            maxIterations = 16,
            downscaleLimit = 2,
            bicubicUpsampling = true,
            threshold = 1f,
            thresholdKnee = 0.5f,
            intensity = 0.2f,
            fadeFireflies = true,
            mode = BloomSettings.Mode.Scattering,
            scatter = 0.7f
        };

        public BloomSettings Settings => settings;

        public override string DisplayName => "Bloom";

        public override IReadOnlyList<string> RequiredPassNames => requiredPasses;

        public override TextureHandle Execute(
            RenderGraph renderGraph,
            PostFXStack stack,
            TextureHandle source,
            in CameraRendererTextures textures)
        {
            if (!IsEnabled || settings.maxIterations == 0 || settings.intensity <= 0f)
                return source;

            Vector2Int sourceSize = settings.ignoreRenderScale
                ? new Vector2Int(stack.Camera.pixelWidth, stack.Camera.pixelHeight)
                : stack.BufferSize;
            Vector2Int size = sourceSize / 2;

            if (size.y < settings.downscaleLimit * 2 || size.x < settings.downscaleLimit * 2)
                return source;

            using RenderGraphBuilder builder = renderGraph.AddRenderPass(
                sampler.name, out BloomRenderPass pass, sampler);
            pass.stack = stack;
            pass.bloomSettings = settings;
            pass.colorSource = builder.ReadTexture(source);
            pass.sourceSize = sourceSize;

            var desc = new TextureDesc(size.x, size.y)
            {
                colorFormat = SystemInfo.GetGraphicsFormat(
                    stack.BufferSettings.allowHDR ? DefaultFormat.HDR : DefaultFormat.LDR),
                name = "Bloom Prefilter"
            };
            BloomRenderPass.pyramid[0] = builder.CreateTransientTexture(desc);
            BloomRenderPass.pyramidSizes[0] = size;
            size /= 2;

            int pyramidIndex = 1;
            int i;
            for (i = 0; i < settings.maxIterations; i++, pyramidIndex += 2)
            {
                if (size.y < settings.downscaleLimit || size.x < settings.downscaleLimit)
                    break;

                desc.width = size.x;
                desc.height = size.y;
                desc.name = "Bloom Pyramid H";
                BloomRenderPass.pyramid[pyramidIndex] = builder.CreateTransientTexture(desc);
                BloomRenderPass.pyramidSizes[pyramidIndex] = size;
                desc.name = "Bloom Pyramid V";
                BloomRenderPass.pyramid[pyramidIndex + 1] = builder.CreateTransientTexture(desc);
                BloomRenderPass.pyramidSizes[pyramidIndex + 1] = size;
                size /= 2;
            }

            pass.stepCount = i;

            desc.width = stack.BufferSize.x;
            desc.height = stack.BufferSize.y;
            desc.name = "Bloom Result";
            pass.bloomResult = builder.WriteTexture(renderGraph.CreateTexture(desc));

            builder.SetRenderFunc<BloomRenderPass>(
                static (pass, context) => pass.Render(context));
            return pass.bloomResult;
        }

        class BloomRenderPass
        {
            const int maxBloomPyramidLevels = 16;

            public static readonly TextureHandle[] pyramid =
                new TextureHandle[2 * maxBloomPyramidLevels + 1];

            public static readonly Vector2Int[] pyramidSizes =
                new Vector2Int[2 * maxBloomPyramidLevels + 1];

            public Vector2Int sourceSize;
            public TextureHandle colorSource, bloomResult;
            public PostFXStack stack;
            public BloomEffect.BloomSettings bloomSettings;
            public int stepCount;

            public void Render(RenderGraphContext context)
            {
                CommandBuffer buffer = context.cmd;
                BloomEffect.BloomSettings bloom = bloomSettings;

                Vector4 threshold;
                threshold.x = Mathf.GammaToLinearSpace(bloom.threshold);
                threshold.y = threshold.x * bloom.thresholdKnee;
                threshold.z = 2f * threshold.y;
                threshold.w = 0.25f / (threshold.y + 0.00001f);
                threshold.y -= threshold.x;
                buffer.SetGlobalVector(thresholdId, threshold);

                stack.SourceSize = sourceSize;
                stack.Draw(buffer, colorSource, pyramid[0],
                    bloom.fadeFireflies
                        ? stack.GetPassIndex(PostFXPassNames.BloomPrefilterFireflies)
                        : stack.GetPassIndex(PostFXPassNames.BloomPrefilter));

                int fromId = 0, toId = 2;
                int i;
                for (i = 0; i < stepCount; i++)
                {
                    int midId = toId - 1;
                    stack.SourceSize = pyramidSizes[fromId];
                    stack.Draw(buffer, pyramid[fromId], pyramid[midId],
                        stack.GetPassIndex(PostFXPassNames.BloomHorizontal));
                    stack.SourceSize = pyramidSizes[midId];
                    stack.Draw(buffer, pyramid[midId], pyramid[toId],
                        stack.GetPassIndex(PostFXPassNames.BloomVertical));
                    fromId = toId;
                    toId += 2;
                }

                buffer.SetGlobalFloat(
                    bicubicUpsamplingId, bloom.bicubicUpsampling ? 1f : 0f);

                int combinePass, finalPass;
                float finalIntensity;
                if (bloom.mode == BloomEffect.BloomSettings.Mode.Additive)
                {
                    combinePass = finalPass = stack.GetPassIndex(PostFXPassNames.BloomAdd);
                    buffer.SetGlobalFloat(intensityId, 1f);
                    finalIntensity = bloom.intensity;
                }
                else
                {
                    combinePass = stack.GetPassIndex(PostFXPassNames.BloomScatter);
                    finalPass = stack.GetPassIndex(PostFXPassNames.BloomScatterFinal);
                    buffer.SetGlobalFloat(intensityId, bloom.scatter);
                    finalIntensity = Mathf.Min(bloom.intensity, 1f);
                }

                if (i > 1)
                {
                    toId -= 5;
                    for (i -= 1; i > 0; i--)
                    {
                        buffer.SetGlobalTexture(PostFXStack.fxSource2Id, pyramid[toId + 1]);
                        stack.SourceSize = pyramidSizes[fromId];
                        stack.Draw(buffer, pyramid[fromId], pyramid[toId], combinePass);
                        fromId = toId;
                        toId -= 2;
                    }
                }

                buffer.SetGlobalFloat(intensityId, finalIntensity);
                buffer.SetGlobalTexture(PostFXStack.fxSource2Id, colorSource);
                stack.SourceSize = pyramidSizes[fromId];
                stack.Draw(buffer, pyramid[fromId], bloomResult, finalPass);
            }
        }
    }
}
