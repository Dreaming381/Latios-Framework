#if UNITY_EDITOR
using System.Collections.Generic;
using Latios.Calligraphics.Authoring;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Latios.Calligraphics.Editor
{
    [CustomEditor(typeof(TextRendererAuthoring))]
    public class TextRendererInspector : UnityEditor.Editor
    {
        public VisualTreeAsset visualTreeAsset;
        PropertyField fontCollectionAssetProperty;
        DropdownField fonts;
        List<string> emptyList = new();

        public override VisualElement CreateInspectorGUI()
        {
            VisualElement myInspector = new VisualElement();
            if (visualTreeAsset == null)
                return myInspector;
            var container = visualTreeAsset.Instantiate();

            fontCollectionAssetProperty = container.Q<PropertyField>("fontCollectionAsset");
            fonts                       = container.Q<DropdownField>();

            //try to add dropdown
            AddFontDropDown();

            //react to changes in fontCollectionAsset assignment: clear or add dropdown
            fontCollectionAssetProperty.RegisterValueChangeCallback((propertyChanged) => AddFontDropDown());

            myInspector.Add(container);
            return myInspector;
        }

        void AddFontDropDown()
        {
            var fontCollectionAsset = ((TextRendererAuthoring)this.target).fontCollectionAsset;
            if (fontCollectionAsset != null)
                fonts.choices = fontCollectionAsset.fontFamilies;
            else
                fonts.choices = emptyList;
        }
    }
}
#endif

