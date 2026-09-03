using System.Collections.Generic;
using UnityEngine;

namespace QSTX.VoxelGI
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Rendering/QSTX/Voxel GI Contributor")]
    public sealed class VoxelGIContributor : MonoBehaviour
    {
        [SerializeField] bool m_IncludeChildren = true;
        [SerializeField] bool m_ContributeSurface = true;
        [SerializeField] bool m_CastVoxelShadow = true;

        readonly List<Renderer> m_Renderers = new List<Renderer>();

        public bool ContributeSurface => m_ContributeSurface;
        public bool CastVoxelShadow => m_CastVoxelShadow;

        void OnEnable() => RefreshRegistration();

        void OnDisable()
        {
            // 组件禁用时注销其 Renderer，避免已失效对象继续参与体素化或阴影绘制。
            foreach (Renderer renderer in m_Renderers)
                VoxelGIRendererRegistry.Unregister(renderer);
            m_Renderers.Clear();
        }

        void OnValidate()
        {
            if (isActiveAndEnabled)
                RefreshRegistration();
        }

        public void RefreshRegistration()
        {
            // 先移除旧注册，再按当前层级和开关重新收集 Renderer，确保 Inspector 修改立即生效。
            foreach (Renderer renderer in m_Renderers)
                VoxelGIRendererRegistry.Unregister(renderer);
            m_Renderers.Clear();

            if (m_IncludeChildren)
                GetComponentsInChildren(true, m_Renderers);
            else if (TryGetComponent<Renderer>(out Renderer renderer))
                m_Renderers.Add(renderer);

            foreach (Renderer item in m_Renderers)
                VoxelGIRendererRegistry.Register(item, m_ContributeSurface, m_CastVoxelShadow);
        }
    }
}
