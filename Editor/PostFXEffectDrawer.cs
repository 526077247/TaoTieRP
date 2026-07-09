using UnityEditor;
using UnityEngine;

namespace TaoTie.RenderPipelines.Editor
{
    /// <summary>
    /// 为 PostFXEffect 及其子类提供自定义 Inspector 显示。
    /// 显示效果名称（DisplayName）、启用开关和折叠的设置内容。
    /// </summary>
    [CustomPropertyDrawer(typeof(PostFXEffect), true)]
    public class PostFXEffectDrawer : PropertyDrawer
    {
        const float headerHeight = 22f;
        const float toggleWidth = 18f;
        const float padding = 4f;

        static bool HasVisibleChildren(SerializedProperty property)
        {
            var child = property.Copy();
            var end = property.GetEndProperty();
            child.NextVisible(true);
            while (!SerializedProperty.EqualContents(child, end))
            {
                if (child.name != "enabled")
                    return true;
                child.NextVisible(false);
            }
            return false;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (property.managedReferenceValue == null || !HasVisibleChildren(property))
                return headerHeight;

            float height = headerHeight;
            if (property.isExpanded)
            {
                var child = property.Copy();
                var end = property.GetEndProperty();
                child.NextVisible(true);
                while (!SerializedProperty.EqualContents(child, end))
                {
                    if (child.name != "enabled")
                        height += EditorGUI.GetPropertyHeight(child, true) + padding;
                    child.NextVisible(false);
                }
            }
            return height;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            Rect headerRect = new(position.x, position.y, position.width, headerHeight);

            string displayName = label.text;
            var value = property.managedReferenceValue;
            if (value is PostFXEffect effect)
                displayName = effect.DisplayName;

            bool hasChildren = HasVisibleChildren(property);

            Rect foldoutRect = new(headerRect.x, headerRect.y, headerRect.width - toggleWidth, headerRect.height);
            if (hasChildren)
                property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, displayName, true);
            else
                EditorGUI.LabelField(foldoutRect, displayName);

            var enabledProp = property.FindPropertyRelative("enabled");
            if (enabledProp != null)
            {
                Rect toggleRect = new(
                    headerRect.xMax - toggleWidth - 2f,
                    headerRect.y + 1f,
                    toggleWidth,
                    headerRect.height - 2f);
                enabledProp.boolValue = EditorGUI.Toggle(toggleRect, enabledProp.boolValue);
            }

            if (hasChildren && property.isExpanded && property.managedReferenceValue != null)
            {
                Rect childRect = new(
                    position.x + 16f,
                    headerRect.yMax,
                    position.width - 16f,
                    0f);

                var child = property.Copy();
                var end = property.GetEndProperty();
                child.NextVisible(true);
                while (!SerializedProperty.EqualContents(child, end))
                {
                    if (child.name != "enabled")
                    {
                        float childHeight = EditorGUI.GetPropertyHeight(child, true);
                        childRect.height = childHeight;
                        EditorGUI.PropertyField(childRect, child, true);
                        childRect.y += childHeight + padding;
                    }
                    child.NextVisible(false);
                }
            }
        }
    }
}
