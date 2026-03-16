using System.IO;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Mathematics;

namespace Latios.Calci.Clipper2
{
    internal static class PolyTreeAccessorExtensions
    {
        /// <summary>
        /// positive area = CCW, negative area = CW (works for closed and open polygon (identical result))
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double SignedArea(NativeList<int2> data, int start, int end)
        {
            double area = default;
            for (int i = start, prev = end - 1; i < end; prev = i++) //from (0, prev) until (end, prev)
                area += ((double)data[prev].x - (double)data[i].x) * ((double)data[i].y + (double)data[prev].y);
            return area * 0.5;
        }
        /// <summary>
        /// positive area = CCW, negative area = CW (works for closed and open polygon (identical result))
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double SignedArea(NativeList<int2> nodes, NativeList<int> startIDs)
        {
            double area = 0;
            for (int k = 0, length = startIDs.Length - 1; k < length; k++)
            {
                int start  = startIDs[k];
                int end    = startIDs[k + 1];
                area      += SignedArea(nodes, start, end);
                //Debug.Log($"Area: {area}");
            }
            return area;
        }

        #region extension methods
        public static void PolyTree_GetSolution_DepthFirst(this ref ClipperL clipperL,
                                                           ref PolyTree polytree,
                                                           ref NativeList<int2> solutionNodes,
                                                           ref NativeList<int>  solutionStartIDs)
        {
            var root  = polytree.root;
            var nodes = polytree.nodes;
            if (root == -1)
                return;
            var stack = new NativeList<int>(16, Allocator.Temp)
            {
                polytree.root
            };
            while (!stack.IsEmpty)
            {
                int nodeIndex = stack[^ 1];
                stack.RemoveAt(stack.Length - 1);

                //process node here
                var node = nodes[nodeIndex];
                if (nodeIndex != 0)  //skip root node
                {
                    var     outrecID = node.outrecIdx;
                    ref var outrec   = ref clipperL.OutrecList.ElementAt(outrecID);
                    clipperL.BuildPath(outrec.pts, false, false, ref solutionNodes, ref solutionStartIDs);
                }

                //push children in reverse order to stack
                //so that first child is processed first
                var first = stack.Length;
                for (int c = node.firstChild; c != -1; c = nodes[c].nextSibling)
                    stack.Add(c);
                var last = stack.Length - 1;
                stack.Reverse(first, last);
            }
        }
        public static void PolyTree_GetSolution_BreathFirst(this ref ClipperL clipperL,
                                                            ref PolyTree polytree,
                                                            ref NativeList<int2> solutionNodes,
                                                            ref NativeList<int>  solutionStartIDs)
        {
            var root  = polytree.root;
            var nodes = polytree.nodes;
            if (root == -1)
                return;
            var queue = new NativeList<int>(16, Allocator.Temp)
            {
                root
            };
            while (!queue.IsEmpty)
            {
                int nodeIndex = queue[0];
                queue.RemoveAt(0);

                //process node here
                var node = nodes[nodeIndex];
                if (nodeIndex != 0)  //skip root node
                {
                    var     outrecID = node.outrecIdx;
                    ref var outrec   = ref clipperL.OutrecList.ElementAt(outrecID);
                    clipperL.BuildPath(outrec.pts, false, false, ref solutionNodes, ref solutionStartIDs);
                }

                //push children into queue
                for (int c = node.firstChild; c != -1; c = nodes[c].nextSibling)
                    queue.Add(c);
            }
        }
        public static bool CheckPolytreeFullyContainsChildren(this ref ClipperL clipperL, ref PolyTree polytree)
        {
            // first get all the top level exterior outrec. Identical to Clipper2 PolyTree64.Count:
            // for (int i = 0; i < polytree.Count; i++)
            var root  = polytree.root;
            var nodes = polytree.nodes;
            if (root == -1)
                return false;
            var stack = new NativeList<int>(2048, Allocator.Temp);
            var first = stack.Length;
            for (int c = nodes[root].firstChild; c != -1; c = nodes[c].nextSibling)
                stack.Add(c);
            var last = stack.Length - 1;
            stack.Reverse(first, last);

            while (!stack.IsEmpty)
            {
                int nodeIndex = stack[^ 1];
                stack.RemoveAt(stack.Length - 1);

                var     node     = nodes[nodeIndex];
                var     outrecID = node.outrecIdx;
                ref var outrec   = ref clipperL.OutrecList.ElementAt(outrecID);
                if (node.childCount > 0)
                {
                    if (!ParentContainsAllChildren(ref clipperL, ref polytree, nodeIndex, outrec.pts))
                        return false;
                }
            }
            return true;
        }
        public static bool PolytreeContainsPoint(this ref ClipperL clipperL, ref PolyTree polytree, long2 pt, ref int counter)
        {
            var root  = polytree.root;
            var nodes = polytree.nodes;
            if (root == -1)
                return false;
            var stack = new NativeList<int>(2048, Allocator.Temp);
            var first = stack.Length;
            for (int c = nodes[0].firstChild; c != -1; c = nodes[c].nextSibling)
                stack.Add(c);
            var last = stack.Length - 1;
            stack.Reverse(first, last);

            while (!stack.IsEmpty)
            {
                int nodeIndex = stack[^ 1];
                stack.RemoveAt(stack.Length - 1);

                var     node     = nodes[nodeIndex];
                var     outrecID = node.outrecIdx;
                ref var outrec   = ref clipperL.OutrecList.ElementAt(outrecID);
                var     parent   = clipperL.GetCleanPath(outrec.pts);
                PolyPathContainsPoint(ref clipperL, ref polytree, nodeIndex, parent, pt, ref counter);
            }
            //Assert.IsTrue(counter >= 0, $"Polytree has too many holes: {counter}");
            return counter != 0;
        }
        static bool ParentContainsAllChildren(this ref ClipperL clipperL, ref PolyTree polytree, int parentNode, int parentOpID)
        {
            var nodes = polytree.nodes;
            if (parentNode == -1)
                return false;
            var queue = new NativeList<int>(16, Allocator.Temp);
            for (int c = nodes[parentNode].firstChild; c != -1; c = nodes[c].nextSibling)
                queue.Add(c);

            while (!queue.IsEmpty)
            {
                int nodeIndex = queue[0];
                queue.RemoveAt(0);

                var     node     = nodes[nodeIndex];
                var     outrecID = node.outrecIdx;
                ref var outrec   = ref clipperL.OutrecList.ElementAt(outrecID);
                if (!clipperL.Path1InsidePath2(outrec.pts, parentOpID))
                    return false;
            }
            return true;
        }
        static void PolyPathContainsPoint(this ref ClipperL clipperL, ref PolyTree polytree, int parentNode, NativeList<long2> parentPolygon, long2 pt, ref int counter)
        {
            var nodes = polytree.nodes;
            if (parentNode == -1)
                return;
            var queue = new NativeList<int>(16, Allocator.Temp);
            for (int c = nodes[parentNode].firstChild; c != -1; c = nodes[c].nextSibling)
                queue.Add(c);

            if (InternalClipper.PointInPolygon(pt, parentPolygon) != PointInPolygonResult.IsOutside)
            {
                if (polytree.IsHole(parentNode))
                    --counter;
                else
                    ++counter;
            }

            while (!queue.IsEmpty)
            {
                int nodeIndex = queue[0];
                queue.RemoveAt(0);

                var     node     = nodes[nodeIndex];
                var     outrecID = node.outrecIdx;
                ref var outrec   = ref clipperL.OutrecList.ElementAt(outrecID);

                var child = clipperL.GetCleanPath(outrec.pts);
                PolyPathContainsPoint(ref clipperL, ref polytree, nodeIndex, child, pt, ref counter);
            }
            return;
        }
        public static void PolyTree_WriteToFile_DepthFirst(this ref ClipperL clipperL,
                                                           ref PolyTree polytree,
                                                           ref NativeList<int2> solutionNodes,
                                                           ref NativeList<int>  solutionStartIDs)
        {
            StreamWriter writer = new StreamWriter("polytree_wrong.txt", false);
            TreeNode     parent;
            TreeNode     parentParent;
            TreeNode     parentParentParent;

            var root  = polytree.root;
            var nodes = polytree.nodes;
            if (root == -1)
                return;
            var stack = new NativeList<int>(16, Allocator.Temp)
            {
                polytree.root
            };
            while (!stack.IsEmpty)
            {
                int nodeIndex = stack[^ 1];
                stack.RemoveAt(stack.Length - 1);

                //process node here
                var node = nodes[nodeIndex];
                if (nodeIndex != 0)  //skip root node
                {
                    var     outrecID = node.outrecIdx;
                    ref var outrec   = ref clipperL.OutrecList.ElementAt(outrecID);
                    clipperL.BuildPath(outrec.pts, false, false, ref solutionNodes, ref solutionStartIDs);

                    var level = polytree.GetLevel(nodeIndex);
                    switch (level)
                    {
                        case 0:
                            //writer.WriteLine($"0000\n");
                            break;
                        case 1:
                            writer.WriteLine($"{node.outrecIdx:D3} ({solutionNodes.Length - 1} nodes)");
                            break;
                        case 2:
                            parent = nodes[node.parent];
                            writer.WriteLine($"{parent.outrecIdx:D3}__{node.outrecIdx:D3} ({solutionNodes.Length - 1} nodes)");
                            break;
                        case 3:
                            parent       = nodes[node.parent];
                            parentParent = nodes[parent.parent];
                            writer.WriteLine($"{parentParent.outrecIdx:D3}__{parent.outrecIdx:D3}__{node.outrecIdx:D3} ({solutionNodes.Length - 1} nodes)");
                            break;
                        case 4:
                            parent             = nodes[node.parent];
                            parentParent       = nodes[parent.parent];
                            parentParentParent = nodes[parentParent.parent];
                            writer.WriteLine(
                                $"{parentParentParent.outrecIdx:D3}__{parentParent.outrecIdx:D3}__{parent.outrecIdx:D3}__{node.outrecIdx:D3} ({solutionNodes.Length - 1} nodes)");
                            break;
                        default:
                            writer.WriteLine($"{node.outrecIdx:D3} (level: {level} ({solutionNodes.Length - 1} nodes)");
                            break;
                    }
                    solutionNodes.Clear();
                    solutionStartIDs.Clear();
                }

                //push children in reverse order to stack
                //so that first child is processed first
                var first = stack.Length;
                for (int c = node.firstChild; c != -1; c = nodes[c].nextSibling)
                    stack.Add(c);
                var last = stack.Length - 1;
                stack.Reverse(first, last);
            }
            writer.Close();
        }
        public static void PolyTree_WriteToFile_BreathFirst(this ref ClipperL clipperL,
                                                            ref PolyTree polytree,
                                                            ref NativeList<int2> solutionNodes,
                                                            ref NativeList<int>  solutionStartIDs)
        {
            StreamWriter writer = new StreamWriter("polytree_wrong.txt", false);
            TreeNode     parent;
            TreeNode     parentParent;
            TreeNode     parentParentParent;

            var root  = polytree.root;
            var nodes = polytree.nodes;
            if (root == -1)
                return;
            var queue = new NativeList<int>(16, Allocator.Temp)
            {
                root
            };
            while (!queue.IsEmpty)
            {
                int nodeIndex = queue[0];
                queue.RemoveAt(0);

                //process node here
                var node = nodes[nodeIndex];
                if (nodeIndex != 0)  //skip root node
                {
                    var     outrecID = node.outrecIdx;
                    ref var outrec   = ref clipperL.OutrecList.ElementAt(outrecID);
                    clipperL.BuildPath(outrec.pts, false, false, ref solutionNodes, ref solutionStartIDs);

                    var level = polytree.GetLevel(nodeIndex);
                    switch (level)
                    {
                        case 0:
                            //writer.WriteLine($"0000\n");
                            break;
                        case 1:
                            writer.WriteLine($"{node.outrecIdx:D3} ({solutionNodes.Length - 1} nodes)");
                            break;
                        case 2:
                            parent = nodes[node.parent];
                            writer.WriteLine($"{parent.outrecIdx:D3}__{node.outrecIdx:D3} ({solutionNodes.Length - 1} nodes)");
                            break;
                        case 3:
                            parent       = nodes[node.parent];
                            parentParent = nodes[parent.parent];
                            writer.WriteLine($"{parentParent.outrecIdx:D3}__{parent.outrecIdx:D3}__{node.outrecIdx:D3} ({solutionNodes.Length - 1} nodes)");
                            break;
                        case 4:
                            parent             = nodes[node.parent];
                            parentParent       = nodes[parent.parent];
                            parentParentParent = nodes[parentParent.parent];
                            writer.WriteLine(
                                $"{parentParentParent.outrecIdx:D3}__{parentParent.outrecIdx:D3}__{parent.outrecIdx:D3}__{node.outrecIdx:D3} ({solutionNodes.Length - 1} nodes)");
                            break;
                        default:
                            writer.WriteLine($"{node.outrecIdx:D3} (level: {level} ({solutionNodes.Length - 1} nodes)");
                            break;
                    }
                    solutionNodes.Clear();
                    solutionStartIDs.Clear();
                }

                //push children into queue
                for (int c = node.firstChild; c != -1; c = nodes[c].nextSibling)
                    queue.Add(c);
            }
        }
        #endregion
        //[BurstCompile]
        //public static class PolyTreeAccessorDelegates
        //{

