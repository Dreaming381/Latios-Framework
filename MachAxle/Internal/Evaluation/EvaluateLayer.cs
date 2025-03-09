using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.MachAxle
{
    internal static partial class Evaluation
    {
        public static unsafe void EvaluateLayer<T>(ref Layer layer, ref Graph graph, ref T bus) where T : unmanaged, IBus
        {
            var ptr  = (byte*)graph.curveStream.GetUnsafePtr();
            ptr     += layer.streamStartByte;

#pragma warning disable CS0162  // Unreachable code detected for i++ when LATIOS_DISABLE_ACL is on
            for (int i = 0; i < layer.curveTypes.Length; i++)
#pragma warning restore CS0162
            {
                var curveType = layer.curveTypes[i];
                switch (curveType)
                {
#if !LATIOS_DISABLE_ACL
                    case CurveType.AclCurve:
                        ((AclCurve*)ptr)->Evaluate(ref graph, ref bus);
                        ptr += UnsafeUtility.SizeOf<AclCurve>();
                        break;
#endif
                    default:
                        throw new System.NotImplementedException($"The curve type {curveType}, ({(int)curveType}) is missing");
                }
            }
        }
    }
}

