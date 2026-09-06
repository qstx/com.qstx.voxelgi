using UnityEngine;

namespace QSTX.VoxelGI.Samples
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class BezierCameraPath : MonoBehaviour
    {
        [Header("控制点（局部空间）")]
        [SerializeField] Vector3 m_Point0 = new Vector3(0f, 3f, -8f);
        [SerializeField] Vector3 m_Point1 = new Vector3(3f, 5f, -2f);
        [SerializeField] Vector3 m_Point2 = new Vector3(-3f, 5f, 4f);
        [SerializeField] Vector3 m_Point3 = new Vector3(0f, 3f, 10f);

        [SerializeField] bool m_ClosedLoop = true;

        [Header("编辑显示")]
        [SerializeField, Min(0.01f)] float m_GizmoStep = 0.05f;
        [SerializeField] Color m_GizmoColor = new Color(0.2f, 0.8f, 1f, 1f);

        public Vector3 Point0 => m_Point0;
        public Vector3 Point1 => m_Point1;
        public Vector3 Point2 => m_Point2;
        public Vector3 Point3 => m_Point3;

        public Vector3 Evaluate(float normalizedTime)
        {
            GetSegment(normalizedTime, out Vector3 p0, out Vector3 p1, out Vector3 p2, out Vector3 p3,
                out float t);
            float inverse = 1f - t;
            return transform.TransformPoint(
                inverse * inverse * inverse * p0 +
                3f * inverse * inverse * t * p1 +
                3f * inverse * t * t * p2 +
                t * t * t * p3);
        }

        public Vector3 EvaluateTangent(float normalizedTime)
        {
            GetSegment(normalizedTime, out Vector3 p0, out Vector3 p1, out Vector3 p2, out Vector3 p3,
                out float t);
            float inverse = 1f - t;
            Vector3 tangent =
                3f * inverse * inverse * (p1 - p0) +
                6f * inverse * t * (p2 - p1) +
                3f * t * t * (p3 - p2);
            return transform.TransformDirection(tangent);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = m_GizmoColor;
            Vector3 previous = Evaluate(0f);
            int steps = Mathf.Max(8, Mathf.CeilToInt(4f / Mathf.Max(m_GizmoStep, 0.01f)));
            for (int i = 1; i <= steps; i++)
            {
                Vector3 current = Evaluate(i / (float)steps);
                Gizmos.DrawLine(previous, current);
                previous = current;
            }

            Gizmos.color = new Color(m_GizmoColor.r, m_GizmoColor.g, m_GizmoColor.b, 0.45f);
            DrawControlPoint(m_Point0);
            DrawControlPoint(m_Point1);
            DrawControlPoint(m_Point2);
            DrawControlPoint(m_Point3);
        }

        void DrawControlPoint(Vector3 point)
        {
            Vector3 worldPoint = transform.TransformPoint(point);
            Gizmos.DrawSphere(worldPoint, 0.12f);
        }

        void GetSegment(float normalizedTime, out Vector3 p0, out Vector3 p1, out Vector3 p2,
            out Vector3 p3, out float segmentTime)
        {
            Vector3[] anchors = { m_Point0, m_Point1, m_Point2, m_Point3 };
            float wrapped = m_ClosedLoop ? Mathf.Repeat(normalizedTime, 1f) : Mathf.Clamp01(normalizedTime);
            float scaled = wrapped * (m_ClosedLoop ? anchors.Length : 1f);
            int segment = m_ClosedLoop
                ? Mathf.Min(Mathf.FloorToInt(scaled), anchors.Length - 1)
                : 0;
            segmentTime = m_ClosedLoop ? scaled - segment : scaled;

            int next = m_ClosedLoop ? (segment + 1) % anchors.Length : 3;
            int previous = m_ClosedLoop ? (segment - 1 + anchors.Length) % anchors.Length : 0;
            int nextNext = m_ClosedLoop ? (segment + 2) % anchors.Length : 3;
            p0 = anchors[segment];
            p3 = anchors[next];
            p1 = p0 + (anchors[next] - anchors[previous]) / 6f;
            p2 = p3 - (anchors[nextNext] - p0) / 6f;
        }
    }
}
