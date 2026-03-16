#if UNITY_EDITOR
using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace Latios.Calligraphics.Editor
{
    ///// <summary>
    ///// PropertyDrawer for a FixedString128Bytes using UE Elements
    ///// </summary>
    //[CustomPropertyDrawer(typeof(FixedString128Bytes))]
    //public class FixedString128BytesPropertyDrawer : PropertyDrawer
    //{
    //    public override VisualElement CreatePropertyGUI(SerializedProperty property)
    //    {

    //        var textField = new TextField(property.displayName);
    //        textField.value = property.boxedValue.ToString(); //how to make it bindable in both directions?
    //        return textField;
    //    }
    //}

    /// <summary>
    /// PropertyDrawer for a FixedString128Bytes using ImGUI
    /// </summary>
    [CustomPropertyDrawer(typeof(FixedString128Bytes))]
    public class FixedString128BytesPropertyDrawer : PropertyDrawer
    {
        #region Public methods

        /// <summary>
        /// Called when the UI is drawn
        /// </summary>
        /// <param name="position">The position of the field</param>
        /// <param name="property">The property to serialize</param>
        /// <param name="label">The text</param>
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginChangeCheck();
            string fixedStringValue = EditorGUI.TextField(position, label, property.boxedValue.ToString());

            if (EditorGUI.EndChangeCheck())
            {
                property.boxedValue = new FixedString128Bytes(fixedStringValue);
            }
        }

        /// <summary>
        /// Ensures the field will stay at the proper position
        /// </summary>
        /// <param name="property">The property</param>
        /// <param name="label">The text</param>
        /// <returns>The proper height of the field</returns>
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight;
        }

        #endregion
    }
}
#endif

