using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;

namespace Latios.Calci
{
    public static partial class BezierMath
    {
        /// <summary>
        /// Evaluates the curve track at the specified time. The time is clamped to the range specified by the curve.
        /// Keyframes must be sorted or else this method may produce unusable results including infinities and NaNs.
        /// </summary>
        /// <param name="curveTrack">The sorted sequence of keyframes</param>
        /// <param name="time">The time to sample at</param>
        /// <returns>The sampled value</returns>
        public static float Evaluate(ReadOnlySpan<Keyframe> curveTrack, float time)
        {
            var nextKeyframe = BinarySearch.FirstGreaterOrEqual(curveTrack, new Keyframe { time = time }, new TimeComparer());
            if (nextKeyframe == 0)
                return curveTrack[0].value;
            if (nextKeyframe == curveTrack.Length)
                return curveTrack[curveTrack.Length - 1].value;

            var keyedCurve = KeyedCurve.FromKeyframes(in curveTrack[nextKeyframe - 1], in curveTrack[nextKeyframe]);
            return Evaluate(in keyedCurve, time);
        }

        private struct TimeComparer : IComparer<Keyframe>
        {
            public int Compare(Keyframe x, Keyframe y) => x.time.CompareTo(y.time);
        }
    }
}

