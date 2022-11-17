using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace Latios
{
    /// <summary>
    /// This is an internal base class for SubSystem
    /// </summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public abstract partial class SubSystemBase : SystemBase, ILatiosSystem
    {
        /// <summary>
        /// The latiosWorld this system belongs to, not valid in editor worlds
        /// </summary>
        public LatiosWorld latiosWorld { get; private set; }

        /// <summary>
        /// The unmanaged aspects of a latiosWorld this system belongs to, which is also valid in editor worlds
        /// </summary>
        public LatiosWorldUnmanaged latiosWorldUnmanaged { get; private set; }

        /// <summary>
        /// The scene blackboard entity for the LatiosWorld of this system
        /// </summary>
        public BlackboardEntity sceneBlackboardEntity => latiosWorld.sceneBlackboardEntity;
        /// <summary>
        /// The world blackboard entity for the LatiosWorld of this system
        /// </summary>
        public BlackboardEntity worldBlackboardEntity => latiosWorld.worldBlackboardEntity;

        /// <summary>
        /// Begins a Fluent query chain
        /// </summary>
        public FluentQuery Fluent => this.Fluent();

        /// <summary>
        /// Override this method to perform additional filtering to decide if this system should run.
        /// </summary>
        /// <returns>true if this system should run</returns>
        public virtual bool ShouldUpdateSystem()
        {
            return Enabled;
        }

        protected sealed override void OnCreate()
        {
            if (World is LatiosWorld lWorld)
            {
                latiosWorld          = lWorld;
                latiosWorldUnmanaged = latiosWorld.latiosWorldUnmanaged;
            }
            else
            {
                latiosWorldUnmanaged = World.Unmanaged.GetLatiosWorldUnmanaged();
            }
            OnCreateInternal();
        }

        protected sealed override void OnUpdate()
        {
            OnUpdateInternal();
        }

        protected sealed override void OnDestroy()
        {
            OnDestroyInternal();
        }

        internal abstract void OnCreateInternal();

        internal abstract void OnDestroyInternal();

        internal abstract void OnUpdateInternal();

        public EntityQuery GetEntityQuery(EntityQueryDesc desc) => GetEntityQuery(new EntityQueryDesc[] { desc });
        public EntityQuery GetEntityQuery(EntityQueryBuilder desc) => GetEntityQuery(desc);

        public abstract void OnNewScene();

        internal JobHandle SystemBaseDependency
        {
            get => Dependency;
            set => Dependency = value;
        }
    }

    /// <summary>
    /// A base class system which subclasses SystemBase and provides Latios Framework Core features
    /// </summary>
    public abstract partial class SubSystem : SubSystemBase
    {
        protected new virtual void OnCreate()
        {
        }

        protected new virtual void OnDestroy()
        {
        }

        protected new abstract void OnUpdate();

        /// <summary>
        /// Override to get alerted whenever a new sceneBlackboardEntity is created
        /// </summary>
        public override void OnNewScene()
        {
        }

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

