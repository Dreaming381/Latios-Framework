#if !LATIOS_TRANSFORMS_UNITY
using AOT;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Transforms
{
    /// <summary>
    /// An IInstantiateCommand to set the root transform of the solo entity or instantiated hierarchy.
    /// This sets both the WorldTransform and TickedWorldTransform depending on which are present.
    /// </summary>
    [BurstCompile]
    public struct WorldTransformCommand : IInstantiateCommand
    {
        public WorldTransformCommand(TransformQvvs newWorldTransform)
        {
            this.newWorldTransform = newWorldTransform;
        }

        public TransformQvvs newWorldTransform;

        public FunctionPointer<IInstantiateCommand.OnPlayback> GetFunctionPointer()
        {
            return BurstCompiler.CompileFunctionPointer<IInstantiateCommand.OnPlayback>(OnPlayback);
        }

        [MonoPInvokeCallback(typeof(IInstantiateCommand.OnPlayback))]
        [BurstCompile]
        static void OnPlayback(ref IInstantiateCommand.Context context)
        {
            var entities = context.entities;
            var em       = context.entityManager;
            for (int i = 0; i < entities.Length; i++)
            {
                var entity  = entities[i];
                var command = context.ReadCommand<WorldTransformCommand>(i);
                if (em.HasComponent<WorldTransform>(entity))
                    TransformTools.SetWorldTransform(entity, command.newWorldTransform, em);
                if (em.HasComponent<TickedWorldTransform>(entity))
                    TransformTools.SetTickedWorldTransform(entity, command.newWorldTransform, em);
            }
        }
    }

    /// <summary>
    /// An IInstantiateCommand to set the parent of the instantiated entity.
    /// This treats any existing WorldTransform or TickedWorldTransform baked into the prefab as
    /// the local transform.
    /// If the target parent entity no longer exists during playback, the instantiated entity will
    /// be immediately destroyed again.
    /// </summary>
    [BurstCompile]
    public struct ParentCommand : IInstantiateCommand
    {
        public ParentCommand(Entity parent,
                             InheritanceFlags inheritanceFlags = InheritanceFlags.Normal,
                             SetParentOptions setParentOptions = SetParentOptions.AttachLinkedEntityGroup)
        {
            this.parent           = parent;
            this.inheritanceFlags = inheritanceFlags;
            this.options          = setParentOptions;
        }

        public Entity           parent;
        public InheritanceFlags inheritanceFlags;
        public SetParentOptions options;

        public FunctionPointer<IInstantiateCommand.OnPlayback> GetFunctionPointer()
        {
            return BurstCompiler.CompileFunctionPointer<IInstantiateCommand.OnPlayback>(OnPlayback);
        }

        [MonoPInvokeCallback(typeof(IInstantiateCommand.OnPlayback))]
        [BurstCompile]
        static void OnPlayback(ref IInstantiateCommand.Context context)
        {
            OnPlaybackBatched(ref context);
            //OnPlaybackSingle(ref context);
        }

        static void OnPlaybackSingle(ref IInstantiateCommand.Context context)
        {
            var entities = context.entities;
            var em       = context.entityManager;
            for (int i = 0; i < entities.Length; i++)
            {
                var entity  = entities[i];
                var command = context.ReadCommand<ParentCommand>(i);
                if (!em.IsAlive(command.parent))
                {
                    context.RequestDestroyEntity(entity);
                    continue;
                }
                bool hadNormal            = em.HasComponent<WorldTransform>(entity);
                bool hadTicked            = em.HasComponent<TickedWorldTransform>(entity);
                var  localTransform       = hadNormal ? em.GetComponentData<WorldTransform>(entity).worldTransform : TransformQvvs.identity;
                var  tickedLocalTransform = hadTicked ? em.GetComponentData<TickedWorldTransform>(entity).worldTransform : localTransform;
                if (hadTicked && !hadNormal)
                    localTransform = tickedLocalTransform;
                em.SetParent(entity, command.parent, command.inheritanceFlags, command.options);
                if (em.HasComponent<WorldTransform>(entity))
                    TransformTools.SetLocalTransform(entity, in localTransform, em);
                if (em.HasComponent<TickedWorldTransform>(entity))
                    TransformTools.SetTickedLocalTransform(entity, in tickedLocalTransform, em);
            }
        }

        static void OnPlaybackBatched(ref IInstantiateCommand.Context context)
        {
            TreeChangeInstantiate.AddChildren(ref context, false);
        }
    }

    /// <summary>
    /// An IInstantiateCommand to set the parent of the instantiated entity and set a new local transform
    /// for the instantiated entity. This sets both the WorldTransform and TickedWorldTransform depending
    /// on which are present.
    /// If the target parent entity no longer exists during playback, the instantiated entity will
    /// be immediately destroyed again.
    /// </summary>
    [BurstCompile]
    public struct ParentAndLocalTransformCommand : IInstantiateCommand
    {
        public ParentAndLocalTransformCommand(Entity parent,
                                              TransformQvvs newLocalTransform,
                                              InheritanceFlags inheritanceFlags = InheritanceFlags.Normal,
                                              SetParentOptions setParentOptions = SetParentOptions.AttachLinkedEntityGroup)
        {
            this.parent            = parent;
            this.inheritanceFlags  = inheritanceFlags;
            this.newLocalTransform = newLocalTransform;
            this.options           = setParentOptions;
        }

        public Entity           parent;
        public TransformQvvs    newLocalTransform;
        public InheritanceFlags inheritanceFlags;
        public SetParentOptions options;

        public FunctionPointer<IInstantiateCommand.OnPlayback> GetFunctionPointer()
        {
            return BurstCompiler.CompileFunctionPointer<IInstantiateCommand.OnPlayback>(OnPlayback);
        }

        [MonoPInvokeCallback(typeof(IInstantiateCommand.OnPlayback))]
        [BurstCompile]
        static void OnPlayback(ref IInstantiateCommand.Context context)
        {
            TreeChangeInstantiate.AddChildren(ref context, true);
        }
    }
}
#endif

