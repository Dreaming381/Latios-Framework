using System;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Calci.Clipper2
{
    internal struct PolyTree
    {
        public NativeList<TreeNode> nodes;
        public int                  root;
        public PolyTree(int outerIDsize, Allocator allocator)
        {
            nodes = new NativeList<TreeNode>(outerIDsize, allocator)
            {
                new TreeNode(true)
            };
            root = 0;
        }
        public void TraverseDepthFirst(
            Action<int, bool, bool, NativeList<int2>, NativeList<int> > buildPath,
            NativeList<int2> solutionNodes,
            NativeList<int> solutionStartIDs)
        {
            if(root == -1)
                return;
            var stack = new NativeList<int>(16, Allocator.Temp)
            {
                root
            };
            while(!stack.IsEmpty)
            {
                int nodeIndex = stack[~1];
                stack.RemoveAt(nodeIndex);

                //process node here
                var node = nodes[nodeIndex];

                //push children in reverse order to stack
                //so that first child is processed first
                var first = stack.Length;
                for (int c = node.firstChild; c != -1; c = nodes[c].nextSibling)
                    stack.Add(c);
                var last = stack.Length - 1;
                stack.Reverse(first, last);
            }
        }

        public void TraverseBreathFirst()
        {
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

                //push children into queue
                for (int c = node.firstChild; c != -1; c = nodes[c].nextSibling)
                    queue.Add(c);
            }
        }

        public int AddNode(int outrecIdx, int parentIdx)
        {
            int newNodeIdx = nodes.Length;

            nodes.Add(new TreeNode
            {
                outrecIdx   = outrecIdx,
                parent      = parentIdx,
                firstChild  = -1,
                nextSibling = -1
            });

            // Attach to parent's child list
            ref var parent = ref nodes.ElementAt(parentIdx);
            parent.childCount++;
            if (parent.firstChild == -1)
                parent.firstChild = newNodeIdx;
            else
            {
                int siblingIdx = parent.firstChild;
                while (nodes[siblingIdx].nextSibling != -1)
                    siblingIdx = nodes[siblingIdx].nextSibling;

                ref var sibling     = ref nodes.ElementAt(siblingIdx);
                sibling.nextSibling = newNodeIdx;
            }

            return newNodeIdx;
        }

        public int ChildCount(int nodeIndex)
        {
            int count = 0;
            for (int c = nodes[nodeIndex].firstChild; c != -1; c = nodes[c].nextSibling)
                count++;
            return count;
        }
        public int Count => nodes[root].childCount;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsHole(int nodeIndex)
        {
            int level = GetLevel(nodeIndex);
            return level != 0 && (level & 1) == 0;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetLevel(int nodeIndex)
        {
            int level = 0;
            while (nodes[nodeIndex].parent != -1)
            {
                level++;
                nodeIndex = nodes[nodeIndex].parent;
            }
            return level;
        }
        public void Clear()
        {
            nodes.Clear();
            nodes.Add(new TreeNode(true));
            root = 0;
        }
        public void Dispose()
        {
            if (nodes.IsCreated)
                nodes.Dispose();
        }
        public void Dispose(JobHandle jobHandle)
        {
            if (nodes.IsCreated)
                nodes.Dispose(jobHandle);
        }
    };
}

