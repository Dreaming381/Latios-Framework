using Unity.Mathematics;

namespace Latios.Myri.Authoring
{
    internal struct DefaultListenerProfileBuilder : IListenerProfileBuilder
    {
        public void BuildProfile(ref ListenerProfileBuildContext context)
        {
            var occlusionFilter = new FrequencyFilter
            {
                cutoff         = 1500f,
                gainInDecibels = 0f,
                q              = 0.707f,
                type           = FrequencyFilterType.Lowpass
            };

            // left unblocked
            context.AddSpatialChannel(new float2(math.PI / 2f, math.PI * 1.25f), new float2(-math.PI, math.PI), 1f, false);
            // left fully blocked
            var leftFilterChannel = context.AddSpatialChannel(new float2(-math.PI / 4f, math.PI / 4f),   new float2(-math.PI, math.PI), 1f, false);
            context.AddFilterToChannel(occlusionFilter, leftFilterChannel);
            // left direct
            context.AddDirectChannel(1f, false);

            // right unblocked
            context.AddSpatialChannel(new float2(-math.PI / 4f, math.PI / 2f), new float2(-math.PI, math.PI), 1f, true);
            // right fully blocked
            var rightFilterChannel = context.AddSpatialChannel(new float2(math.PI * 0.75f, math.PI * 1.25f), new float2(-math.PI, math.PI), 1f, true);
            context.AddFilterToChannel(occlusionFilter, rightFilterChannel);
            // right direct
            context.AddDirectChannel(1f, true);
        }
    }
}

