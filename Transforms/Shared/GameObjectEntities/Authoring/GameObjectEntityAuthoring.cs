using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
    [AddComponentMenu("Latios/Transforms/Game Object Entity")]
    [DisallowMultipleComponent]
    public class GameObjectEntityAuthoring : MonoBehaviour
    {
        [SerializeField] GameObjectEntityBindingAuthoring hostEntity;

        [Tooltip("Applies only when no host is specified.")]
        public bool useUniformScale = false;

        public Unity.Entities.Hash128 guid => hostEntity.hostGuid;

        public Entity entity { get; internal set; }

        internal EntityManager entityManager;

        static List<IInitializeGameObjectEntity> s_initializeCache = new List<IInitializeGameObjectEntity>();

        void Awake()
        {
            var latiosWorld = World.DefaultGameObjectInjectionWorld as LatiosWorld;
            if (latiosWorld == null)
            {
                Debug.LogError("The default World is not a LatiosWorld. GameObjectEntityAuthoring will not function correctly.");
                return;
            }

            entityManager                                                                                                 = latiosWorld.EntityManager;
            entity                                                                                                        = entityManager.CreateEntity();
            latiosWorld.latiosWorldUnmanaged.AddManagedStructComponent(entity, new GameObjectEntity { gameObjectTransform = transform });

            if (guid.Equals(default))
            {
                // Self-hosted
                entityManager.AddComponent(entity, Abstract.QueryExtensions.GetAbstractWorldTransformRWComponentType());
                entityManager.AddComponentData(entity, new CopyTransformToEntity { useUniformScale = useUniformScale });

                s_initializeCache.Clear();
                GetComponents(s_initializeCache);
                foreach (var initializer in s_initializeCache)
                {
                    initializer.Initialize(latiosWorld, entity);
                }
            }
            else
            {
                // Bind to host
                entityManager.AddComponentData(entity, new GameObjectEntityBindClient { guid = guid });
            }
            if (!entityManager.HasComponent<DontDestroyOnSceneChangeTag>(entity))
            {
                entityManager.AddComponent<DontDestroyOnSceneChangeTag>(      entity);
                entityManager.AddComponent<RemoveDontDestroyOnSceneChangeTag>(entity);
            }
        }

        void OnDestroy()
        {
            if (entityManager == default)
                return;

            if (entityManager.Exists(entity))
                entityManager.DestroyEntity(entity);
        }
    }
}

