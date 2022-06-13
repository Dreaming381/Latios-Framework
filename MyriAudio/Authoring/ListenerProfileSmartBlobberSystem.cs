using System.Collections.Generic;
using Latios.Authoring;
using Latios.Authoring.Systems;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Myri.Authoring
{
    public struct ListenerProfileBakeData
    {
        public ListenerProfileBuilder builder;
    }

    public static class ListenerProfileBlobberAPIExtensions
    {
        public static SmartBlobberHandle<ListenerProfileBlob> CreateBlob(this GameObjectConversionSystem conversionSystem,
                                                                         GameObject gameObject,
                                                                         ListenerProfileBakeData bakeData)
        {
            return conversionSystem.World.GetExistingSystem<Systems.ListenerProfileSmartBlobberSystem>().AddToConvert(gameObject, bakeData);
        }

        public static SmartBlobberHandleUntyped CreateBlobUntyped(this GameObjectConversionSystem conversionSystem,
                                                                  GameObject gameObject,
                                                                  ListenerProfileBakeData bakeData)
        {
            return conversionSystem.World.GetExistingSystem<Systems.ListenerProfileSmartBlobberSystem>().AddToConvertUntyped(gameObject, bakeData);
        }
    }
}

namespace Latios.Myri.Authoring.Systems
{
    [ConverterVersion("Latios", 4)]
    public sealed class ListenerProfileSmartBlobberSystem : SmartBlobberConversionSystem<ListenerProfileBlob, ListenerProfileBakeData, ListenerProfileConverter>
    {
        struct AuthoringHandlePair
        {
            public AudioListenerAuthoring                  authoring;
            public SmartBlobberHandle<ListenerProfileBlob> handle;
        }

        List<AuthoringHandlePair> m_listenerList = new List<AuthoringHandlePair>();

        DefaultListenerProfileBuilder m_defaultProfile;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_defaultProfile = ScriptableObject.CreateInstance<DefaultListenerProfileBuilder>();
        }

        protected override void OnDestroy()
        {
            Object.Destroy(m_defaultProfile);
            base.OnDestroy();
        }

        protected override void GatherInputs()
        {
            m_listenerList.Clear();
            Entities.ForEach((AudioListenerAuthoring authoring) =>
            {
                var handle = AddToConvert(authoring.gameObject, new ListenerProfileBakeData { builder = authoring.listenerResponseProfile });
                m_listenerList.Add(new AuthoringHandlePair { authoring                                = authoring, handle = handle });
            });
        }

        protected override void FinalizeOutputs()
        {
            foreach (var listener in m_listenerList)
            {
                var authoring = listener.authoring;
                var entity    = GetPrimaryEntity(authoring);
                DstEntityManager.AddComponentData(entity, new AudioListener
                {
                    volume        = authoring.volume,
                    itdResolution = authoring.interauralTimeDifferenceResolution,
                    ildProfile    = listener.handle.Resolve()
                });
            }
        }

        protected override bool Filter(in ListenerProfileBakeData input, GameObject gameObject, out ListenerProfileConverter converter)
        {
            if (input.builder != null)
                DeclareAssetDependency(gameObject, input.builder);

            var profile    = input.builder == null ? m_defaultProfile : input.builder;
            converter.blob = profile.ComputeBlob();
            return true;
        }
    }

    public struct ListenerProfileConverter : ISmartBlobberSimpleBuilder<ListenerProfileBlob>
    {
        public BlobAssetReference<ListenerProfileBlob> blob;

        public BlobAssetReference<ListenerProfileBlob> BuildBlob() => blob;
    }
}

