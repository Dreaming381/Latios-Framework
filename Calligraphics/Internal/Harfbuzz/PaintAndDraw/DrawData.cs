using Latios.Calligraphics.HarfBuzz.Bitmap;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Calligraphics.HarfBuzz
{
    internal struct DrawData
    {
        public BBox                glyphRect;
        public NativeList<SDFEdge> edges;
        public float               maxDeviation;  // Not used currently.
        /// <summary> list of first indices of a new contour. Use last index to store length of edges list for easier iteration</summary>
        public NativeList<int> contourIDs;
        public DrawData(int edgeCapacity, int contourCapacity, float maxDeviation, Allocator allocator)
        {
            this.maxDeviation = maxDeviation;
            edges             = new NativeList<SDFEdge>(edgeCapacity, allocator);
            contourIDs        = new NativeList<int>(contourCapacity, allocator);
            glyphRect         = BBox.Empty;
            contourIDs.Add(0);
        }
        public void Clear()
        {
            glyphRect = BBox.Empty;
            edges.Clear();
            contourIDs.Clear();
            contourIDs.Add(0);
        }
        public void Dispose()
        {
            if (edges.IsCreated)
                edges.Dispose();
            if (contourIDs.IsCreated)
                contourIDs.Dispose();
        }
        public void PrintOrientations()
        {
            for (int contourID = 0, end = contourIDs.Length - 1; contourID < end; contourID++)  //for each remaining contour
            {
                var startID            = contourIDs[contourID];
                var nextStartID        = contourIDs[contourID + 1];
                var contourOrientation = SDFCommon.GetPolyOrientation(SDFCommon.SignedArea(edges, startID, nextStartID));
                Debug.Log($"{contourOrientation}");
            }
        }
    }
    /// <summary>Represent an edge of a contour  </summary>
    /// <param name="start_pos">Start position of an edge.Valid for all types of edges.</param>
    /// <param name="end_pos">End position of an edge.  Valid for all types of edges.</param>
    /// <param name="control1">A control point of the edge.Valid only for <see cref="SDFEdgeType.QUADRATIC"/> and <see cref="SDFEdgeType.CUBIC"/> </param>
    /// <param name="control2">A control point of the edge.Valid only for <see cref="SDFEdgeType.CUBIC"/> </param>
    /// <param name="edge_type">Type of the edge, see <see cref="SDFEdgeType"/> for all possible edge types. </param>
    internal struct SDFEdge
    {
        public float2      start_pos;
        public float2      end_pos;
        public float2      control1;
        public float2      control2;
        public SDFEdgeType edge_type;

        /// <summary> Line Edge </summary>
        public SDFEdge(float current_x, float current_y, float to_x, float to_y)
        {
            start_pos = new float2(current_x, current_y);
            end_pos   = new float2(to_x, to_y);
            control1  = default;
            control2  = default;
            edge_type = SDFEdgeType.LINE;
        }
        /// <summary> Quadratic Edge </summary>
        public SDFEdge(float current_x, float current_y, float control_x, float control_y, float to_x, float to_y)
        {
            start_pos = new float2(current_x, current_y);
            end_pos   = new float2(to_x, to_y);
            control1  = new float2(control_x, control_y);
            control2  = default;
            edge_type = SDFEdgeType.QUADRATIC;
        }
        /// <summary> Cubic Edge </summary>
        public SDFEdge(float current_x, float current_y, float control1_x, float control1_y, float control2_x, float control2_y, float to_x, float to_y)
        {
            start_pos = new float2(current_x, current_y);
            end_pos   = new float2(to_x, to_y);
            control1  = new float2(control1_x, control1_y);
            control2  = new float2(control2_x, control2_y);
            edge_type = SDFEdgeType.CUBIC;
        }
    }
    internal enum SDFEdgeType : byte
    {
        UNDEFINED = 0,
        LINE = 1,
        QUADRATIC = 2,
        CUBIC = 3
    }
}

