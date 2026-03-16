/*******************************************************************************
* Author    :  Angus Johnson                                                   *
* Date      :  16 July 2023                                                    *
* Website   :  http://www.angusj.com                                           *
* Copyright :  Angus Johnson 2010-2023                                         *
* Purpose   :  Path Offset (Inflate/Shrink)                                    *
* License   :  http://www.boost.org/LICENSE_1_0.txt                            *
*******************************************************************************/

using System;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Mathematics;

namespace Latios.Calci.Clipper2
{
    internal enum JoinType
    {
        Square,
        Round,
        Miter
    };

    internal enum EndType
    {
        Polygon,
        Joined,
        Butt,
        Square,
        Round
    };

    internal struct ClipperOffsetL
    {
        public ClipperOffsetL(
            NativeArray<int2> path,
            JoinType joinType,
            ref ClipperL clipperL,
            Allocator allocator,
            double miterLimit        = 2.0,
            double arcTolerance      = 0.0,
            bool preserveCollinear = false,
            bool reverseSolution   = false,
            EndType endType           = EndType.Polygon)
        {
            int cnt = path.Length;
            inPath  = new NativeList<int2>(path.Length, allocator);

            var lastPt = path[0];
            inPath.Add(lastPt);
            for (int i = 1; i < cnt; i++)
                if (!math.all(lastPt == path[i]))
                {
                    lastPt = path[i];
                    inPath.Add(lastPt);
                }
            if (math.all(lastPt == inPath[0]))
                inPath.RemoveAt(inPath.Length - 1);

            //for (int i = 0, length = path.Length; i < length; i++)
            //    inPath.Add(path[i]);

            outPath       = new NativeList<int2>(path.Length, allocator);
            _joinType     = joinType;
            _endType      = endType;
            pathsReversed = false;
            this.clipperL = clipperL;

            _normals          = new NativeList<double2>(path.Length, allocator);
            MiterLimit        = miterLimit;
            ArcTolerance      = arcTolerance;
            MergeGroups       = true;
            PreserveCollinear = preserveCollinear;
            ReverseSolution   = reverseSolution;

            _groupDelta  = default;  //*0.5 for open paths; *-1.0 for negative areas
            _delta       = default;
            _mitLimSqr   = default;
            _stepsPerRad = default;
            _stepSin     = default;
            _stepCos     = default;
        }

        internal ClipperL         clipperL;
        internal NativeList<int2> inPath;
        internal NativeList<int2> outPath;
        internal bool             pathsReversed;

        private const double Tolerance = 1.0E-12;

        // Clipper2 approximates arcs by using series of relatively short straight
        //line segments. And logically, shorter line segments will produce better arc
        // approximations. But very short segments can degrade performance, usually
        // with little or no discernable improvement in curve quality. Very short
        // segments can even detract from curve quality, due to the effects of integer
        // rounding. Since there isn't an optimal number of line segments for any given
        // arc radius (that perfectly balances curve approximation with performance),
        // arc tolerance is user defined. Nevertheless, when the user doesn't define
        // an arc tolerance (ie leaves alone the 0 default value), the calculated
        // default arc tolerance (offset_radius / 500) generally produces good (smooth)
        // arc approximations without producing excessively small segment lengths.
        // See also: https://www.angusj.com/clipper2/Docs/Trigonometry.htm
        private const double arc_const = 0.002;  // <-- 1/500

        private NativeList<double2> _normals;
        private double              _groupDelta;  //*0.5 for open paths; *-1.0 for negative areas
        private double              _delta;
        private double              _mitLimSqr;
        private double              _stepsPerRad;
        private double              _stepSin;
        private double              _stepCos;
        private JoinType            _joinType;
        private EndType             _endType;
        public double ArcTolerance { get; set; }
        public bool MergeGroups { get; set; }
        public double MiterLimit { get; set; }
        public bool PreserveCollinear { get; set; }
        public bool ReverseSolution { get; set; }

        private void ExecuteInternal(double delta)
        {
            outPath.Clear();

            if (math.abs(delta) < 0.5)
            {
                outPath.AddRange(inPath.AsArray());
            }
            else
            {
                _delta     = delta;
                _mitLimSqr = (MiterLimit <= 1 ?
                              2.0 : 2.0 / (MiterLimit * MiterLimit));

                DoGroupOffset();
            }
        }

