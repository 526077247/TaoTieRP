using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines.Editor
{
    [CanEditMultipleObjects]
    [CustomEditorForRenderPipeline(typeof(Camera), typeof(TaoTieRenderPipelineAsset))]
    public class TaoTieCameraEditor : CameraEditor
    {
        SerializedProperty m_ClearFlags;
        SerializedProperty m_BackGroundColor;
        SerializedProperty m_NormalizedViewPortRect;
        SerializedProperty m_CullingMask;
        SerializedProperty m_Orthographic;
        SerializedProperty m_FieldOfView;
        SerializedProperty m_OrthographicSize;
        SerializedProperty m_NearClipPlane;
        SerializedProperty m_FarClipPlane;
        SerializedProperty m_Depth;
        SerializedProperty m_HDR;
        SerializedProperty m_TargetTexture;
        SerializedProperty m_TargetDisplay;
        SerializedProperty m_OcclusionCulling;

        static class Styles
        {
            public static readonly GUIContent HDRContent = new("Allow HDR",
                "HDR rendering is enabled when both this toggle and the pipeline asset's Allow HDR are on.");

            public static readonly GUIContent HiddenPropsInfo =
                new("Rendering Path, MSAA, Dynamic Resolution and Stereo Target Eye are controlled by the TaoTie RP pipeline asset.");

            public static readonly GUIContent TaoTieHeader =
                new("TaoTie RP Camera Settings");

            public static readonly GUIContent AddComponentBtn =
                new("Add TaoTie RP Camera Component");

            public static readonly GUIContent SetOverlayBtn =
                new("Set as Overlay Camera",
                    "Sets Clear Flags to Don't Clear and Final Blend Mode to Alpha Blend for overlay rendering.");
        }

        void InitProperties()
        {
            m_ClearFlags = serializedObject.FindProperty("m_ClearFlags");
            m_BackGroundColor = serializedObject.FindProperty("m_BackGroundColor");
            m_NormalizedViewPortRect = serializedObject.FindProperty("m_NormalizedViewPortRect");
            m_CullingMask = serializedObject.FindProperty("m_CullingMask");
            // Camera is a native component — some serialized names contain spaces.
            m_Orthographic = serializedObject.FindProperty("orthographic");
            m_FieldOfView = serializedObject.FindProperty("field of view");
            m_OrthographicSize = serializedObject.FindProperty("orthographic size");
            m_NearClipPlane = serializedObject.FindProperty("near clip plane");
            m_FarClipPlane = serializedObject.FindProperty("far clip plane");
            m_Depth = serializedObject.FindProperty("m_Depth");
            m_HDR = serializedObject.FindProperty("m_HDR");
            m_TargetTexture = serializedObject.FindProperty("m_TargetTexture");
            m_TargetDisplay = serializedObject.FindProperty("m_TargetDisplay");
            m_OcclusionCulling = serializedObject.FindProperty("m_OcclusionCulling");
        }

        public override void OnInspectorGUI()
        {
            InitProperties();

            serializedObject.Update();

            // --- Projection ---
            EditorGUILayout.PropertyField(m_Orthographic);
            if (m_Orthographic.boolValue)
                EditorGUILayout.PropertyField(m_OrthographicSize);
            else
                EditorGUILayout.PropertyField(m_FieldOfView);

            // --- Clipping Planes ---
            EditorGUILayout.PropertyField(m_NearClipPlane);
            EditorGUILayout.PropertyField(m_FarClipPlane);

            // --- Viewport Rect ---
            EditorGUILayout.PropertyField(m_NormalizedViewPortRect);

            // --- Depth ---
            EditorGUILayout.PropertyField(m_Depth);

            // --- Culling Mask ---
            EditorGUILayout.PropertyField(m_CullingMask);

            // --- Clear Flags & Background ---
            EditorGUILayout.PropertyField(m_ClearFlags);
            EditorGUILayout.PropertyField(m_BackGroundColor);

            // --- HDR (combined with pipeline setting) ---
            EditorGUILayout.PropertyField(m_HDR, Styles.HDRContent);

            // --- Target Texture & Display ---
            EditorGUILayout.PropertyField(m_TargetTexture);
            if (m_TargetTexture.objectReferenceValue == null)
                EditorGUILayout.PropertyField(m_TargetDisplay);

            // --- Occlusion Culling ---
            EditorGUILayout.PropertyField(m_OcclusionCulling);

            serializedObject.ApplyModifiedProperties();

            // --- Hidden properties note ---
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(Styles.HiddenPropsInfo.text, MessageType.Info);

            // --- TaoTie RP Camera settings ---
            EditorGUILayout.Space(8);
            DrawTaoTieCameraSettings();
        }

        void DrawTaoTieCameraSettings()
        {
            EditorGUILayout.LabelField(Styles.TaoTieHeader, EditorStyles.boldLabel);

            // Rebuild SerializedObject every frame so it survives domain reloads
            // and picks up component add/remove immediately.
            var taoTieCams = new List<Object>();
            foreach (var t in targets)
            {
                var cam = t as Camera;
                if (cam == null) continue;
                var c = cam.GetComponent<TaoTieRenderPipelineCamera>();
                if (c != null)
                    taoTieCams.Add(c);
            }

            if (taoTieCams.Count > 0)
            {
                var so = new SerializedObject(taoTieCams.ToArray());
                var settings = so.FindProperty("settings");
                so.Update();
                EditorGUILayout.PropertyField(settings, true);
                so.ApplyModifiedProperties();

                if (taoTieCams.Count < targets.Length)
                {
                    EditorGUILayout.HelpBox(
                        "Some selected cameras are missing the TaoTie RP Camera component.",
                        MessageType.Warning);
                }

                EditorGUILayout.Space(4);
                if (GUILayout.Button(Styles.SetOverlayBtn))
                {
                    SetAsOverlayCamera(targets, taoTieCams.ToArray());
                }
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "No TaoTie RP Camera component found. Default camera settings will be used.",
                    MessageType.Info);
                if (GUILayout.Button(Styles.AddComponentBtn))
                {
                    foreach (var t in targets)
                    {
                        var cam = t as Camera;
                        if (cam != null && cam.GetComponent<TaoTieRenderPipelineCamera>() == null)
                            Undo.AddComponent<TaoTieRenderPipelineCamera>(cam.gameObject);
                    }
                }
            }
        }

        static void SetAsOverlayCamera(Object[] targets, Object[] taoTieCams)
        {
            Undo.RecordObjects(targets, "Set as Overlay Camera");
            Undo.RecordObjects(taoTieCams, "Set as Overlay Camera");

            foreach (var t in targets)
            {
                if (t is not Camera cam) continue;
                cam.clearFlags = CameraClearFlags.Nothing;
                cam.backgroundColor = Color.clear;
                EditorUtility.SetDirty(cam);
            }

            foreach (var c in taoTieCams)
            {
                if (c is not TaoTieRenderPipelineCamera rpCam) continue;
                var s = rpCam.Settings;
                s.finalBlendMode = new CameraSettings.FinalBlendMode
                {
                    source = BlendMode.SrcAlpha,
                    destination = BlendMode.OneMinusSrcAlpha
                };
                EditorUtility.SetDirty(rpCam);
            }
        }
    }
}
