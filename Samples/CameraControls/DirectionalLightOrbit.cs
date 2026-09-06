using UnityEngine;

namespace QSTX.VoxelGI.Samples
{
    [DisallowMultipleComponent]
    public sealed class DirectionalLightOrbit : MonoBehaviour
    {
        [SerializeField, Tooltip("方向光绕世界 Y 轴旋转的角速度，单位为度/秒。")]
        float m_Speed = 10f;

        [SerializeField, Tooltip("是否在运行时启用方向光旋转。")]
        bool m_PlayOnStart = true;

        public float Speed
        {
            get => m_Speed;
            set => m_Speed = value;
        }

        void Update()
        {
            if (m_PlayOnStart)
                transform.Rotate(Vector3.up, m_Speed * Time.deltaTime, Space.World);
        }
    }
}
