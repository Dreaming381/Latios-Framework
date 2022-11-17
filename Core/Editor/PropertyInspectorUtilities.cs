using UnityEngine.UIElements;

namespace Latios.Editor
{
    public static class PropertyInspectorUtilities
    {
        public static VisualElement MakeReadOnlyElement<TElement, TValue>(string label, TValue value, string tooltip = null) where TElement : BaseField<TValue>, new()
        {
            var newElement = new TElement { label = label, tooltip = tooltip };
            newElement.SetValueWithoutNotify(value);
            newElement.SetEnabled(false);
            return newElement;
        }
    }
}

