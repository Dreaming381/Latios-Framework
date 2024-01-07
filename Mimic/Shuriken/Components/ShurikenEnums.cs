#if false
namespace Latios.Mimic.Shuriken
{
    // /// <summary>
    // ///   <para>Options for which physics system to use the gravity setting from.</para>
    // /// </summary>
    // public enum ParticleSystemGravitySource : byte
    // {
    //     /// <summary>
    //     ///   <para>Use gravity from the 3D physics system.</para>
    //     /// </summary>
    //     Physics3D,
    //     /// <summary>
    //     ///   <para>Use gravity from the 2D physics system.</para>
    //     /// </summary>
    //     Physics2D,
    // }
    // /// <summary>
    // ///   <para>The space to simulate particles in.</para>
    // /// </summary>
    // public enum ParticleSystemSimulationSpace : byte
    // {
    //     /// <summary>
    //     ///   <para>Simulate particles in local space.</para>
    //     /// </summary>
    //     Local,
    //     /// <summary>
    //     ///   <para>Simulate particles in world space.</para>
    //     /// </summary>
    //     World,
    //     /// <summary>
    //     ///   <para>Simulate particles relative to a custom transform component, defined by ParticleSystem.MainModule.customSimulationSpace.</para>
    //     /// </summary>
    //     Custom,
    // }
    //
    // /// <summary>
    // ///   <para>Control how particle systems apply transform scale.</para>
    // /// </summary>
    // public enum ParticleSystemScalingMode : byte
    // {
    //     /// <summary>
    //     ///   <para>Scale the Particle System using the entire transform hierarchy.</para>
    //     /// </summary>
    //     Hierarchy,
    //     /// <summary>
    //     ///   <para>Scale the Particle System using only its own transform scale. (Ignores parent scale).</para>
    //     /// </summary>
    //     Local,
    //     /// <summary>
    //     ///   <para>Only apply transform scale to the shape component, which controls where
    //     ///   particles are spawned, but does not affect their size or movement.
    //     ///   </para>
    //     /// </summary>
    //     Shape,
    // }
    //
    // /// <summary>
    // ///   <para>Control how a Particle System calculates its velocity.</para>
    // /// </summary>
    // public enum ParticleSystemEmitterVelocityMode : byte
    // {
    //     /// <summary>
    //     ///   <para>Calculate the Particle System velocity by using the Transform component.</para>
    //     /// </summary>
    //     Transform,
    //     /// <summary>
    //     ///   <para>Calculate the Particle System velocity by using a Rigidbody or Rigidbody2D component, if one exists on the GameObject.</para>
    //     /// </summary>
    //     Rigidbody,
    //     /// <summary>
    //     ///   <para>When the Particle System calculates its velocity, it instead uses the custom stableSeed set in ParticleSystem.MainModule.emitterVelocity.</para>
    //     /// </summary>
    //     Custom,
    // }
    //
    // /// <summary>
    // ///   <para>The action to perform when the Particle System stops.</para>
    // /// </summary>
    // public enum ParticleSystemStopAction : byte
    // {
    //     /// <summary>
    //     ///   <para>Do nothing.</para>
    //     /// </summary>
    //     None,
    //     /// <summary>
    //     ///   <para>Disable the GameObject containing the Particle System.</para>
    //     /// </summary>
    //     Disable,
    //     /// <summary>
    //     ///   <para>Destroy the GameObject containing the Particle System.</para>
    //     /// </summary>
    //     Destroy,
    //     /// <summary>
    //     ///   <para>Call MonoBehaviour.OnParticleSystemStopped on all scripts attached to the same GameObject.</para>
    //     /// </summary>
    //     Callback,
    // }
    //
    // /// <summary>
    // ///   <para>The action to perform when the Particle System is offscreen.</para>
    // /// </summary>
    // public enum ParticleSystemCullingMode : byte
    // {
    //     /// <summary>
    //     ///   <para>For looping effects, the simulation is paused when offscreen, and for one-shot effects, the simulation will continue playing.</para>
    //     /// </summary>
    //     Automatic,
    //     /// <summary>
    //     ///   <para>Pause the Particle System simulation when it is offscreen, and perform an extra simulation when the system comes back onscreen, creating the impression that it was never paused.</para>
    //     /// </summary>
    //     PauseAndCatchup,
    //     /// <summary>
    //     ///   <para>Pause the Particle System simulation when it is offscreen.</para>
    //     /// </summary>
    //     Pause,
    //     /// <summary>
    //     ///   <para>Continue simulating the Particle System when it is offscreen.</para>
    //     /// </summary>
    //     AlwaysSimulate,
    // }
    //
    // /// <summary>
    // ///   <para>Control how particles are removed from the Particle System.</para>
    // /// </summary>
    // public enum ParticleSystemRingBufferMode
    // {
    //     /// <summary>
    //     ///   <para>Particles are removed when their age exceeds their lifetime.</para>
    //     /// </summary>
    //     Disabled,
    //     /// <summary>
    //     ///   <para>Particle ages pause at the end of their lifetime until they need to be removed. Particles are removed when creating new particles would exceed the Max Particles property.</para>
    //     /// </summary>
    //     PauseUntilReplaced,
    //     /// <summary>
    //     ///   <para>Particle ages loop until they need to be removed. Particles are removed when creating new particles would exceed the Max Particles property.</para>
    //     /// </summary>
    //     LoopUntilReplaced,
    // }

    public enum UVChannelFlags : byte
    {
        /// <summary>
        ///   <para>First UV channel.</para>
        /// </summary>
        UV0 = 1,
        /// <summary>
        ///   <para>Second UV channel.</para>
        /// </summary>
        UV1 = 2,
        /// <summary>
        ///   <para>Third UV channel.</para>
        /// </summary>
        UV2 = 4,
        /// <summary>
        ///   <para>Fourth UV channel.</para>
        /// </summary>
        UV3 = 8,
    }
}
#endif

