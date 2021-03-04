using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Myri.Authoring
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Latios/Audio (Myri)/Audio Listener")]
    public class AudioListenerAuthoring : MonoBehaviour
    {
        public float volume = 1f;

        [Range(0, 15)]
        public int interauralTimeDifferenceResolution = 2;

        public AudioIldProfileBuilder listenerResponseProfile;
    }
}