        //    public unsafe delegate bool PolyTreeTopNodesDelegate(int nodeIndex, PolyTree* polyTree, ClipperL* clipperL, long2 pt, int* counter);

        //    public readonly unsafe static PolyTreeTopNodesDelegate ContainsPointInvoke = BurstCompiler.CompileFunctionPointer<PolyTreeTopNodesDelegate>(ContainsPointFunction).Invoke;

        //    public readonly unsafe static PolyTreeTopNodesDelegate ContainsChildrenInvoke = BurstCompiler.CompileFunctionPointer<PolyTreeTopNodesDelegate>(ContainsChildrenFunction).Invoke;
        //    [BurstCompile]
        //    public static unsafe bool ContainsPointFunction(int nodeIndex, PolyTree* polyTree, ClipperL* clipperL, long2 point, int* counter)
        //    {
        //        var nodes = (*polyTree).nodes;
        //        var node = nodes[nodeIndex];

        //        var outrecID = node.outrecIdx;
        //        ref var outrec = ref (*clipperL).OutrecList.ElementAt(outrecID);
        //        var parent = (*clipperL).GetCleanPath(outrec.pts);
        //        PolyPathContainsPoint(ref (*polyTree), nodeIndex, parent, point, ref *counter);
        //        return true;

        //        void PolyPathContainsPoint(ref PolyTree polytree, int parentNode, NativeList<long2> parentPolygon, long2 pt, ref int counter)
        //        {
        //            var nodes = polytree.nodes;
        //            if (parentNode == -1) return;
        //            var queue = new NativeList<int>(16, Allocator.Temp);
        //            for (int c = nodes[parentNode].firstChild; c != -1; c = nodes[c].nextSibling)
        //                queue.Add(c);

