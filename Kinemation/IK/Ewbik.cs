using System;
using System.Collections.Generic;
using Latios.Transforms;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

// This is a custom implementation of the EWBIK algorithm. The original algorithm can be found
// licensed under the MIT license at the following location:
// https://github.com/EGjoni/Everything-Will-Be-IK
// The license is as follows:
//
// The MIT License (MIT)
//
// Copyright(c) 2016 Eron Gjoni
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

namespace Latios.Kinemation
{
    /// <summary>
    /// A solver utility for Everything-Will-Be-IK (EWBIK).
    /// EWBIK uses QCP to solve for both position and orientation targets with support for multiple weighted targets.
    /// While flexible, the algorithm can be a bit more expensive compared to CCD or FABRIK.
    /// </summary>
    public static class Ewbik
    {
        /// <summary>
        /// An IK target configuration
        /// </summary>
        public struct Target
        {
            /// <summary>
            /// The root-space rotation of the target
            /// </summary>
            public quaternion rootRelativeRotation;
            /// <summary>
            /// The root-space position of the target
            /// </summary>
            public float3 rootRelativePosition;
            /// <summary>
            /// An offset on the target bone in that bone's local space which should be used to evaluate arrival
            /// of the bone to the target.
            /// </summary>
            public float3 boneLocalPositionOffsetToMatchTargetPosition;
            /// <summary>
            /// The index of the bone in the skeleton that this target applies to
            /// </summary>
            public short boneIndex;
            /// <summary>
            /// A user value that can be used to identify targets in a user-created constraint solver
            /// </summary>
            public ushort targetUserId;
            /// <summary>
            /// Determines how much EWBIK should prioritize this target's rotation compared to all other targets
            /// </summary>
            public float rotationWeight;
            /// <summary>
            /// Determines how much EWBIK should prioritize this target's position compared to all other targets
            /// </summary>
            public float positionWeight;
        }

        /// <summary>
        /// The interface used by the EWBIK algorithm that is responsible for the modification of bone transforms.
        /// This makes the EWBIK algorithm modular, flexible, and customizable. However, the burden of stability
        /// and convergence also falls on the implementation of this interface.
        /// </summary>
        public interface IConstraintSolver
        {
            /// <summary>
            /// Called for each bone at the start of the algorithm to determine whether EWBIK should process it.
            /// Bones that are fixe to their parent will skip being solved in a chain. It is still valid for such
            /// a bone to have a target, and the target will still be considered by the bone's ancestors.
            /// </summary>
            /// <param name="bone">The bone to consider</param>
            /// <returns>True if the bone is fixed to the parent and does not require direct solving for; false otherwise</returns>
            public bool IsFixedToParent(OptimizedBone bone);
            /// <summary>
            /// Called prior to performing each iteration of the hierarchy. Each iteration attempts to solve each non-fixed bone once.
            /// </summary>
            /// <param name="skeleton">The full skeleton hierarchy, which can be used to evaluate solution convergence or set up soft constraints</param>
            /// <param name="sortedTargets">The targets sorted by bone index, which can be used to evaluate solution convergence</param>
            /// <param name="iterationsPerformedSoFar">The number of full hierarchy iterations the algorithm has completed, which could be compared to a max limit</param>
            /// <returns>True if the algorithm should perform another full hierarchy iteration; false otherwise</returns>
            public bool NeedsSkeletonIteration(OptimizedSkeletonAspect skeleton, ReadOnlySpan<Target> sortedTargets, int iterationsPerformedSoFar);
            /// <summary>
            /// Called once prior to each bone evaluation. The result is used to tell QCP whether it is allowed to hypothetically move the bone
            /// before attempting to derive the optimal orientation. This can lead to very different proposed results. You should only ever consider
            /// returning true for this value if the bone is allowed to move or stretch.
            /// </summary>
            /// <param name="bone">The bone to consider that is about to be solved for</param>
            /// <param name="iterationsSoFarForThisBone">The number of iterations that have been applied to this bone so far within the current
            /// skeleton hierarchy iteration</param>
            /// <returns>True, if QCP should be allowed to move the bone and propose a translation delta; false if QCP should only focus on rotation</returns>
            public bool UseTranslationInSolve(OptimizedBone bone, int iterationsSoFarForThisBone);
            // The constraint solver is responsible for applying the proposed new orientation and translation (translation could be applied as stretch instead)
            // as well as any damping. Return true to request another immediate solve iteration on this same bone.
            /// <summary>
            /// Called once for each bone evaluation. This method is responsible for applying a constrained transform delta to the bone. This method can
            /// return true to have QCP immediately re-evaluate the bone with the updated bone transform. That's especially useful for performing a mix of
            /// rotation+translation and rotation-only solves to help guide a moveable or stretchable bone toward a valid solution.
            /// </summary>
            /// <param name="bone">The bone being evaluated</param>
            /// <param name="proposedTransformDelta">The proposed transform delta relative to the bone's existing root-space transform that QCP computed</param>
            /// <param name="boneSolveState">Additional info about the IK state, including the iterations performed, as well as a means to evaluate the
            /// efficacy of constrained transforms relative to the the targets, to ensure forward progress</param>
            /// <returns>True if the bone should be re-evaluated immediately with its new transform; false if the algorithm should move on to the next bone</returns>
            public bool ApplyConstraintsToBone(OptimizedBone bone, in RigidTransform proposedTransformDelta, in BoneSolveState boneSolveState);
        }

