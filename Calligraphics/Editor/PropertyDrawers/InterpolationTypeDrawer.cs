using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Latios.Calligraphics.Editor
{
    [CustomPropertyDrawer(typeof(InterpolationType))]
    public class InterpolationTypeDrawer : PropertyDrawer
    {
        private List<InterpolationTypeItem> _interpolationTypeItems;

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {

            if (_interpolationTypeItems == null)
            {
                _interpolationTypeItems = GetInterpolationTypes();
            }

            var root = new VisualElement();
            
            // Create a custom dropdown using ListView
            var popupList = new ListView(_interpolationTypeItems, 30, MakeInterpolationTypeElement, BindInterpolationTypeItemByIndex);
            popupList.AddToClassList("unity-collection-view--with-border");
            popupList.AddToClassList("unity-list-view__scroll-view--with-footer");
            popupList.selectionType = SelectionType.Single;
            
            var popupContainer = new VisualElement();
            popupContainer.AddToClassList("unity-base-field");
            popupContainer.AddToClassList("unity-base-popup-field");
            popupContainer.AddToClassList("unity-popup-field");
            popupContainer.AddToClassList("unity-base-field__inspector");
            root.Add(popupContainer);

            var label = new Label(property.displayName);
            label.AddToClassList("unity-base-field");
            label.AddToClassList("unity-label");
            label.AddToClassList("unity-base-field__label");
            label.AddToClassList("unity-base-popup-field__label");
            popupContainer.Add(label);
            
            VisualElement popupButton = new VisualElement();
            popupButton.AddToClassList("unity-base-field__input");
            popupButton.AddToClassList("unity-base-popup-field__input");
            popupButton.AddToClassList("unity-popup-field__input");
            popupContainer.Add(popupButton);

            VisualElement popupButtonContent = new VisualElement();
            popupButtonContent.AddToClassList("unity-text-element");
            popupButtonContent.AddToClassList("unity-base-popup-field__text");
            popupButton.Add(popupButtonContent);

            VisualElement popupCaret = new VisualElement();
            popupCaret.AddToClassList("unity-base-popup-field__arrow");
            popupButton.Add(popupCaret);
            
            popupButton.RegisterCallback<ClickEvent>((e) =>
            {
                if (ContentPopup.IsDisplayed)
                {
                    ContentPopup.ClosePopup();
                }
                else
                {

                    Vector3 size = new Vector2(200f, 200f);
                    Rect popupLayout = new Rect(new Vector2(popupButton.contentContainer.worldBound.x, size.y + popupButton.contentContainer.worldBound.height), size);
                    ContentPopup.Open(popupLayout, popupList);
                }
                
            });

            popupList.selectionChanged += selection =>
            {
                if (selection.Count() == 0) return;

                var selectedItem = (InterpolationTypeItem)selection.ElementAt(0);
                property.enumValueIndex = (int)selectedItem.InterpolationType;
                property.serializedObject.ApplyModifiedProperties();

                popupButtonContent.Clear();
                var interpolationTypeElement = MakeInterpolationTypeElement(15f, 15f);
                BindInterpolationTypeItem(interpolationTypeElement, selectedItem);
                popupButtonContent.Add(interpolationTypeElement);
                
                ContentPopup.ClosePopup();
            };
            popupList.selectedIndex = property.enumValueIndex;


            return root;
        }
        
        
        
        private VisualElement MakeInterpolationTypeElement()
        {
            return MakeInterpolationTypeElement(30f, 30f);
        }

        private VisualElement MakeInterpolationTypeElement(float width, float height)
        {
            var item = new VisualElement();
            item.style.flexDirection = FlexDirection.Row;
            item.style.alignItems = Align.Center;

            var image = new Image { name = "InterpolationTypeImage" };
            image.style.width = width;
            image.style.height = height;
            item.Add(image);

            var label = new Label { name = "InterpolationTypeLabel" };
            item.Add(label);

            return item;
        }
        
        private void BindInterpolationTypeItem(VisualElement element, InterpolationTypeItem interpolationTypeItem)
        {
            var image = element.Q<Image>("InterpolationTypeImage");
            image.image = interpolationTypeItem.Image;

            var label = element.Q<Label>("InterpolationTypeLabel");
            label.text = interpolationTypeItem.InterpolationType.ToString();
        }
        
        private void BindInterpolationTypeItemByIndex(VisualElement element, int index)
        {
            var interpolationTypeItem = _interpolationTypeItems[index];
            var image = element.Q<Image>("InterpolationTypeImage");
            image.image = interpolationTypeItem.Image;

            var label = element.Q<Label>("InterpolationTypeLabel");
            label.text = interpolationTypeItem.InterpolationType.ToString();
        }
        private static List<InterpolationTypeItem> GetInterpolationTypes()
        {
            var interpolationTypes = Enum.GetValues(typeof(InterpolationType)).OfType<InterpolationType>();
            return interpolationTypes.Select(interpolationType => new InterpolationTypeItem(interpolationType)).ToList();
        }

        public class InterpolationTypeItem
        {
            public InterpolationType InterpolationType;
            public Texture2D Image;
            
            public InterpolationTypeItem(InterpolationType interpolationType)
            {
                this.InterpolationType = interpolationType;
                this.Image = GenerateInterpolationTypeTexture(interpolationType, 20, 20);
            }
            
            private Texture2D GenerateInterpolationTypeTexture(InterpolationType mode, int width, int height)
            {
                //TODO:  Need top and bottom padding
                var padding = height / 5;
            
                var texture = new Texture2D(width, height);
                Color[] pixels = new Color[width * height];

                for (int x = 0; x < width; x++)
                {
                    float t = (float)x / (width - 1);
                    int interpolatedValue = Mathf.FloorToInt(Interpolation.Interpolate(padding, height - padding - 1, t, mode));
                    int colorIndex = interpolatedValue * width + x;
                
                    pixels[colorIndex] = Color.black;
                }

                texture.SetPixels(pixels);
                texture.Apply();

                return texture;
            }
        }
        

    }

    public class ContentPopup : EditorWindow
    {
        private static ContentPopup PopupWindow;

        public static bool IsDisplayed => PopupWindow != null;

        public static ContentPopup Open(Rect layoutRect, VisualElement content)
        {
            ClosePopup();
            
           
            PopupWindow = CreateInstance<ContentPopup>();
            PopupWindow.Content = content;
            
            Rect rectActivator = new Rect(
                focusedWindow.position.x + layoutRect.x,
                focusedWindow.position.y + layoutRect.y,
                layoutRect.width,
                layoutRect.height
            );
            
            PopupWindow.ShowAsDropDown(rectActivator, rectActivator.size);

            return PopupWindow;
        }

        public static void ClosePopup()
        {
            if (PopupWindow != null)
            {
                try
                {
                    PopupWindow.Close();
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }
        }

        public VisualElement Content;
        private void CreateGUI()
        {
            this.rootVisualElement.Clear();
            this.rootVisualElement.Add(this.Content);
        }
        
        private void OnDestroy()
        {
            PopupWindow = null;
        }
    }
}