        //            if (InternalClipper.PointInPolygon(pt, parentPolygon) != PointInPolygonResult.IsOutside)
        //            {
        //                if (polytree.IsHole(parentNode)) --counter; else ++counter;
        //            }

        //            while (!queue.IsEmpty)
        //            {
        //                int nodeIndex = queue[0];
        //                queue.RemoveAt(0);

        //                var node = nodes[nodeIndex];
        //                var outrecID = node.outrecIdx;
        //                ref var outrec = ref (*clipperL).OutrecList.ElementAt(outrecID);

        //                var child = (*clipperL).GetCleanPath(outrec.pts);
        //                PolyPathContainsPoint(ref polytree, nodeIndex, child, pt, ref counter);
        //            }
        //            return;
        //        }
        //    }
        //    [BurstCompile]
        //    public static unsafe bool ContainsChildrenFunction(int nodeIndex, PolyTree* polyTree, ClipperL* clipperL, long2 dummy, int* dummy2)
        //    {
        //        var nodes = (*polyTree).nodes;
        //        var node = nodes[nodeIndex];
        //        if (node.childCount > 0)
        //        {
        //            ref var outrec = ref (*clipperL).OutrecList.ElementAt(node.outrecIdx);
        //            if (!ParentContainsAllChildren(nodeIndex, outrec.pts))
        //                return false;
        //        }
        //        return true;

