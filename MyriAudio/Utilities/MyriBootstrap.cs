using Latios.Myri.AudioEcsBuiltin;
using Unity.Entities;
using UnityEngine.Audio;

namespace Latios.Myri
{
    /// <summary>
    /// Static class containing installers for optional runtime features in the Myri module
    /// </summary>
    public static class MyriBootstrap
    {
        /// <summary>
        /// Installs Myri into the World at its default location
        /// </summary>
        /// <param name="world">The runtime world in which Myri should be installed</param>
        /// <param name="customAudioEcsBootstrap">An optional custom Audio ECS Bootstrap. If left null, then the built-in bootstrap will be used.</param>
        public static void InstallMyri(LatiosWorld world, IAudioEcsBootstrap customAudioEcsBootstrap = null)
        {
            if (world.Flags.HasFlag(WorldFlags.Conversion))
                throw new System.InvalidOperationException("Cannot install Myri runtime in a conversion world.");

            if (customAudioEcsBootstrap == null)
                customAudioEcsBootstrap                                                                    = new BuiltInRunnerBootstrap();
            world.worldBlackboardEntity.AddManagedStructComponent(new AudioEcsBootstrapCarrier { bootstrap = customAudioEcsBootstrap });
            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<Systems.AudioSystem>(), world);
        }

        /// <summary>
        /// Instantiates a RootOutput which contains the Audio ECS runtime. Only call this yourself if you want to use
        /// the Audio ECS runtime without Myri's sources, listeners, nor blob asset lifecycle preserver.
        /// </summary>
        /// <param name="bootstrap">An interface which is used to construct the Audio ECS runtime.
        /// Typically, this instance is kept alive for the full lifecycle of the runtime as the owner of any read-only
        /// configuration resources used by the runtime. By calling this method, you are fully responsible for its
        /// lifecycle.</param>
        /// <param name="latiosWorld">The LatiosWorld to install the AudioEcsCommandPipe, AudioEcsFeedbackPipe, and
        /// AudioEcsAtomicFeedbackId components onto the worldBlackboardEntity</param>
        /// <param name="controlContext">The ControlContext to allocate the RootOutput into</param>
        /// <returns>A handle to the RootOutput. You must call ShutdownCustomAudioEcsRuntime() and pass in this instance
        /// when you are done using it.</returns>
        public static RootOutputInstance CreateCustomAudioEcsRuntime(IAudioEcsBootstrap bootstrap, LatiosWorldUnmanaged latiosWorld, ControlContext controlContext)
        {
            var control  = new AudioEcsController(bootstrap, latiosWorld);
            var realtime = control.CreateRealtime();
            return controlContext.AllocateRootOutput(in realtime, in control, new ProcessorInstance.CreationParameters
            {
                controlUpdateSetting  = ProcessorInstance.UpdateSetting.UpdateAlways,
                realtimeUpdateSetting = ProcessorInstance.UpdateSetting.UpdateAlways,
            });
        }

        /// <summary>
        /// Shuts down a custom Audio ECS runtime. This method ensures that any external resources still being used
        /// by the runtime are safe to dispose after this method returns.
        /// </summary>
        /// <param name="instance">The RootOutputInstance returned by CreateCustomAudioEcsRuntime()</param>
        /// <param name="controlContext">The ControlContext that was passed into CreateCustomAudioEcsRuntime()</param>
        public static void ShutdownCustomAudioEcsRuntime(RootOutputInstance instance, ControlContext controlContext)
        {
            ShutdownControlMessage message = default;
            controlContext.SendMessage(instance, ref message);
            controlContext.Destroy(instance);
            ControlContext.WaitForBuiltInQueueFlush();
        }
    }
}

