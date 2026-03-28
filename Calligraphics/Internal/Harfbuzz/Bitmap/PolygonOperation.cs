using Latios.Calci;
using Unity.Collections;
using Unity.Mathematics;

namespace Latios.Calligraphics.HarfBuzz
{
    internal class PolygonOperation
    {
        /// <summary> Use BURST compatible Clipper2 library to do a union of polygon on itself to remove self-intersections.  </summary>
        public static void RemoveSelfIntersections(ref DrawData subject)
        {
            var nodes      = subject.edges;
            var contourIDs = subject.contourIDs;
            // We aim for as much precision as we can get to help out Clipper.
            double scale = 5120000.0 / math.max(subject.glyphRect.width, subject.glyphRect.height);
            //var scale      = 1000.0;
            var invScale = 1.0 / scale;

            var subjectNodes = new NativeList<int2>(nodes.Length, Allocator.Temp);
            for (int i = 0, length = contourIDs.Length - 1; i < length; i++)
            {
                int start = contourIDs[i];
                int end   = contourIDs[i + 1];
                for (int k = start; k < end; k++)
                    subjectNodes.Add(new int2((int)(nodes[k].start_pos.x * scale), (int)(nodes[k].start_pos.y * scale)));
            }

            ComputationalGeometry2D.RemoveSelfIntersections(ref subjectNodes, ref contourIDs);

            if (contourIDs.Length < 2)
                return;

            nodes.Clear();
            var solutionStarts = new NativeArray<int>(contourIDs.AsArray(), Allocator.Temp);
            contourIDs.Clear();
            for (int i = 0, length = solutionStarts.Length - 1; i < length; i++)
            {
                contourIDs.Add(nodes.Length);
                int start = solutionStarts[i];
                int end   = solutionStarts[i + 1];
                for (int k = start; k < end - 1; k++)
                {
                    var startPos                      = (float2)((double2)(subjectNodes[k]) * invScale);
                    var endPos                        = (float2)((double2)(subjectNodes[k + 1]) * invScale);
                    nodes.Add(new SDFEdge { start_pos = startPos, end_pos = endPos, edge_type = SDFEdgeType.LINE });
                }
                solutionStarts[i] -= i;
            }
            contourIDs.Add(nodes.Length);
        }
    }
}

