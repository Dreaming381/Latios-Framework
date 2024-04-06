using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.MachAxle
{
    interface ICurve
    {
        void Evaluate<T>(ref Graph graph, ref T bus) where T : unmanaged, IBus;
    }

    enum CurveType : ushort
    {
        AclCurve
    }
}

