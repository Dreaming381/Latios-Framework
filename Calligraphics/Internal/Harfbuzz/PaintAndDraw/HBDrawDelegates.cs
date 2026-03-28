using AOT;
using System;
using Unity.Burst;

namespace Latios.Calligraphics.HarfBuzz
{
    [BurstCompile]
    internal struct DrawDelegates : IDisposable
    {
        public IntPtr ptr;
        public DrawDelegates(bool dummyProperty)
        {
            ptr = Harfbuzz.hb_draw_funcs_create();
            FunctionPointer<MoveToDelegate> moveToFunctionPointer = BurstCompiler.CompileFunctionPointer<MoveToDelegate>(HB_draw_move_to_func_t);
            FunctionPointer<MoveToDelegate> lineToFunctionPointer = BurstCompiler.CompileFunctionPointer<MoveToDelegate>(HB_draw_move_to_func_t);
            FunctionPointer<QuadraticToDelegate> quadraticToFunctionPointer = BurstCompiler.CompileFunctionPointer<QuadraticToDelegate>(HB_draw_quadratic_to_func_t);
            FunctionPointer<CubicToDelegate> cubicToFunctionPointer = BurstCompiler.CompileFunctionPointer<CubicToDelegate>(HB_draw_cubic_to_func_t);
            FunctionPointer<CloseDelegate> closeFunctionPointer = BurstCompiler.CompileFunctionPointer<CloseDelegate>(HB_draw_close_path_func_t);
            FunctionPointer<ReleaseDelegate> releaseFunctionPointer = BurstCompiler.CompileFunctionPointer<ReleaseDelegate>(Test);

            Harfbuzz.hb_draw_funcs_set_move_to_func(ptr, moveToFunctionPointer, IntPtr.Zero, releaseFunctionPointer);
            Harfbuzz.hb_draw_funcs_set_line_to_func(ptr, lineToFunctionPointer, IntPtr.Zero, releaseFunctionPointer);
            Harfbuzz.hb_draw_funcs_set_quadratic_to_func(ptr, quadraticToFunctionPointer, IntPtr.Zero, releaseFunctionPointer);
            Harfbuzz.hb_draw_funcs_set_cubic_to_func(ptr, cubicToFunctionPointer, IntPtr.Zero, releaseFunctionPointer);
            Harfbuzz.hb_draw_funcs_set_close_path_func(ptr, closeFunctionPointer, IntPtr.Zero, releaseFunctionPointer);

            Harfbuzz.hb_draw_funcs_make_immutable(ptr);
        }

        public void Dispose()
        {
            Harfbuzz.hb_draw_funcs_destroy(ptr);
        }
        [BurstCompile]
        [MonoPInvokeCallback(typeof(ReleaseDelegate))]
        public static void Test()
        {
            //Debug.Log($"harfbuzz blob called this delegate upon destroying blob ");
        }
        [BurstCompile]
        [MonoPInvokeCallback(typeof(CloseDelegate))]
        public static void HB_draw_close_path_func_t(IntPtr dfuncs, ref DrawData data, ref DrawState st, IntPtr user_data)
        {
            //Debug.Log($"Close Path");
            data.contourIDs.Add(data.edges.Length);
        }
        [BurstCompile]
        [MonoPInvokeCallback(typeof(MoveToDelegate))]
        public static void HB_draw_move_to_func_t(IntPtr dfuncs, ref DrawData data, ref DrawState st, float to_x, float to_y, IntPtr user_data)
        {
            //Debug.Log($"Move from {st.current_x},{st.current_y}  to {to_x}, {to_y} (DrawState: path_open {st.path_open} {st.path_start_x} {st.path_start_y}");
            if (st.PathOpen)//if path is open, a moveto operation is an implicit lineto opeation https://learn.microsoft.com/en-us/typography/opentype/spec/cff2
            {
                var edge = new SDFEdge(st.current_x, st.current_y, to_x, to_y);
                var edgeBBox = BezierMath.GetLineBBox(edge.start_pos, edge.end_pos);
                data.edges.Add(edge);
                data.glyphRect = BBox.Union(data.glyphRect, edgeBBox);
            }
        }
        [BurstCompile]
        [MonoPInvokeCallback(typeof(MoveToDelegate))]
        public static void HB_draw_line_to_func_t(IntPtr dfuncs, ref DrawData data, ref DrawState st, float to_x, float to_y, IntPtr user_data)
        {
            //Debug.Log($"Line from {st.current_x},{st.current_y}  to {to_x}, {to_y}");
            var edge = new SDFEdge(st.current_x, st.current_y, to_x, to_y);
            var edgeBBox = BezierMath.GetLineBBox(edge.start_pos, edge.end_pos);
            data.edges.Add(edge);
            data.glyphRect = BBox.Union(data.glyphRect, edgeBBox);
        }
        [BurstCompile]
        [MonoPInvokeCallback(typeof(QuadraticToDelegate))]
        public static void HB_draw_quadratic_to_func_t(IntPtr dfuncs, ref DrawData data, ref DrawState st, float control_x, float control_y, float to_x, float to_y, IntPtr user_data)
        {
            //Debug.Log($"Quadratic from {st.current_x},{st.current_y}  to {to_x}, {to_y}");
            var edge = new SDFEdge(st.current_x, st.current_y, control_x, control_y, to_x, to_y);
            var edgeBBox = BezierMath.GetQuadraticBezierBBox(edge.start_pos, edge.control1, edge.end_pos);
            //data.edges.Add(edge);
            BezierMath.SplitQuadraticEdge(data.edges, ref edge);
            data.glyphRect = BBox.Union(data.glyphRect, edgeBBox);
        }
        [BurstCompile]
        [MonoPInvokeCallback(typeof(CubicToDelegate))]
        public static void HB_draw_cubic_to_func_t(IntPtr dfuncs, ref DrawData data, ref DrawState st, float control1_x, float control1_y, float control2_x, float control2_y, float to_x, float to_y, IntPtr user_data)
        {
            //Debug.Log($"Cubic from {st.current_x},{st.current_y}  to {to_x}, {to_y}");
            var edge = new SDFEdge(st.current_x, st.current_y, control1_x, control1_y, control2_x, control2_y, to_x, to_y);
            var edgeBBox = BezierMath.GetCubicBezierBBox(edge.start_pos, edge.control1, edge.control1, edge.end_pos);
            //data.edges.Add(edge);
            BezierMath.SplitCubicEdge(data.edges, ref edge);
            data.glyphRect = BBox.Union(data.glyphRect, edgeBBox);
        }


        public delegate void ReleaseDelegate();
  
        public delegate void MoveToDelegate(IntPtr dfuncs, ref DrawData data, ref DrawState st, float to_x, float to_y, IntPtr user_data);

        public delegate void QuadraticToDelegate(IntPtr dfuncs, ref DrawData data, ref DrawState st, float control_x, float control_y, float to_x, float to_y, IntPtr user_data);

        public delegate void CubicToDelegate(IntPtr dfuncs, ref DrawData data, ref DrawState st, float control1_x, float control1_y, float control2_x, float control2_y, float to_x, float to_y, IntPtr user_data);
        
        public delegate void CloseDelegate(IntPtr dfuncs, ref DrawData data, ref DrawState st, IntPtr user_data);
    }
}
