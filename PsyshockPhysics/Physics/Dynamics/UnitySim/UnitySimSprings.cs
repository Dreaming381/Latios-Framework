using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    public static partial class UnitySim
    {
        /// <summary>
        /// An extremely stiff spring frequency which can be used to represent rigid constraints
        /// </summary>
        public const float kStiffSpringFrequency = 74341.31f;
        /// <summary>
        /// An extremely stiff damping ratio which can be used to represent rigid constraints
        /// </summary>
        public const float kStiffDampingRatio = 2530.126f;

        /// <summary>
        /// Converts the spring constant of a spring force and its mass into a mass-independent frequency
        /// </summary>
        /// <param name="springConstant">The spring force constant, as specified by Hooke's Law</param>
        /// <param name="inverseMass">The reciprocal of the mass</param>
        /// <returns>A frequency of oscillation that the spring exhibits when no damping is present</returns>
        public static float SpringFrequencyFrom(float springConstant, float inverseMass)
        {
            return springConstant * inverseMass * rcpTwoPI;
        }

        /// <summary>
        /// Converts the spring mass-independent frequency into a spring force constant
        /// </summary>
        /// <param name="springFrequency">The natural frequency of oscillation of the spring</param>
        /// <param name="mass">The mass load of the spring which results in the frequency of oscillation</param>
        /// <returns>The spring force constant, as specified by Hooke's Law</returns>
        public static float SpringConstantFrom(float springFrequency, float mass)
        {
            return springFrequency * mass * 2f * math.PI;
        }

        /// <summary>
        /// Computes the mass-independent spring damping ratio from various other spring properties
        /// </summary>
        /// <param name="springConstant">The spring force constant, as specified by Hooke's Law</param>
        /// <param name="dampingConstant">A damping force constant from a spring-damper model</param>
        /// <param name="mass">The mass load of the spring</param>
        /// <returns>A mass-independent damping ratio which can be used alongside a spring frequency to parameterize a joint</returns>
        public static float DampingRatioFrom(float springConstant, float dampingConstant, float mass)
        {
            var product = springConstant * mass;
            if (product < math.EPSILON)
            {
                if (springConstant < math.EPSILON || mass < math.EPSILON)
                    return kStiffDampingRatio;

                var critical = 2f * math.sqrt(springConstant) * math.sqrt(mass);
                return dampingConstant / critical;
            }
            return dampingConstant / (2f * math.sqrt(product));  // damping coefficient / critical damping coefficient
        }

        /// <summary>
        /// Computes the spring damping constant given its damping ratio and other spring properties
        /// </summary>
        /// <param name="springConstant">The spring force constant, as specified by Hooke's Law</param>
        /// <param name="dampingRatio">The mass-independent damping ratio of the spring</param>
        /// <param name="mass">The mass load of the spring</param>
        /// <returns>The mass constant in the spring-damper model</returns>
        public static float DampingConstantFrom(float springConstant, float dampingRatio, float mass)
        {
            var product = springConstant * mass;
            if (product < math.EPSILON)
            {
                if (springConstant < math.EPSILON || mass < math.EPSILON)
                    return dampingRatio * float.Epsilon;
                var critical = 2f * math.sqrt(springConstant) * math.sqrt(mass);
                return dampingRatio * critical;
            }
            return dampingRatio * 2f * math.sqrt(product);
        }

        /// <summary>
        /// Computes the spring frequency and damping ratio from the simulation-normalized solver constraint parameters
        /// </summary>
        /// <param name="constraintTau">The normalized constraint parameter of the spring's force</param>
        /// <param name="constraintDamping">The normalized constraint parameter of the spring's internal resistance</param>
        /// <param name="deltaTime">The timestep from which the simulation-normalized parameters were derived</param>
        /// <param name="iterations">The number of velocity solver iterations from which the simulation-normalized parameters were derived</param>
        /// <param name="springFrequency">The resulting mass-independent spring frequency of oscillation</param>
        /// <param name="dampingRatio">The resulting mass-independent internal resistance parameter of the spring</param>
        public static void SpringFrequencyAndDampingRatioFrom(float constraintTau, float constraintDamping, float deltaTime, int iterations,
                                                              out float springFrequency, out float dampingRatio)
        {
            int   n    = iterations;
            float h    = deltaTime;
            float hh   = h * h;
            float a    = 1.0f - constraintDamping;
            float aSum = 1.0f;
            for (int i = 1; i < n; i++)
            {
                aSum += math.pow(a, i);
            }

            float w         = math.sqrt(constraintTau * aSum / math.pow(a, n)) / h;
            float ww        = w * w;
            springFrequency = w / (2.0f * math.PI);
            dampingRatio    = (math.pow(a, -n) - 1 - hh * ww) / (2.0f * h * w);
        }

        // This is the inverse function to CalculateSpringFrequencyAndDamping
        /// <summary>
        /// Computes the spring constraint solver normalization parameters relative to the simulation timestep and iteration count
        /// </summary>
        /// <param name="springFrequency">The spring's mass-independent oscillating frequency when no internal resistance is present</param>
        /// <param name="dampingRatio">The spring's mass-independent internal resistance factor</param>
        /// <param name="deltaTime">The timestep of the simulation</param>
        /// <param name="iterations">The number of velocity solver iterations to be used with the parameters</param>
        /// <param name="constraintTau">The resulting constraint solver normalized parameter of the spring's force</param>
        /// <param name="constraintDamping">The resulting constraint solver normalized parameter of the spring's internal resistance</param>
        public static void ConstraintTauAndDampingFrom(float springFrequency, float dampingRatio, float deltaTime, int iterations,
                                                       out float constraintTau, out float constraintDamping)
        {
            /*
               In the following we derive the formulas for converting spring frequency and damping ratio to the solver constraint regularization parameters tau and damping,
               representing a normalized stiffness factor and damping factor, respectively.
               To this end, we compare the integration of spring-damper using implicit Euler integration with the time stepping formula for the constraint solver, and make both equivalent.

               1.  Implicit Euler integration of a spring-damper

                Constitutive equation of a spring-damper:
                    F = -kx - cx'
                with k = spring stiffness, c = damping coefficient, x = position, and x' = velocity.

                Backwards euler of the equations of motion a = x'' and v = x' with a = F/m where h = step length:

                    x2 = x1 + hv2
                    v2 = v1 + hx''
                       = v1 + hF/m
                       = v1 + h(-kx2 - cv2)/m
                       = v1 + h(-kx1 - hkv2 - cv2)/m
                       = 1 / (1 + h^2k/m + hc/m) * v1 - hk / (m + h^2k + hc) * x1

               2.  Gauss-Seidel iterations of a stiff constraint with Baumgarte stabilization parameters t and a, where
                t = tau, d = damping, and a = 1 - d.

                Example for four iterations:

                    v2 = av1 - (t / h)x1
                    v3 = av2 - (t / h)x1
                    v4 = av3 - (t / h)x1
                    v5 = av4 - (t / h)x1
                       = a^4v1 - (a^3 + a^2 + a + 1)(t / h)x1

                Given the recursive nature of the relationship above we can derive a closed-form expression for the new velocity with n iterations:
                    v_n = a * v_n-1 - (t / h) * x1
                        = a^n * v1 - (a^(n-1) + a^(n-2) + ... + a + 1)(t / h) * x1
                        = a^n * v1 - (\sum_{i=0}^{n-1} a^i)(t / h) * x1
                        = a^n * v1 - ((1 - a^n) / (1 - a))(t / h) * x1                      (1)

                Note that above we replaced the geometric series from 1 to n-1 with the closed form expression (1 - a^n) / (1 - a). This is valid for
                a != 1.0. If a == 1.0, the following closed form expression needs to be used instead:

                  \sum_{i=0}^{n-1} a^i) = n

                In this case, the equation above simplifies to:

                v_n = a^n * v1 - (\sum_{i=0}^{n-1} a^i)(t / h) * x1
                    = a^n * v1 - n(t / h) * x1

                For now we will ignore this special case. We will see if a can become 1 and under which conditions, once we have found an expression for a in the following step.

               3.1 Via coefficient matching, we can map the stiffness and damping parameters in the spring-damper to the tau and damping parameters in the stiff constraint.
                For n iterations, we have the following equations:

                    a^n = 1 / (1 + h^2k / m + hc / m), and                                  (2)
                    ((1 - a^n) / (1 - a))(t / h) = hk / (m + h^2k + hc)                     (3)

                where k is the spring constant, c is the damping constant, m is the mass, h is the time step, and a and t are the
                damping and tau parameters of the stiff constraint, respectively.

                We can solve (2) and (3) for a and t in terms of k, c, m, and h as follows.

                First, solve equation (2) for a:

                    a = (1 / (1 + h^2k / m + hc / m))^(1/n)                                 (4)
                <=> d = 1 - a
                      = 1 - (1 / (1 + h^2k / m + hc / m))^(1/n)                             (5)

                Then plug a into equation (3) to solve for t:

                         ((1 - a^n) / (1 - a))(t / h) = hk / (m + h^2k + hc)
                    <=>  ((1 - 1 / (1 + h^2k / m + hc / m)) / (1 - a))(t / h) = hk / (m + h^2k + hc)
                    <=>  ((1 - 1 / (1 + h^2k / m + hc / m)) / (1 - (1 / (1 + h^2k / m + hc / m))^(1/n)))(t / h) = hk / (m + h^2k + hc)
                    <=> t = h^2k / (m + h^2k + hc) * (1 - (1 / (1 + h^2k / m + hc / m))^(1/n)) / ((1 - 1 / (1 + h^2k / m + hc / m))

               We can simplify this further as follows:

                    t = h^2k / (m + h^2k + hc) * (1 - (1 / (1 + h^2k / m + hc / m))^(1/n)) * (1 + h^2k / m + hc / m) / ((1 + h^2k / m + hc / m) - 1)
                    t = h^2k / (m + h^2k + hc) * (1 - (1 / (1 + h^2k / m + hc / m))^(1/n)) * (1 + h^2k / m + hc / m) / (h^2k / m + hc / m)
                    t = h^2k / (m + h^2k + hc) * (1 - (1 / (1 + h^2k / m + hc / m))^(1/n)) * (m + h^2k + hc) / (h^2k + hc)
                    t = h^2k / (h^2k + hc) * (1 - (1 / (1 + h^2k / m + hc / m))^(1/n)) * (m + h^2k + hc) / (m + h^2k + hc)
                    t = h^2k / (h^2k + hc) * (1 - (1 / (1 + h^2k / m + hc / m))^(1/n))

               This yields the final expression for t:

                    t = h^2k / (h^2k + hc) * (1 - (1 / (1 + h^2k / m + hc / m))^(1/n))
                      = h^2k / (h^2k + hc) * d                                              (6)

               3.2 Coming back to our requirement from above that a != 1, let's examine in what situation a can become 1:

                    a = (1 / (1 + h^2k / m + hc / m))^(1/n) = 1

                We can see that a can only be 1 iff (if and only if) the term h^2k / m + hc / m equals 0.

                Given that k and c are both positive values and both m and h are strictly positive, this can only be the case if both k and h are 0, in which case our spring-damper
                will simply not apply any force, meaning, the constraint will not be active. We can deal with this case by simply setting the constraint regularization parameters
                t and d (= 1 - a) to 0 in this case. This will result in the constraint being inactive, which is what we want.

               3.4 Parametrization using Spring Frequency and Damping Ratio:

                Given spring frequency f, damping ratio z and effective mass m, we have the following relationships:
                    w = f * 2 * pi
                    k = m * w^2 <=> k/m = w^2
                    c = z * 2 * w * m <=> c/m = z * 2 * w
                where w denotes the angular spring frequency, k denotes the spring stiffness coefficient and c denotes the damping coefficient.

                We can use the relationships above to convert the expressions (5) and (6) for d and t to the following expressions in terms of spring frequency and damping ratio:
                    d = 1 - (1 / (1 + h^2 * m * w^2 / m + h * z * 2 * w * m / m))^(1/n)
                      = 1 - (1 / (1 + h^2 * w^2 + h * z * 2 * w))^(1/n)                     (7)

                In (6), substitute k for m * w^2 and c for z * 2 * w * m to get:
                    t = h^2k / (h^2k + hc) * d
                      = h^2 * m * w^2 / (h^2 * m * w^2 + h * z * 2 * w * m) * d

                Eliminate m to obtain the final expression:
                    t = h^2 * w^2 / (h^2 * w^2 + h * z * 2 * w) * d                         (8)

                This allows us to parametrize our constraint using the spring frequency and damping ratio of an equivalent spring-damper system.
             */

            // Compute damping factor d from spring frequency f, damping ratio z, time step h, and number of iterations n using equation (7) above.
            // With d in hand, compute stiffness factor tau from spring frequency f, damping coefficient c, time step h, number of iterations n and damping factor d using equation (8) above.

            // f: spring frequency, w: angular spring frequency, z: damping ratio
            float f     = springFrequency;
            float z     = dampingRatio;
            float h     = deltaTime;
            float w     = f * 2 * math.PI;  // convert frequency to angular frequency, i.e., oscillations/sec to radians/sec
            float hw    = h * w;
            float hhww  = hw * hw;  // = h^2 * w^2
            float denom = hhww + hw * z * 2;
            float exp1  = 1 / (1 + denom);
            float exp2  = math.pow(exp1, 1f / iterations);

            constraintDamping = 1 - exp2;
            constraintTau     = denom < math.EPSILON ? 0f : hhww / denom * constraintDamping;
        }

        const float rcpTwoPI = 0.5f / math.PI;
    }
}