        //        bool ParentContainsAllChildren(int parentNode, int parentOpID)
        //        {
        //            if (parentNode == -1) return false;
        //            var queue = new NativeList<int>(16, Allocator.Temp);
        //            for (int c = nodes[parentNode].firstChild; c != -1; c = nodes[c].nextSibling)
        //                queue.Add(c);

        //            while (!queue.IsEmpty)
        //            {
        //                int nodeIndex = queue[0];
        //                queue.RemoveAt(0);

        //                var node = nodes[nodeIndex];
        //                var outrecID = node.outrecIdx;
        //                ref var outrec = ref (*clipperL).OutrecList.ElementAt(outrecID);
        //                if (!(*clipperL).Path1InsidePath2(outrec.pts, parentOpID))
        //                    return false;
        //            }
        //            return true;
        //        }
        //    }

        //    public unsafe delegate bool PolyTreeDelegate(int nodeIndex, PolyTree* polyTree, ClipperL* clipperL, NativeList<int2> solutionNodes, NativeList<int> solutionStartIDs, double* result);
        //    public readonly unsafe static PolyTreeDelegate BuildPathInvoke = BurstCompiler.CompileFunctionPointer<PolyTreeDelegate>(BuildPathFunction).Invoke;
        //    [BurstCompile]
        //    public static unsafe bool BuildPathFunction(int nodeIndex, PolyTree* polyTree, ClipperL* clipperL, NativeList<int2> solutionNodes, NativeList<int> solutionStartIDs, double* dummy)
        //    {
        //        var nodes = (*polyTree).nodes;
        //        var node = nodes[nodeIndex];
        //        ref var outrec = ref (*clipperL).OutrecList.ElementAt(node.outrecIdx);
        //        (*clipperL).BuildPath(outrec.pts, false, false, ref solutionNodes, ref solutionStartIDs);
        //        return true;
        //    }

