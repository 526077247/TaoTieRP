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
        static readonly HashSet<string> stripKeywords = new()
        {
            "_COMPUTE_BUFFER",
        };

        public int callbackOrder => 0;

        public void OnProcessShader(
            Shader shader, ShaderSnippetData snippet,
            IList<ShaderCompilerData> data)
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL) return;

            // Meta Pass is only used for lightmap baking, not needed at runtime.
            if (snippet.passType == PassType.Meta)
            {
                data.Clear();
                return;
            }

            for (int i = data.Count - 1; i >= 0; i--)
            {
                ShaderKeyword[] keywords = data[i].shaderKeywordSet.GetShaderKeywords();
                foreach (ShaderKeyword kw in keywords)
                {
                    if (stripKeywords.Contains(kw.GetKeywordName()))
                    {
                        data.RemoveAt(i);
                        break;
                    }
                }
            }
        }
    }
}
