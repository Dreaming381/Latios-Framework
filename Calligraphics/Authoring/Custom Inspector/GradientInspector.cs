#if UNITY_EDITOR
using System;
using Latios.Calligraphics.Authoring;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Latios.Calligraphics.Editor
{
    [CustomEditor(typeof(TextGradientAuthoring))]
    public class GradientInspector : UnityEditor.Editor
    {
        public VisualTreeAsset visualTreeAsset;
        ListView listView;
        public override VisualElement CreateInspectorGUI()
        {
            VisualElement myInspector = new VisualElement();

            listView = new ListView
            {
                showAddRemoveFooter           = true,
                reorderMode                   = ListViewReorderMode.Animated,
                showBorder                    = true,
                showFoldoutHeader             = true,
                headerTitle                   = "Gradients",
                showBoundCollectionSize       = true,
                showAlternatingRowBackgrounds = AlternatingRowBackground.All,
                horizontalScrollingEnabled    = false,
                bindingPath                   = "gradients",
            };
            listView.itemTemplate         = visualTreeAsset;
            listView.virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight;
            listView.style.height         = 350;
            listView.makeItem             = OnMakeItem;
            listView.bindItem             = OnBindItem;
            listView.unbindItem           = OnUnBindItem;

            myInspector.Add(listView);
            return myInspector;
        }
        VisualElement OnMakeItem()
        {
            var item = listView.itemTemplate.Instantiate();
            return item;
        }
        void OnBindItem(VisualElement element, int index)
        {
            var bindable = (BindableElement)element;
            bindable.BindProperty((SerializedProperty)listView.itemsSource[index]);
            var enumField = element.Q<EnumField>();
            enumField.RegisterValueChangedCallback(OnEnumChanged);
            SetColorFieldVisibility(enumField.parent, (ColorGradientMode)enumField.value);
        }
        void OnUnBindItem(VisualElement element, int item)
        {
            element.Unbind();
            var enumField = element.Q<EnumField>();
            enumField.UnregisterValueChangedCallback(OnEnumChanged);
        }
        void OnEnumChanged(ChangeEvent<Enum> evt)
        {
            var enumField = (EnumField)evt.currentTarget;
            SetColorFieldVisibility(enumField.parent, (ColorGradientMode)enumField.value);
        }
        void SetColorFieldVisibility(VisualElement parent, ColorGradientMode mode)
        {
            var top         = parent.Q("Top");
            var bottom      = parent.Q("Bottom");
            var topRight    = top.Q<ColorField>("Right");
            var bottomRight = bottom.Q("Right");
            switch (mode)
            {
                case ColorGradientMode.VerticalGradient:
                    topRight.style.display    = DisplayStyle.None;
                    bottomRight.style.display = DisplayStyle.None;
                    bottom.style.display      = DisplayStyle.Flex;
                    break;
                case ColorGradientMode.HorizontalGradient:
                    topRight.style.display    = DisplayStyle.Flex;
                    bottomRight.style.display = DisplayStyle.Flex;
                    bottom.style.display      = DisplayStyle.None;
                    break;
                case ColorGradientMode.Single:
                    topRight.style.display = DisplayStyle.None;
                    bottom.style.display   = DisplayStyle.None;
                    break;
                case ColorGradientMode.FourCornersGradient:
                    topRight.style.display    = DisplayStyle.Flex;
                    bottomRight.style.display = DisplayStyle.Flex;
                    bottom.style.display      = DisplayStyle.Flex;
                    break;
            }
        }
    }
}
#endif

