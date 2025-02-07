using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;

namespace TaoTie
{
    [CanEditMultipleObjects]
    [CustomEditorForRenderPipeline(typeof(Light), typeof(TaoTieRenderPipelineAsset))]
    public class TaoTieLightEditor : LightEditor
    {
        static GUIContent renderingLayerMaskLabel =
            new GUIContent("Rendering Layer Mask", "Functional version of above property.");
        
        public override void OnInspectorGUI() {
            base.OnInspectorGUI();
            RenderingLayerMaskDrawer.Draw(
                settings.renderingLayerMask, renderingLayerMaskLabel
            );
            
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