        public void Execute(double delta,
                            ref NativeList<int2> solutionNodes,
                            ref NativeList<int>  solutionStartIDs,
                            ref NativeList<int2> solutionOpenNodes,
                            ref NativeList<int>  solutionOpenStartIDs)
        {
            ExecuteInternal(delta);

            // clean up self-intersections ...
            clipperL.AddPath(outPath.AsArray(), 0, outPath.Length, PathType.Subject);
            clipperL.PreserveCollinear = PreserveCollinear;
            clipperL.ReverseSolution   = ReverseSolution;
            clipperL.Execute(ClipType.Union, FillRule.Positive, ref solutionNodes, ref solutionStartIDs, ref solutionOpenNodes, ref solutionOpenStartIDs);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static double2 GetUnitNormal(long2 pt1, long2 pt2)
        {
            double dx = (pt2.x - pt1.x);
            double dy = (pt2.y - pt1.y);
            if ((dx == 0) && (dy == 0))
                return new double2();

            double f  = 1.0 / math.sqrt(dx * dx + dy * dy);
            dx       *= f;
            dy       *= f;

            return new double2(dy, -dx);
        }

        public void Execute(ref NativeList<int2> solutionNodes,
                            ref NativeList<int>  solutionStartIDs,
                            ref NativeList<int2> solutionOpenNodes,
                            ref NativeList<int>  solutionOpenStartIDs)
        {
            Execute(1.0, ref solutionNodes, ref solutionStartIDs, ref solutionOpenNodes, ref solutionOpenStartIDs);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double2 TranslatePoint(double2 pt, double dx, double dy)
        {
            return new double2(pt.x + dx, pt.y + dy);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double2 ReflectPoint(double2 pt, double2 pivot)
        {
            return new double2(pivot.x + (pivot.x - pt.x), pivot.y + (pivot.y - pt.y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool AlmostZero(double value, double epsilon = 0.001)
        {
            return Math.Abs(value) < epsilon;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double Hypotenuse(double x, double y)
        {
            return Math.Sqrt(Math.Pow(x, 2) + Math.Pow(y, 2));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double2 NormalizeVector(double2 vec)
        {
            double h = Hypotenuse(vec.x, vec.y);
            if (AlmostZero(h))
                return new double2(0, 0);
            double inverseHypot = 1 / h;
            return new double2(vec.x * inverseHypot, vec.y * inverseHypot);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double2 GetAvgUnitVector(double2 vec1, double2 vec2)
        {
            return NormalizeVector(new double2(vec1.x + vec2.x, vec1.y + vec2.y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double2 IntersectPoint(double2 pt1a, double2 pt1b, double2 pt2a, double2 pt2b)
        {
            if (InternalClipper.IsAlmostZero(pt1a.x - pt1b.x))  //vertical
            {
                if (InternalClipper.IsAlmostZero(pt2a.x - pt2b.x))
                    return new double2(0, 0);
                double m2 = (pt2b.y - pt2a.y) / (pt2b.x - pt2a.x);
                double b2 = pt2a.y - m2 * pt2a.x;
                return new double2(pt1a.x, m2 * pt1a.x + b2);
            }

            if (InternalClipper.IsAlmostZero(pt2a.x - pt2b.x))  //vertical
            {
                double m1 = (pt1b.y - pt1a.y) / (pt1b.x - pt1a.x);
                double b1 = pt1a.y - m1 * pt1a.x;
                return new double2(pt2a.x, m1 * pt2a.x + b1);
            }
            else
            {
                double m1 = (pt1b.y - pt1a.y) / (pt1b.x - pt1a.x);
                double b1 = pt1a.y - m1 * pt1a.x;
                double m2 = (pt2b.y - pt2a.y) / (pt2b.x - pt2a.x);
                double b2 = pt2a.y - m2 * pt2a.x;
                if (InternalClipper.IsAlmostZero(m1 - m2))
                    return new double2(0, 0);
                double x = (b2 - b1) / (m1 - m2);
                return new double2(x, m1 * x + b1);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long2 GetPerpendic(long2 pt, double2 norm)
        {
            return new long2(pt.x + norm.x * _groupDelta,
                             pt.y + norm.y * _groupDelta);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private double2 GetPerpendicD(long2 pt, double2 norm)
        {
            return new double2(pt.x + norm.x * _groupDelta,
                               pt.y + norm.y * _groupDelta);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DoSquare(NativeList<int2> path, int j, int k)
        {
            double2 vec;
            if (j == k)
            {
                vec = new double2(_normals[j].y, -_normals[j].x);
            }
            else
            {
                vec = GetAvgUnitVector(
                    new double2(-_normals[k].y, _normals[k].x),
                    new double2(_normals[j].y, -_normals[j].x));
            }

            double absDelta = Math.Abs(_groupDelta);
            // now offset the original vertex delta units along unit vector
            double2 ptQ = new double2(path[j]);
            ptQ         = TranslatePoint(ptQ, absDelta * vec.x, absDelta * vec.y);

            // get perpendicular vertices
            double2 pt1 = TranslatePoint(ptQ, _groupDelta * vec.y, _groupDelta * -vec.x);
            double2 pt2 = TranslatePoint(ptQ, _groupDelta * -vec.y, _groupDelta * vec.x);
            // get 2 vertices along one edge offset
            double2 pt3 = GetPerpendicD(path[k], _normals[k]);

            if (j == k)
            {
                double2 pt4 = new double2(
                    pt3.x + vec.x * _groupDelta,
                    pt3.y + vec.y * _groupDelta);
                double2 pt = IntersectPoint(pt1, pt2, pt3, pt4);

                //get the second intersect point through reflecion
                outPath.Add(new long2(ReflectPoint(pt, ptQ)));
                outPath.Add(new long2(pt));
            }
            else
            {
                double2 pt4 = GetPerpendicD(path[j], _normals[k]);
                double2 pt  = IntersectPoint(pt1, pt2, pt3, pt4);

                outPath.Add(new long2(pt));
                //get the second intersect point through reflecion
                outPath.Add(new long2(ReflectPoint(pt, ptQ)));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DoMiter(NativeList<int2> path, int j, int k, double cosA)
        {
            double q = _groupDelta / (cosA + 1);

            outPath.Add(new long2(
                            path[j].x + (_normals[k].x + _normals[j].x) * q,
                            path[j].y + (_normals[k].y + _normals[j].y) * q));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DoRound(NativeList<int2> path, int j, int k, double angle)
        {
            long2   pt        = path[j];
            double2 offsetVec = new double2(_normals[k].x * _groupDelta, _normals[k].y * _groupDelta);
            if (j == k)
                offsetVec *= -1;

            outPath.Add(new long2(pt.x + offsetVec.x, pt.y + offsetVec.y));

            if (angle > -Math.PI + 0.01)  // avoid 180deg concave
            {
                int steps = (int)Math.Ceiling(_stepsPerRad * Math.Abs(angle));
                for (int i = 1; i < steps; i++)  // ie 1 less than steps
                {
                    offsetVec = new double2(offsetVec.x * _stepCos - _stepSin * offsetVec.y,
                                            offsetVec.x * _stepSin + offsetVec.y * _stepCos);

                    outPath.Add(new long2(pt.x + offsetVec.x, pt.y + offsetVec.y));
                }
            }
            outPath.Add(GetPerpendic(pt, _normals[j]));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BuildNormals(NativeList<int2> path)
        {
            int cnt = path.Length;
            _normals.Clear();
            _normals.Capacity = cnt;

            for (int i = 0; i < cnt - 1; i++)
                _normals.Add(GetUnitNormal(path[i], path[i + 1]));
            _normals.Add(GetUnitNormal(path[cnt - 1], path[0]));
        }

        private void OffsetPoint(NativeList<int2> path, int j, ref int k)
        {
            // Let A = change in angle where edges join
            // A == 0: ie no change in angle (flat join)
            // A == PI: edges 'spike'
            // sin(A) < 0: right turning
            // cos(A) < 0: change in angle is more than 90 degree
            double sinA = InternalClipper.CrossProduct(_normals[j], _normals[k]);
            double cosA = InternalClipper.DotProduct(_normals[j], _normals[k]);
            if (sinA > 1.0)
                sinA = 1.0;
            else if (sinA < -1.0)
                sinA = -1.0;

            if (math.abs(_groupDelta) < Tolerance)
            {
                outPath.Add(path[j]);
                return;
            }

            if (cosA > 0.999)
                DoMiter(path, j, k, cosA);
            else if (cosA > -0.99 && (sinA * _groupDelta < 0))
            {
                // is concave
                outPath.Add(GetPerpendic(path[j], _normals[k]));
                // this extra point is the only (simple) way to ensure that
                // path reversals are fully cleaned with the trailing clipper
                outPath.Add(path[j]);  // (#405)
                outPath.Add(GetPerpendic(path[j], _normals[j]));
            }
            else if (_joinType == JoinType.Miter)
            {
                // miter unless the angle is so acute the miter would exceeds ML
                if (cosA > _mitLimSqr - 1)
                    DoMiter(path, j, k, cosA);
                else
                    DoSquare(path, j, k);
            }
            else if (cosA > 0.99 || _joinType == JoinType.Square)
                //angle less than 8 degrees or a squared join
                DoSquare(path, j, k);
            else
                DoRound(path, j, k, Math.Atan2(sinA, cosA));
            k = j;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void OffsetPolygon(NativeList<int2> path)
        {
            int cnt = path.Length, prev = cnt - 1;
            for (int i = 0; i < cnt; i++)
                OffsetPoint(path, i, ref prev);
        }

        /// <summary>
        /// positive area = CCW, negative area = CW (works for closed and open polygon (identical result))
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double SignedArea(NativeList<int2> data, int start, int end)
        {
            double area = default;
            for (int i = start, prev = end - 1; i < end; prev = i++) //from (0, prev) until (end, prev)
                area += ((double)data[prev].x - (double)data[i].x) * ((double)data[i].y + (double)data[prev].y);
            return area * 0.5;
        }
        private void DoGroupOffset()
        {
            if (_endType == EndType.Polygon)
            {
                double area = SignedArea(inPath, 0, inPath.Length);
                //if (area == 0) return; // this is probably unhelpful (#430)
                pathsReversed = (area < 0);
                if (pathsReversed)
                    _groupDelta = -_delta;
                else
                    _groupDelta = _delta;
            }
            else
            {
                pathsReversed = false;
                _groupDelta   = math.abs(_delta) * 0.5;
            }
            double absDelta = Math.Abs(_groupDelta);

            if ((_joinType == JoinType.Round || _endType == EndType.Round))
            {
                // calculate a sensible number of steps (for 360 deg for the given offset
                // arcTol - when fArcTolerance is undefined (0), the amount of
                // curve imprecision that's allowed is based on the size of the
                // offset (delta). Obviously very large offsets will almost always
                // require much less precision. See also offset_triginometry2.svg
                double arcTol      = ArcTolerance > 0.01 ? ArcTolerance : absDelta * arc_const;
                double stepsPer360 = Math.PI / Math.Acos(1 - arcTol / absDelta);
                _stepSin           = Math.Sin((2 * Math.PI) / stepsPer360);
                _stepCos           = Math.Cos((2 * Math.PI) / stepsPer360);
                if (_groupDelta < 0.0)
                    _stepSin = -_stepSin;
                _stepsPerRad = stepsPer360 / (2 * Math.PI);
            }

            bool isJoined =
                (_endType == EndType.Joined) ||
                (_endType == EndType.Polygon);

            int cnt = inPath.Length;
            if ((cnt == 0) || ((cnt < 3) && (_endType == EndType.Polygon)))
                return;

            if (cnt == 2 && _endType == EndType.Joined)
            {
                if (_joinType == JoinType.Round)
                    _endType = EndType.Round;
                else
                    _endType = EndType.Square;
            }
            BuildNormals(inPath);
            if (_endType == EndType.Polygon)
                OffsetPolygon(inPath);
        }
    }
}  // namespace