        //    public readonly unsafe static PolyTreeDelegate AreaInvoke = BurstCompiler.CompileFunctionPointer<PolyTreeDelegate>(AreaFunction).Invoke;
        //    [BurstCompile]
        //    public static unsafe bool AreaFunction(int nodeIndex, PolyTree* polyTree, ClipperL* clipperL, NativeList<int2> solutionNodes, NativeList<int> solutionStartIDs, double* result)
        //    {
        //        var nodes = (*polyTree).nodes;
        //        var node = nodes[nodeIndex];
        //        ref var outrec = ref (*clipperL).OutrecList.ElementAt(node.outrecIdx);
        //        (*clipperL).BuildPath(outrec.pts, false, false, ref solutionNodes, ref solutionStartIDs);
        //        var area = PolyTreeAccessorExtensions.SignedArea(solutionNodes, solutionStartIDs);
        //        //var area = (*clipperL).Area(outrec.pts);
        //        (*result) += area;
        //        solutionNodes.Clear();
        //        solutionStartIDs.Clear();
        //        return true;
        //    }

        //    public static bool PolyTree_GetSolution_DepthFirst(ref PolyTree polytree, ref ClipperL clipperL, ref NativeList<int2> solutionNodes, ref NativeList<int> solutionStartIDs, ref double result, PolyTreeDelegate func)
        //    {
        //        bool ret = false;
        //        var root = polytree.root;
        //        var nodes = polytree.nodes;
        //        if (root == -1) return ret;
        //        var stack = new NativeList<int>(16, Allocator.Temp)
        //        {
        //            polytree.root
        //        };
        //        while (!stack.IsEmpty)
        //        {
        //            int nodeIndex = stack[^1];
        //            stack.RemoveAt(stack.Length - 1);

