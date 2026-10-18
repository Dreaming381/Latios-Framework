using System;
using System.Reflection;
using Latios.Unika.Authoring;
using Object = UnityEngine.Object;
using UnityEditor;
using UnityEngine;

namespace Latios.Unika.Editor
{
    /// <summary>
    /// Draws an InterfaceAuthoring&lt;T&gt; field as an object-field-styled picker that accepts a
    /// GameObject or authoring component, resolves it to whichever sibling UnikaScriptAuthoringBase
    /// on that GameObject implements the target interface, and rejects (with a console warning)
    /// anything that doesn't. Modeled on GUIDRefereceDrawer
    /// (Transforms/Shared/GameObjectEntities/Authoring/GameObjectEntityBindingAuthoring.cs).
    /// </summary>
    [CustomPropertyDrawer(typeof(InterfaceAuthoring<>))]
    public class InterfaceAuthoringDrawer : PropertyDrawer
    {
        static readonly GUIContent s_mixedValueContent      = EditorGUIUtility.TrTextContent("—", "Mixed Values");
        static readonly Color      s_mixedValueContentColor = new Color(1, 1, 1, 0.5f);

        static GUIStyle objectFieldButtonCache;
        static GUIStyle objectFieldButton
        {
            get
            {
                if (objectFieldButtonCache == null)
                {
                    objectFieldButtonCache = (GUIStyle)typeof(EditorStyles)
                                             .GetProperty(nameof(objectFieldButton), BindingFlags.NonPublic | BindingFlags.Static)
                                             .GetValue(null);
                }
                return objectFieldButtonCache;
            }
        }

        // The InterfaceRef struct nested inside the target IUnikaInterface (e.g. ITestInterface.InterfaceRef),
        // resolved from the closed generic field type since a generic PropertyDrawer only sees T via reflection.
        static Type GetInterfaceRefType(Type fieldType)
        {
            if (fieldType.IsArray)
                fieldType = fieldType.GetElementType();
            else if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(System.Collections.Generic.List<>))
                fieldType = fieldType.GetGenericArguments()[0];
            if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(InterfaceAuthoring<>))
                return fieldType.GetGenericArguments()[0];
            return null;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var interfaceRefType = GetInterfaceRefType(fieldInfo.FieldType);
            if (interfaceRefType == null)
            {
                EditorGUI.LabelField(position, label, new GUIContent("Unsupported field type for InterfaceAuthoringDrawer"));
                return;
            }
            var hintInterfaceType      = interfaceRefType.DeclaringType;
            var authoringInterfaceType = typeof(IUnikaInterfaceAuthoring<>).MakeGenericType(interfaceRefType);
            var pRef                   = property.FindPropertyRelative("authoringReference");

            bool TryFindMatch(Object[] references, out UnikaScriptAuthoringBase result)
            {
                result = null;
                foreach (var reference in references)
                {
                    if (reference is UnikaScriptAuthoringBase direct && authoringInterfaceType.IsInstanceOfType(direct))
                    {
                        result = direct;
                        return true;
                    }

                    var go = reference as GameObject;
                    if (go == null && reference is Component component)
                        go = component.gameObject;
                    if (go == null)
                        continue;

                    foreach (var candidate in go.GetComponents<UnikaScriptAuthoringBase>())
                    {
                        if (authoringInterfaceType.IsInstanceOfType(candidate))
                        {
                            result = candidate;
                            return true;
                        }
                    }
                }
                return false;
            }

            void SetValue(UnikaScriptAuthoringBase value)
            {
                pRef.objectReferenceValue = value;
                property.serializedObject.ApplyModifiedProperties();
            }

            label = EditorGUI.BeginProperty(position, label, pRef);
            var controlId = GUIUtility.GetControlID(FocusType.Keyboard, position);

            var totalPos  = position;
            var fieldPos  = position; fieldPos.xMin += EditorGUIUtility.labelWidth + 2;
            var buttonPos = objectFieldButton.margin.Remove(new Rect(position.xMax - 19, position.y, 19, position.height));

