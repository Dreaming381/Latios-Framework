using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.MachAxle
{
    // Todo: Optimize this to batch evaluations by shared inputs or even shared "time" values.
    struct AclCurve : ICurve
    {
        public int clipIndex;
        public int sourcePortsStart;
        public int sourcePortsCount;
        public int destinationPortSpansStart;
        public int destinationPortSpansCount;
        public int journalBaseIndex;

        public void Evaluate<T>(ref Graph graph, ref T bus) where T : unmanaged, IBus
        {
            bus.SetJournalCurveBaseIndex(journalBaseIndex);
            ref var clip             = ref graph.parameterClips[clipIndex];
            var     sourcePorts      = graph.sourcePorts.AsSpan().Slice(sourcePortsStart, sourcePortsCount);
            var     destinationPorts = graph.destinationPortSpans.AsSpan().Slice(destinationPortSpansStart, destinationPortSpansCount);
            int     instanceCount    = bus.InstanceCount();

            if (bus.HasFilters())
            {
                var bitfieldArray = bus.GetFilter();
                for (int i = 0; i < sourcePortsCount; i++)
                {
                    var bitfield = bitfieldArray[(i + journalBaseIndex) >> 6];
                    if (!bitfield.IsSet((i + journalBaseIndex) & 0x3f))
                        continue;
                    bus.SetJournalCurveLocalIndex(i);
                    for (int j = 0; j < instanceCount; j++)
                    {
                        bus.SetJournalPortIndex(0);
                        float input  = bus.Import(sourcePorts[i], j);
                        float output = clip.SampleParameter(i, input);
                        bus.SetJournalPortIndex(1);
                        bus.Export(destinationPorts[i], j, output);
                    }
                }
            }
            else
            {
                for (int i = 0; i < sourcePortsCount; i++)
                {
                    bus.SetJournalCurveLocalIndex(i);
                    for (int j = 0; j < instanceCount; j++)
                    {
                        bus.SetJournalPortIndex(0);
                        float input  = bus.Import(sourcePorts[i], j);
                        float output = clip.SampleParameter(i, input);
                        bus.SetJournalPortIndex(1);
                        bus.Export(destinationPorts[i], j, output);
                    }
                }
            }
        }
    }
}

