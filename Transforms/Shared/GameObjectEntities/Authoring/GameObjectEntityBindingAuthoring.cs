using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Unity.Mathematics;
using UnityEngine;

// This code contained in this file is a heavily-modified implementation of unity-guid by Alexandr Frolov: https://github.com/Maligan/unity-guid
// It is licensed under the MIT license copied below.
//
// MIT License
//
// Copyright(c) 2023 Alexandr Frolov
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

namespace Latios.Transforms
{
    [System.Serializable]
    internal partial struct GameObjectEntityBindingAuthoring
    {
        public Unity.Entities.Hash128 hostGuid => (Unity.Entities.Hash128)m_hostGuid;
        public string hostScene => m_hostScene;

        [SerializeField] UnityEngine.Hash128 m_hostGuid;
        [SerializeField] string              m_hostScene;

        public bool TryGetHost(out GameObjectEntityHostAuthoring host)
        {
            return (host = GameObjectEntityHostAuthoring.Find(m_hostGuid)) != null;
        }
    }

#if UNITY_EDITOR
    internal partial struct GameObjectEntityBindingAuthoring : ISerializationCallbackReceiver
    {
        [SerializeField] private UnityEditor.SceneAsset m_editorSceneAsset;
        [SerializeField] private string m_editorGameObjectName;

        public void OnAfterDeserialize()
        {
        }
        public void OnBeforeSerialize()
        {
            m_hostScene = m_editorSceneAsset ? m_editorSceneAsset.name : null;
        }
    }

    [UnityEditor.CustomPropertyDrawer(typeof(GameObjectEntityBindingAuthoring))]
    public class GUIDRefereceDrawer : UnityEditor.PropertyDrawer
    {
        static readonly GUIContent s_mixedValueContent = UnityEditor.EditorGUIUtility.TrTextContent("\u2014", "Mixed Values");
        static readonly Color s_mixedValueContentColor = new Color(1, 1, 1, 0.5f);

        static Texture2D iconCache;
        static Texture2D icon
        {
            get
            {
                if (iconCache == null)
                {
                    var scriptType   = typeof(GameObjectEntityHostAuthoring);
                    var scriptAsset  = UnityEditor.AssetDatabase.FindAssets(scriptType.Name + " t:" + nameof(UnityEditor.MonoScript));
                    var scriptPath   = UnityEditor.AssetDatabase.GUIDToAssetPath(scriptAsset[0]);
                    var scriptObject = UnityEditor.AssetDatabase.LoadAssetAtPath(scriptPath, typeof(UnityEditor.MonoScript));
                    iconCache = UnityEditor.AssetPreview.GetMiniThumbnail(scriptObject);
                }

                return iconCache;
            }
        }

        static GUIStyle objectFieldButtonCache;
        static GUIStyle objectFieldButton
        {
            get
            {
                if (objectFieldButtonCache == null)
                {
                    objectFieldButtonCache = (GUIStyle)typeof(UnityEditor.EditorStyles)
                                             .GetProperty(nameof(objectFieldButton), BindingFlags.NonPublic | BindingFlags.Static)
                                             .GetValue(null);
                }

                return objectFieldButtonCache;
            }
        }

