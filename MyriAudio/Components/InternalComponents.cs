using System;
using Unity.Entities;

namespace Latios.Myri
{
    internal struct TrackedListener : ICleanupComponentData
    {
        public byte packed;
        public bool hasChannelIDs
        {
            get => Bits.GetBit(packed, 0);
            set => Bits.SetBit(ref packed, 0, value);
        }
    }

    internal partial struct AudioEcsBootstrapCarrier : IManagedStructComponent, IDisposable
    {
        public IAudioEcsBootstrap bootstrap;

        public void Dispose()
        {
            if (bootstrap is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}

