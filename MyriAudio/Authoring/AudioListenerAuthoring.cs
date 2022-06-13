using Unity.Entities;
using Unity.Mathematics;
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
}

