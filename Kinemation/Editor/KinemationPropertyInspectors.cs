using System.Collections;
using System.Collections.Generic;
using Latios.Editor;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Entities.UI;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;

namespace Latios.Kinemation.Editor
{
    class MeshBindingPathsBlobPropertyInspector : PropertyInspector<BlobAssetReference<MeshBindingPathsBlob> >
    {
        public override VisualElement Build()
        {
            var root = new VisualElement();
            if (Target.IsCreated)
            {
                root.Add(PropertyInspectorUtilities.MakeReadOnlyElement<LongField, long>("Hash", math.aslong(Target.GetHash64())));

                var listOfNames = new Foldout { text = "Paths In Reversed Notation" };
                //listOfNames.value = false;
                root.Add(listOfNames);

                WrappedPath wrapped = new WrappedPath { blob = Target };
                for (int i = 0; i < Target.Value.pathsInReversedNotation.Length; i++)
                {
                    wrapped.index = i;
                    listOfNames.Add(PropertyInspectorUtilities.MakeReadOnlyElement<TextField, string>($"Path {i}", wrapped.ConvertToString()));
                }
            }

            return root;
        }

        struct WrappedPath : INativeList<byte>, IUTF8Bytes
        {
            public BlobAssetReference<MeshBindingPathsBlob> blob;
            public int                                      index;

            public byte this[int i] { get => blob.Value.pathsInReversedNotation[index][i]; set => throw new System.NotImplementedException(); }

            public int Capacity { get => blob.Value.pathsInReversedNotation[index].Length; set => throw new System.NotImplementedException(); }

            public bool IsEmpty => blob.Value.pathsInReversedNotation[index].Length == 0;

            public int Length { get => blob.Value.pathsInReversedNotation[index].Length; set => throw new System.NotImplementedException(); }

            public void Clear()
            {
                throw new System.NotImplementedException();
            }

            public ref byte ElementAt(int i)
            {
                return ref blob.Value.pathsInReversedNotation[index][i];
            }

            public unsafe byte* GetUnsafePtr()
            {
                return (byte*)blob.Value.pathsInReversedNotation[index].GetUnsafePtr();
            }

            public bool TryResize(int newLength, NativeArrayOptions clearOptions = NativeArrayOptions.ClearMemory)
            {
                throw new System.NotImplementedException();
            }
        }
    }

    class SkeletonBindingPathsBlobPropertyInspector : PropertyInspector<BlobAssetReference<SkeletonBindingPathsBlob> >
    {
        public override VisualElement Build()
        {
            var root = new VisualElement();
            if (Target.IsCreated)
            {
                root.Add(PropertyInspectorUtilities.MakeReadOnlyElement<LongField, long>("Hash", math.aslong(Target.GetHash64())));

                var listOfNames = new Foldout { text = "Paths In Reversed Notation" };
                //listOfNames.value = false;
                root.Add(listOfNames);

                WrappedPath wrapped = new WrappedPath { blob = Target };
                for (int i = 0; i < Target.Value.pathsInReversedNotation.Length; i++)
                {
                    wrapped.index = i;
                    listOfNames.Add(PropertyInspectorUtilities.MakeReadOnlyElement<TextField, string>($"Path {i}", wrapped.ConvertToString()));
                }
            }

            return root;
        }

        struct WrappedPath : INativeList<byte>, IUTF8Bytes
        {
            public BlobAssetReference<SkeletonBindingPathsBlob> blob;
            public int                                          index;

            public byte this[int i] { get => blob.Value.pathsInReversedNotation[index][i]; set => throw new System.NotImplementedException(); }

            public int Capacity { get => blob.Value.pathsInReversedNotation[index].Length; set => throw new System.NotImplementedException(); }

            public bool IsEmpty => blob.Value.pathsInReversedNotation[index].Length == 0;

            public int Length { get => blob.Value.pathsInReversedNotation[index].Length; set => throw new System.NotImplementedException(); }

            public void Clear()
            {
                throw new System.NotImplementedException();
            }

            public ref byte ElementAt(int i)
            {
                return ref blob.Value.pathsInReversedNotation[index][i];
            }

            public unsafe byte* GetUnsafePtr()
            {
                return (byte*)blob.Value.pathsInReversedNotation[index].GetUnsafePtr();
            }

            public bool TryResize(int newLength, NativeArrayOptions clearOptions = NativeArrayOptions.ClearMemory)
            {
                throw new System.NotImplementedException();
            }
        }
    }

    class OptimizedSkeletonHiearchyBlobPropertyInspector : PropertyInspector<BlobAssetReference<OptimizedSkeletonHierarchyBlob> >
    {
        public override VisualElement Build()
        {
            var root = new VisualElement();
            if (Target.IsCreated)
            {
                root.Add(PropertyInspectorUtilities.MakeReadOnlyElement<LongField, long>("Hash", math.aslong(Target.GetHash64())));

                var listOfNames = new Foldout { text = "Parent Indices" };
                //listOfNames.value = false;
                root.Add(listOfNames);

                for (int i = 0; i < Target.Value.parentIndices.Length; i++)
                {
                    listOfNames.Add(PropertyInspectorUtilities.MakeReadOnlyElement<IntegerField, int>($"Parent Index of {i}", Target.Value.parentIndices[i]));
                }
            }

            return root;
        }
    }
}

