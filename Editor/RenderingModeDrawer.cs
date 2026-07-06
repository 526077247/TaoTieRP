using UnityEditor;
using UnityEngine;

namespace TaoTie.RenderPipelines.Editor
{
    [CustomPropertyDrawer(typeof(RenderingModeFieldAttribute))]
    public class RenderingModeDrawer : PropertyDrawer
    {
        static readonly string[] kOptionNames = { "Forward", "Deferred" };

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            bool isWebGL =
                EditorUserBuildSettings.selectedBuildTargetGroup == BuildTargetGroup.WebGL;
            
            if (isWebGL)
            {
                position = EditorGUI.PrefixLabel(position, label);
                var content = new GUIContent(kOptionNames[property.enumValueIndex]);
                if (EditorGUI.DropdownButton(position, content, FocusType.Passive))
                {
                    var menu = new GenericMenu();
                    menu.AddItem(
                        new GUIContent("Forward"),
                        property.enumValueIndex == 0,
                        () =>
                        {
                            property.enumValueIndex = 0;
                            property.serializedObject.ApplyModifiedProperties();
                        });
                    menu.AddItem(
                        new GUIContent("Deferred (Not supported on WebGL)"),
                        property.enumValueIndex == 1,
                        () =>
                        {
                            property.enumValueIndex = 1;
                            property.serializedObject.ApplyModifiedProperties();
                        });
                    menu.DropDown(position);
                }
            }
            else
            {
                EditorGUI.PropertyField(position, property, label);
            }

            EditorGUI.EndProperty();
        }
    }
}
