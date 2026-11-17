using Unity.Entities;
using Unity.Netcode;
using UnityEngine;

public class PlayerEntityPrefabsAuthoring : MonoBehaviour
{
    [field: SerializeField] public GhostAuthoringComponent ClientInputEntityPrefab { get; private set; }
    // [field: SerializeField] public GhostAuthoringComponent ClientSpectateEntityPrefab { get; private set; }
    [field: SerializeField] public GhostAuthoringComponent PlayerRifleEntityPrefab { get; private set; }
    [field: SerializeField] public GhostAuthoringComponent PlayerRifleEntityPrefabB { get; private set; }
    [field: SerializeField] public GhostAuthoringComponent PlayerRifleEntityPrefabG { get; private set; }
    [field: SerializeField] public GhostAuthoringComponent PlayerShotgunEntityPrefab { get; private set; }
    [field: SerializeField] public GhostAuthoringComponent PlayerShotgunEntityPrefabB { get; private set; }
    [field: SerializeField] public GhostAuthoringComponent PlayerShotgunEntityPrefabG { get; private set; }
    [field: SerializeField] public GhostAuthoringComponent PlayerSharkEntityPrefab { get; private set; }
    [field: SerializeField] public GhostAuthoringComponent PlayerSharkEntityPrefabB { get; private set; }
    [field: SerializeField] public GhostAuthoringComponent PlayerSharkEntityPrefabG { get; private set; }
    [field: SerializeField] public GhostAuthoringComponent PlayerHammerEntityPrefab { get; private set; }
    [field: SerializeField] public GhostAuthoringComponent PlayerHammerEntityPrefabB { get; private set; }
    [field: SerializeField] public GhostAuthoringComponent PlayerHammerEntityPrefabG { get; private set; }
    [field: SerializeField] public GhostAuthoringComponent PlayerSixShooterEntityPrefab { get; private set; }
    [field: SerializeField] public GhostAuthoringComponent PlayerSixShooterEntityPrefabB { get; private set; }
    [field: SerializeField] public GhostAuthoringComponent PlayerSixShooterEntityPrefabG { get; private set; }
    [field: SerializeField] public GhostAuthoringComponent PlayerBuildEntityPrefab { get; private set; }
    [field: SerializeField] public GhostAuthoringComponent PlayerBuildEntityPrefabB { get; private set; }
    [field: SerializeField] public GhostAuthoringComponent PlayerBuildEntityPrefabG { get; private set; }
    // [field: SerializeField] public GhostAuthoringComponent PlayerSpectateEntityPrefab { get; private set; }
}

public struct PlayerEntityPrefabs : IComponentData
{
    public Entity ClientInputEntityPrefab;
    public Entity ClientSpectateEntityPrefab;
    public Entity PlayerRifleEntityPrefab;
    public Entity PlayerRifleEntityPrefabB;
    public Entity PlayerRifleEntityPrefabG;
    public Entity PlayerShotgunEntityPrefab;
    public Entity PlayerShotgunEntityPrefabB;
    public Entity PlayerShotgunEntityPrefabG;
    public Entity PlayerSharkEntityPrefab;
    public Entity PlayerSharkEntityPrefabB;
    public Entity PlayerSharkEntityPrefabG;
    public Entity PlayerHammerEntityPrefab;
    public Entity PlayerHammerEntityPrefabB;
    public Entity PlayerHammerEntityPrefabG;
    public Entity PlayerSixShooterEntityPrefab;
    public Entity PlayerSixShooterEntityPrefabB;
    public Entity PlayerSixShooterEntityPrefabG;
    public Entity PlayerBuildEntityPrefab;
    public Entity PlayerBuildEntityPrefabB;
    public Entity PlayerBuildEntityPrefabG;
    public Entity PlayerSpectateEntityPrefab;
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
            PlayerRifleEntityPrefabB =
                authoring.PlayerRifleEntityPrefabB != null ?
                    GetEntity(authoring.PlayerRifleEntityPrefabB.gameObject, TransformUsageFlags.None)
                    : Entity.Null,
            PlayerRifleEntityPrefabG =
                authoring.PlayerRifleEntityPrefabG != null ?
                    GetEntity(authoring.PlayerRifleEntityPrefabG.gameObject, TransformUsageFlags.None)
                    : Entity.Null,

