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

            for (int i = 0; i < layer.curveTypes.Length; i++)
            {
                var curveType = layer.curveTypes[i];
                switch (curveType)
                {
                    case CurveType.AclCurve:
                        ((AclCurve*)ptr)->Evaluate(ref graph, ref bus);
                        ptr += UnsafeUtility.SizeOf<AclCurve>();
                        break;
                    default:
                        throw new System.NotImplementedException($"The curve type {curveType}, ({(int)curveType}) is missing");
                }
            }
        }
    }
}

