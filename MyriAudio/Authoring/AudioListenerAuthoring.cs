using Unity.Entities;
using UnityEngine;

namespace Latios.Myri.Authoring
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Latios/Audio (Myri)/Audio Listener")]
    public class AudioListenerAuthoring : MonoBehaviour
    {
        [Tooltip("The raw volume applied to everything the listener hears. This value is not in decibels.")]
        public float volume = 1f;

        [Tooltip("The resolution of time-based spatialization. Increasing this value incurs a higher cost but may increase the player's sense of direction.")]
        [Range(0, 15)]
        public int interauralTimeDifferenceResolution = 2;

        [Tooltip("A custom volume and frequency spatialization profile. If empty, a default profile will be used.")]
        public ListenerProfileBuilder listenerResponseProfile;
    }

    public class AudioListenerBaker : Baker<AudioListenerAuthoring>
    {
        public override void Bake(AudioListenerAuthoring authoring)
        {
            BlobAssetReference<ListenerProfileBlob> blob;
            if (authoring.listenerResponseProfile == null)
            {
                var defaultBuilder = new DefaultListenerProfileBuilder();
                blob               = this.BuildAndRegisterListenerProfileBlob(defaultBuilder);
            }
            else
                blob = this.BuildAndRegisterListenerProfileBlob(authoring.listenerResponseProfile);

            var entity = GetEntity(TransformUsageFlags.Renderable);
            AddComponent(entity, new AudioListener
            {
                ildProfile    = blob,
                itdResolution = authoring.interauralTimeDifferenceResolution,
                volume        = authoring.volume
            });
        }
    }
}

