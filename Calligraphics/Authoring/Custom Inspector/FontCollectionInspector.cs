#if UNITY_EDITOR
using Latios.Calligraphics.Authoring;
using UnityEditor;
using UnityEngine.UIElements;

namespace Latios.Calligraphics.Editor
{
    [CustomEditor(typeof(FontCollectionAsset))]
    public class FontCollectionInspector : UnityEditor.Editor
    {
        public VisualTreeAsset visualTreeAsset;
        public override VisualElement CreateInspectorGUI()
        {
            VisualElement myInspector = new VisualElement();

            if(visualTreeAsset == null)
                visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Packages/com.textmeshdots/Authoring/Custom Inspector/FontCollectionAsset.uxml");
            //visualTree.CloneTree(myInspector);

            var container = visualTreeAsset.Instantiate();
            var button    = container.Q<Button>();
            button.clicked += OnProcessButtonClicked;
            myInspector.Add(container);

            return myInspector;
        }
        void OnProcessButtonClicked()
        {
            var fontCollectionAsset = (FontCollectionAsset)target;
            fontCollectionAsset.ProcessFonts();
        }
    }
}
#endif

