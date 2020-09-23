using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace Latios
{
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public abstract class SubSystemBase : SystemBase, ILatiosSystem
    {
        public LatiosWorld latiosWorld { get; private set; }

        public ManagedEntity sceneGlobalEntity => latiosWorld.sceneGlobalEntity;
        public ManagedEntity worldGlobalEntity => latiosWorld.worldGlobalEntity;

        public FluentQuery Fluent => this.Fluent();

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

        protected sealed override void OnUpdate()
        {
            latiosWorld.BeginCollectionTracking(this);
            OnUpdateInternal();
            latiosWorld.EndCollectionTracking(Dependency);
        }

        protected sealed override void OnDestroy()
        {
            OnDestroyInternal();
        }

        internal abstract void OnCreateInternal();

        internal abstract void OnDestroyInternal();

        internal abstract void OnUpdateInternal();

        public EntityQuery GetEntityQuery(EntityQueryDesc desc) => GetEntityQuery(new EntityQueryDesc[] { desc });

        internal JobHandle SystemBaseDependency
        {
            get => Dependency;
            set => Dependency = value;
        }
    }

    public abstract class SubSystem : SubSystemBase
    {
        protected new virtual void OnCreate()
        {
        }

        protected new virtual void OnDestroy()
        {
        }

        protected new abstract void OnUpdate();

        internal override void OnCreateInternal()
        {
            OnCreate();
        }

        internal override void OnDestroyInternal()
        {
            OnDestroy();
        }

        internal override void OnUpdateInternal()
        {
            OnUpdate();
        }
    }
}

