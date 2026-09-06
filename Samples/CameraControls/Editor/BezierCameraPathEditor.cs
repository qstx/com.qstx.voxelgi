using UnityEditor;
using UnityEngine;

namespace QSTX.VoxelGI.Samples.EditorTools
{
    [CustomEditor(typeof(BezierCameraPath))]
    sealed class BezierCameraPathEditor : UnityEditor.Editor
    {
        SerializedProperty m_Point0;
        SerializedProperty m_Point1;
        SerializedProperty m_Point2;
        SerializedProperty m_Point3;

        void OnEnable()
        {
            m_Point0 = serializedObject.FindProperty("m_Point0");
            m_Point1 = serializedObject.FindProperty("m_Point1");
            m_Point2 = serializedObject.FindProperty("m_Point2");
            m_Point3 = serializedObject.FindProperty("m_Point3");
        }

        void OnSceneGUI()
        {
            var path = (BezierCameraPath)target;
            Transform pathTransform = path.transform;
            Vector3[] localPoints =
            {
                m_Point0.vector3Value,
                m_Point1.vector3Value,
                m_Point2.vector3Value,
                m_Point3.vector3Value
            };
            Handles.color = new Color(0.25f, 0.85f, 1f, 0.9f);
            for (int i = 0; i < localPoints.Length; i++)
            {
                Vector3 worldPoint = pathTransform.TransformPoint(localPoints[i]);
                float size = HandleUtility.GetHandleSize(worldPoint) * 0.08f;
                EditorGUI.BeginChangeCheck();
                Vector3 moved = Handles.PositionHandle(worldPoint, Quaternion.identity);
                if (!EditorGUI.EndChangeCheck())
                    continue;

                Undo.RecordObject(path, "Move Bezier Camera Control Point");
                SerializedProperty property = i switch
                {
                    0 => m_Point0,
                    1 => m_Point1,
                    2 => m_Point2,
                    _ => m_Point3
                };
                property.vector3Value = pathTransform.InverseTransformPoint(moved);
                serializedObject.ApplyModifiedProperties();
                Handles.SphereHandleCap(0, moved, Quaternion.identity, size, EventType.Repaint);
            }

            Handles.color = new Color(0.25f, 0.85f, 1f, 0.35f);
            Handles.DrawLine(pathTransform.TransformPoint(m_Point0.vector3Value),
                pathTransform.TransformPoint(m_Point1.vector3Value));
            Handles.DrawLine(pathTransform.TransformPoint(m_Point3.vector3Value),
                pathTransform.TransformPoint(m_Point2.vector3Value));
        }
    }
}
