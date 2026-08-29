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
