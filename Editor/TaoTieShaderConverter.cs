using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines.Editor
{
    public class TaoTieShaderConverter : EditorWindow
    {
        static class Styles
        {
            public static readonly GUIContent ScanButton = new("Scan Project", "Find all materials using Built-in or URP shaders that can be converted to TaoTie RP shaders.");
            public static readonly GUIContent ConvertButton = new("Convert Selected", "Convert all checked materials to their TaoTie RP equivalents.");
            public static readonly GUIContent SelectAll = new("All", "Select all materials.");
            public static readonly GUIContent SelectNone = new("None", "Deselect all materials.");
        }

        struct ConvertibleMaterial
        {
            public Material material;
            public string sourceShaderName;
            public string targetShaderName;
            public bool selected;
        }

        static readonly Dictionary<string, string> ShaderMap = new()
        {
            // Built-in
            { "Standard",                       "TaoTie RP/Lit" },
            { "Standard (Specular setup)",      "TaoTie RP/Lit" },
            { "Legacy Shaders/Diffuse",         "TaoTie RP/Lit" },
            { "Legacy Shaders/Specular",        "TaoTie RP/Lit" },
            { "Legacy Shaders/Bumped Diffuse",  "TaoTie RP/Lit" },
            { "Legacy Shaders/Bumped Specular", "TaoTie RP/Lit" },
            { "Legacy Shaders/Transparent/Diffuse",  "TaoTie RP/Lit" },
            { "Legacy Shaders/Transparent/Cutout",   "TaoTie RP/Lit" },
            { "Unlit/Color",                    "TaoTie RP/Unlit" },
            { "Unlit/Texture",                  "TaoTie RP/Unlit" },
            { "Unlit/Transparent",              "TaoTie RP/Unlit" },
            { "Unlit/Transparent Cutout",       "TaoTie RP/Unlit" },
            { "UI/Default",                     "TaoTie RP/UI TaoTie Blending" },
            { "UI/Default Font",                "TaoTie RP/UI TaoTie Blending" },
            // URP
            { "Universal Render Pipeline/Lit",           "TaoTie RP/Lit" },
            { "Universal Render Pipeline/Simple Lit",    "TaoTie RP/Lit" },
            { "Universal Render Pipeline/Baked Lit",     "TaoTie RP/Lit" },
            { "Universal Render Pipeline/Unlit",         "TaoTie RP/Unlit" },
            { "Universal Render Pipeline/Particles/Unlit", "TaoTie RP/Particles/Unlit" },
            { "Universal Render Pipeline/Particles/Lit",   "TaoTie RP/Particles/Unlit" },
        };

        // Property name remapping: source name → target name
        static readonly Dictionary<string, string> PropertyRemap = new()
        {
            { "_MainTex", "_BaseMap" },
            { "_Color",   "_BaseColor" },
            { "_Glossiness", "_Smoothness" },
            { "_GlossMapScale", "_Smoothness" },
        };

        List<ConvertibleMaterial> items = new();
        Vector2 scroll;
        string statusText = string.Empty;

        [MenuItem("Edit/Rendering/Convert Shaders to TaoTie RP")]
        static void Open()
        {
            var w = GetWindow<TaoTieShaderConverter>("TaoTie Shader Converter");
            w.minSize = new Vector2(520, 320);
        }

        void OnGUI()
        {
            DrawToolbar();

            if (items.Count > 0)
            {
                DrawSelectionButtons();
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            for (int i = 0; i < items.Count; i++)
            {
                DrawItemRow(i);
            }
            if (items.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No materials scanned yet. Click \"Scan Project\" to find materials using Built-in or URP shaders.",
                    MessageType.Info);
            }
            EditorGUILayout.EndScrollView();

            if (!string.IsNullOrEmpty(statusText))
            {
                EditorGUILayout.HelpBox(statusText, MessageType.Info);
            }
        }

        void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button(Styles.ScanButton, EditorStyles.toolbarButton))
                {
                    ScanProject();
                }
                EditorGUI.BeginDisabledGroup(items.Count == 0);
                if (GUILayout.Button(Styles.ConvertButton, EditorStyles.toolbarButton))
                {
                    ConvertSelected();
                }
                EditorGUI.EndDisabledGroup();
                GUILayout.FlexibleSpace();
            }
        }

        void DrawSelectionButtons()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(Styles.SelectAll, GUILayout.Width(60)))
                {
                    for (int i = 0; i < items.Count; i++)
                    {
                        var item = items[i];
                        item.selected = true;
                        items[i] = item;
                    }
                }
                if (GUILayout.Button(Styles.SelectNone, GUILayout.Width(60)))
                {
                    for (int i = 0; i < items.Count; i++)
                    {
                        var item = items[i];
                        item.selected = false;
                        items[i] = item;
                    }
                }
                GUILayout.Label($"{items.Count} material(s) found", EditorStyles.miniLabel);
            }
        }

        void DrawItemRow(int index)
        {
            var item = items[index];
            using (new EditorGUILayout.HorizontalScope())
            {
                item.selected = EditorGUILayout.Toggle(item.selected, GUILayout.Width(20));
                var matLabel = new GUIContent(
                    item.material != null ? item.material.name : "(missing)",
                    AssetDatabase.GetAssetPath(item.material));
                EditorGUILayout.ObjectField(matLabel, item.material, typeof(Material), false, GUILayout.Width(160));
                EditorGUILayout.LabelField(item.sourceShaderName, EditorStyles.miniLabel, GUILayout.Width(160));
                EditorGUILayout.LabelField("→", GUILayout.Width(20));
                EditorGUILayout.LabelField(item.targetShaderName, EditorStyles.boldLabel);
            }
            items[index] = item;
        }

        void ScanProject()
        {
            items.Clear();
            var guids = AssetDatabase.FindAssets("t:Material");
            int found = 0;
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null || mat.shader == null) continue;
                if (ShaderMap.TryGetValue(mat.shader.name, out var targetName))
                {
                    items.Add(new ConvertibleMaterial
                    {
                        material = mat,
                        sourceShaderName = mat.shader.name,
                        targetShaderName = targetName,
                        selected = true,
                    });
                    found++;
                }
            }
            statusText = $"Found {found} convertible material(s).";
            Repaint();
        }

        void ConvertSelected()
        {
            int converted = 0, skipped = 0;
            var converter = new MaterialConverter();

            foreach (var item in items)
            {
                if (!item.selected || item.material == null) { skipped++; continue; }

                Undo.RecordObject(item.material, "Convert to TaoTie RP Shader");

                Shader targetShader = Shader.Find(item.targetShaderName);
                if (targetShader == null)
                {
                    Debug.LogWarning($"Target shader not found: {item.targetShaderName} (material: {item.material.name})", item.material);
                    skipped++;
                    continue;
                }

                converter.Convert(item.material, targetShader, item.sourceShaderName);
                EditorUtility.SetDirty(item.material);
                converted++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            statusText = $"Converted {converted} material(s), skipped {skipped}.";
        }

        /// <summary>
        /// Handles the actual material property transfer for a single material.
        /// </summary>
        class MaterialConverter
        {
            /// <summary>Source property names cached from the original shader before swap.</summary>
            Dictionary<string, (ShaderUtil.ShaderPropertyType type, object value)> sourceProps;

            public void Convert(Material material, Shader targetShader, string sourceShaderName)
            {
                CacheSourceProperties(material);
                material.shader = targetShader;
                TransferProperties(material, targetShader);
                ApplySpecialHandling(material, sourceShaderName);
            }

            void CacheSourceProperties(Material mat)
            {
                sourceProps = new();
                var srcShader = mat.shader;
                int count = ShaderUtil.GetPropertyCount(srcShader);
                for (int i = 0; i < count; i++)
                {
                    var type = ShaderUtil.GetPropertyType(srcShader, i);
                    string name = ShaderUtil.GetPropertyName(srcShader, i);
                    sourceProps[name] = type switch
                    {
                        ShaderUtil.ShaderPropertyType.Color   => (type, (object)mat.GetColor(name)),
                        ShaderUtil.ShaderPropertyType.Vector  => (type, (object)mat.GetVector(name)),
                        ShaderUtil.ShaderPropertyType.Float   => (type, (object)mat.GetFloat(name)),
                        ShaderUtil.ShaderPropertyType.Range   => (type, (object)mat.GetFloat(name)),
                        ShaderUtil.ShaderPropertyType.TexEnv => (type, (object)mat.GetTexture(name)),
                        _ => (type, null),
                    };
                }
            }

            void TransferProperties(Material mat, Shader targetShader)
            {
                int count = ShaderUtil.GetPropertyCount(targetShader);
                for (int i = 0; i < count; i++)
                {
                    string targetName = ShaderUtil.GetPropertyName(targetShader, i);
                    var targetType = ShaderUtil.GetPropertyType(targetShader, i);

                    // Try direct name match, then try remapped name
                    string sourceName = targetName;
                    if (!sourceProps.ContainsKey(sourceName))
                    {
                        // Reverse-lookup: is there a remap whose value equals targetName?
                        foreach (var kvp in PropertyRemap)
                        {
                            if (kvp.Value == targetName && sourceProps.ContainsKey(kvp.Key))
                            {
                                sourceName = kvp.Key;
                                break;
                            }
                        }
                    }

                    if (!sourceProps.TryGetValue(sourceName, out var src)) continue;
                    if (src.type != targetType) continue;

                    switch (targetType)
                    {
                        case ShaderUtil.ShaderPropertyType.Color:   mat.SetColor(targetName, (Color)src.value); break;
                        case ShaderUtil.ShaderPropertyType.Vector:  mat.SetVector(targetName, (Vector4)src.value); break;
                        case ShaderUtil.ShaderPropertyType.Float:
                        case ShaderUtil.ShaderPropertyType.Range:   mat.SetFloat(targetName, (float)src.value); break;
                        case ShaderUtil.ShaderPropertyType.TexEnv:
                            var tex = (Texture)src.value;
                            mat.SetTexture(targetName, tex);
                            // Transfer tiling/offset if both source and target use _BaseMap
                            if (sourceName == "_MainTex" || sourceName == "_BaseMap")
                            {
                                var st = sourceProps.ContainsKey(sourceName + "_ST")
                                    ? mat.GetVector(sourceName + "_ST")
                                    : new Vector4(1, 1, 0, 0);
                                mat.SetTextureScale(targetName, new Vector2(st.x, st.y));
                                mat.SetTextureOffset(targetName, new Vector2(st.z, st.w));
                            }
                            break;
                    }
                }
            }

            void ApplySpecialHandling(Material mat, string sourceShaderName)
            {
                bool isLit = mat.shader.name == "TaoTie RP/Lit";

                // Alpha clipping
                bool hasCutoff = sourceProps.ContainsKey("_Cutoff") || sourceProps.ContainsKey("_AlphaClip");
                if (hasCutoff)
                {
                    float cutoff = sourceProps.ContainsKey("_Cutoff")
                        ? (float)sourceProps["_Cutoff"].value
                        : 0.5f;
                    mat.SetFloat("_Cutoff", cutoff);
                    mat.SetFloat("_Clipping", 1);
                    mat.EnableKeyword("_CLIPPING");
                }
                else
                {
                    mat.SetFloat("_Clipping", 0);
                    mat.DisableKeyword("_CLIPPING");
                }

                // Normal map
                if (isLit)
                {
                    bool hasBump = sourceProps.ContainsKey("_BumpMap") &&
                                   (Texture)sourceProps["_BumpMap"].value != null;
                    mat.SetFloat("_NormalMapToggle", hasBump ? 1 : 0);
                    if (hasBump)
                        mat.EnableKeyword("_NORMAL_MAP");
                    else
                        mat.DisableKeyword("_NORMAL_MAP");
                }

                // Blend mode — Built-in Standard _Mode
                // 0=Opaque, 1=Cutout, 2=Fade, 3=Transparent
                if (sourceProps.TryGetValue("_Mode", out var modeProp))
                {
                    int mode = (int)(float)modeProp.value;
                    switch (mode)
                    {
                        case 0: // Opaque
                            mat.SetFloat("_SrcBlend", (float)BlendMode.One);
                            mat.SetFloat("_DstBlend", (float)BlendMode.Zero);
                            mat.SetFloat("_ZWrite", 1);
                            mat.SetFloat("_Clipping", 0);
                            mat.DisableKeyword("_CLIPPING");
                            mat.DisableKeyword("_PREMULTIPLY_ALPHA");
                            break;
                        case 1: // Cutout
                            mat.SetFloat("_SrcBlend", (float)BlendMode.One);
                            mat.SetFloat("_DstBlend", (float)BlendMode.Zero);
                            mat.SetFloat("_ZWrite", 1);
                            mat.SetFloat("_Clipping", 1);
                            mat.EnableKeyword("_CLIPPING");
                            mat.DisableKeyword("_PREMULTIPLY_ALPHA");
                            break;
                        case 2: // Fade
                            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                            mat.SetFloat("_ZWrite", 0);
                            mat.SetFloat("_Clipping", 0);
                            mat.DisableKeyword("_CLIPPING");
                            mat.DisableKeyword("_PREMULTIPLY_ALPHA");
                            break;
                        case 3: // Transparent
                            mat.SetFloat("_SrcBlend", (float)BlendMode.One);
                            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                            mat.SetFloat("_ZWrite", 0);
                            mat.SetFloat("_Clipping", 0);
                            mat.DisableKeyword("_CLIPPING");
                            mat.EnableKeyword("_PREMULTIPLY_ALPHA");
                            break;
                    }
                }

                // URP surface type: _Surface 0=Opaque, 1=Transparent
                // URP blend mode: _Blend 0=Alpha, 1=Premultiply, 2=Additive, 3=Multiply
                if (sourceProps.TryGetValue("_Surface", out var surfProp))
                {
                    int surface = (int)(float)surfProp.value;
                    if (surface == 1)
                    {
                        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                        mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                        mat.SetFloat("_ZWrite", 0);
                    }
                    else
                    {
                        mat.SetFloat("_SrcBlend", (float)BlendMode.One);
                        mat.SetFloat("_DstBlend", (float)BlendMode.Zero);
                        mat.SetFloat("_ZWrite", 1);
                    }
                }

                // Shadow settings for Lit
                if (isLit)
                {
                    mat.SetFloat("_ReceiveShadows", 1);
                }
            }
        }
    }
}
