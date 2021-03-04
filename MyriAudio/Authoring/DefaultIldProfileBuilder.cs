using Unity.Mathematics;
using UnityEngine;

namespace Latios.Myri.Authoring
{
    internal class DefaultIldProfileBuilder : AudioIldProfileBuilder
    {
        protected override void BuildProfile()
        {
            //left unblocked
            AddChannel(new float2(math.PI / 2f, math.PI * 1.25f), new float2(-math.PI, math.PI), 1f, 0f, 1f, false);
            //left fully blocked
            var leftFilterChannel = AddChannel(new float2(-math.PI / 4f, math.PI / 4f),   new float2(-math.PI, math.PI), 0f, 1f, 0f, false);
            AddFilterToChannel(new FrequencyFilter
            {
                cutoff         = 1500f,
                gainInDecibels = 0f,
                q              = 0.707f,
                type           = FrequencyFilterType.Lowpass
            }, leftFilterChannel);
            //right unblocked
            AddChannel(new float2(-math.PI / 4f, math.PI / 2f), new float2(-math.PI, math.PI), 1f, 0f, 1f, true);
            //right fully blocked
            var rightFilterChannel = AddChannel(new float2(math.PI * 0.75f, math.PI * 1.25f), new float2(-math.PI, math.PI), 0f, 1f, 0f, true);
            AddFilterToChannel(new FrequencyFilter
            {
                cutoff         = 1500f,
                gainInDecibels = 0f,
                q              = 0.707f,
                type           = FrequencyFilterType.Lowpass
            }, rightFilterChannel);
        }
    }
}

