using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace Latios
{
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public abstract class JobSubSystemBase : JobComponentSystem, ILatiosSystem
    {
        public LatiosWorld latiosWorld { get; private set; }

        public ManagedEntity sceneGlobalEntity => latiosWorld.sceneGlobalEntity;
        public ManagedEntity worldGlobalEntity => latiosWorld.worldGlobalEntity;

        public virtual bool ShouldUpdateSystem()
        {
            return Enabled;
        }

        protected sealed override void OnCreate()
        {
            if (World is LatiosWorld lWorld)
            {
                latiosWorld = lWorld;
            }
            else
            {
                throw new InvalidOperationException("The current world is not of type LatiosWorld required for Latios framework functionality.");
            }
            OnCreateInternal();
        }

        protected sealed override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var outputDeps = OnUpdateInternal(inputDeps);
            latiosWorld.SetJobHandleForCollections(outputDeps);
            return outputDeps;
        }

        protected sealed override void OnDestroy()
        {
            OnDestroyInternal();
        }

        internal abstract void OnCreateInternal();

        internal abstract void OnDestroyInternal();

        internal abstract JobHandle OnUpdateInternal(JobHandle inputDeps);
    }

    public abstract class JobSubSystem : JobSubSystemBase
    {
        protected new virtual void OnCreate()
        {
        }

        protected new virtual void OnDestroy()
        {
        }

        protected new abstract JobHandle OnUpdate(JobHandle inputDeps);

        internal override void OnCreateInternal()
        {
            OnCreate();
        }

        internal override void OnDestroyInternal()
        {
            OnDestroy();
        }

        internal override JobHandle OnUpdateInternal(JobHandle inputDeps)
        {
            return OnUpdate(inputDeps);
        }
    }
}

