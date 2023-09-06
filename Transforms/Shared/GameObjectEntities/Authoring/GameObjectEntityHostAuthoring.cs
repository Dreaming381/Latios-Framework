using System;
using System.Collections.Generic;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
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
    [AddComponentMenu("Latios/Transforms/Game Object Entity Host")]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(int.MinValue)]
    public partial class GameObjectEntityHostAuthoring : MonoBehaviour
    {
        private static Dictionary<Unity.Entities.Hash128, GameObjectEntityHostAuthoring> s_active = new Dictionary<Unity.Entities.Hash128, GameObjectEntityHostAuthoring>();

        public static GameObjectEntityHostAuthoring Find(Unity.Entities.Hash128 guid)
        {
            if (!guid.Equals(default) && s_active.TryGetValue(guid, out var host) && host != null)
                return host;

            return null;
        }

        public Unity.Entities.Hash128 guid => (Unity.Entities.Hash128)m_guid;

        [SerializeField]
        UnityEngine.Hash128 m_guid;

        void Awake() => s_active.TryAdd(m_guid, this);

        void OnDestroy() => s_active.Remove(m_guid);
    }

    public class GameObjectEntityHostAuthoringBaker : Baker<GameObjectEntityHostAuthoring>
    {
        public override void Bake(GameObjectEntityHostAuthoring authoring)
        {
            var entity                                           = GetEntity(TransformUsageFlags.Renderable);
            AddComponent(entity, new GameObjectEntityHost { guid = authoring.guid });
        }
    }

#if UNITY_EDITOR
    public partial class GameObjectEntityHostAuthoring
    {
        int m_editorInstanceId;
        UnityEngine.Hash128 m_editorCachedGuid;

        void Reset() => OnValidate();

        unsafe void OnValidate()
        {
            if (IsInstance())
            {
                if (IsDuplicate())
                    m_guid = default;

                if (IsReverted())
                    m_guid = m_editorCachedGuid;

                if (guid.Equals(default))
                {
                    var managedGuid = Guid.NewGuid();
                    Span<byte> temp        = stackalloc byte[16];
                    managedGuid.TryWriteBytes(temp);
                    v128 vector = new v128(temp[0], temp[1], temp[2], temp[3],
                                           temp[4], temp[5], temp[6], temp[7],
                                           temp[8], temp[9], temp[10], temp[11],
                                           temp[12], temp[13], temp[14], temp[15]);
                    m_guid = new Unity.Entities.Hash128(vector.UInt0, vector.UInt1, vector.UInt2, vector.UInt3);
                }

                s_active[m_guid] = this;
            }
            else
            {
                m_guid = default;
            }

            m_editorCachedGuid = m_guid;
            m_editorInstanceId = GetInstanceID();
        }

        bool IsDuplicate()
        {
            return gameObject.scene.isLoaded && !guid.Equals(default) && m_editorInstanceId != GetInstanceID();
        }

        bool IsReverted()
        {
            return guid.Equals(default) && !m_editorCachedGuid.Equals(default);
        }

        bool IsInstance()
        {
            return !UnityEditor.PrefabUtility.IsPartOfAnyPrefab(this);
        }

        [UnityEditor.CustomEditor(typeof(GameObjectEntityHostAuthoring))]
        [UnityEditor.CanEditMultipleObjects]
        public class GameObjectEntityHostAuthoringEditor : UnityEditor.Editor
        {
            const string kGuidName   = nameof(m_guid);
            const string kGuidAccess = "guid";
            const string kHelp       = "A Game Object Entity Host cannot be part of a prefab!";

            public override void OnInspectorGUI()
            {
                var guidProp = serializedObject.FindProperty(kGuidName);
                guidProp.prefabOverride = false;

                var isPrefab = false;
                foreach (var obj in targets)
                {
                    isPrefab = !(obj as GameObjectEntityHostAuthoring).IsInstance();
                    if (isPrefab)
                        break;
                }

                if (isPrefab)
                    UnityEditor.EditorGUILayout.HelpBox(kHelp, UnityEditor.MessageType.Error);
                else
                {
                    UnityEditor.EditorGUI.BeginDisabledGroup(true);
                    UnityEditor.EditorGUILayout.PropertyField(guidProp, new GUIContent(kGuidAccess));
                    UnityEditor.EditorGUI.EndDisabledGroup();
                }
            }
        }
    }
#endif
}

