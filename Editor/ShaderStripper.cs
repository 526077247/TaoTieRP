using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines.Editor
{
    public class ShaderStripper : IPreprocessShaders, IPreprocessBuildWithReport
    {
        HashSet<string> debuggerShaders = new HashSet<string>()
        {
            "Hidden/TaoTie RP/ForwardPlus Debugger",
            "Hidden/TaoTie RP/Depth Debugger",
            "Hidden/TaoTie RP/Overdraw",
            "Hidden/TaoTie RP/Overdraw Resolve",
        };

        static readonly HashSet<string> smaaPassNames = new HashSet<string>()
        {
            "SMAA Edge Detection",
            "SMAA Blend Weight",
            "SMAA Neighborhood Blending"
        };

        static readonly HashSet<string> fxaaPassNames = new HashSet<string>()
        {
            "FXAA"
        };

        static readonly HashSet<string> bloomPassNames = new HashSet<string>()
        {
            "Bloom Horizontal",
            "Bloom Vertical",
            "Bloom Add",
            "Bloom Prefilter",
            "Bloom PrefilterFireflies",
            "Bloom Scatter",
            "Bloom ScatterFinal"
        };

        static readonly HashSet<string> colorGradingPassNames = new HashSet<string>()
        {
            "ColorGrading None",
            "ColorGrading ACES",
            "ColorGrading Neutral",
            "ColorGrading Reinhard"
        };

        static readonly Dictionary<string, Type> dedicatedPostFXShaders = new()
        {
            { "Hidden/TaoTie RP/Depth Of Field", typeof(DepthOfFieldEffect) },
            { "Hidden/TaoTie RP/Chromatic Aberration", typeof(ChromaticAberrationEffect) },
            { "Hidden/TaoTie RP/Color Curves", typeof(ColorCurvesEffect) },
            { "Hidden/TaoTie RP/Film Grain", typeof(FilmGrainEffect) },
            { "Hidden/TaoTie RP/Lens Distortion", typeof(LensDistortionEffect) },
            { "Hidden/TaoTie RP/Motion Blur", typeof(MotionBlurEffect) },
            { "Hidden/TaoTie RP/Panini Projection", typeof(PaniniProjectionEffect) },
            { "Hidden/TaoTie RP/Pixelate", typeof(PixelateEffect) },
            { "Hidden/TaoTie RP/Posterize", typeof(PosterizeEffect) },
            { "Hidden/TaoTie RP/Sharpen", typeof(SharpenEffect) },
            { "Hidden/TaoTie RP/Vignette", typeof(VignetteEffect) },
            { "Hidden/TaoTie RP/Volumetric Fog", typeof(VolumetricFogEffect) },
            { "Hidden/TaoTie RP/Outline", typeof(OutlineEffect) },
        };

        const string SMAA_DISABLED_DEFINE = "SMAA_DISABLED";
        const string FXAA_DISABLED_DEFINE = "FXAA_DISABLED";
        const string BLOOM_DISABLED_DEFINE = "BLOOM_DISABLED";
        const string SSAO_DISABLED_DEFINE = "SSAO_DISABLED";

        TaoTieRenderPipelineSettings settings;

        // Cached build-time platform info
        BuildTarget buildTarget;
        bool isWebGL1;

        TaoTieRenderPipelineSettings GetSettings()
        {
            if (settings != null) return settings;
            var rpAsset = GraphicsSettings.currentRenderPipeline as TaoTieRenderPipelineAsset;
            if (rpAsset != null)
                settings = rpAsset.settings;
            return settings;
        }

        bool IsWebGL1()
        {
            // WebGL1 = WebGL build target without OpenGLES3 in GraphicsAPIs
            if (buildTarget != BuildTarget.WebGL) return false;
            GraphicsDeviceType[] apis = PlayerSettings.GetGraphicsAPIs(buildTarget);
            for (int i = 0; i < apis.Length; i++)
            {
                if (apis[i] == GraphicsDeviceType.OpenGLES3)
                    return false; // WebGL2
            }
            return true; // WebGL1 (GLES2 only)
        }

        bool IsForward()
        {
            if (buildTarget == BuildTarget.WebGL && isWebGL1) return true;
            var s = GetSettings();
            return s != null && s.renderingMode == TaoTieRenderPipelineSettings.RenderingMode.Forward;
        }

        bool IsDeferred()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return false;
#else
            var s = GetSettings();
            return s != null && s.renderingMode == TaoTieRenderPipelineSettings.RenderingMode.Deferred;
#endif
        }

        bool ShouldStripSMAA()
        {
            // SMAA uses SM3.0+ features not available on GLES2/WebGL1
            if (isWebGL1) return true;
            var s = GetSettings();
            if (s == null) return false;
            if (!s.cameraBuffer.stripSMAAWhenUnused) return false;
            return s.cameraBuffer.postProcessAA != CameraBufferSettings.PostProcessAA.SMAA;
        }

        bool ShouldStripFXAA()
        {
            var s = GetSettings();
            if (s == null) return false;
            return s.cameraBuffer.postProcessAA != CameraBufferSettings.PostProcessAA.FXAA;
        }

        bool ShouldStripTAA()
        {
            var s = GetSettings();
            if (s == null) return false;
            return s.cameraBuffer.highQualityAA != CameraBufferSettings.HighQualityAAMode.TAA;
        }

        bool ShouldStripSSAO()
        {
            var s = GetSettings();
            if (s == null) return false;
            return s.shadows?.ssao?.enabled != true;
        }

        bool ShouldStripBloom()
        {
            var s = GetSettings();
            if (s == null) return false;
            return s.postFXSettings == null;
        }

        bool ShouldStripShadows()
        {
            var s = GetSettings();
            if (s == null) return false;
            return s.shadows.directional.maxLightCount == 0;
        }

        bool ShouldStripLensFlare()
        {
            // Strip if no LensFlareData assets exist in the project
            var guids = AssetDatabase.FindAssets("t:LensFlareData");
            return guids.Length == 0;
        }

        bool ShouldStripForwardPlus()
        {
            var s = GetSettings();
            if (s == null) return false;
            if (s.shadows.forwardPlus == ShadowSettings.ForwardPlusMode.Off) return true;
            // Forward+ runtime guards against GLES2, but the shader variant
            // is still needed for GLES3/WebGL2. Strip only on WebGL1.
            if (isWebGL1) return true;
            return false;
        }

        bool ShouldStripPostFX()
        {
            var s = GetSettings();
            if (s == null) return false;
            return s.postFXSettings == null;
        }

        HashSet<Type> effectTypesInUse;
        bool effectTypesCached;

        HashSet<Type> GetEffectTypesInUse()
        {
            if (effectTypesCached) return effectTypesInUse;
            effectTypesCached = true;
            effectTypesInUse = new HashSet<Type>();

            // Check pipeline-level PostFXSettings
            var s = GetSettings();
            if (s?.postFXSettings != null)
                CollectEffectTypes(s.postFXSettings, effectTypesInUse);

            // Check all PostFXSettings assets in the project (camera-level overrides)
            var guids = AssetDatabase.FindAssets("t:PostFXSettings");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var postFX = AssetDatabase.LoadAssetAtPath<PostFXSettings>(path);
                if (postFX != null)
                    CollectEffectTypes(postFX, effectTypesInUse);
            }
            return effectTypesInUse;
        }

        static void CollectEffectTypes(PostFXSettings settings, HashSet<Type> set)
        {
            foreach (var effect in settings.Effects)
            {
                if (effect != null)
                    set.Add(effect.GetType());
            }
        }

        bool IsEffectInUse(Type effectType)
        {
            return GetEffectTypesInUse().Contains(effectType);
        }

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            // Reset effect type cache for fresh build
            effectTypesCached = false;
            effectTypesInUse = null;

            // Cache build target and detect WebGL1 (GLES2 only, no OpenGLES3)
            buildTarget = EditorUserBuildSettings.activeBuildTarget;
            isWebGL1 = IsWebGL1();

            bool stripSMAA = ShouldStripSMAA();
            bool stripFXAA = ShouldStripFXAA();
            bool stripTAA = ShouldStripTAA();
            bool stripSSAO = ShouldStripSSAO();

            var targetGroup = BuildPipeline.GetBuildTargetGroup(
                EditorUserBuildSettings.activeBuildTarget);
            var defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(
                targetGroup);

            defines = UpdateDefine(defines, SMAA_DISABLED_DEFINE, stripSMAA);
            defines = UpdateDefine(defines, FXAA_DISABLED_DEFINE, stripFXAA);
            defines = UpdateDefine(defines, SSAO_DISABLED_DEFINE, stripSSAO);

            PlayerSettings.SetScriptingDefineSymbolsForGroup(targetGroup, defines);
        }

        static string UpdateDefine(string defines, string define, bool shouldStrip)
        {
            bool hasDefine = defines.Contains(define);
            if (shouldStrip && !hasDefine)
            {
                if (string.IsNullOrEmpty(defines))
                    defines = define;
                else
                    defines += ";" + define;
            }
            else if (!shouldStrip && hasDefine)
            {
                defines = defines
                    .Replace(";" + define, "")
                    .Replace(define + ";", "")
                    .Replace(define, "");
            }
            return defines;
        }

        public void OnProcessShader(
            Shader shader, ShaderSnippetData snippet,
            IList<ShaderCompilerData> data)
        {
            if (data.Count == 0) return;

            // --- Always strip debugger shaders and Meta passes ---
            if (debuggerShaders.Contains(shader.name))
            {
                data.Clear();
                return;
            }

            if (snippet.passType == PassType.Meta)
            {
                data.Clear();
                return;
            }

            // --- Strip ShadowCaster pass when shadows are disabled ---
            if (snippet.passType == PassType.ShadowCaster && ShouldStripShadows())
            {
                data.Clear();
                return;
            }

            // --- Strip ShadowMask keyword when no shadowmask ---
            if (ShouldStripShadows())
            {
                StripKeyword(data, "_SHADOW_MASK");
            }

            // --- Strip dedicated PostFX shaders if their effect is not in any PostFXSettings queue ---
            if (dedicatedPostFXShaders.TryGetValue(shader.name, out var effectType))
            {
                if (!IsEffectInUse(effectType))
                {
                    data.Clear();
                    return;
                }
                return;
            }

            // --- Strip entire hidden shaders based on feature state ---
            switch (shader.name)
            {
                case "Hidden/TaoTie RP/TAA":
                    if (ShouldStripTAA()) data.Clear();
                    return;

                case "Hidden/TaoTie RP/SSAO":
                    if (ShouldStripSSAO()) data.Clear();
                    return;

                case "Hidden/TaoTie RP/Deferred Lighting":
                    if (!IsDeferred()) data.Clear();
                    return;

                case "Hidden/TaoTie RP/Lens Flare":
                    if (ShouldStripLensFlare()) data.Clear();
                    return;

                case "Hidden/TaoTie RP/Post FX Stack":
                    StripPostFXShader(shader, snippet, data);
                    return;
            }

            // --- Strip shader keywords for features that are disabled ---
            // Lit.shader and DeferredLighting.shader have _TAOTIE_FORWARD_PLUS keyword
            if (ShouldStripForwardPlus())
            {
                StripKeyword(data, "_TAOTIE_FORWARD_PLUS");
            }

            // _SSAO_ENABLED keyword on Lit and DeferredLighting
            if (ShouldStripSSAO())
            {
                StripKeyword(data, "_SSAO_ENABLED");
            }
            
            // --- Strip DeferredGBuffer pass from Lit.shader when in Forward mode ---
            if (shader.name == "TaoTie RP/Lit" && IsForward())
            {
                if (snippet.passName == "DeferredGBuffer")
                {
                    data.Clear();
                    return;
                }
            }

            // --- Strip DeferredGBuffer pass from Unlit.shader when in Forward mode ---
            if (shader.name == "TaoTie RP/Unlit" && IsForward())
            {
                if (snippet.passName == "DeferredGBuffer")
                {
                    data.Clear();
                    return;
                }
            }
        }

        void StripPostFXShader(
            Shader shader, ShaderSnippetData snippet,
            IList<ShaderCompilerData> data)
        {
            // Strip entire Post FX Stack if no PostFXSettings
            if (ShouldStripPostFX())
            {
                data.Clear();
                return;
            }

            // Strip SMAA passes
            if (smaaPassNames.Contains(snippet.passName) && ShouldStripSMAA())
            {
                data.Clear();
                return;
            }

            // Strip FXAA pass
            if (fxaaPassNames.Contains(snippet.passName) && ShouldStripFXAA())
            {
                data.Clear();
                return;
            }

            // Strip Bloom passes if BloomEffect not in any PostFXSettings queue
            if (bloomPassNames.Contains(snippet.passName) && !IsEffectInUse(typeof(BloomEffect)))
            {
                data.Clear();
                return;
            }

            // Strip ColorGrading passes if ColorGradingEffect not in any PostFXSettings queue
            if (colorGradingPassNames.Contains(snippet.passName) && !IsEffectInUse(typeof(ColorGradingEffect)))
            {
                data.Clear();
                return;
            }
        }

        static void StripKeyword(IList<ShaderCompilerData> data, string keywordName)
        {
            if (data.Count == 0) return;
            var kw = new ShaderKeyword(keywordName);
            for (int i = data.Count - 1; i >= 0; i--)
            {
                if (data[i].shaderKeywordSet.IsEnabled(kw))
                {
                    data.RemoveAt(i);
                }
            }
        }
    }
}
