using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Unity.MP_FPS
{
    public class HitCubeLifetime : GhostMonoBehaviour
    {
        public struct HitCubeDimensions : IComponentData
        {
            public float3 Value;
        }

        private Vector3 m_Dimensions;

        private void Awake()
        {
            Destroy(gameObject, 0.5f);
        }

        public override void OnGhostLinked()
        {
            if (TryReadGhostComponentData<HitCubeDimensions>(out var dimensions))
            {
                m_Dimensions = new Vector3(
                    dimensions.Value.x,
                    dimensions.Value.y,
                    dimensions.Value.z);
                transform.localScale = m_Dimensions;
            }
        }

        private void LateUpdate()
        {
            if (m_Dimensions != Vector3.zero)
                transform.localScale = m_Dimensions;
        }
    }
}
