using System.Collections.Generic;
using UnityEngine;
using System;
using UnityEditor;

namespace TaoTie.RenderPipelines
{
    public class PostFXSettings : ScriptableObject
    {
        [SerializeReference]
        [SerializeField]
        List<PostFXEffect> effects = new();

        public IReadOnlyList<PostFXEffect> Effects => effects;

        [HideInInspector]
        public Shader shader = default;

        [System.NonSerialized] Material material;

        static Shader cachedShader;

        public Material Material
        {
            get
            {
                if (cachedShader == null)
                    cachedShader = Shader.Find("Hidden/TaoTie RP/Post FX Stack");
                if (cachedShader == null)
                {
                    Debug.LogError("Hidden/TaoTie RP/Post FX Stack shader not found!");
                    return null;
                }
                if (shader == null || shader != cachedShader)
                {
                    shader = cachedShader;
                    material = null;
                }
                if (material == null)
                {
                    material = new Material(shader);
                    material.hideFlags = HideFlags.HideAndDontSave;
                }

                return material;
            }
        }

        public static bool AreApplicableTo(Camera camera)
        {
#if UNITY_EDITOR
            if (camera.cameraType == CameraType.SceneView &&
                !SceneView.currentDrawingSceneView.sceneViewState.showImageEffects)
            {
                return false;
            }
#endif
            return true;
        }

        void OnEnable()
        {
            if (effects == null || effects.Count == 0)
            {
                effects = new List<PostFXEffect>();
#if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(this);
#endif
            }
#if UNITY_EDITOR
            EnsureEffectShaders();
#endif
        }

#if UNITY_EDITOR
        void EnsureEffectShaders()
        {
            if (effects == null) return;
            foreach (var effect in effects)
            {
                effect?.EnsureShaderReference();
            }
        }

        void OnValidate()
        {
            EnsureEffectShaders();
            UnityEditor.SceneView.RepaintAll();
            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
        }
#endif
    }
}
