using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    public static partial class Physics
    {
        /// <summary>
        /// Creates an enumerator that divides up a timestep into substeps, where each substep returns a substep time.
        /// Use this in a foreach loop to perform the same routine over each substep in a simulation.
        /// </summary>
        /// <param name="deltaTime">The time to divide into substeps</param>
        /// <param name="numSubsteps">The number of substeps to divide deltaTime into</param>
        /// <returns>An enumerator that can be iterated upon in a foreach statement</returns>
        public static SubstepEnumerator Substep(float deltaTime, int numSubsteps) => new SubstepEnumerator(deltaTime, numSubsteps);

        public struct SubstepEnumerator : IEnumerator<float>, IEnumerable<float>
        {
            float substepTime;
            float deltaTime;
            float accumulatedTime;
            int   currentSubstep;
            int   totalSubsteps;

            internal SubstepEnumerator(float deltaTime, int substeps)
            {
                this.deltaTime  = deltaTime;
                totalSubsteps   = substeps;
                accumulatedTime = 0f;
                substepTime     = deltaTime / substeps;
                currentSubstep  = 0;
            }

            internal int substepIndex => currentSubstep - 1;

            public float Current => substepTime;

            object IEnumerator.Current => substepTime;

            /// <summary>
            /// Modifies the enumerator to additionally return the index of the substep along with the timestep.
            /// Use as follows:
            ///     foreach ((float timestep, int substepIndex) in Physics.Substep(deltaTime, numSubsteps).WithIndex())
            /// </summary>
            /// <returns>An enumerator that can be iterated upon in a foreach statement</returns>
            public SubstepWithIndexEnumerator WithIndex() => new SubstepWithIndexEnumerator(this);

            public void Dispose()
            {
            }

            public bool MoveNext()
            {
                if (currentSubstep + 1 < totalSubsteps)
                {
                    accumulatedTime += substepTime;
                    currentSubstep++;
                    return true;
                }
                else if (currentSubstep < totalSubsteps)
                {
                    substepTime = deltaTime - accumulatedTime;
                    currentSubstep++;
                    return true;
                }

                return false;
            }

            public void Reset()
            {
                this = new SubstepEnumerator(deltaTime, totalSubsteps);
            }

            public SubstepEnumerator GetEnumerator() => this;

            IEnumerator<float> IEnumerable<float>.GetEnumerator() => this;

            IEnumerator IEnumerable.GetEnumerator() => this;
        }

        public struct SubstepWithIndex
        {
            public float substepTimeDuration;
            public int   substepIndex;

            public void Deconstruct(out float time, out float index)
            {
                time  = substepTimeDuration;
                index = substepIndex;
            }
        }

        public struct SubstepWithIndexEnumerator : IEnumerator<SubstepWithIndex>, IEnumerable<SubstepWithIndex>
        {
            SubstepEnumerator m_enumerator;

            public SubstepWithIndexEnumerator(SubstepEnumerator enumerator)
            {
                m_enumerator = enumerator;
            }

            public SubstepWithIndex Current => new SubstepWithIndex { substepTimeDuration = m_enumerator.Current, substepIndex = m_enumerator.substepIndex };

            object IEnumerator.Current => new SubstepWithIndex { substepTimeDuration = m_enumerator.Current, substepIndex = m_enumerator.substepIndex };

            public void Dispose()
            {
            }

            public bool MoveNext() => m_enumerator.MoveNext();

            public void Reset() => m_enumerator.Reset();

            public SubstepWithIndexEnumerator GetEnumerator() => this;

            IEnumerator<SubstepWithIndex> IEnumerable<SubstepWithIndex>.GetEnumerator() => this;

            IEnumerator IEnumerable.GetEnumerator() => this;
        }
    }
}

