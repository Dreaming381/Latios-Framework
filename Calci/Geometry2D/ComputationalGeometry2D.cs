using Latios.Calci.Clipper2;
using Unity.Collections;
using Unity.Mathematics;

namespace Latios.Calci
{
    /// <summary>
    /// A static class which contains methods to perform complex computational geometry operations.
    /// </summary>
    public static class ComputationalGeometry2D
    {
        /// <summary>
        /// Given a set of contours (also known as linear rings) which may be intersecting or self-intersecting,
        /// computes a clean set of non-intersecting contours using a non-zero winding fill rule. Regardless of
        /// the input winding orders, the output winding order will be counter-clockwise for exterior countours,
        /// and clockwise for interior contours.
        /// </summary>
        /// <param name="vertices">The vertices of the contours, starting with all the vertices for the first
        /// contour, and then all the vertices for the second contour, and so on. It does not matter if there
        /// are accidental duplicate adjacent points nor whether the first point and last point of a contour
        /// are identical.</param>
        /// <param name="contourStartIndices">The indices of the first vertex of each contour. It is assumed that
        /// each contour spans to the next start index or the end of the list of vertices.</param>
        public static void RemoveSelfIntersections(ref NativeList<int2> vertices, ref NativeList<int> contourStartIndices)
        {
            var clipper = new ClipperL(Allocator.Temp);
            clipper.AddSubject(vertices.AsArray(), contourStartIndices.AsArray());

            var solutionNodesClosed    = new NativeList<int2>(vertices.Length, Allocator.Temp);
            var solutionStartIDsClosed = new NativeList<int>(contourStartIndices.Length, Allocator.Temp);
            var solutionNodesOpen      = new NativeList<int2>(0, Allocator.Temp);
            var solutionStartIDsOpen   = new NativeList<int>(0, Allocator.Temp);
            clipper.Execute(ClipType.Union, FillRule.NonZero, ref solutionNodesClosed, ref solutionStartIDsClosed, ref solutionNodesOpen, ref solutionStartIDsOpen);
            vertices.Clear();
            vertices.CopyFrom(solutionNodesClosed);
            contourStartIndices.Clear();
            contourStartIndices.CopyFrom(solutionStartIDsClosed);
        }
    }
}