        /// <summary>
        /// Useful info given to an IConstraintSolver to help it apply constraints to bones in a stable manner.
        /// </summary>
        public ref struct BoneSolveState
        {
            internal Span<float3> currentPoints;
            internal Span<float3> targetPoints;
            internal Span<float>  weights;
            internal int          boneIterations;
            internal int          skeletonIterations;

            /// <summary>
            /// The number of evaluations that have been performed so far for this bone within the current skeleton iteration.
            /// This includes the current call to ApplyConstraintsToBone(), so the value is always 1 or greater.
            /// </summary>
            public int iterationsSoFarForThisBone => boneIterations;
            /// <summary>
            /// The number of complete skeleton hierarchy iterations performed so far. This is 0 on the first pass.
            /// </summary>
            public int iterationsCompletedForSkeleton => skeletonIterations;

            /// <summary>
            /// Evaluates the mean square distance of the point pairs used by QCP, after applying the transform to the first set of points.
            /// You can call this once with an identity transform to get the initial state, and again with your constrained transform delta.
            /// If the latter produces a smaller value than the former, then forward progress has been made. Otherwise, it is recommended to
            /// discard the transform delta and move on.
            /// </summary>
            /// <param name="deltaTransformToApply">A root-space transform delta that would be applied to the bone's initial orientation at the
            /// start of the evaluation</param>
            /// <returns>A value which determines how much the new transform is unaligned with the targets. Smaller values means more alignment
            /// and closer to a converged solution.</returns>
            public float MeanSquareDistanceFrom(TransformQvvs deltaTransformToApply)
            {
                float msd         = 0f;
                float totalWeight = 0f;
                for (int i = 0; i < currentPoints.Length; i++)
                {
                    msd         += weights[i] * math.distancesq(qvvs.TransformPoint(deltaTransformToApply, currentPoints[i]), targetPoints[i]);
                    totalWeight += weights[i];
                }
                return msd / totalWeight;
            }
        }

