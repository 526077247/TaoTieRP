using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace TaoTie.RenderPipelines.Editor
{
    [CustomPropertyDrawer(typeof(EnumLabelAttribute))]
    public class EnumLabelDrawer : PropertyDrawer
    {
        string[] displayNames;
        bool initialized;

        void Init()
        {
            if (initialized) return;

            Type enumType = fieldInfo.FieldType;
            if (!enumType.IsEnum)
            {
                initialized = true;
                return;
            }

            string[] names = Enum.GetNames(enumType);
            displayNames = new string[names.Length];

            for (int i = 0; i < names.Length; i++)
            {
                FieldInfo field = enumType.GetField(names[i]);
                var attr = field?.GetCustomAttribute<LabelTextAttribute>();
                displayNames[i] = attr != null ? attr.Text : names[i];
            }

            initialized = true;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            Init();

            if (displayNames == null || property.propertyType != SerializedPropertyType.Enum)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            int currentIndex = property.enumValueIndex;
            int selectedIndex = EditorGUI.Popup(position, label.text, currentIndex, displayNames);

            if (selectedIndex != currentIndex)
            {
                property.enumValueIndex = selectedIndex;
            }
        }
    }
}
