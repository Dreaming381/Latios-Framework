using System;
using Latios.Myri.Authoring;
using UnityEditor;
using UnityEngine;

namespace Latios.Myri.Editor
{
    [CustomEditor(typeof(AudioSourceAuthoring))]
    public class AudioSourceAuthoringEditor : UnityEditor.Editor
    {
        static readonly Color InnerColor = new(.2f, 0.6f, 1f, .8f);
        static readonly Color OuterColor = new(.5f, 0.85f, 1f, .8f);
        static readonly float MaxAngle   = 89.5f; // Cone's outer and inner angles are half-angles.
        
        void OnSceneGUI()
        {
            
            var t = target as AudioSourceAuthoring;
            if (!t) return;
            
            if (!t.useFalloff)
                return;

            var tr = t.transform;

            if (!t.useCone)
            {
                
                EditorGUI.BeginChangeCheck();
                Handles.color = InnerColor;
                
                var innerRange = Handles.RadiusHandle(tr.rotation, tr.position, t.innerRange);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(target, "Edit Myri Audio Source Inner Range");

                    if (t.outerRange < innerRange)
                        t.outerRange = innerRange;

                    t.innerRange = innerRange;
                }
                
                EditorGUI.BeginChangeCheck();
                Handles.color = OuterColor;
                
                var outerRange = Handles.RadiusHandle(tr.rotation, tr.position, t.outerRange);
                if (EditorGUI.EndChangeCheck())
                {
                    if (outerRange < t.innerRange)
                        outerRange = t.innerRange;

                    Undo.RecordObject(target, "Edit Myri Audio Source Outer Range");
                    t.outerRange = outerRange;
                }
                
            }
            else
            {
                // Inner Cone
                
                EditorGUI.BeginChangeCheck();
                Handles.color = InnerColor;
                
                var angleAndRange = EditorHandles.ConeHandle(tr.position, tr.rotation,  t.innerAngle, t.innerRange);

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(target, "Edit Myri Audio Source Inner Angle and Range");
                
                    var angle = angleAndRange.x;
                    var range = angleAndRange.y;
                    angle = Mathf.Clamp(angle, 0f, MaxAngle);
                    
                    if (angle > t.outerAngle)
                        t.outerAngle = angle;
                    
                    if (range > t.outerRange)
                        t.outerRange = range;
                    
                    t.innerAngle = angle;
                    t.innerRange = range;
                }
                
                // Outer Cone

                EditorGUI.BeginChangeCheck();
                Handles.color = OuterColor;
                
                var outerAngleAndRange = EditorHandles.ConeHandle(tr.position, tr.rotation,  t.outerAngle, t.outerRange);
                
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(target, "Edit Myri Audio Source Outer Angle and Range");
                
                    var angle = outerAngleAndRange.x;
                    var range = outerAngleAndRange.y;
                    
                    angle = Mathf.Clamp(angle, t.innerAngle, MaxAngle);
                    range = Mathf.Clamp(range, t.innerRange, float.MaxValue);
                    
                    t.outerAngle = angle;
                    t.outerRange = range;
                }
                
            }
            
        }
    }
}