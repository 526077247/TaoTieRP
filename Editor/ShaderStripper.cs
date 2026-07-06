using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines.Editor
{
    public class ShaderStripper : IPreprocessShaders
    {
        private HashSet<string> debuggerShaders = new HashSet<string>()
        {
            "Hidden/TaoTie RP/ForwardPlus Debugger",
            "Hidden/TaoTie RP/Depth Debugger",
        };
        public int callbackOrder => 0;

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