        /// <summary>
        /// Performs the EWBIK algorithm on the optimized skeleton, given the specified targets, and using the specified constraint solver.
        /// </summary>
        /// <typeparam name="T">An IConstraintSolver which is used to evaluate and apply QCP transform deltas, as well as control the number of iterations</typeparam>
        /// <param name="skeleton">The optimized skeleton to perform EWBIK on</param>
        /// <param name="targets">The array of IK targets and their weights. The algorithm will sort these in-place by bone index.</param>
        /// <param name="constraintSolver">The solver used to apply constraints and update the bone transforms</param>
        public static unsafe void Solve<T>(ref OptimizedSkeletonAspect skeleton, ref Span<Target> targets, ref T constraintSolver) where T : unmanaged, IConstraintSolver
        {
            // Our setup is a bit complicated. We need to figure out the bone solve order, which is simply iterating the bones backwards,
            // except we skip bones that don't have targets on themselves or descendants or are fixed to their parent.
            // In addition, we build a list of indices into our sorted targets (we sort them by bone for performance) per bone, such that
            // each bone knows all targets that influence both itself and its descendants.
            fixed (Target* p = &targets[0])
            {
                NativeSortExtension.Sort(p, targets.Length, new TargetSorter());
            }
            Span<SolveBoneItem> solveList            = stackalloc SolveBoneItem[skeleton.boneCount];
            Span<short>         indexInSolveList     = stackalloc short[skeleton.boneCount];
            int                 expandedTargetsCount = 0;
            {
                Span<short> targetCountsByBone = stackalloc short[skeleton.boneCount];
                targetCountsByBone.Clear();
                int currentTargetIndex = targets.Length - 1;
                for (int i = targetCountsByBone.Length - 1; i >= 0; i--)
                {
                    short count = 0;
                    while (currentTargetIndex >= 0 && targets[currentTargetIndex].boneIndex > i)
                        currentTargetIndex--;
                    for (; currentTargetIndex >= 0 && targets[currentTargetIndex].boneIndex == i; currentTargetIndex--)
                    {
                        count++;
                    }
                    targetCountsByBone[i] += count;
                    var optimizedBone      = skeleton.bones[i];
                    if (optimizedBone.index > 0)
                        targetCountsByBone[optimizedBone.parentIndex] += targetCountsByBone[i];
                    if (constraintSolver.IsFixedToParent(optimizedBone))
                        targetCountsByBone[i] = 0;
                }

                short solveListLength = 0;
                int   runningStart    = 0;

                for (short i = (short)(indexInSolveList.Length - 1); i >= 0; i--)
                {
                    if (targetCountsByBone[i] == 0)
                        indexInSolveList[i] = -1;
                    else
                    {
                        indexInSolveList[i]        = solveListLength;
                        solveList[solveListLength] = new SolveBoneItem
                        {
                            targetsByBoneStart = runningStart,
                            targetsByBoneCount = 0,
                            boneIndex          = i
                        };
                        runningStart += targetCountsByBone[i];
                        solveListLength++;
                    }
                }
                solveList            = solveList.Slice(0, solveListLength);
                expandedTargetsCount = runningStart;
            }
            Span<short> targetIndicesByBone = stackalloc short[expandedTargetsCount];
            {
                int   currentIndexInSolveList = 0;
                short currentTargetIndex      = (short)(targets.Length - 1);
                for (int boneIndex = solveList[0].boneIndex; boneIndex >= 0; boneIndex--)
                {
                    while (currentIndexInSolveList < solveList.Length && solveList[currentIndexInSolveList].boneIndex > boneIndex)
                        currentIndexInSolveList++;

                    while (currentTargetIndex >= 0 && targets[currentTargetIndex].boneIndex > boneIndex)
                        currentTargetIndex--;

                    if (currentIndexInSolveList < solveList.Length && solveList[currentIndexInSolveList].boneIndex == boneIndex)
                    {
                        ref var solveItem = ref solveList[currentIndexInSolveList];
                        for (; currentTargetIndex >= 0 && targets[currentTargetIndex].boneIndex == boneIndex; currentTargetIndex--)
                        {
                            targetIndicesByBone[solveItem.targetsByBoneStart + solveItem.targetsByBoneCount] = currentTargetIndex;
                            solveItem.targetsByBoneCount++;
                        }

                        var parentIndex = skeleton.bones[boneIndex].parentIndex;
                        while (parentIndex > 0 && indexInSolveList[parentIndex] == -1)
                            parentIndex = skeleton.bones[parentIndex].parentIndex;
                        if (parentIndex > 0)
                        {
                            ref var parentItem = ref solveList[indexInSolveList[parentIndex]];
                            for (int i = 0; i < solveItem.targetsByBoneCount; i++)
                            {
                                targetIndicesByBone[parentItem.targetsByBoneStart + parentItem.targetsByBoneCount] = targetIndicesByBone[solveItem.targetsByBoneStart + i];
                                parentItem.targetsByBoneCount++;
                            }
                        }
                    }
                    else
                    {
                        // A target could influence a bone that is fixed to its parent.
                        var parentIndex = skeleton.bones[boneIndex].parentIndex;
                        while (parentIndex > 0 && indexInSolveList[parentIndex] == -1)
                            parentIndex = skeleton.bones[parentIndex].parentIndex;
                        if (parentIndex > 0)
                        {
                            ref var parentItem = ref solveList[indexInSolveList[parentIndex]];

                            for (; currentTargetIndex >= 0 && targets[currentTargetIndex].boneIndex == boneIndex; currentTargetIndex--)
                            {
                                targetIndicesByBone[parentItem.targetsByBoneStart + parentItem.targetsByBoneCount] = currentTargetIndex;
                                parentItem.targetsByBoneCount++;
                            }
                        }
                    }
                }
            }

            // Next step, we can start solving.
            int skeletonIterations = 0;
            while (constraintSolver.NeedsSkeletonIteration(skeleton, targets, skeletonIterations))
            {
                for (int solveItemIndex = 0; solveItemIndex < solveList.Length; solveItemIndex++)
                {
                    var          solveItem             = solveList[solveItemIndex];
                    var          bone                  = skeleton.bones[solveItem.boneIndex];
                    var          boneTargetIndices     = targetIndicesByBone.Slice(solveItem.targetsByBoneStart, solveItem.targetsByBoneCount);
                    var          conservativePairCount = 7 * solveItem.targetsByBoneCount;
                    Span<float3> currentPoints         = stackalloc float3[conservativePairCount];
                    Span<float3> targetPoints          = stackalloc float3[conservativePairCount];
                    Span<float>  weights               = stackalloc float[conservativePairCount];
                    int          boneSolveIterations   = 0;
                    var          boneTransform         = bone.rootTransform;
                    bool         repeat                = false;

                    do
                    {
                        int pairCount = 0;
                        for (int i = 0; i < boneTargetIndices.Length; i++)
                        {
                            ref var target                  = ref targets[boneTargetIndices[i]];
                            var     targetedBoneTransform   = skeleton.bones[target.boneIndex].rootTransform;
                            targetedBoneTransform.position -= boneTransform.position;
                            var targetPosition              = target.rootRelativePosition - boneTransform.position;
                            var boneOffsetPosition          = qvvs.TransformPoint(targetedBoneTransform, target.boneLocalPositionOffsetToMatchTargetPosition);
                            if (target.positionWeight > 0f)
                            {
                                currentPoints[pairCount] = boneOffsetPosition;
                                targetPoints[pairCount]  = targetPosition;
                                weights[pairCount]       = target.positionWeight;
                                pairCount++;
                            }
                            if (target.rotationWeight > 0f)
                            {
                                var matrixCurrent = new float3x3(targetedBoneTransform.rotation);
                                var matrixTarget  = new float3x3(target.rootRelativeRotation);

                                currentPoints[pairCount] = boneOffsetPosition + matrixCurrent.c0;
                                targetPoints[pairCount]  = targetPosition + matrixTarget.c0;
                                weights[pairCount]       = target.rotationWeight;
                                pairCount++;
                                currentPoints[pairCount] = boneOffsetPosition - matrixCurrent.c0;
                                targetPoints[pairCount]  = targetPosition - matrixTarget.c0;
                                weights[pairCount]       = target.rotationWeight;
                                pairCount++;

                                currentPoints[pairCount] = boneOffsetPosition + matrixCurrent.c1;
                                targetPoints[pairCount]  = targetPosition + matrixTarget.c1;
                                weights[pairCount]       = target.rotationWeight;
                                pairCount++;
                                currentPoints[pairCount] = boneOffsetPosition - matrixCurrent.c1;
                                targetPoints[pairCount]  = targetPosition - matrixTarget.c1;
                                weights[pairCount]       = target.rotationWeight;
                                pairCount++;

                                currentPoints[pairCount] = boneOffsetPosition + matrixCurrent.c2;
                                targetPoints[pairCount]  = targetPosition + matrixTarget.c2;
                                weights[pairCount]       = target.rotationWeight;
                                pairCount++;
                                currentPoints[pairCount] = boneOffsetPosition - matrixCurrent.c2;
                                targetPoints[pairCount]  = targetPosition - matrixTarget.c2;
                                weights[pairCount]       = target.rotationWeight;
                                pairCount++;
                            }
                        }

                        var useTranslation = constraintSolver.UseTranslationInSolve(bone, boneSolveIterations);
                        boneSolveIterations++;
                        var boneSolveState = new BoneSolveState
                        {
                            currentPoints      = currentPoints.Slice(0, pairCount),
                            targetPoints       = targetPoints.Slice(0, pairCount),
                            weights            = weights.Slice(0, pairCount),
                            boneIterations     = boneSolveIterations,
                            skeletonIterations = skeletonIterations,
                        };
                        var proposedDelta = Qcp.Solve(boneSolveState.currentPoints, boneSolveState.targetPoints, boneSolveState.weights, useTranslation);
                        repeat            = constraintSolver.ApplyConstraintsToBone(bone, in proposedDelta, in boneSolveState);
                    }
                    while (repeat);
                }
                skeletonIterations++;
            }
        }

        struct SolveBoneItem
        {
            public int   targetsByBoneStart;
            public short targetsByBoneCount;
            public short boneIndex;
        }

        struct TargetSorter : IComparer<Target>
        {
            public int Compare(Target a, Target b)
            {
                return a.boneIndex.CompareTo(b.boneIndex);
            }
        }
    }
}

