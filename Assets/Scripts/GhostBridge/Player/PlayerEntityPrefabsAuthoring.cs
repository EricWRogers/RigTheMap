using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

public class PlayerEntityPrefabsAuthoring : MonoBehaviour
{
    [field: SerializeField] public GhostAuthoringComponent ClientInputEntityPrefab { get; private set; }
    [field: SerializeField] public GhostAuthoringComponent PlayerRifleEntityPrefab { get; private set; }
    [field: SerializeField] public GhostAuthoringComponent PlayerShotgunEntityPrefab { get; private set; }
    [field: SerializeField] public GhostAuthoringComponent PlayerSharkEntityPrefab { get; private set; }
    [field: SerializeField] public GhostAuthoringComponent PlayerHammerEntityPrefab { get; private set; }
    [field: SerializeField] public GhostAuthoringComponent PlayerBuildEntityPrefab { get; private set; }
}

public struct PlayerEntityPrefabs : IComponentData
{
    public Entity ClientInputEntityPrefab;
    public Entity PlayerRifleEntityPrefab;
    public Entity PlayerShotgunEntityPrefab;
    public Entity PlayerSharkEntityPrefab;
    public Entity PlayerHammerEntityPrefab;
    public Entity PlayerBuildEntityPrefab;
}

public class PlayerEntityPrefabsBaker : Baker<PlayerEntityPrefabsAuthoring>
{
    public override void Bake(PlayerEntityPrefabsAuthoring authoring)
    {
        if (authoring.ClientInputEntityPrefab == null)
        {
            return;
        }
        
        var playerPrefabsEntity = GetEntity(TransformUsageFlags.None);
        AddComponent(playerPrefabsEntity, new PlayerEntityPrefabs
        {
            ClientInputEntityPrefab = GetEntity(authoring.ClientInputEntityPrefab.gameObject, TransformUsageFlags.None),
            PlayerRifleEntityPrefab = 
                authoring.PlayerRifleEntityPrefab != null ?
                    GetEntity(authoring.PlayerRifleEntityPrefab.gameObject, TransformUsageFlags.None) 
                    : Entity.Null,
            PlayerShotgunEntityPrefab = 
                authoring.PlayerShotgunEntityPrefab !=null ? 
                    GetEntity(authoring.PlayerShotgunEntityPrefab.gameObject, TransformUsageFlags.None) 
                    : Entity.Null,

            PlayerSharkEntityPrefab = 
                authoring.PlayerSharkEntityPrefab !=null ? 
                    GetEntity(authoring.PlayerSharkEntityPrefab.gameObject, TransformUsageFlags.None) 
                    : Entity.Null,

            PlayerHammerEntityPrefab =
                authoring.PlayerHammerEntityPrefab != null
                    ? GetEntity(
                        authoring.PlayerHammerEntityPrefab.gameObject,
                        TransformUsageFlags.None)
                    : Entity.Null,

            PlayerBuildEntityPrefab =
                authoring.PlayerBuildEntityPrefab != null
                    ? GetEntity(
                        authoring.PlayerBuildEntityPrefab.gameObject,
                        TransformUsageFlags.None)
                    : Entity.Null
        });
    }
}
