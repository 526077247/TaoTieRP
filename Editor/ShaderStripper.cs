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

        const string SMAA_DISABLED_DEFINE = "SMAA_DISABLED";

        bool ShouldStripSMAA()
        {
            var rpAsset = GraphicsSettings.currentRenderPipeline as TaoTieRenderPipelineAsset;
            if (rpAsset == null) return true;

            // Check user setting
            if (!rpAsset.settings.stripSMAAWhenUnused)
                return false;

            // Strip when Post-Process AA is not SMAA
            return rpAsset.settings.cameraBuffer.postProcessAA !=
                CameraBufferSettings.PostProcessAA.SMAA;
        }

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            bool stripSMAA = ShouldStripSMAA();

            var targetGroup = BuildPipeline.GetBuildTargetGroup(
                EditorUserBuildSettings.activeBuildTarget);
            var defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(
                targetGroup);
            bool hasDefine = defines.Contains(SMAA_DISABLED_DEFINE);

            if (stripSMAA && !hasDefine)
            {
                if (string.IsNullOrEmpty(defines))
                    defines = SMAA_DISABLED_DEFINE;
                else
                    defines += ";" + SMAA_DISABLED_DEFINE;
                PlayerSettings.SetScriptingDefineSymbolsForGroup(targetGroup, defines);
            }
            else if (!stripSMAA && hasDefine)
            {
                defines = defines
                    .Replace(";" + SMAA_DISABLED_DEFINE, "")
                    .Replace(SMAA_DISABLED_DEFINE + ";", "")
                    .Replace(SMAA_DISABLED_DEFINE, "");
                PlayerSettings.SetScriptingDefineSymbolsForGroup(targetGroup, defines);
            }
        }

        public void OnProcessShader(
            Shader shader, ShaderSnippetData snippet,
            IList<ShaderCompilerData> data)
        {
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

            // Strip SMAA passes from Post FX Stack shader
            if (shader.name == "Hidden/TaoTie RP/Post FX Stack")
            {
                if (smaaPassNames.Contains(snippet.passName))
                {
                    data.Clear();
                    return;
                }
            }

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
            {
                for (int i = data.Count - 1; i >= 0; i--)
                {
                    ShaderKeyword[] keywords = data[i].shaderKeywordSet.GetShaderKeywords();
                    foreach (ShaderKeyword kw in keywords)
                    {
                        if ("_COMPUTE_BUFFER" == kw.GetKeywordName())
                        {
                            data.RemoveAt(i);
                            break;
                        }
                    }
                }
            }
        }
    }
}
