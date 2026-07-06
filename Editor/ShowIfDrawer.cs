using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace TaoTie.RenderPipelines.Editor
{
    [CustomPropertyDrawer(typeof(ShowIfAttribute), true)]
    public class ShowIfDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return ShouldShow(property)
                ? EditorGUI.GetPropertyHeight(property, label, true)
                : 0f;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (!ShouldShow(property))
                return;

            EditorGUI.PropertyField(position, property, label, true);
        }

        bool ShouldShow(SerializedProperty property)
        {
            var attrs = fieldInfo?.GetCustomAttributes<ShowIfAttribute>();
            if (attrs == null)
                return true;

            foreach (var attr in attrs)
            {
                if (!EvaluateCondition(property, attr))
                    return false;
            }

            return true;
        }

        static bool EvaluateCondition(SerializedProperty property, ShowIfAttribute attr)
        {
            SerializedProperty sibling = FindSiblingProperty(property, attr.FieldName);
            if (sibling == null)
                return true;

            double actual = GetNumericValue(sibling);

            return attr.Operator switch
            {
                ShowIfOperator.IsTrue => actual != 0d,
                ShowIfOperator.NotEqual => actual != attr.Value,
                ShowIfOperator.Equal => actual == attr.Value,
                ShowIfOperator.GreaterThan => actual > attr.Value,
                ShowIfOperator.GreaterThanOrEqual => actual >= attr.Value,
                ShowIfOperator.LessThan => actual < attr.Value,
                ShowIfOperator.LessThanOrEqual => actual <= attr.Value,
                _ => true,
            };
        }

        static double GetNumericValue(SerializedProperty prop)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Boolean:
                    return prop.boolValue ? 1d : 0d;
                case SerializedPropertyType.Integer:
                    return prop.intValue;
                case SerializedPropertyType.Float:
                    return prop.floatValue;
                case SerializedPropertyType.Enum:
                    return prop.enumValueIndex;
                default:
                    return 0d;
            }
        }

        static SerializedProperty FindSiblingProperty(SerializedProperty property, string fieldName)
        {
            string path = property.propertyPath;
            int lastDot = path.LastIndexOf('.');
            string parentPath = lastDot >= 0 ? path.Substring(0, lastDot) : string.Empty;
            string siblingPath = string.IsNullOrEmpty(parentPath)
                ? fieldName
                : parentPath + "." + fieldName;

            return property.serializedObject.FindProperty(siblingPath);
        }
    }
}