        public override void OnGUI(Rect position, UnityEditor.SerializedProperty property, GUIContent label)
        {
            var pGUID       = property.FindPropertyRelative("m_hostGuid");
            var pSceneName  = property.FindPropertyRelative("m_hostScene");
            var pSceneAsset = property.FindPropertyRelative("m_editorSceneAsset");
            var pName       = property.FindPropertyRelative("m_editorGameObjectName");

            var currentHostGuid = pGUID.hash128Value;
            if (!currentHostGuid.Equals(default))
            {
                var component = GameObjectEntityHostAuthoring.Find(currentHostGuid);
                if (component != null && component.name != pName.stringValue)
                {
                    pName.stringValue = component.name;
                    property.serializedObject.ApplyModifiedProperties();
                }

                var sceneAsset = (UnityEditor.SceneAsset)pSceneAsset.objectReferenceValue;
                if (sceneAsset != null && sceneAsset.name != pSceneName.stringValue)
                {
                    pSceneName.stringValue = sceneAsset.name;
                    property.serializedObject.ApplyModifiedProperties();
                }
            }

            {
                var controlId = GUIUtility.GetControlID(FocusType.Keyboard, position);

                var totalPos  = position;
                var fieldPos  = position; fieldPos.xMin += UnityEditor.EditorGUIUtility.labelWidth + 2;
                var buttonPos = objectFieldButton.margin.Remove(new Rect(position.xMax - 19, position.y, 19, position.height));

                switch (Event.current.type)
                {
                    case EventType.ContextClick:
                        if (totalPos.Contains(Event.current.mousePosition))
                        {
                            UnityEditor.GenericMenu context = new UnityEditor.GenericMenu();
                            context.AddSeparator(string.Empty);
                            context.ShowAsContext();
                            Event.current.Use();
                        }
                        break;

                    case EventType.DragUpdated:
                        if (fieldPos.Contains(Event.current.mousePosition) && TryGetGUID(UnityEditor.DragAndDrop.objectReferences, out _))
                        {
                            UnityEditor.DragAndDrop.visualMode      = UnityEditor.DragAndDropVisualMode.Generic;
                            UnityEditor.DragAndDrop.activeControlID = controlId;
                            Event.current.Use();
                        }
                        else
                        {
                            UnityEditor.DragAndDrop.activeControlID = 0;
                        }
                        break;

                    case EventType.DragPerform:
                        if (fieldPos.Contains(Event.current.mousePosition) && TryGetGUID(UnityEditor.DragAndDrop.objectReferences, out var newValue))
                        {
                            SetValue(newValue);
                            GUI.changed = true;
                            UnityEditor.DragAndDrop.AcceptDrag();
                            UnityEditor.DragAndDrop.activeControlID = 0;
                            Event.current.Use();
                        }
                        break;

                    case EventType.MouseDown:
                        if (Event.current.button == 0 && totalPos.Contains(Event.current.mousePosition))
                        {
                            if (buttonPos.Contains(Event.current.mousePosition))
                            {
                                UnityEditor.EditorGUIUtility.ShowObjectPicker<GameObjectEntityHostAuthoring>(GameObjectEntityHostAuthoring.Find(pGUID.hash128Value),
                                                                                                             true,
                                                                                                             string.Empty,
                                                                                                             controlId);
                            }
                            else if (fieldPos.Contains(Event.current.mousePosition))
                            {
                                Ping(Event.current.clickCount > 1);
                            }

                            // focus control in any case
                            GUIUtility.keyboardControl = controlId;
                            Event.current.Use();
                        }
                        break;

                    case EventType.KeyDown:
                        if (GUIUtility.keyboardControl == controlId)
                        {
                            var hasModifier = (Event.current.alt || Event.current.shift || Event.current.command || Event.current.control);
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

                            var cmdSelect = Event.current.keyCode == KeyCode.Space || Event.current.keyCode == KeyCode.Return;
                            if (cmdSelect)
                            {
                                UnityEditor.EditorGUIUtility.ShowObjectPicker<GameObjectEntityHostAuthoring>(GameObjectEntityHostAuthoring.Find(pGUID.hash128Value),
                                                                                                             true,
                                                                                                             string.Empty,
                                                                                                             controlId);
                                Event.current.Use();
                                break;
                            }
                        }
                        break;

                    case EventType.ExecuteCommand:
                        if (Event.current.commandName == "ObjectSelectorUpdated" && UnityEditor.EditorGUIUtility.GetObjectPickerControlID() == controlId)
                        {
                            if (TryGetGUID(new[] { UnityEditor.EditorGUIUtility.GetObjectPickerObject() }, out var value))
                            {
                                SetValue(value);
                            }
                            else
                            {
                                SetValue(null);
                            }

                            GUI.changed = true;
                            Event.current.Use();
                        }
                        break;

                    case EventType.Repaint:

                        // Prefix
                        UnityEditor.EditorGUI.PrefixLabel(totalPos, controlId, label);

                        // Field
                        var prevColor = GUI.contentColor;
                        if (pGUID.hasMultipleDifferentValues || pSceneAsset.objectReferenceValue && !IsLoaded(pSceneAsset))
                            GUI.contentColor *= s_mixedValueContentColor;
                        UnityEditor.EditorStyles.objectField.Draw(fieldPos,
                                                                  GetContent(pGUID, pName, pSceneAsset),
                                                                  controlId,
                                                                  UnityEditor.DragAndDrop.activeControlID == controlId,
                                                                  fieldPos.Contains(Event.current.mousePosition));
                        GUI.contentColor = prevColor;

                        // Button
                        var prevSize = UnityEditor.EditorGUIUtility.GetIconSize();
                        UnityEditor.EditorGUIUtility.SetIconSize(new Vector2(12, 12));
                        objectFieldButton.Draw(buttonPos, GUIContent.none, controlId, UnityEditor.DragAndDrop.activeControlID == controlId,
                                               buttonPos.Contains(Event.current.mousePosition));
                        UnityEditor.EditorGUIUtility.SetIconSize(prevSize);

                        break;
                }
            }

            void Ping(bool doubleClick)
            {
                if (pGUID.hasMultipleDifferentValues)
                    return;

                var targetGUID = pGUID.hash128Value;
                var target     = GameObjectEntityHostAuthoring.Find(targetGUID);

                if (target != null)
                {
                    if (!doubleClick)
                        UnityEditor.EditorGUIUtility.PingObject(target);
                    else
                        UnityEditor.Selection.activeObject = target;
                }
                else
                {
                    var targetScene = pSceneAsset.objectReferenceValue as UnityEditor.SceneAsset;
                    if (targetScene == null)
                        return;

                    // Try to find a subscene containing the target scene
#if UNITY_6000_0_OR_NEWER
                    var allSubscenes = Object.FindObjectsByType<Unity.Scenes.SubScene>(FindObjectsSortMode.None);
#else
                    var allSubscenes = Object.FindObjectsOfType<Unity.Scenes.SubScene>();
#endif
                    foreach (var subscene in allSubscenes)
                    {
                        if (subscene.SceneAsset == targetScene)
                        {
                            if (!doubleClick)
                                UnityEditor.EditorGUIUtility.PingObject(subscene);
                            else
                                UnityEditor.Selection.activeObject = subscene;
                            return;
                        }
                    }

                    if (!doubleClick)
                        UnityEditor.EditorGUIUtility.PingObject(targetScene);
                    else
                    {
                        UnityEditor.Selection.activeObject = targetScene;
                    }
                }
            }

            void SetValue(GameObjectEntityHostAuthoring component)
            {
                if (component != null)
                {
                    pGUID.hash128Value     = component.guid;
                    pName.stringValue      = component.name;
                    pSceneName.stringValue = component.gameObject.scene.name;
                    // nb! unsaved scene isn't possible because GUID doesn't exist for unsaved scene
                    pSceneAsset.objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.SceneAsset>(component.gameObject.scene.path);
                }
                else
                {
                    pGUID.hash128Value               = default;
                    pName.stringValue                = string.Empty;
                    pSceneAsset.objectReferenceValue = null;
                    pSceneName.stringValue           = null;
                }

                property.serializedObject.ApplyModifiedProperties();
            }
        }

