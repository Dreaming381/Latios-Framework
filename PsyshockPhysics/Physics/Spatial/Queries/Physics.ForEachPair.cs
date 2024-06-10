using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    /// <summary>
    /// An interface whose Execute method is invoked for each pair found in the PairStream.
    /// </summary>
    public interface IForEachPairProcessor
    {
        /// <summary>
        /// The main pair processing callback. Disabled pairs may not receive invocations depending on the settings used.
        /// </summary>
        void Execute(ref PairStream.Pair pair);

        /// <summary>
        /// An optional callback prior to processing a batch of streams.
        /// </summary>
        /// <returns>Returns true if the Execute() and EndBucket() methods should be called. Otherwise, further
        /// processing of the bucket is skipped.</returns>
        bool BeginStreamBatch(ForEachPairBatchContext context) => true;
        /// <summary>
        /// An optional callback following processing a batch of streams.
        /// </summary>
        void EndStreamBatch(ForEachPairBatchContext context)
        {
        }
    }

    /// <summary>
    /// A context struct passed into BeginStreamBatch and EndStreamBatch of an IForEachPairProcessor which provides
    /// additional information about the streams being processed.
    /// </summary>
    public struct ForEachPairBatchContext
    {
        internal PairStream.Enumerator enumerator;

        /// <summary>
        /// Gets an enumerator that enumerates over the batch of streams.
        /// </summary>
        public PairStream.Enumerator GetEnumerator() => enumerator;
        /// <summary>
        /// The first stream index in the batch
        /// </summary>
        public int startStreamIndex => enumerator.pair.streamIndex;
        /// <summary>
        /// The number of streams in the batch
        /// </summary>
        public int streamCountInBatch => enumerator.onePastLastStreamIndex - startStreamIndex;
    }

    public static partial class Physics
    {
        public static ForEachPairConfig<T> ForEachPair<T>(in PairStream pairStream, in T processor) where T : struct, IForEachPairProcessor
        {
            return new ForEachPairConfig<T>
            {
                processor       = processor,
                pairStream      = pairStream,
                includeDisabled = false,
            };
        }
    }

    public partial struct ForEachPairConfig<T> where T : struct, IForEachPairProcessor
    {
        internal T          processor;
        internal PairStream pairStream;
        internal bool       includeDisabled;

        /// <summary>
        /// Includes disabled pairs when calling IForEachPairProcessor.Execute()
        /// </summary>
        public ForEachPairConfig<T> IncludeDisabled()
        {
            includeDisabled = true;
            return this;
        }

        /// <summary>
        /// Run the ForEachPair operation without using a job. This method can be invoked from inside a job.
        /// </summary>
        public void RunImmediate()
        {
            ForEachPairMethods.ExecuteBatch(ref pairStream, ref processor, 0, pairStream.data.pairHeaders.indexCount, false, false, includeDisabled);
        }

        /// <summary>
        /// Run the ForEachPair operation on the main thread using a Bursted job.
        /// </summary>
        public void Run()
        {
            new ForEachPairInternal.ForEachPairJob(in pairStream, in processor, includeDisabled).Run();
        }

        /// <summary>
        /// Run the ForEachPair operation on a single worker thread.
        /// </summary>
        /// <param name="inputDeps">The input dependencies from any previous operation that touches the PairStream</param>
        /// <returns>A JobHandle for the scheduled job</returns>
        public JobHandle ScheduleSingle(JobHandle inputDeps)
        {
            return new ForEachPairInternal.ForEachPairJob(in pairStream, in processor, includeDisabled).ScheduleSingle(inputDeps);
        }

        /// <summary>
        /// Run the ForEachPair operation using multiple worker threads in multiple phases.
        /// If the PairStream was constructed from only a single cell (all subdivisions == 1), this falls back to ScheduleSingle().
        /// </summary>
        /// <param name="inputDeps">The input dependencies from any previous operation that touches the PairStream</param>
        /// <returns>The final JobHandle for the scheduled jobs</returns>
        public JobHandle ScheduleParallel(JobHandle inputDeps)
        {
            if (IndexStrategies.ScheduleParallelShouldActuallyBeSingle(pairStream.data.cellCount))
                return ScheduleSingle(inputDeps);

            var jh = new ForEachPairInternal.ForEachPairJob(in pairStream, in processor, includeDisabled).ScheduleParallel(inputDeps, ScheduleMode.ParallelPart1);
            ForEachPairMethods.ScheduleBumpVersions(ref pairStream, ref jh);
            return new ForEachPairInternal.ForEachPairJob(in pairStream, in processor, includeDisabled).ScheduleParallel(jh, ScheduleMode.ParallelPart2);
        }

        /// <summary>
        /// Run the ForEachPair operation using multiple worker threads all at once without entity thread-safety.
        /// If the PairStream was constructed from only a single cell (all subdivisions == 1), this falls back to ScheduleSingle().
        /// </summary>
        /// <param name="inputDeps">The input dependencies from any previous operation that touches the PairStream</param>
        /// <returns>A JobHandle for the scheduled job</returns>
        public JobHandle ScheduleParallelUnsafe(JobHandle inputDeps)
        {
            if (IndexStrategies.ScheduleParallelShouldActuallyBeSingle(pairStream.data.cellCount))
                return ScheduleSingle(inputDeps);

            return new ForEachPairInternal.ForEachPairJob(in pairStream, in processor, includeDisabled).ScheduleParallel(inputDeps, ScheduleMode.ParallelUnsafe);
        }
    }
}