            switch (Event.current.type)
            {
                case EventType.DragUpdated:
                    if (fieldPos.Contains(Event.current.mousePosition) && TryFindMatch(DragAndDrop.objectReferences, out _))
                    {
                        DragAndDrop.visualMode      = DragAndDropVisualMode.Generic;
                        DragAndDrop.activeControlID = controlId;
                        Event.current.Use();
                    }
                    else
                    {
                        DragAndDrop.activeControlID = 0;
                    }
                    break;

                case EventType.DragPerform:
                    if (fieldPos.Contains(Event.current.mousePosition) && TryFindMatch(DragAndDrop.objectReferences, out var dragged))
                    {
                        SetValue(dragged);
                        GUI.changed = true;
                        DragAndDrop.AcceptDrag();
                        DragAndDrop.activeControlID = 0;
                        Event.current.Use();
                    }
                    break;

                case EventType.MouseDown:
                    if (Event.current.button == 0 && totalPos.Contains(Event.current.mousePosition))
                    {
                        if (buttonPos.Contains(Event.current.mousePosition))
                        {
                            EditorGUIUtility.ShowObjectPicker<GameObject>(
                                (pRef.objectReferenceValue as UnikaScriptAuthoringBase)?.gameObject, true, string.Empty, controlId);
                        }
                        else if (fieldPos.Contains(Event.current.mousePosition))
                        {
                            Ping(Event.current.clickCount > 1);
                        }

                        GUIUtility.keyboardControl = controlId;
                        Event.current.Use();
                    }
                    break;

                case EventType.KeyDown:
                    if (GUIUtility.keyboardControl == controlId)
                    {
                        var hasModifier = Event.current.alt || Event.current.shift || Event.current.command || Event.current.control;
                        if (hasModifier)
                            break;

                        var cmdDelete = Event.current.keyCode == KeyCode.Backspace || Event.current.keyCode == KeyCode.Delete;
                        if (cmdDelete)
                        {
                            SetValue(null);
                            GUI.changed = true;
                            Event.current.Use();
                            break;
                        }
                    }
                    break;

                case EventType.ExecuteCommand:
                    if (Event.current.commandName == "ObjectSelectorUpdated" && EditorGUIUtility.GetObjectPickerControlID() == controlId)
                    {
                        var picked = EditorGUIUtility.GetObjectPickerObject();
                        if (picked == null)
                        {
                            SetValue(null);
                            GUI.changed = true;
                        }
                        else if (TryFindMatch(new[] { picked }, out var pickedMatch))
                        {
                            SetValue(pickedMatch);
                            GUI.changed = true;
                        }
                        else
                        {
                            Debug.LogWarning(
                                $"'{picked.name}' has no authoring component implementing {ObjectNames.NicifyVariableName(hintInterfaceType.Name)}. Assignment reverted.");
                        }
                        Event.current.Use();
                    }
                    break;

                case EventType.Repaint:
                    EditorGUI.PrefixLabel(totalPos, controlId, label);

                    var prevColor = GUI.contentColor;
                    if (pRef.hasMultipleDifferentValues)
                        GUI.contentColor *= s_mixedValueContentColor;
                    EditorStyles.objectField.Draw(fieldPos,
                                                  GetContent(pRef, hintInterfaceType),
                                                  controlId,
                                                  DragAndDrop.activeControlID == controlId,
                                                  fieldPos.Contains(Event.current.mousePosition));
                    GUI.contentColor = prevColor;

                    var prevSize = EditorGUIUtility.GetIconSize();
                    EditorGUIUtility.SetIconSize(new Vector2(12, 12));
                    objectFieldButton.Draw(buttonPos, GUIContent.none, controlId, DragAndDrop.activeControlID == controlId,
                                           buttonPos.Contains(Event.current.mousePosition));
                    EditorGUIUtility.SetIconSize(prevSize);
                    break;
            }
            EditorGUI.EndProperty();

            void Ping(bool doubleClick)
            {
                if (pRef.hasMultipleDifferentValues)
                    return;
                var target = pRef.objectReferenceValue;
                if (target == null)
                    return;
                if (!doubleClick)
                    EditorGUIUtility.PingObject(target);
                else
                    Selection.activeObject = target;
            }
        }

        static GUIContent GetContent(SerializedProperty pRef, Type hintInterfaceType)
        {
            if (pRef.hasMultipleDifferentValues)
                return s_mixedValueContent;

            var hintName = ObjectNames.NicifyVariableName(hintInterfaceType.Name);
            var current  = pRef.objectReferenceValue;
            if (current == null)
                return new GUIContent($"None ({hintName})");

            var icon = EditorGUIUtility.ObjectContent(current, current.GetType()).image;
            return new GUIContent($"{current.name} ({hintName})", icon);
        }
    }
}