        private static GUIContent GetContent(UnityEditor.SerializedProperty guid, UnityEditor.SerializedProperty name, UnityEditor.SerializedProperty scene)
        {
            if (guid.hasMultipleDifferentValues)
                return s_mixedValueContent;

            var className = UnityEditor.ObjectNames.NicifyVariableName(nameof(GameObjectEntityHostAuthoring));

            var guidValue = guid.hash128Value;

            if (guidValue.Equals(default))
                return new GUIContent($"None ({className})");

            if (scene.objectReferenceValue == null)
                return new GUIContent($"Missing ({className})");

            if (IsLoaded(scene) && GameObjectEntityHostAuthoring.Find(guidValue) == null)
                return new GUIContent($"Missing ({className})");

            return new GUIContent($"{name.stringValue} ({className})", icon);
        }

        private static bool IsLoaded(UnityEditor.SerializedProperty scene)
        {
            var sceneAsset = (UnityEditor.SceneAsset)scene.objectReferenceValue;
            if (sceneAsset == null)
                return false;

            var scenePath = UnityEditor.AssetDatabase.GetAssetPath(sceneAsset);
            if (scenePath == null)
                return false;

            var sceneObject = UnityEditor.SceneManagement.EditorSceneManager.GetSceneByPath(scenePath);
            if (sceneObject.IsValid() == false)
                return false;

            return sceneObject.isLoaded;
        }

        static void AddItem(UnityEditor.GenericMenu menu, string name, bool enabled, UnityEditor.GenericMenu.MenuFunction action)
        {
            var label = UnityEditor.EditorGUIUtility.TrTextContent(name).text;

            if (enabled)
                menu.AddItem(new GUIContent(label), false, action);
            else
                menu.AddDisabledItem(new GUIContent(label));
        }

        static bool TryGetGUID(Object[] references, out GameObjectEntityHostAuthoring result)
        {
            result = null;

            for (var i = 0; i < references.Length; i++)
            {
                switch (references[i])
                {
                    case GameObject gameObject:
                        gameObject.TryGetComponent(out result);
                        break;

                    case GameObjectEntityHostAuthoring component:
                        result = component;
                        break;
                }
            }

            return result != null && !result.guid.Equals(default);
        }
    }
#endif
}

