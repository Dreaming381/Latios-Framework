﻿using System;
using Unity.Mathematics;

//Note: A == B seems to work with SegmentSegmentDistance
namespace Latios.Psyshock
{
    /// <summary>
    /// A capsule composed of a segment and an inflated radius around the segment
    /// </summary>
    [Serializable]
    public struct CapsuleCollider
    {
        public float3 pointA;
        public float  radius;
        public float3 pointB;

        public CapsuleCollider(float3 pointA, float3 pointB, float radius)
        {
            this.pointA = pointA;
            this.pointB = pointB;
            this.radius = radius;
        }
    }
}

