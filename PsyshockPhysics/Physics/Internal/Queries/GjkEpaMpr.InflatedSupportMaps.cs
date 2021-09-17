using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static partial class SpatialInternal
    {
        internal struct InflatedSupportMap
        {
            NativeArray<float>        m_x;
            NativeArray<float>        m_y;
            NativeArray<float>        m_z;
            NativeArray<SupportPoint> m_uninflatedSupports;
            NativeArray<int>          m_offsetIds;
            NativeArray<float3>       m_offsets;

            internal float3 GetSupport(float3 direction, out int index)
            {
                float bestDot   = float.MinValue;
                int   bestIndex = 0;
                int   count     = m_x.Length;
                float dx        = direction.x;
                float dy        = direction.y;
                float dz        = direction.z;
                for (int i = 0; i < count; i++)
                {
                    float dot      = m_x[i] * dx + m_y[i] * dy + m_z[i] * dz;
                    bool  isBetter = dot > bestDot;
                    bestDot        = math.select(bestDot, dot, isBetter);
                    bestIndex      = math.select(bestIndex, i, isBetter);
                }
                index = bestIndex;
                return new float3(m_x[bestIndex], m_y[bestIndex], m_z[bestIndex]);
            }

            internal SupportPoint GetSupport(int index) => m_uninflatedSupports[index];

            internal int GetOffsetId(int index) => m_offsetIds[index];
            internal float3 GetOffset(int index) => m_offsets[GetOffsetId(index)];

            internal InflatedSupportMap(NativeArray<float>        x,
                                        NativeArray<float>        y,
                                        NativeArray<float>        z,
                                        NativeArray<SupportPoint> uninflatedSupports,
                                        NativeArray<int>          offsetIds,
                                        NativeArray<float3>       offsets)
            {
                m_x                  = x;
                m_y                  = y;
                m_z                  = z;
                m_uninflatedSupports = uninflatedSupports;
                m_offsetIds          = offsetIds;
                m_offsets            = offsets;
            }
        }

        // Pass around by ref if necessary. Otherwise keep as local variable.
        internal unsafe struct CapsuleBoxInflatedSupportMap
        {
            // pointsPerFace * faces * capPoints + edges * pointsPerEdge * capPoints * directionsOfCross
            const int           k_elements = 4 * 6 * 2 + 12 * 2 * 2 * 2;
            const int           k_offsets  = 6 + 3 * 2;  // 6 faces + 3 axes * 2 directions
            private fixed float m_x[k_elements];
            private fixed float m_y[k_elements];
            private fixed float m_z[k_elements];
            private fixed uint  m_uninflatedSupports[k_elements * 4];
            private fixed int   m_offsetIds[k_elements];
            private fixed float m_offsets[k_offsets * 3];

            CapsuleCollider m_capsuleInBoxSpace;
            BoxCollider     m_box;

            internal InflatedSupportMap GetInflatedSupportMap()
            {
                fixed (void* px    = m_x)
                fixed (void* py    = m_y)
                fixed (void* pz    = m_z)
                fixed (void* pus   = m_uninflatedSupports)
                fixed (void* poids = m_offsetIds)
                fixed (void* po    = m_offsets)

                {
                    var x    = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<float>(px, k_elements, Allocator.None);
                    var y    = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<float>(py, k_elements, Allocator.None);
                    var z    = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<float>(pz, k_elements, Allocator.None);
                    var us   = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<SupportPoint>(pus, k_elements, Allocator.None);
                    var oids = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<int>(poids, k_elements, Allocator.None);
                    var o    = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<float3>(po, k_offsets, Allocator.None);

                    return new InflatedSupportMap(x, y, z, us, oids, o);
                }
            }

            internal CapsuleBoxInflatedSupportMap(CapsuleCollider capsuleInBoxSpace, BoxCollider box)
            {
                UnityEngine.Assertions.Assert.IsTrue(sizeof(SupportPoint) == 16 && UnsafeUtility.AlignOf<SupportPoint>() <= 16);

                m_capsuleInBoxSpace = capsuleInBoxSpace;
                m_box               = box;

                int i = 0;

                int  capA                = 0;
                int  capB                = 1;
                bool neg                 = true;
                bool pos                 = false;
                int  boxTopLeftFront     = math.bitmask(new bool4(neg, pos, neg, false));
                int  boxTopRightFront    = math.bitmask(new bool4(pos, pos, neg, false));
                int  boxTopLeftBack      = math.bitmask(new bool4(neg, pos, pos, false));
                int  boxTopRightBack     = math.bitmask(new bool4(pos, pos, pos, false));
                int  boxBottomLeftFront  = math.bitmask(new  bool4(neg, neg, neg, false));
                int  boxBottomRightFront = math.bitmask(new bool4(pos, neg, neg, false));
                int  boxBottomLeftBack   = math.bitmask(new   bool4(neg, neg, pos, false));
                int  boxBottomRightBack  = math.bitmask(new  bool4(pos, neg, pos, false));

                int up       = 0;  //math.up()
                int down     = 1;  //math.down()
                int left     = 2;  //math.left()
                int right    = 3;  //math.right()
                int forward  = 4;  //math.forward()
                int backward = 5;  //math.back()

                float3 edge = math.normalizesafe(capsuleInBoxSpace.pointB - capsuleInBoxSpace.pointA);
                int    posX = 6;  //math.cross(right, edge)
                int    posY = 7;  //math.cross(up, edge)
                int    posZ = 8;  //math.cross(forward, edge)
                int    negX = 9;  //posX
                int    negY = 10;  //posY
                int    negZ = 11;  //-posZ

                fixed (void* offsets = m_offsets)
                {
                    int j = 0;
                    UnsafeUtility.WriteArrayElement(offsets, j++, math.up());
                    UnsafeUtility.WriteArrayElement(offsets, j++, math.down());
                    UnsafeUtility.WriteArrayElement(offsets, j++, math.left());
                    UnsafeUtility.WriteArrayElement(offsets, j++, math.right());
                    UnsafeUtility.WriteArrayElement(offsets, j++, math.forward());
                    UnsafeUtility.WriteArrayElement(offsets, j++, math.back());

                    float3 px = math.cross(right, edge);;
                    float3 py = math.cross(up, edge);
                    float3 pz = math.cross(forward, edge);

                    UnsafeUtility.WriteArrayElement(offsets, j++, px);
                    UnsafeUtility.WriteArrayElement(offsets, j++, py);
                    UnsafeUtility.WriteArrayElement(offsets, j++, pz);
                    UnsafeUtility.WriteArrayElement(offsets, j++, -px);
                    UnsafeUtility.WriteArrayElement(offsets, j++, -py);
                    UnsafeUtility.WriteArrayElement(offsets, j++, -pz);
                }

                // Cap A and faces
                BuildElement(i++, boxTopLeftFront,     capA, up);
                BuildElement(i++, boxTopRightFront,    capA, up);
                BuildElement(i++, boxTopLeftBack,      capA, up);
                BuildElement(i++, boxTopRightBack,     capA, up);

                BuildElement(i++, boxBottomLeftFront,  capA, down);
                BuildElement(i++, boxBottomRightFront, capA, down);
                BuildElement(i++, boxBottomLeftBack,   capA, down);
                BuildElement(i++, boxBottomRightBack,  capA, down);

                BuildElement(i++, boxTopLeftFront,     capA, left);
                BuildElement(i++, boxTopLeftBack,      capA, left);
                BuildElement(i++, boxBottomLeftFront,  capA, left);
                BuildElement(i++, boxBottomLeftBack,   capA, left);

                BuildElement(i++, boxTopRightFront,    capA, right);
                BuildElement(i++, boxTopRightBack,     capA, right);
                BuildElement(i++, boxBottomRightFront, capA, right);
                BuildElement(i++, boxBottomRightBack,  capA, right);

                BuildElement(i++, boxTopLeftFront,     capA, forward);
                BuildElement(i++, boxTopRightFront,    capA, forward);
                BuildElement(i++, boxBottomLeftFront,  capA, forward);
                BuildElement(i++, boxBottomRightFront, capA, forward);

                BuildElement(i++, boxTopLeftBack,      capA, backward);
                BuildElement(i++, boxTopRightBack,     capA, backward);
                BuildElement(i++, boxBottomLeftBack,   capA, backward);
                BuildElement(i++, boxBottomRightBack,  capA, backward);

                // Cap B and faces
                BuildElement(i++, boxTopLeftFront,     capB, up);
                BuildElement(i++, boxTopRightFront,    capB, up);
                BuildElement(i++, boxTopLeftBack,      capB, up);
                BuildElement(i++, boxTopRightBack,     capB, up);

                BuildElement(i++, boxBottomLeftFront,  capB, down);
                BuildElement(i++, boxBottomRightFront, capB, down);
                BuildElement(i++, boxBottomLeftBack,   capB, down);
                BuildElement(i++, boxBottomRightBack,  capB, down);

                BuildElement(i++, boxTopLeftFront,     capB, left);
                BuildElement(i++, boxTopLeftBack,      capB, left);
                BuildElement(i++, boxBottomLeftFront,  capB, left);
                BuildElement(i++, boxBottomLeftBack,   capB, left);

                BuildElement(i++, boxTopRightFront,    capB, right);
                BuildElement(i++, boxTopRightBack,     capB, right);
                BuildElement(i++, boxBottomRightFront, capB, right);
                BuildElement(i++, boxBottomRightBack,  capB, right);

                BuildElement(i++, boxTopLeftFront,     capB, backward);
                BuildElement(i++, boxTopRightFront,    capB, backward);
                BuildElement(i++, boxBottomLeftFront,  capB, backward);
                BuildElement(i++, boxBottomRightFront, capB, backward);

                BuildElement(i++, boxTopLeftBack,      capB, forward);
                BuildElement(i++, boxTopRightBack,     capB, forward);
                BuildElement(i++, boxBottomLeftBack,   capB, forward);
                BuildElement(i++, boxBottomRightBack,  capB, forward);

                // Cap A and pos edges
                BuildElement(i++, boxTopLeftFront,     capA, posY);
                BuildElement(i++, boxTopRightFront,    capA, posY);
                BuildElement(i++, boxTopLeftBack,      capA, posY);
                BuildElement(i++, boxTopRightBack,     capA, posY);

                BuildElement(i++, boxBottomLeftFront,  capA, posY);
                BuildElement(i++, boxBottomRightFront, capA, posY);
                BuildElement(i++, boxBottomLeftBack,   capA, posY);
                BuildElement(i++, boxBottomRightBack,  capA, posY);

                BuildElement(i++, boxTopLeftFront,     capA, posX);
                BuildElement(i++, boxTopLeftBack,      capA, posX);
                BuildElement(i++, boxBottomLeftFront,  capA, posX);
                BuildElement(i++, boxBottomLeftBack,   capA, posX);

                BuildElement(i++, boxTopRightFront,    capA, posX);
                BuildElement(i++, boxTopRightBack,     capA, posX);
                BuildElement(i++, boxBottomRightFront, capA, posX);
                BuildElement(i++, boxBottomRightBack,  capA, posX);

                BuildElement(i++, boxTopLeftFront,     capA, posZ);
                BuildElement(i++, boxTopRightFront,    capA, posZ);
                BuildElement(i++, boxBottomLeftFront,  capA, posZ);
                BuildElement(i++, boxBottomRightFront, capA, posZ);

                BuildElement(i++, boxTopLeftBack,      capA, posZ);
                BuildElement(i++, boxTopRightBack,     capA, posZ);
                BuildElement(i++, boxBottomLeftBack,   capA, posZ);
                BuildElement(i++, boxBottomRightBack,  capA, posZ);

                // Cap A and neg edges
                BuildElement(i++, boxTopLeftFront,     capA, negY);
                BuildElement(i++, boxTopRightFront,    capA, negY);
                BuildElement(i++, boxTopLeftBack,      capA, negY);
                BuildElement(i++, boxTopRightBack,     capA, negY);

                BuildElement(i++, boxBottomLeftFront,  capA, negY);
                BuildElement(i++, boxBottomRightFront, capA, negY);
                BuildElement(i++, boxBottomLeftBack,   capA, negY);
                BuildElement(i++, boxBottomRightBack,  capA, negY);

                BuildElement(i++, boxTopLeftFront,     capA, negX);
                BuildElement(i++, boxTopLeftBack,      capA, negX);
                BuildElement(i++, boxBottomLeftFront,  capA, negX);
                BuildElement(i++, boxBottomLeftBack,   capA, negX);

                BuildElement(i++, boxTopRightFront,    capA, negX);
                BuildElement(i++, boxTopRightBack,     capA, negX);
                BuildElement(i++, boxBottomRightFront, capA, negX);
                BuildElement(i++, boxBottomRightBack,  capA, negX);

                BuildElement(i++, boxTopLeftFront,     capA, negZ);
                BuildElement(i++, boxTopRightFront,    capA, negZ);
                BuildElement(i++, boxBottomLeftFront,  capA, negZ);
                BuildElement(i++, boxBottomRightFront, capA, negZ);

                BuildElement(i++, boxTopLeftBack,      capA, negZ);
                BuildElement(i++, boxTopRightBack,     capA, negZ);
                BuildElement(i++, boxBottomLeftBack,   capA, negZ);
                BuildElement(i++, boxBottomRightBack,  capA, negZ);

                // Cap B and pos edges
                BuildElement(i++, boxTopLeftFront,     capB, posY);
                BuildElement(i++, boxTopRightFront,    capB, posY);
                BuildElement(i++, boxTopLeftBack,      capB, posY);
                BuildElement(i++, boxTopRightBack,     capB, posY);

                BuildElement(i++, boxBottomLeftFront,  capB, posY);
                BuildElement(i++, boxBottomRightFront, capB, posY);
                BuildElement(i++, boxBottomLeftBack,   capB, posY);
                BuildElement(i++, boxBottomRightBack,  capB, posY);

                BuildElement(i++, boxTopLeftFront,     capB, posX);
                BuildElement(i++, boxTopLeftBack,      capB, posX);
                BuildElement(i++, boxBottomLeftFront,  capB, posX);
                BuildElement(i++, boxBottomLeftBack,   capB, posX);

                BuildElement(i++, boxTopRightFront,    capB, posX);
                BuildElement(i++, boxTopRightBack,     capB, posX);
                BuildElement(i++, boxBottomRightFront, capB, posX);
                BuildElement(i++, boxBottomRightBack,  capB, posX);

                BuildElement(i++, boxTopLeftFront,     capB, posZ);
                BuildElement(i++, boxTopRightFront,    capB, posZ);
                BuildElement(i++, boxBottomLeftFront,  capB, posZ);
                BuildElement(i++, boxBottomRightFront, capB, posZ);

                BuildElement(i++, boxTopLeftBack,      capB, posZ);
                BuildElement(i++, boxTopRightBack,     capB, posZ);
                BuildElement(i++, boxBottomLeftBack,   capB, posZ);
                BuildElement(i++, boxBottomRightBack,  capB, posZ);

                // Cap B and neg edges
                BuildElement(i++, boxTopLeftFront,     capB, negY);
                BuildElement(i++, boxTopRightFront,    capB, negY);
                BuildElement(i++, boxTopLeftBack,      capB, negY);
                BuildElement(i++, boxTopRightBack,     capB, negY);

                BuildElement(i++, boxBottomLeftFront,  capB, negY);
                BuildElement(i++, boxBottomRightFront, capB, negY);
                BuildElement(i++, boxBottomLeftBack,   capB, negY);
                BuildElement(i++, boxBottomRightBack,  capB, negY);

                BuildElement(i++, boxTopLeftFront,     capB, negX);
                BuildElement(i++, boxTopLeftBack,      capB, negX);
                BuildElement(i++, boxBottomLeftFront,  capB, negX);
                BuildElement(i++, boxBottomLeftBack,   capB, negX);

                BuildElement(i++, boxTopRightFront,    capB, negX);
                BuildElement(i++, boxTopRightBack,     capB, negX);
                BuildElement(i++, boxBottomRightFront, capB, negX);
                BuildElement(i++, boxBottomRightBack,  capB, negX);

                BuildElement(i++, boxTopLeftFront,     capB, negZ);
                BuildElement(i++, boxTopRightFront,    capB, negZ);
                BuildElement(i++, boxBottomLeftFront,  capB, negZ);
                BuildElement(i++, boxBottomRightFront, capB, negZ);

                BuildElement(i++, boxTopLeftBack,      capB, negZ);
                BuildElement(i++, boxTopRightBack,     capB, negZ);
                BuildElement(i++, boxBottomLeftBack,   capB, negZ);
                BuildElement(i++, boxBottomRightBack,  capB, negZ);
            }

            private void BuildElement(int index, int boxIndex, int capsuleIndex, int offsetDirection)
            {
                SupportPoint support = default;
                support.id           = ((uint)capsuleIndex << 16) | (uint)boxIndex;
                support.pos          =
                    math.select(m_capsuleInBoxSpace.pointA, m_capsuleInBoxSpace.pointB,
                                capsuleIndex != 0) - (m_box.center + math.select(m_box.halfSize, -m_box.halfSize, (new uint3(1, 2, 4) & (uint)boxIndex) != 0));
                float3 inflatedPos = support.pos + offsetDirection * m_capsuleInBoxSpace.radius;
                m_x[index]         = inflatedPos.x;
                m_y[index]         = inflatedPos.y;
                m_z[index]         = inflatedPos.z;
                fixed (void* us    = m_uninflatedSupports)
                UnsafeUtility.WriteArrayElement(us, index, support);
                m_offsetIds[index] = offsetDirection;
            }
        }
    }
}

