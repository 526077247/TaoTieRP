using System;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace TaoTie.RenderPipelines.Editor
{
    [CustomEditor(typeof(PostFXSettings))]
    public class PostFXSettingsEditor : UnityEditor.Editor
    {
        SerializedProperty effectsProp;
        ReorderableList reorderableList;

        const float elementLeftPadding = 14f;

        void OnEnable()
        {
            effectsProp = serializedObject.FindProperty("effects");

            reorderableList = new ReorderableList(serializedObject, effectsProp,
                draggable: true, displayHeader: true, displayAddButton: true, displayRemoveButton: true)
            {
                drawHeaderCallback = rect =>
                    EditorGUI.LabelField(rect, "Effects", "Reorder to change execution order"),

                drawElementCallback = (rect, index, isActive, isFocused) =>
                {
                    rect.x += elementLeftPadding;
                    rect.width -= elementLeftPadding;
                    SerializedProperty prop = effectsProp.GetArrayElementAtIndex(index);
                    EditorGUI.PropertyField(rect, prop, true);
                },

                elementHeightCallback = index =>
                {
                    SerializedProperty prop = effectsProp.GetArrayElementAtIndex(index);
                    return EditorGUI.GetPropertyHeight(prop, true) + 4f;
                },

                onAddDropdownCallback = OnAddDropdown
            };
        }

        void OnAddDropdown(Rect buttonRect, ReorderableList list)
        {
            GenericMenu menu = new();
            var types = PostFXEffectRegistry.GetEffectTypes();

            if (types.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("No effect types found"));
            }
            else
            {
                foreach (var type in types)
                {
                    string displayName = PostFXEffectRegistry.GetDisplayName(type);
                    bool alreadyExists = EffectTypeExists(type);
                    if (alreadyExists)
                        menu.AddDisabledItem(new GUIContent(displayName));
                    else
                        menu.AddItem(new GUIContent(displayName), false, () => AddEffect(type));
                }
            }

            menu.DropDown(buttonRect);
        }

        void AddEffect(Type type)
        {
            if (EffectTypeExists(type))
                return;
            serializedObject.Update();
            var effect = Activator.CreateInstance(type) as PostFXEffect;
            effectsProp.arraySize++;
            SerializedProperty newProp = effectsProp.GetArrayElementAtIndex(effectsProp.arraySize - 1);
            newProp.managedReferenceValue = effect;
            serializedObject.ApplyModifiedProperties();
        }

        bool EffectTypeExists(Type type)
        {
            for (int i = 0; i < effectsProp.arraySize; i++)
            {
                var prop = effectsProp.GetArrayElementAtIndex(i);
                if (prop.managedReferenceValue != null &&
                    prop.managedReferenceValue.GetType() == type)
                    return true;
            }
            return false;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            reorderableList.DoLayoutList();
            serializedObject.ApplyModifiedProperties();
        }
    }
}
