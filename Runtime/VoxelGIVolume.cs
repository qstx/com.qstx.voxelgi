using UnityEngine;
using UnityEngine.Rendering;

namespace QSTX.VoxelGI
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    [AddComponentMenu("Rendering/QSTX/Voxel GI Volume")]
    public sealed class VoxelGIVolume : Volume
    {
        [SerializeField, Tooltip("A BoxCollider on another GameObject that defines the world-space voxel grid.")]
        BoxCollider m_VoxelizationBounds;

        int m_UpdateVersion;

        public BoxCollider VoxelizationBounds => m_VoxelizationBounds;
        internal int UpdateVersion => m_UpdateVersion;

        void Reset()
        {
            // Volume 本身只负责定义相机影响区域；体素网格范围由独立的 BoxCollider 提供。
            isGlobal = false;
            if (TryGetComponent<BoxCollider>(out var influenceCollider))
                influenceCollider.isTrigger = true;
            UpdateColliders();
            RequestVoxelizationUpdate();
        }

        void OnValidate() => RequestVoxelizationUpdate();

        public void RequestVoxelizationUpdate()
        {
            // 递增版本号通知每个相机上下文：Manual 更新模式需要重新执行体素化。
            unchecked { m_UpdateVersion++; }
        }

        public bool TryGetVoxelGridBounds(out Bounds bounds)
        {
            // 将外部 BoxCollider 转换为包围整个体素网格的立方体，保证三个轴使用统一体素尺寸。
            if (m_VoxelizationBounds == null ||
                m_VoxelizationBounds.gameObject == gameObject ||
                !m_VoxelizationBounds.enabled ||
                !m_VoxelizationBounds.gameObject.activeInHierarchy)
            {
                bounds = default;
                return false;
            }

            Bounds source = m_VoxelizationBounds.bounds;
            float side = Mathf.Max(source.size.x, source.size.y, source.size.z);
            if (side <= Mathf.Epsilon)
            {
                bounds = default;
                return false;
            }

            bounds = new Bounds(source.center, Vector3.one * side);
            return true;
        }

        internal static bool TryGetActive(Camera camera, out VoxelGIVolume volume, out Bounds bounds)
        {
            // 在相机进入局部 Volume 的影响范围后，按优先级和距离选择当前生效的 VoxelGI 配置。
            volume = null;
            bounds = default;
            float bestPriority = float.NegativeInfinity;
            float bestDistance = float.PositiveInfinity;

            var volumes = Object.FindObjectsByType<VoxelGIVolume>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (VoxelGIVolume candidate in volumes)
            {
                if (candidate == null || !candidate.isActiveAndEnabled || candidate.weight <= 0f ||
                    !candidate.TryGetVoxelGridBounds(out Bounds candidateBounds))
                    continue;

                float distance = 0f;
                if (!candidate.isGlobal)
                {
                    if (camera == null || !candidate.TryGetDistance(camera.transform.position, out distance) ||
                        distance > candidate.blendDistance)
                        continue;
                }

                if (candidate.priority < bestPriority ||
                    (Mathf.Approximately(candidate.priority, bestPriority) && distance >= bestDistance))
                    continue;

                volume = candidate;
                bounds = candidateBounds;
                bestPriority = candidate.priority;
                bestDistance = distance;
            }

            return volume != null;
        }

        bool TryGetDistance(Vector3 position, out float distance)
        {
            // 使用 Volume 自身 Collider 的 ClosestPoint 计算相机到影响区域的距离。
            UpdateColliders();
            distance = float.PositiveInfinity;
            bool found = false;
            foreach (Collider volumeCollider in colliders)
            {
                if (volumeCollider == null || !volumeCollider.enabled)
                    continue;

                found = true;
                distance = Mathf.Min(distance, Vector3.Distance(position, volumeCollider.ClosestPoint(position)));
            }
            return found;
        }

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            if (!TryGetVoxelGridBounds(out Bounds gridBounds))
                return;

            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(gridBounds.center, gridBounds.size);
            if (m_VoxelizationBounds != null)
            {
                Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.65f);
                Gizmos.DrawWireCube(m_VoxelizationBounds.bounds.center, m_VoxelizationBounds.bounds.size);
            }
        }
#endif
    }
}