        //            //process node here
        //            var node = nodes[nodeIndex];
        //            if (nodeIndex != 0) //skip root node
        //            {
        //                unsafe
        //                {
        //                    fixed (double* ptrToResult = &result)
        //                    fixed (PolyTree* ptrToPolyTree = &polytree)
        //                    fixed (ClipperL* ptrToClipperL = &clipperL)
        //                    {
        //                        ret = func.Invoke(nodeIndex, ptrToPolyTree, ptrToClipperL, solutionNodes, solutionStartIDs, ptrToResult);
        //                        if (!ret)
        //                            return ret;
        //                    }
        //                }
        //            }

        //            //push children in reverse order to stack
        //            //so that first child is processed first
        //            var first = stack.Length;
        //            for (int c = node.firstChild; c != -1; c = nodes[c].nextSibling)
        //                stack.Add(c);
        //            var last = stack.Length - 1;
        //            stack.Reverse(first, last);
        //        }
        //        return true;
        //    }
        //    public static bool PolyTree_GetSolution_BreathFirst(ref PolyTree polytree, ref ClipperL clipperL, ref NativeList<int2> solutionNodes, ref NativeList<int> solutionStartIDs, ref double result, PolyTreeDelegate func)
        //    {
        //        bool ret = false;
        //        var root = polytree.root;
        //        var nodes = polytree.nodes;
        //        if (root == -1) return ret;
        //        var queue = new NativeList<int>(16, Allocator.Temp)
        //        {
        //            root
        //        };
        //        while (!queue.IsEmpty)
        //        {
        //            int nodeIndex = queue[0];
        //            queue.RemoveAt(0);

        //            //process node here
        //            var node = nodes[nodeIndex];
        //            if (nodeIndex != 0) //skip root node
        //            {
        //                unsafe
        //                {
        //                    fixed (double* ptrToResult = &result)
        //                    fixed (PolyTree* ptrToPolyTree = &polytree)
        //                    fixed (ClipperL* ptrToClipperL = &clipperL)
        //                    {
        //                        ret = func.Invoke(nodeIndex, ptrToPolyTree, ptrToClipperL, solutionNodes, solutionStartIDs, ptrToResult);
        //                        if (!ret)
        //                            return ret;
        //                    }
        //                }
        //            }

        //            //push children into queue
        //            for (int c = node.firstChild; c != -1; c = nodes[c].nextSibling)
        //                queue.Add(c);
        //        }
        //        return true;
        //    }
        //    public static bool PolyTree_ForAllExteriorNodes(ref PolyTree polytree, ref ClipperL clipperL, long2 pt, ref int counter, PolyTreeTopNodesDelegate func)
        //    {
        //        // first get all the top level exterior outrec. Identical to Clipper2 PolyTree64.Count:
        //        // for (int i = 0; i < polytree.Count; i++)
        //        bool result = false;
        //        var root = polytree.root;
        //        var nodes = polytree.nodes;
        //        if (root == -1) return result;
        //        var stack = new NativeList<int>(2048, Allocator.Temp);
        //        var first = stack.Length;
        //        for (int c = nodes[root].firstChild; c != -1; c = nodes[c].nextSibling)
        //            stack.Add(c);
        //        var last = stack.Length - 1;
        //        stack.Reverse(first, last);

        //        while (!stack.IsEmpty)
        //        {
        //            int nodeIndex = stack[^1];
        //            stack.RemoveAt(stack.Length - 1);

        //            if (nodeIndex != 0) //skip root node
        //            {
        //                unsafe
        //                {
        //                    fixed (int* ptrToCounter = &counter)
        //                    fixed (PolyTree* ptrToPolyTree = &polytree)
        //                    fixed (ClipperL* ptrToClipperL = &clipperL)
        //                    {
        //                        result = func.Invoke(nodeIndex, ptrToPolyTree, ptrToClipperL, pt, ptrToCounter);
        //                        if (!result)
        //                            return result; //interup parsing further children when this child fails
        //                    }
        //                }
        //            }
        //        }
        //        return result;
        //    }
        //}
    }
}

