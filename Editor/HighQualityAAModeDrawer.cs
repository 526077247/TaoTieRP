using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines.Editor
{
    [CustomPropertyDrawer(typeof(MSAAFieldAttribute))]
    public class HighQualityAAModeDrawer : PropertyDrawer
    {
        const int DeferredModeIndex = 1;

        bool IsDeferred(SerializedProperty property)
        {
            string path = property.propertyPath;
            int firstDot = path.IndexOf('.');
            if (firstDot >= 0)
            {
                string root = path.Substring(0, firstDot);
                SerializedProperty mode = property.serializedObject.FindProperty(root + ".renderingMode");
                if (mode != null)
                    return mode.enumValueIndex == DeferredModeIndex;
            }

            if (GraphicsSettings.currentRenderPipeline is TaoTieRenderPipelineAsset rpAsset)
                return rpAsset.settings.renderingMode ==
                    TaoTieRenderPipelineSettings.RenderingMode.Deferred;

            return false;
        }

        bool IsHighQualityAAMode(SerializedProperty property)
        {
            return property.type == "Enum" &&
                property.enumNames.Length == 3 &&
                property.enumDisplayNames[0] == "Off" &&
                property.enumDisplayNames[1] == "MSAA" &&
                property.enumDisplayNames[2] == "TAA";
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            // Hide non-HighQualityAAMode enum fields (e.g. msaaSamples) in deferred
            if (property.propertyType == SerializedPropertyType.Enum &&
                !IsHighQualityAAMode(property) &&
                IsDeferred(property))
            {
                return 0f;
            }
            return EditorGUI.GetPropertyHeight(property, label, true);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            // Skip non-HighQualityAAMode enum fields (e.g. msaaSamples) in deferred
            if (property.propertyType == SerializedPropertyType.Enum &&
                !IsHighQualityAAMode(property) &&
                IsDeferred(property))
            {
                return;
            }

            if (property.propertyType == SerializedPropertyType.Enum &&
                IsHighQualityAAMode(property))
            {
                bool deferred = IsDeferred(property);
                string[] names = property.enumDisplayNames;
                int current = property.enumValueIndex;

                EditorGUI.BeginProperty(position, label, property);

                Rect popupRect = EditorGUI.PrefixLabel(position, label);
                GUIContent displayLabel = new GUIContent(names[current]);
                if (deferred && current == 1)
                    displayLabel.text = "Off";

                if (EditorGUI.DropdownButton(popupRect, displayLabel, FocusType.Keyboard, EditorStyles.popup))
                {
                    var menu = new GenericMenu();
                    for (int i = 0; i < names.Length; i++)
                    {
                        if (deferred && i == 1)
                        {
                            menu.AddDisabledItem(new GUIContent(names[i]));
                        }
                        else
                        {
                            int idx = i;
                            menu.AddItem(new GUIContent(names[i]), i == current, () =>
                            {
                                property.serializedObject.Update();
                                property.enumValueIndex = idx;
                                property.serializedObject.ApplyModifiedProperties();
                            });
                        }
                    }
                    menu.DropDown(popupRect);
                }

                EditorGUI.EndProperty();
            }
            else
            {
                EditorGUI.PropertyField(position, property, label, true);
            }
        }
    }
}
