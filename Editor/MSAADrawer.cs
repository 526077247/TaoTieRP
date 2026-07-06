using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines.Editor
{
    [CustomPropertyDrawer(typeof(MSAAFieldAttribute))]
    public class MSAADrawer : PropertyDrawer
    {
        // RenderingMode enum values: Forward = 0, Deferred = 1
        const int DeferredModeIndex = 1;

        bool IsDeferred(SerializedProperty property)
        {
            // Case 1: property is on the RP asset (e.g. "settings.cameraBuffer.msaa")
            // → look for sibling "settings.renderingMode"
            string path = property.propertyPath;
            int firstDot = path.IndexOf('.');
            if (firstDot >= 0)
            {
                string root = path.Substring(0, firstDot);
                SerializedProperty mode = property.serializedObject.FindProperty(root + ".renderingMode");
                if (mode != null)
                    return mode.enumValueIndex == DeferredModeIndex;
            }

            // Case 2: property is on a camera component (e.g. "settings.allowMSAA")
            // → read renderingMode from the active RP asset
            if (GraphicsSettings.currentRenderPipeline is TaoTieRenderPipelineAsset rpAsset)
                return rpAsset.settings.renderingMode ==
                    TaoTieRenderPipelineSettings.RenderingMode.Deferred;

            return false;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return IsDeferred(property) ? 0f : EditorGUI.GetPropertyHeight(property, label, true);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (IsDeferred(property)) return;
            EditorGUI.PropertyField(position, property, label, true);
        }
    }
}
