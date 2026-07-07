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

        const string SMAA_DISABLED_DEFINE = "SMAA_DISABLED";
        const string FXAA_DISABLED_DEFINE = "FXAA_DISABLED";
        const string BLOOM_DISABLED_DEFINE = "BLOOM_DISABLED";
        const string SSAO_DISABLED_DEFINE = "SSAO_DISABLED";

        TaoTieRenderPipelineSettings settings;

        TaoTieRenderPipelineSettings GetSettings()
        {
            if (settings != null) return settings;
            var rpAsset = GraphicsSettings.currentRenderPipeline as TaoTieRenderPipelineAsset;
            if (rpAsset != null)
                settings = rpAsset.settings;
            return settings;
        }

        bool IsForward()
        {
            var s = GetSettings();
            return s != null && s.renderingMode == TaoTieRenderPipelineSettings.RenderingMode.Forward;
        }

        bool IsDeferred()
        {
            var s = GetSettings();
            return s != null && s.renderingMode == TaoTieRenderPipelineSettings.RenderingMode.Deferred;
        }

        bool ShouldStripSMAA()
        {
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

        bool ShouldStripForwardPlus()
        {
            var s = GetSettings();
            if (s == null) return false;
            return !s.shadows.useForwardPlus ||
                   SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES2;
        }

        bool ShouldStripPostFX()
        {
            var s = GetSettings();
            if (s == null) return false;
            return s.postFXSettings == null;
        }

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
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
                    if (IsForward()) data.Clear();
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
