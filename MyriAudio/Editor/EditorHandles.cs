using UnityEditor;
using UnityEngine;

namespace Latios.Myri.Editor
{
    public static class EditorHandles
    {

        /// <summary>
        ///  Draws a cone handle that can be used to edit both the angle and range of a cone.
        /// </summary>
        /// <param name="position">
        ///  The position of the cone's apex.
        /// </param>
        /// <param name="rotation">
        ///  The rotation of the cone, defining its forward direction.
        /// </param>
        /// <param name="angle">
        ///  The initial angle of the cone in degrees.
        /// </param>
        /// <param name="range">
        ///  The initial range (length) of the cone.
        /// </param>
        /// <returns>
        /// A Vector2 where x is the modified angle in degrees and y is the modified range.
        /// </returns>
        public static Vector2 ConeHandle(Vector3 position, Quaternion rotation, float angle, float range)
        {
            
            Vector2 angleAndRange = new Vector2(angle, range);
            
            var forward = rotation * Vector3.forward;
            var up      = rotation * Vector3.up;
            var right   = rotation * Vector3.right;
            
            // Draw the cone lines
            for (int i = 0; i < 4; i++)
            {
                var deltaAngle = i * 90f;
                var rotAxis = Quaternion.AngleAxis(deltaAngle, forward);
                var dir = rotAxis * (Quaternion.AngleAxis(angle, up) * forward);
                Handles.DrawLine(position, position + dir * range);
            }

            bool rangeChanged = GUI.changed;
            GUI.changed = false;
            
            // Handle used for range editing
            var rangeHandlePos = position + forward * range;
            var rangeHandleSize = HandleUtility.GetHandleSize(rangeHandlePos) * .03f;
            var newRangeHandlePos = Handles.Slider(
                rangeHandlePos, forward, rangeHandleSize,
                Handles.DotHandleCap, 0f);
            
            if (GUI.changed)
            {
                range           = Vector3.Distance(position, newRangeHandlePos);
                angleAndRange.y = range;
            }
            
            GUI.changed |= rangeChanged;
            
            bool angleChanged = GUI.changed;
            GUI.changed = false;
            
            
            // Handles used for angle editing
            
            // Right-side angle handle
            var newAngle = AngleSlider(position, forward, up, angle, range);
            // Bottom-side angle handle
            newAngle = AngleSlider(position, forward, right, newAngle, range);
            // Left-side angle handle
            newAngle = AngleSlider(position, forward, -up, newAngle, range);
            // Top-side angle handle
            newAngle = AngleSlider(position, forward, -right, newAngle, range);


            if (GUI.changed)
            {
                angleAndRange.x = newAngle;
            }
            GUI.changed |= angleChanged;

            // Draw the circular arcs at the end of the cone
            var from = Quaternion.AngleAxis(-newAngle, up) * forward;
            Handles.DrawWireArc(position, up, from, newAngle*2f, range);
            from = Quaternion.AngleAxis(-newAngle, right) * forward;
            Handles.DrawWireArc(position, right, from, newAngle*2f, range);
        
            
            // Draw the circle at the end of the cone
            float forwardDistance = Mathf.Cos(Mathf.Deg2Rad * newAngle) * range;
            float radius = Mathf.Tan(Mathf.Deg2Rad * newAngle ) * forwardDistance;
            Handles.DrawWireDisc(position + forward * forwardDistance, forward, radius );
            
            return angleAndRange;
            
        }

        static float AngleSlider(Vector3 origin, Vector3 forward, Vector3 angleAxis, float angle, float range)
        {
            var angleDir = Quaternion.AngleAxis(angle, angleAxis) * forward;
            var handlePos = origin + angleDir * range;
            var handleSize = HandleUtility.GetHandleSize(handlePos) * 0.03f;
            
            bool changed = GUI.changed;
            GUI.changed = false;
            
            var slideDirection = Vector3.Cross(forward, angleAxis).normalized;
            
            var newHandlePos = Handles.Slider(
                handlePos, slideDirection, handleSize,
                Handles.DotHandleCap, 0f);
            
            var newAngle = angle;
            if (GUI.changed)
            {
                var dirFromOrigin = (newHandlePos - origin).normalized;
                var a = Vector3.SignedAngle(forward, dirFromOrigin, angleAxis);
                newAngle = Mathf.Clamp( Mathf.Abs(a), 0f, 179f);
            }
            
            GUI.changed |= changed;
            return newAngle;
        }
        
    }
}