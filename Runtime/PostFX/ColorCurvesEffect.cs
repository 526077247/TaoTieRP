using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    [System.Serializable]
    public class ColorCurvesEffect : PostFXEffect
    {
        static readonly ProfilingSampler sampler = new("Color Curves");
        static readonly int
            ccSourceID = Shader.PropertyToID("_CCSource"),
            ccMasterID = Shader.PropertyToID("_CCMaster"),
            ccRGBID = Shader.PropertyToID("_CCRGB"),
            ccHueHueID = Shader.PropertyToID("_CCHueHue"),
            ccHueSatID = Shader.PropertyToID("_CCHueSat"),
            ccSatSatID = Shader.PropertyToID("_CCSatSat"),
            ccLumSatID = Shader.PropertyToID("_CCLumSat"),
            ccTexelSizeID = Shader.PropertyToID("_CCTexelSize");

        static Material ccMaterial;
        static Shader cachedShader;
        static bool disposedRegistered;

        [HideInInspector] public Shader ccShader;

        const int LUT_SIZE = 256;

        [System.Serializable]
        public struct ColorCurveSettings
        {
            public AnimationCurve master;
            public AnimationCurve red;
            public AnimationCurve green;
            public AnimationCurve blue;
            public AnimationCurve hueVsHue;
            public AnimationCurve hueVsSat;
            public AnimationCurve satVsSat;
            public AnimationCurve lumVsSat;
        }

        [SerializeField] public ColorCurveSettings settings = new ColorCurveSettings
        {
            master = AnimationCurve.Linear(0f, 0f, 1f, 1f),
            red = AnimationCurve.Linear(0f, 0f, 1f, 1f),
            green = AnimationCurve.Linear(0f, 0f, 1f, 1f),
            blue = AnimationCurve.Linear(0f, 0f, 1f, 1f),
            hueVsHue = AnimationCurve.Linear(0f, 0f, 1f, 0f),
            hueVsSat = AnimationCurve.Linear(0f, 0f, 1f, 1f),
            satVsSat = AnimationCurve.Linear(0f, 0f, 1f, 1f),
            lumVsSat = AnimationCurve.Linear(0f, 0f, 1f, 1f),
        };

        // Persistent LUT textures — only recreated when curves change
        Texture2D masterLUT, rgbLUT, hueHueLUT, hueSatLUT, satSatLUT, lumSatLUT;
        int bakedHash;
        bool lutsDirty = true;

        // Reusable pixel buffer (avoid per-frame array allocation)
        static readonly Color[] pixelsR = new Color[LUT_SIZE];
        static readonly Color[] pixelsRGBA = new Color[LUT_SIZE];

        public ColorCurveSettings Settings => settings;
        public override string DisplayName => "Color Curves";
        public override string ShaderName => "Hidden/TaoTie RP/Color Curves";
        public override IReadOnlyList<string> RequiredPassNames => System.Array.Empty<string>();

        public override void EnsureShaderReference()
        {
            if (ccShader == null)
                ccShader = Shader.Find(ShaderName);
        }

        /// <summary>
        /// Quick hash of curve lengths + a few sample points (no array allocation).
        /// Returns different hash when curves change.
        /// </summary>
        int GetCurvesHash()
        {
            int hash = 0;
            hash = Combine(hash, CurveHash(settings.master));
            hash = Combine(hash, CurveHash(settings.red));
            hash = Combine(hash, CurveHash(settings.green));
            hash = Combine(hash, CurveHash(settings.blue));
            hash = Combine(hash, CurveHash(settings.hueVsHue));
            hash = Combine(hash, CurveHash(settings.hueVsSat));
            hash = Combine(hash, CurveHash(settings.satVsSat));
            hash = Combine(hash, CurveHash(settings.lumVsSat));
            return hash;
        }

        static int Combine(int a, int b) => unchecked(a * 31 + b);

        static int CurveHash(AnimationCurve curve)
        {
            if (curve == null) return 0;
            int h = curve.length;
            // Sample at 5 probe points to detect value changes without allocating arrays
            h = Combine(h, Mathf.RoundToInt(curve.Evaluate(0f) * 1000));
            h = Combine(h, Mathf.RoundToInt(curve.Evaluate(0.25f) * 1000));
            h = Combine(h, Mathf.RoundToInt(curve.Evaluate(0.5f) * 1000));
            h = Combine(h, Mathf.RoundToInt(curve.Evaluate(0.75f) * 1000));
            h = Combine(h, Mathf.RoundToInt(curve.Evaluate(1f) * 1000));
            return h;
        }

        void EnsureLUTs()
        {
            int currentHash = GetCurvesHash();
            if (!lutsDirty && currentHash == bakedHash)
                return;

            lutsDirty = false;
            bakedHash = currentHash;

            BakeCurveInto(settings.master, pixelsR, false);
            EnsureTexture(ref masterLUT, TextureFormat.RFloat);
            masterLUT.SetPixels(pixelsR);
            masterLUT.Apply();

            BakeRGBInto(settings.red, settings.green, settings.blue, pixelsRGBA);
            EnsureTexture(ref rgbLUT, TextureFormat.RGBAFloat);
            rgbLUT.SetPixels(pixelsRGBA);
            rgbLUT.Apply();

            BakeCurveInto(settings.hueVsHue, pixelsR, false);
            EnsureTexture(ref hueHueLUT, TextureFormat.RFloat);
            hueHueLUT.SetPixels(pixelsR);
            hueHueLUT.Apply();

            BakeCurveInto(settings.hueVsSat, pixelsR, false);
            EnsureTexture(ref hueSatLUT, TextureFormat.RFloat);
            hueSatLUT.SetPixels(pixelsR);
            hueSatLUT.Apply();

            BakeCurveInto(settings.satVsSat, pixelsR, false);
            EnsureTexture(ref satSatLUT, TextureFormat.RFloat);
            satSatLUT.SetPixels(pixelsR);
            satSatLUT.Apply();

            BakeCurveInto(settings.lumVsSat, pixelsR, false);
            EnsureTexture(ref lumSatLUT, TextureFormat.RFloat);
            lumSatLUT.SetPixels(pixelsR);
            lumSatLUT.Apply();
        }

        static void EnsureTexture(ref Texture2D tex, TextureFormat format)
        {
            // Fall back to RGBA32 if the requested format isn't supported (WebGL/GLES)
            if (!SystemInfo.SupportsTextureFormat(format))
                format = TextureFormat.RGBA32;

            if (tex == null || tex.format != format)
            {
                if (tex != null) Object.DestroyImmediate(tex);
                tex = new Texture2D(LUT_SIZE, 1, format, false)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave
                };
            }
        }

        static void BakeCurveInto(AnimationCurve curve, Color[] buffer, bool isRGB)
        {
            for (int i = 0; i < LUT_SIZE; i++)
            {
                float t = (float)i / (LUT_SIZE - 1);
                float v = curve != null ? curve.Evaluate(t) : t;
                buffer[i] = new Color(v, 0, 0, 1);
            }
        }

        static void BakeRGBInto(AnimationCurve r, AnimationCurve g, AnimationCurve b, Color[] buffer)
        {
            for (int i = 0; i < LUT_SIZE; i++)
            {
                float t = (float)i / (LUT_SIZE - 1);
                buffer[i] = new Color(
                    r != null ? r.Evaluate(t) : t,
                    g != null ? g.Evaluate(t) : t,
                    b != null ? b.Evaluate(t) : t,
                    1);
            }
        }

        public override TextureHandle Execute(
            RenderGraph renderGraph, PostFXStack stack,
            TextureHandle source, in CameraRendererTextures textures)
        {
            if (!IsEnabled) return source;

            EnsureMaterial();
            if (ccMaterial == null) return source;

            EnsureLUTs();

            GraphicsFormat colorFormat = stack.UseHDR &&
                SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16A16_SFloat, FormatUsage.Render)
                ? GraphicsFormat.R16G16B16A16_SFloat
                : GraphicsFormat.R8G8B8A8_UNorm;

            using RenderGraphBuilder builder = renderGraph.AddRenderPass(
                sampler.name, out CCRenderPass pass, sampler);

            TextureHandle colorSource = source.IsValid() ? source : textures.colorAttachment;
            pass.source = builder.ReadTexture(colorSource);
            pass.camera = stack.Camera;
            pass.bufferSize = stack.BufferSize;
            pass.masterLUT = masterLUT;
            pass.rgbLUT = rgbLUT;
            pass.hueHueLUT = hueHueLUT;
            pass.hueSatLUT = hueSatLUT;
            pass.satSatLUT = satSatLUT;
            pass.lumSatLUT = lumSatLUT;

            var desc = new TextureDesc(stack.BufferSize.x, stack.BufferSize.y)
            {
                colorFormat = colorFormat,
                name = "Color Curves Result"
            };
            pass.resultTexture = builder.WriteTexture(renderGraph.CreateTexture(desc));

            builder.AllowPassCulling(false);
            builder.SetRenderFunc<CCRenderPass>(
                static (pass, context) => pass.Render(context));

            return pass.resultTexture;
        }

        void EnsureMaterial()
        {
            Shader shader = ccShader;
            if (shader == null)
            {
                if (cachedShader == null)
                    cachedShader = Shader.Find(ShaderName);
                shader = cachedShader;
            }
            else
            {
                cachedShader = shader;
            }
            if (ccMaterial == null || ccMaterial.shader != shader)
            {
                if (shader == null) { ccMaterial = null; return; }
                if (ccMaterial != null) CoreUtils.Destroy(ccMaterial);
                ccMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            if (!disposedRegistered)
            {
                disposedRegistered = true;
                RegisterDispose(Dispose);
            }
        }

        protected override void DisposeInternal() => Dispose();

        static void Dispose()
        {
            if (ccMaterial != null)
            {
                CoreUtils.Destroy(ccMaterial);
                ccMaterial = null;
            }
        }

        class CCRenderPass
        {
            public TextureHandle source;
            public Camera camera;
            public Vector2Int bufferSize;
            public Texture masterLUT, rgbLUT, hueHueLUT, hueSatLUT, satSatLUT, lumSatLUT;
            public TextureHandle resultTexture;

            public void Render(RenderGraphContext context)
            {
                CommandBuffer cmd = context.cmd;
                cmd.SetGlobalTexture(ccSourceID, source);
                cmd.SetGlobalTexture(ccMasterID, masterLUT);
                cmd.SetGlobalTexture(ccRGBID, rgbLUT);
                cmd.SetGlobalTexture(ccHueHueID, hueHueLUT);
                cmd.SetGlobalTexture(ccHueSatID, hueSatLUT);
                cmd.SetGlobalTexture(ccSatSatID, satSatLUT);
                cmd.SetGlobalTexture(ccLumSatID, lumSatLUT);
                cmd.SetGlobalVector(ccTexelSizeID, new Vector4(1f / LUT_SIZE, 1f, LUT_SIZE, 1f));

                cmd.SetRenderTarget(resultTexture,
                    RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
                cmd.SetViewport(new Rect(0, 0, bufferSize.x, bufferSize.y));
                cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
                cmd.DrawMesh(CameraRendererCopier.FullscreenMesh, Matrix4x4.identity, ccMaterial, 0, 0);
                cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix, camera.projectionMatrix);

                context.renderContext.ExecuteCommandBuffer(cmd);
                cmd.Clear();
            }
        }
    }
}
