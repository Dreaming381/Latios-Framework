using Unity.Mathematics;
using UnityEngine;

namespace Latios.Myri.Authoring
{
    internal struct DefaultListenerProfileBuilder : IListenerProfileBuilder
    {
        public void BuildProfile(ref ListenerProfileBuildContext context)
        {
            //left unblocked
            context.AddChannel(new float2(math.PI / 2f, math.PI * 1.25f), new float2(-math.PI, math.PI), 1f, 0f, 1f, false);
            //left fully blocked
            var leftFilterChannel = context.AddChannel(new float2(-math.PI / 4f, math.PI / 4f),   new float2(-math.PI, math.PI), 0f, 1f, 0f, false);
            context.AddFilterToChannel(new FrequencyFilter
            {
                cutoff         = 1500f,
                gainInDecibels = 0f,
                q              = 0.707f,
                type           = FrequencyFilterType.Lowpass
            }, leftFilterChannel);
            //right unblocked
            context.AddChannel(new float2(-math.PI / 4f, math.PI / 2f), new float2(-math.PI, math.PI), 1f, 0f, 1f, true);
            //right fully blocked
            var rightFilterChannel = context.AddChannel(new float2(math.PI * 0.75f, math.PI * 1.25f), new float2(-math.PI, math.PI), 0f, 1f, 0f, true);
            context.AddFilterToChannel(new FrequencyFilter
            {
                cutoff         = 1500f,
                gainInDecibels = 0f,
                q              = 0.707f,
                type           = FrequencyFilterType.Lowpass
            }, rightFilterChannel);
        }
    }
}

