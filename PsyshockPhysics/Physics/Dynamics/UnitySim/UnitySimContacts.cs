using System.Diagnostics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    public static partial class UnitySim
    {
        public unsafe struct ContactsBetweenResult
        {
            public float3      contactNormal;
            public int         contactCount;
            public fixed float contactsData[128];

            public ref ContactOnB this[int index]
            {
                get
                {
                    CheckInRange(index);
                    fixed(void* ptr = contactsData)
                    return ref ((ContactOnB*)ptr)[index];
                }
            }

            public void Add(ContactOnB contact)
            {
                CheckCapacityBeforeAdd();
                this[contactCount] = contact;
                contactCount++;
            }

            public void Add(float3 locationOnB, float distanceToA)
            {
                Add(new ContactOnB { location = locationOnB, distanceToA = distanceToA });
            }

            public void Remove(int index)
            {
                CheckInRange(index);
                this[index] = this[contactCount - 1];
                contactCount--;
            }

            public struct ContactOnB
            {
                public float4 contactData;
                public float3 location
                {
                    get => contactData.xyz;
                    set => contactData.xyz = value;
                }
                public float distanceToA
                {
                    get => contactData.w;
                    set => contactData.w = value;
                }
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            void CheckCapacityBeforeAdd()
            {
                if (contactCount >= 32)
                    throw new System.InvalidOperationException("Cannot add more than 32 contacts.");
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            void CheckInRange(int index)
            {
                if (index < 0 || index >= contactCount)
                    throw new System.ArgumentOutOfRangeException($"Contact index {index} is out of range of [0, {contactCount})");
            }
        }
    }
}

