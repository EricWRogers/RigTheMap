using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

namespace Unity.MP_FPS
{
    public class SpawnPointAuthoring : MonoBehaviour
    {
        [SerializeField] private int m_TeamId;

        public class Baker : Unity.Entities.Baker<SpawnPointAuthoring>
        {
            public override void Bake(SpawnPointAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, new SpawnPoint
                {
                    TeamId = authoring.m_TeamId
                });
                AddComponent<LocalToWorld>(entity);
            }
        }
    }

    /// <summary>
    /// Placed in the GameScene subscene, the SpawnPoint components are used by the <see cref="ServerGameSystem"/>
    /// to spawn player characters during a game session.
    /// </summary>
    public struct SpawnPoint : IComponentData
    {
        public int TeamId;
    }
}