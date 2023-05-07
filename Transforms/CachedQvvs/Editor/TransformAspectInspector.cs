#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
using Unity.Entities.Editor;
using Unity.Entities.UI;
using UnityEditor;
using UnityEngine.UIElements;

namespace Latios.Transforms.Editor
{
    class TransformAspectInspector : PropertyInspector<EntityAspectContainer<TransformAspect> >
    {
        const string k_EditorPrefsTransformAspectInspectorBase = "com.unity.dots.editor.transform_aspect_inspector_";

        public override VisualElement Build()
        {
            var root                 = new VisualElement();
            var toolHandleLocalName  = EditorGUIUtility.isProSkin ? "d_ToolHandleLocal" : "ToolHandleLocal";
            var toolHandleGlobalName = EditorGUIUtility.isProSkin ? "d_ToolHandleGlobal" : "ToolHandleGlobal";

            var localPosition  = new Vector3Field { label = "Local Position", bindingPath = "localPosition" }.WithIconPrefix(toolHandleLocalName);
            var globalPosition                                                            =
                new Vector3Field { label                                                  = "World Position", bindingPath = "worldPosition" }.WithIconPrefix(toolHandleGlobalName);

            var localRotation  = new Vector3Field { label = "Local Rotation", bindingPath = "localRotation" }.WithIconPrefix(toolHandleLocalName);
            var globalRotation                                                            =
                new Vector3Field { label                                                  = "World Rotation", bindingPath = "worldRotation" }.WithIconPrefix(toolHandleGlobalName);

            var localUniformScale  = new FloatField { label = "Local Uniform Scale", bindingPath = "localScale" }.WithIconPrefix(toolHandleLocalName);
            var globalUniformScale                                                               =
                new FloatField { label                                                           = "World Uniform Scale", bindingPath = "worldScale" }.WithIconPrefix(
                toolHandleGlobalName);

            var stretch = new Vector3Field { label = "Stretch", bindingPath = "stretch" }.WithIconPrefix(toolHandleLocalName);

            root.Add(new ContextualElement
                     (
                         k_EditorPrefsTransformAspectInspectorBase + "position",
                         new ContextualElement.Item
            {
                Element          = localPosition,
                ContextMenuLabel = localPosition.label
            },
                         new ContextualElement.Item
            {
                Element          = globalPosition,
                ContextMenuLabel = globalPosition.label
            }
                     ));

            root.Add(new ContextualElement
                     (
                         k_EditorPrefsTransformAspectInspectorBase + "rotation",
                         new ContextualElement.Item
            {
                Element          = localRotation,
                ContextMenuLabel = localRotation.label
            },
                         new ContextualElement.Item
            {
                Element          = globalRotation,
                ContextMenuLabel = globalRotation.label
            }
                     ));

            root.Add(new ContextualElement
                     (
                         k_EditorPrefsTransformAspectInspectorBase + "scale",
                         new ContextualElement.Item
            {
                Element          = localUniformScale,
                ContextMenuLabel = localUniformScale.label
            },
                         new ContextualElement.Item
            {
                Element          = globalUniformScale,
                ContextMenuLabel = globalUniformScale.label
            }
                     ));

            root.Add(new ContextualElement
                     (
                         k_EditorPrefsTransformAspectInspectorBase + "stretch",
                         new ContextualElement.Item
            {
                Element          = stretch,
                ContextMenuLabel = stretch.label
            }
                     ));

            return root;
        }
    }
}
#endif

