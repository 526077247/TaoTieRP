using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace TaoTie.RenderPipelines
{
    public partial class TaoTieRenderPipelineAsset
    {
        partial void CreatePipelineForEditor();
#if UNITY_EDITOR

        partial void CreatePipelineForEditor()
        {
            EnsureComputeShader(ref settings.forwardPlusCullCompute);
        }
        
        static string[] renderingLayerNames;

        static TaoTieRenderPipelineAsset()
        {
            renderingLayerNames = new string[31];
            for (int i = 0; i < renderingLayerNames.Length; i++)
            {
                renderingLayerNames[i] = "Layer " + (i + 1);
            }
            ObjectChangeEvents.changesPublished += OnObjectChanges;
            EditorApplication.projectChanged += OnProjectChanged;
            UnityEditor.SceneManagement.EditorSceneManager.sceneOpened += OnSceneOpened;
            // Scan already-open scenes after compilation/domain reload
            EditorApplication.delayCall += EnsureCamerasInAllScenes;
        }

        static Material LoadDefaultMaterial()
        {
            return AssetDatabase.LoadAssetAtPath<Material>(
                AssetDatabase.GUIDToAssetPath("154398cb26997bf4ebf4af5ff1db036e"));
        }

        static void OnProjectChanged()
        {
            var guids = AssetDatabase.FindAssets("t:TaoTieRenderPipelineAsset");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<TaoTieRenderPipelineAsset>(path);
                if (asset != null)
                {
                    bool dirty = false;
                    if (asset.settings.defaultMaterial == null)
                    {
                        asset.settings.defaultMaterial = LoadDefaultMaterial();
                        dirty = true;
                    }
                    if (asset.settings.forwardPlusCullCompute == null)
                    {
                        EnsureComputeShader(ref asset.settings.forwardPlusCullCompute);
                        if (asset.settings.forwardPlusCullCompute != null)
                            dirty = true;
                    }
                    if (dirty)
                    {
                        EditorUtility.SetDirty(asset);
                        AssetDatabase.SaveAssetIfDirty(asset);
                    }
                }
            }
        }

        public override string[] renderingLayerMaskNames => renderingLayerNames;

        static void OnObjectChanges(ref ObjectChangeEventStream stream)
        {
            for (int i = 0; i < stream.length; ++i)
            {
                if (stream.GetEventType(i) == ObjectChangeKind.CreateAssetObject)
                {
                    stream.GetCreateAssetObjectEvent(i, out var createEvent);
                    var obj = EditorUtility.InstanceIDToObject(createEvent.instanceId);
                    if (obj is TaoTieRenderPipelineAsset rpAsset)
                    {
                        bool dirty = false;
                        if (rpAsset.settings.defaultMaterial == null)
                        {
                            rpAsset.settings.defaultMaterial = LoadDefaultMaterial();
                            dirty = true;
                        }
                        if (rpAsset.settings.forwardPlusCullCompute == null)
                        {
                            EnsureComputeShader(ref rpAsset.settings.forwardPlusCullCompute);
                            if (rpAsset.settings.forwardPlusCullCompute != null)
                                dirty = true;
                        }
                        if (dirty)
                        {
                            EditorUtility.SetDirty(rpAsset);
                            AssetDatabase.SaveAssetIfDirty(rpAsset);
                        }
                    }
                }
                else if (stream.GetEventType(i) == ObjectChangeKind.CreateGameObjectHierarchy)
                {
                    var rpAsset = GraphicsSettings.currentRenderPipeline as TaoTieRenderPipelineAsset;
                    if (rpAsset == null || rpAsset.settings.defaultMaterial == null) continue;

                    Material defaultMat = rpAsset.settings.defaultMaterial;
                    stream.GetCreateGameObjectHierarchyEvent(i, out var createEvent);
                    var go = EditorUtility.InstanceIDToObject(createEvent.instanceId) as GameObject;
                    if (go == null) continue;

                    // Assign default material to renderers
                    var renderer = go.GetComponent<Renderer>();
                    if (renderer != null &&
                        (renderer.sharedMaterial == null ||
                         renderer.sharedMaterial.shader.name == "Standard"))
                    {
                        Undo.RecordObject(renderer, "Assign Default Material");
                        renderer.sharedMaterial = defaultMat;
                    }

                    // Auto-add TaoTieRenderPipelineCamera to cameras
                    EnsureCameraComponent(go);
                }
                else if (stream.GetEventType(i) == ObjectChangeKind.ChangeGameObjectStructure)
                {
                    stream.GetChangeGameObjectStructureEvent(i, out var changeEvent);
                    var go = EditorUtility.InstanceIDToObject(changeEvent.instanceId) as GameObject;
                    if (go != null)
                        EnsureCameraComponent(go);
                }
            }
        }

        static void EnsureCameraComponent(GameObject go)
        {
            var camera = go.GetComponent<Camera>();
            if (camera != null && camera.GetComponent<TaoTieRenderPipelineCamera>() == null)
            {
                Undo.AddComponent<TaoTieRenderPipelineCamera>(go);
            }
        }

        static void OnSceneOpened(Scene scene, UnityEditor.SceneManagement.OpenSceneMode mode)
        {
            EnsureCamerasInScene(scene);
        }

        static void EnsureCamerasInScene(Scene scene)
        {
            if (GraphicsSettings.currentRenderPipeline is not TaoTieRenderPipelineAsset) return;

            bool changed = false;
            foreach (var go in scene.GetRootGameObjects())
            {
                foreach (var camera in go.GetComponentsInChildren<Camera>(true))
                {
                    if (camera.GetComponent<TaoTieRenderPipelineCamera>() == null)
                    {
                        Undo.AddComponent<TaoTieRenderPipelineCamera>(camera.gameObject);
                        changed = true;
                    }
                }
            }
            if (changed)
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        }

        static void EnsureCamerasInAllScenes()
        {
            if (GraphicsSettings.currentRenderPipeline is not TaoTieRenderPipelineAsset) return;

            for (int i = 0; i < UnityEditor.SceneManagement.EditorSceneManager.sceneCount; i++)
            {
                EnsureCamerasInScene(UnityEditor.SceneManagement.EditorSceneManager.GetSceneAt(i));
            }
        }
        
        static void EnsureComputeShader(ref ComputeShader field)
        {
            if (field != null) return;
            var guids = AssetDatabase.FindAssets("ForwardPlusCulling t:ComputeShader");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("com.taotie.render-pipelines"))
                {
                    field = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
                    if (field != null) return;
                }
            }
        }
        void OnValidate()
        {
            if (settings.defaultMaterial == null)
            {
                settings.defaultMaterial = LoadDefaultMaterial();
            }
            if (settings.forwardPlusCullCompute == null)
            {
                EnsureComputeShader(ref settings.forwardPlusCullCompute);
            }
            UnityEditor.SceneView.RepaintAll();
            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
        }

#endif
    }
}