            // ClientSpectateEntityPrefab = GetEntity(authoring.ClientSpectateEntityPrefab.gameObject, TransformUsageFlags.None),

            PlayerShotgunEntityPrefab = 
                authoring.PlayerShotgunEntityPrefab !=null ? 
                    GetEntity(authoring.PlayerShotgunEntityPrefab.gameObject, TransformUsageFlags.None) 
                    : Entity.Null,
            PlayerShotgunEntityPrefabB =
                authoring.PlayerShotgunEntityPrefabB != null ?
                    GetEntity(authoring.PlayerShotgunEntityPrefabB.gameObject, TransformUsageFlags.None)
                    : Entity.Null,
            PlayerShotgunEntityPrefabG =
                authoring.PlayerShotgunEntityPrefabG != null ?
                    GetEntity(authoring.PlayerShotgunEntityPrefabG.gameObject, TransformUsageFlags.None)
                    : Entity.Null,

            PlayerSharkEntityPrefab = 
                authoring.PlayerSharkEntityPrefab !=null ? 
                    GetEntity(authoring.PlayerSharkEntityPrefab.gameObject, TransformUsageFlags.None) 
                    : Entity.Null,
            PlayerSharkEntityPrefabB =
                authoring.PlayerSharkEntityPrefabB != null ?
                    GetEntity(authoring.PlayerSharkEntityPrefabB.gameObject, TransformUsageFlags.None)
                    : Entity.Null,
            PlayerSharkEntityPrefabG =
                authoring.PlayerSharkEntityPrefabG != null ?
                    GetEntity(authoring.PlayerSharkEntityPrefabG.gameObject, TransformUsageFlags.None)
                    : Entity.Null,

            PlayerHammerEntityPrefab =
                authoring.PlayerHammerEntityPrefab != null
                    ? GetEntity(
                        authoring.PlayerHammerEntityPrefab.gameObject,
                        TransformUsageFlags.None)
                    : Entity.Null,
            PlayerHammerEntityPrefabB =
                authoring.PlayerHammerEntityPrefabB != null
                    ? GetEntity(
                        authoring.PlayerHammerEntityPrefabB.gameObject,
                        TransformUsageFlags.None)
                    : Entity.Null,
            PlayerHammerEntityPrefabG =
                authoring.PlayerHammerEntityPrefabG != null
                    ? GetEntity(
                        authoring.PlayerHammerEntityPrefabG.gameObject,
                        TransformUsageFlags.None)
                    : Entity.Null,

            PlayerSixShooterEntityPrefab =
                authoring.PlayerSixShooterEntityPrefab != null
                    ? GetEntity(
                        authoring.PlayerSixShooterEntityPrefab.gameObject,
                        TransformUsageFlags.None)
                    : Entity.Null,
            PlayerSixShooterEntityPrefabB =
                authoring.PlayerSixShooterEntityPrefabB != null
                    ? GetEntity(
                        authoring.PlayerSixShooterEntityPrefabB.gameObject,
                        TransformUsageFlags.None)
                    : Entity.Null,
            PlayerSixShooterEntityPrefabG =
                authoring.PlayerSixShooterEntityPrefabG != null
                    ? GetEntity(
                        authoring.PlayerSixShooterEntityPrefabG.gameObject,
                        TransformUsageFlags.None)
                    : Entity.Null,

            PlayerBuildEntityPrefab =
                authoring.PlayerBuildEntityPrefab != null
                    ? GetEntity(
                        authoring.PlayerBuildEntityPrefab.gameObject,
                        TransformUsageFlags.None)
                    : Entity.Null,
            PlayerBuildEntityPrefabB =
                authoring.PlayerBuildEntityPrefabB != null
                    ? GetEntity(
                        authoring.PlayerBuildEntityPrefabB.gameObject,
                        TransformUsageFlags.None)
                    : Entity.Null,
            PlayerBuildEntityPrefabG =
                authoring.PlayerBuildEntityPrefabG != null
                    ? GetEntity(
                        authoring.PlayerBuildEntityPrefabG.gameObject,
                        TransformUsageFlags.None)
                    : Entity.Null

            // PlayerSpectateEntityPrefab =
            //     authoring.PlayerSpectateEntityPrefab != null
            //         ? GetEntity(
            //             authoring.PlayerSpectateEntityPrefab.gameObject,
            //             TransformUsageFlags.None)
            //         : Entity.Null
        });
    }
}
