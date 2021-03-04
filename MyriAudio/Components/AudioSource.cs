using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Myri
{
    public struct AudioSourceLooped : IComponentData
    {
        //Internal until we can detect changes. Will likely require a separate component.
        internal BlobAssetReference<AudioClipBlob> m_clip;
        internal int                               m_loopOffsetIndex;
        public float                               volume;
        public float                               innerRange;
        public float                               outerRange;
        public float                               rangeFadeMargin;
        internal bool                              m_initialized;

        public BlobAssetReference<AudioClipBlob> clip
        {
            get => m_clip;
            set
            {
                if (m_clip != value)
                {
                    m_initialized     = false;
                    m_loopOffsetIndex = 0;
                    m_clip            = value;
                }
            }
        }

        public void ResetPlaybackState()
        {
            var c = clip;
            clip  = default;
            clip  = c;
        }
    }

    public struct AudioSourceOneShot : IComponentData
    {
        internal BlobAssetReference<AudioClipBlob> m_clip;
        internal int                               m_spawnedAudioFrame;
        internal int                               m_spawnedBufferId;
        public float                               volume;
        public float                               innerRange;
        public float                               outerRange;
        public float                               rangeFadeMargin;

        public BlobAssetReference<AudioClipBlob> clip
        {
            get => m_clip;
            set
            {
                if (m_clip != value)
                {
                    m_spawnedAudioFrame = 0;
                    m_spawnedBufferId   = 0;
                    m_clip              = value;
                }
            }
        }

        public void ResetPlaybackState()
        {
            var c = clip;
            clip  = default;
            clip  = c;
        }

        internal bool isInitialized => (m_spawnedBufferId != 0) | (m_spawnedBufferId != m_spawnedAudioFrame);
    }

    public struct AudioSourceEmitterCone : IComponentData
    {
        public float cosInnerAngle;
        public float cosOuterAngle;
        public float outerAngleAttenuation;
    }

    public struct AudioSourceDestroyOneShotWhenFinished : IComponentData { }

    public struct AudioClipBlob
    {
        public BlobArray<float> samplesLeftOrMono;
        public BlobArray<float> samplesRight;
        public BlobArray<int>   loopedOffsets;
        public BlobString       name;
        public int              sampleRate;

        public bool isStereo => samplesRight.Length == samplesLeftOrMono.Length;
    }
}

