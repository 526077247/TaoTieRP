using System;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines.Editor
{
    [CanEditMultipleObjects]
    [CustomEditorForRenderPipeline(typeof(Light), typeof(TaoTieRenderPipelineAsset))]
    public class TaoTieLightEditor : LightEditor
    {
        static readonly GUIContent renderingLayerMaskLabel =
            new GUIContent("Rendering Layer Mask",
                "Controls which rendering layers this light affects. " +
                "Surface rendering layers are filtered via bitwise AND.\n\n" +
                "Limitation: Due to float32 precision, single-layer selection works for all 31 layers. " +
                "Multi-layer masks are reliable for Layers 1–24; combinations mixing Layers 1–24 with 25–31 may lose precision.");

        // Unity's built-in DrawRenderingLayerMask reads the MaskField selection
        // but never writes it back for uint properties. Null the backing field
        // before base.OnInspectorGUI() so the built-in call NREs before consuming
        // any layout space, then draw our working version.
        static readonly FieldInfo renderingLayerMaskField =
            typeof(LightEditor.Settings).GetField(
                "<renderingLayerMask>k__BackingField",
                BindingFlags.NonPublic | BindingFlags.Instance);

        public override void OnInspectorGUI() {
            SerializedProperty savedRLM = null;
            if (renderingLayerMaskField != null)
            {
                savedRLM = settings.renderingLayerMask;
                renderingLayerMaskField.SetValue(settings, null);
            }

            try
            {
                base.OnInspectorGUI();
            }
            catch (NullReferenceException)
            {
                // Expected: DrawRenderingLayerMask NREs on null property.
                // NRE occurs before GetControlRect(), so layout state is clean.
            }

            if (savedRLM != null)
            {
                renderingLayerMaskField.SetValue(settings, savedRLM);
                RenderingLayerMaskDrawer.Draw(savedRLM, renderingLayerMaskLabel);
            }
            
            if (
                !settings.lightType.hasMultipleDifferentValues &&
                (LightType)settings.lightType.enumValueIndex == LightType.Spot
            )
            {
                settings.DrawInnerAndOuterSpotAngle();
            }
            
            settings.ApplyModifiedProperties();
            var light = target as Light;
            if (light.cullingMask != -1) {
                EditorGUILayout.HelpBox(
                    light.type == LightType.Directional ?
                        "Culling Mask only affects shadows." :
                        "Culling Mask only affects shadow unless Use Lights Per Objects is on.",
                    MessageType.Warning
                );
            }
        }
        
    }
}