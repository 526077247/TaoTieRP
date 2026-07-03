using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    public partial class TaoTieRenderPipelineAsset
    {
#if UNITY_EDITOR

        static string[] renderingLayerNames;

        static TaoTieRenderPipelineAsset()
        {
            renderingLayerNames = new string[31];
            for (int i = 0; i < renderingLayerNames.Length; i++)
            {
                renderingLayerNames[i] = "Layer " + (i + 1);
            }
            ObjectChangeEvents.changesPublished += OnObjectChanges;
        }

        public override string[] renderingLayerMaskNames => renderingLayerNames;

        static void OnObjectChanges(ref ObjectChangeEventStream stream)
        {
            var rpAsset = GraphicsSettings.currentRenderPipeline as TaoTieRenderPipelineAsset;
            if (rpAsset == null || rpAsset.settings.defaultMaterial == null) return;

            Material defaultMat = rpAsset.settings.defaultMaterial;
            for (int i = 0; i < stream.length; ++i)
            {
                if (stream.GetEventType(i) == ObjectChangeKind.CreateGameObjectHierarchy)
                {
                    stream.GetCreateGameObjectHierarchyEvent(i, out var createEvent);
                    var go = EditorUtility.InstanceIDToObject(createEvent.instanceId) as GameObject;
                    if (go == null) continue;
                    var renderer = go.GetComponent<Renderer>();
                    if (renderer == null) continue;
                    if (renderer.sharedMaterial == null ||
                        renderer.sharedMaterial.shader.name == "Standard")
                    {
                        Undo.RecordObject(renderer, "Assign Default Material");
                        renderer.sharedMaterial = defaultMat;
                    }
                }
            }
        }

        void OnValidate()
        {
            UnityEditor.SceneView.RepaintAll();
            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
        }

#endif
    }
}