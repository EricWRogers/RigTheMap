using System.Collections.Generic;
using Gameplay.Leaderboard;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Unity.MP_FPS
{
    public class PlayerControllerLink : IComponentData
    {
        public FirstPersonController Controller;
    }

    struct ProjectileSpawnData
    {
        public Entity Prefab;
        public float3 Position;
        public quaternion Rotation;
        public int OwnerNetworkId;
        public uint SpawnTick;
        public uint WeaponId;
    }

    struct VfxSpawnData
    {
        public Entity Prefab;
        public float3 Position;
        public quaternion Rotation;
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
    public partial class ServerPlayerMovementSystem : SingletonSystem<ServerPlayerMovementSystem>
    {
        private const int k_NumHistoryTicks = 20;
        private const int k_BuildItemsPerPlayer = 3;

        private static readonly int s_ShootableLayerMask =
            LayerMask.GetMask("Ground", "Default");

        private static readonly int s_HitscanLayerMask =
            LayerMask.GetMask("ServerPlayer", "Default", "Ground");

        private NativeList<ClientCommandInput> m_ProcessedClientInputCommands;
        private ComponentLookup<PredictedClientInput> m_PredictedClientInputComponentLookup;
        private ComponentLookup<PredictedPlayerGhost> m_PlayerGhostLookup;
        private ComponentLookup<PlayerTeam> m_PlayerTeamLookup;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init()
        {
            s_PlayerMovementActive = false;
            s_PlayerMovementTick = 0;
        }

        private static bool s_PlayerMovementActive = false;
        private static uint s_PlayerMovementTick;

        public static bool PlayerMovementActive => s_PlayerMovementActive;
        public static uint PlayerMovementTick => s_PlayerMovementTick;

        private ComponentLookup<GhostOwner> ghostOwnerLookup;

        public PlayerMovementHistory MovementHistory { get; private set; } =
            new PlayerMovementHistory(k_NumHistoryTicks);

        private bool m_WasBuildMode = false;

        private HashSet<Entity> m_HammerLaunchers = new HashSet<Entity>();

        protected override void OnCreate()
        {
            base.OnCreate();

            RequireForUpdate<NetworkStreamInGame>();
            RequireForUpdate<NetworkTime>();

            m_PredictedClientInputComponentLookup =
                GetComponentLookup<PredictedClientInput>(true);

            m_PlayerGhostLookup =
                GetComponentLookup<PredictedPlayerGhost>();

            m_PlayerTeamLookup =
                GetComponentLookup<PlayerTeam>(true);

            const int maxLocalPlayers = 4; //PlayerGhostManager.k_MaxLocalPlayers

            m_ProcessedClientInputCommands =
                new NativeList<ClientCommandInput>(
                    maxLocalPlayers * 32,
                    Allocator.Persistent);

            ghostOwnerLookup =
                GetComponentLookup<GhostOwner>(true);
        }

        protected override void OnDestroy()
        {
            m_ProcessedClientInputCommands.Dispose();

            var query =
                GetEntityQuery(typeof(PlayerControllerLink));

            foreach (var entity in
                     query.ToEntityArray(Allocator.Temp))
            {
                var controllerLink =
                    SystemAPI.ManagedAPI.GetComponent<PlayerControllerLink>(
                        entity);

                controllerLink.Controller = null;

                EntityManager.SetComponentData(
                    entity,
                    controllerLink);
            }

            base.OnDestroy();
        }

        private void KillPlayersOnModeChange()
        {
            foreach (var predictedPlayer in
                     SystemAPI.Query<RefRW<PredictedPlayerGhost>>()
                         .WithAll<Simulate>())
            {
                predictedPlayer.ValueRW.CurrentHealth = 0;
            }
        }

        private void KillPlayersBelowY(float killYThreshold = -50f)
        {
            foreach (var (predictedPlayer, transform) in
                     SystemAPI.Query<
                         RefRW<PredictedPlayerGhost>,
                         RefRO<LocalTransform>>()
                         .WithAll<Simulate>())
            {
                if (transform.ValueRO.Position.y < killYThreshold)
                {
                    predictedPlayer.ValueRW.CurrentHealth = 0;
                }
            }
        }

        private void GiveRandomBuildItems(
            ref PredictedPlayerGhost predictedPlayer,
            int networkId)
        {
            if (WeaponManager.Instance == null ||
                WeaponManager.Instance.WeaponRegistry == null)
            {
                return;
            }

            var buildGunData =
                WeaponManager.Instance.WeaponRegistry.GetWeaponData(4);

                Debug.Log(
                    $"[BUILD DEBUG] Weapon ID 2 = " +
                    $"{buildGunData?.WeaponName}");

                Debug.Log(
                    $"[BUILD DEBUG] IsPlacementWeapon = " +
                    $"{buildGunData?.IsPlacementWeapon}");

                Debug.Log(
                    $"[BUILD DEBUG] Placement Prefab Count = " +
                    $"{buildGunData?.PlacementGhostPrefabs?.Count ?? 0}");

            if (buildGunData == null ||
                !buildGunData.IsPlacementWeapon ||
                buildGunData.PlacementGhostPrefabs == null ||
                buildGunData.PlacementGhostPrefabs.Count < k_BuildItemsPerPlayer)
            {
                Debug.LogWarning(
                    "[Build Mode] Build weapon needs at least 3 placement prefabs.");

                return;
            }

            int prefabCount =
                buildGunData.PlacementGhostPrefabs.Count;

            System.Random random =
                new System.Random(
                    networkId * 1009 +
                    (LeaderboardManager.Instance != null
                        ? LeaderboardManager.Instance.CurrentRound * 9176
                        : 1));

            List<int> availableIndices =
                new List<int>();

            for (int i = 0; i < prefabCount; i++)
            {
                availableIndices.Add(i);
            }

            // Shuffle the prefab indices.
            for (int i = availableIndices.Count - 1; i > 0; i--)
            {
                int randomIndex =
                    random.Next(i + 1);

                int temp =
                    availableIndices[i];

                availableIndices[i] =
                    availableIndices[randomIndex];

                availableIndices[randomIndex] =
                    temp;
            }

            int item0 = availableIndices[0];
            int item1 = availableIndices[1];
            int item2 = availableIndices[2];

            predictedPlayer.BuildItem0 = item0;
            predictedPlayer.BuildItem1 = item1;
            predictedPlayer.BuildItem2 = item2;

            predictedPlayer.BuildItemsUsedMask = 0;

            // Start with the first randomly selected item.
            predictedPlayer.SelectedPlacementPrefabIndex = item0;

            Debug.Log(
                $"[Build Mode] Player {networkId} received build items: " +
                $"{item0}, {item1}, {item2}");

            Debug.Log(
                $"[Build Mode] Item names: " +
                $"{buildGunData.PlacementGhostPrefabs[item0].GhostPrefab.editorAsset?.name}, " +
                $"{buildGunData.PlacementGhostPrefabs[item1].GhostPrefab.editorAsset?.name}, " +
                $"{buildGunData.PlacementGhostPrefabs[item2].GhostPrefab.editorAsset?.name}");
        }

        private bool IsBuildItemUsed(
            in PredictedPlayerGhost player,
            int slot)
        {
            return
                (player.BuildItemsUsedMask & (1 << slot)) != 0;
        }

        private void MarkBuildItemUsed(
            ref PredictedPlayerGhost player,
            int slot)
        {
            player.BuildItemsUsedMask |=
                (byte)(1 << slot);
        }

        private int GetBuildItemIndex(
            in PredictedPlayerGhost player,
            int slot)
        {
            switch (slot)
            {
                case 0:
                    return player.BuildItem0;

                case 1:
                    return player.BuildItem1;

                case 2:
                    return player.BuildItem2;

                default:
                    return -1;
            }
        }

        private int FindNextAvailableBuildItemSlot(
            in PredictedPlayerGhost player,
            int currentSlot,
            int direction)
        {
            for (int i = 1;
                i <= k_BuildItemsPerPlayer;
                i++)
            {
                int slot =
                    (currentSlot +
                    direction * i +
                    k_BuildItemsPerPlayer) %
                    k_BuildItemsPerPlayer;

                if (!IsBuildItemUsed(player, slot))
                {
                    return slot;
                }
            }

            return -1;
        }

        private int FindBuildItemSlotForPrefab(
            in PredictedPlayerGhost player,
            int prefabIndex)
        {
            if (!IsBuildItemUsed(player, 0) &&
                player.BuildItem0 == prefabIndex)
            {
                return 0;
            }

            if (!IsBuildItemUsed(player, 1) &&
                player.BuildItem1 == prefabIndex)
            {
                return 1;
            }

            if (!IsBuildItemUsed(player, 2) &&
                player.BuildItem2 == prefabIndex)
            {
                return 2;
            }

            return -1;
        }

        private void ResolveHammerLanding(
            ref PredictedPlayerGhost hammerPlayer,
            Entity hammerEntity,
            uint serverTick,
            WeaponData weaponData)
        {
            if (weaponData == null)
                return;

            Vector3 landingPosition =
                hammerPlayer.ControllerState.CurrentPosition;

            Collider[] hits =
                UnityEngine.Physics.OverlapSphere(
                    landingPosition,
                    weaponData.HammerImpactRadius,
                    LayerMask.GetMask("ServerPlayer"),
                    QueryTriggerInteraction.Ignore);

            HashSet<Entity> alreadyHit =
                new HashSet<Entity>();

            foreach (var hitCollider in hits)
            {
                if (!GhostGameObject.TryFindGhostGameObject(
                        hitCollider.gameObject,
                        out var hitGhostObject))
                {
                    continue;
                }

                Entity targetEntity =
                    hitGhostObject.LinkedEntity;

                if (targetEntity == hammerEntity)
                    continue;

                if (!m_PlayerGhostLookup.HasComponent(targetEntity))
                    continue;

                if (!ghostOwnerLookup.HasComponent(targetEntity))
                    continue;

                if (!m_PlayerTeamLookup.HasComponent(hammerEntity))
                    continue;

                if (!m_PlayerTeamLookup.HasComponent(targetEntity))
                    continue;

                if (alreadyHit.Contains(targetEntity))
                    continue;

                alreadyHit.Add(targetEntity);

                var targetPlayer =
                    m_PlayerGhostLookup.GetRefRW(targetEntity);

                int hammerPlayerNetworkId =
                    ghostOwnerLookup[hammerEntity].NetworkId;

                int targetNetworkId =
                    ghostOwnerLookup[targetEntity].NetworkId;

                if (hammerPlayerNetworkId == targetNetworkId)
                    continue;

                var hammerTeam =
                    m_PlayerTeamLookup[hammerEntity];

                var targetTeam =
                    m_PlayerTeamLookup[targetEntity];

                if (hammerTeam.TeamId == targetTeam.TeamId)
                {
                    Debug.Log(
                        $"[Team Damage] Friendly fire prevented. " +
                        $"Player {hammerPlayerNetworkId} and target {targetNetworkId} are on the same team.");

                    continue;
                }

                float healthBeforeDamage =
                    targetPlayer.ValueRO.CurrentHealth;

                targetPlayer.ValueRW.CurrentHealth -=
                    weaponData.HammerDamage;

                targetPlayer.ValueRW.ControllerState.IsHit =
                    true;

                targetPlayer.ValueRW.LastDamageAmount =
                    weaponData.HammerDamage;

                targetPlayer.ValueRW.LastHitTick =
                    serverTick;

                Debug.Log(
                    $"[Hammer] Player {hammerPlayerNetworkId} landed on " +
                    $"Player {targetNetworkId} for " +
                    $"{weaponData.HammerDamage} damage.");

                if (healthBeforeDamage > 0 &&
                    targetPlayer.ValueRO.CurrentHealth <= 0)
                {
                    Debug.Log(
                        $"[Server] Player {hammerPlayerNetworkId} killed player {targetNetworkId}."
                    );
                }
            }

            Debug.DrawRay(
                landingPosition,
                Vector3.up * 2f,
                Color.red,
                1f);
        }

        protected override void OnUpdate()
        {
            float deltaTime =
                World.Time.DeltaTime;

            ghostOwnerLookup.Update(this);

            if (LeaderboardManager.Instance != null)
            {
                bool isBuildMode =
                    LeaderboardManager.Instance.CurrentPhase ==
                    LeaderboardManager.RoundPhase.BuildMode;

                if (isBuildMode != m_WasBuildMode)
                {
                    KillPlayersOnModeChange();

                    if (isBuildMode)
                    {
                        foreach (var (predictedPlayer, entity) in
                                 SystemAPI.Query<
                                     RefRW<PredictedPlayerGhost>>()
                                     .WithAll<GhostOwner>()
                                     .WithEntityAccess())
                        {
                            int networkId =
                                ghostOwnerLookup.HasComponent(entity)
                                    ? ghostOwnerLookup[entity].NetworkId
                                    : entity.Index;

                            GiveRandomBuildItems(
                                ref predictedPlayer.ValueRW,
                                networkId);
                        }
                    }

                    m_WasBuildMode = isBuildMode;
                }
            }

            var networkTime =
                SystemAPI.GetSingleton<NetworkTime>();

            if (!networkTime.ServerTick.IsValid)
            {
                return;
            }

            uint serverTick =
                networkTime.ServerTick.TickIndexForValidTick;

            s_PlayerMovementActive = true;
            s_PlayerMovementTick = serverTick;

            var ecb =
                new EntityCommandBuffer(
                    Unity.Collections.Allocator.Temp);

            foreach (var (predictedPlayer, localTransform, entity) in
                     SystemAPI.Query<
                         RefRW<PredictedPlayerGhost>,
                         RefRO<LocalTransform>>()
                         .WithEntityAccess()
                         .WithAll<PlayerInputComponent, GhostGameObjectLink>()
                         .WithNone<PlayerControllerLink>())
            {
                var gameObjectLink =
                    SystemAPI.ManagedAPI.GetComponent<GhostGameObjectLink>(
                        entity);

                if (gameObjectLink.LinkedInstance != null)
                {
                    if (gameObjectLink.LinkedInstance.TryGetComponent<
                            FirstPersonController>(
                            out var controller))
                    {
                        ecb.AddComponent(
                            entity,
                            new PlayerControllerLink
                            {
                                Controller = controller
                            });

                        predictedPlayer.ValueRW.ControllerState.Init(
                            localTransform.ValueRO.Position,
                            localTransform.ValueRO.Rotation);
                    }
                }
            }

            // Kill players who fall below the Y-level threshold (e.g., -50)
            float killYThreshold = -50f;

            foreach (var (predictedPlayer, transform) in
                     SystemAPI.Query<
                         RefRW<PredictedPlayerGhost>,
                         RefRO<LocalTransform>>()
                         .WithAll<Simulate>())
            {
                if (transform.ValueRO.Position.y < killYThreshold &&
                    predictedPlayer.ValueRO.CurrentHealth > 0)
                {
                    predictedPlayer.ValueRW.CurrentHealth = 0;

                    predictedPlayer.ValueRW.ControllerState.IsHit =
                        true;

                    predictedPlayer.ValueRW.LastDamageAmount =
                        999f;

                    predictedPlayer.ValueRW.LastHitTick =
                        serverTick;

                    Debug.Log(
                        $"[Server] Player fell out of bounds below Y={killYThreshold} and was killed.");
                }
            }

            // for each client
            // we process the input and create a list of commandInputs that need to be processed by the movement code
            // the movement code processes the inputs in order
            m_ProcessedClientInputCommands.Clear();

            var commands =
                m_ProcessedClientInputCommands;

            var unityFrameCount =
                UnityEngine.Time.frameCount;

            var logPredictionWarnings =
                PlayerPredictionSystem.CheckForPredictionErrors.IsEnabled;

            var clientCommandInputBufferLookup =
                SystemAPI.GetBufferLookup<ClientCommandInput>(true);

            foreach (var (predictedClient, entity)
                     in SystemAPI.Query<
                         RefRW<PredictedClientInput>>()
                         .WithEntityAccess()
                         .WithAll<GhostOwner, Simulate>())
            {
                if (!clientCommandInputBufferLookup.TryGetBuffer(
                        entity,
                        out var buffer) ||
                    buffer.Length == 0)
                {
                    continue;
                }

                predictedClient.ValueRW.BeginInputIndex =
                    commands.Length;

                if (predictedClient.ValueRO.LastProcessedServerTick == 0)
                {
                    // first frame, let's just get the latest data and set it
                    if (buffer.GetDataAtTick(
                            new NetworkTick(serverTick),
                            out var commandInput))
                    {
                        ClientCommandInput input =
                            commandInput;

                        commands.Add(input);

                        predictedClient.ValueRW.LastProcessedServerTick =
                            commandInput.Tick.TickIndexForValidTick;
                    }
                }
                else
                {
                    if (logPredictionWarnings &&
                        serverTick -
                        predictedClient.ValueRO.LastProcessedServerTick >
                        1)
                    {
                        Debug.LogWarning(
                            $"[{unityFrameCount.ToString()}] " +
                            $"[ServerPlayerMovementSystem] Server tick has skipped a frame of input. " +
                            $"Current server tick {serverTick.ToString()}, " +
                            $"last processed tick {predictedClient.ValueRO.LastProcessedServerTick.ToString()}");
                    }

                    // we need to check we aren't missing data
                    // so let's check for any data from the ticks in between
                    for (uint tick =
                             predictedClient.ValueRO.LastProcessedServerTick + 1;
                         tick <= serverTick;
                         tick++)
                    {
                        if (buffer.GetDataAtTick(
                                new NetworkTick(tick),
                                out var commandInput)
                            && commandInput.Tick.TickIndexForValidTick !=
                            predictedClient.ValueRO.LastProcessedServerTick)
                        {
                            ClientCommandInput input =
                                commandInput;

                            commands.Add(input);

                            if (commandInput.Tick.TickIndexForValidTick >
                                predictedClient.ValueRO.LastProcessedServerTick + 1)
                            {
                                // if we have missing ticks here, it means the server did not (and will not) receive it.
                                // So the best we can do is assume the input is the same and accumulate our movement based on that
                                for (uint i =
                                         predictedClient.ValueRO.LastProcessedServerTick + 1;
                                     i <
                                     commandInput.Tick.TickIndexForValidTick;
                                     i++)
                                {
                                    if (logPredictionWarnings)
                                    {
                                        Debug.LogWarning(
                                            $"[{unityFrameCount.ToString()}] " +
                                            $"[ServerPlayerMovementSystem] Missing client input for tick {i.ToString()} - " +
                                            $"using the input from tick {commandInput.Tick.TickIndexForValidTick.ToString()}");
                                    }

                                    commands.Add(commandInput);
                                }
                            }

                            predictedClient.ValueRW.LastProcessedServerTick =
                                commandInput.Tick.TickIndexForValidTick;
                        }
                    }
                }

                predictedClient.ValueRW.InputCount =
                    commands.Length -
                    predictedClient.ValueRO.BeginInputIndex;

                if (logPredictionWarnings)
                {
                    var unityFrameCountString =
                        unityFrameCount.ToString();

                    var serverTickString =
                        serverTick.ToString();

                    if (predictedClient.ValueRO.InputCount == 0)
                    {
                        Debug.LogWarning(
                            $"[{unityFrameCountString}] " +
                            $"[ServerPlayerMovementSystem] No input to process for tick {serverTickString}.");
                    }
                    else if (predictedClient.ValueRO.LastProcessedServerTick !=
                             serverTick)
                    {
                        Debug.LogWarning(
                            $"[{unityFrameCountString}] " +
                            $"[ServerPlayerMovementSystem] Last processed tick not set to current. " +
                            $"Current server tick {serverTickString}, " +
                            $"last processed tick {predictedClient.ValueRO.LastProcessedServerTick.ToString()}");
                    }
                }
            }

            ghostOwnerLookup.Update(this);
            m_PlayerGhostLookup.Update(this);
            m_PredictedClientInputComponentLookup.Update(this);
            m_PlayerTeamLookup.Update(this);

            var predictedClientInputComponentLookup =
                m_PredictedClientInputComponentLookup;

            var playerGhostLookup =
                m_PlayerGhostLookup;

            var projectileSpawnList =
                new NativeList<ProjectileSpawnData>(
                    Allocator.Temp);

            var vfxSpawnList =
                new NativeList<VfxSpawnData>(
                    Allocator.Temp);

            var placementSpawnList =
                new List<(
                    Vector3 Position,
                    Quaternion Rotation,
                    GhostSpawner.GhostReference Prefab)>(8);

            foreach (var predictedPlayer in
                     SystemAPI.Query<
                         RefRW<PredictedPlayerGhost>>()
                         .WithAll<Simulate>())
            {
                if (predictedPlayer.ValueRO.WeaponCooldown <
                    float.MaxValue)
                {
                    predictedPlayer.ValueRW.WeaponCooldown +=
                        deltaTime;
                }

                if (predictedPlayer.ValueRO.ControllerState.IsReloadingState)
                {
                    predictedPlayer.ValueRW.ReloadTimer -=
                        deltaTime;

                    if (predictedPlayer.ValueRO.ReloadTimer <= 0f)
                    {
                        predictedPlayer.ValueRW.ControllerState.IsReloadingState =
                            false;

                        var weaponData =
                            WeaponManager.Instance.WeaponRegistry.GetWeaponData(
                                predictedPlayer.ValueRO.EquippedWeaponID);

                        Debug.Log(
                            $"[WEAPON TEST] ID={predictedPlayer.ValueRO.EquippedWeaponID} " +
                            $"NAME={weaponData?.WeaponName} " +
                            $"TYPE={weaponData?.Type}"
                        );

                        if (weaponData != null)
                        {
                            predictedPlayer.ValueRW.CurrentAmmo =
                                weaponData.MagazineSize;
                        }
                    }
                }
            }

            foreach (var (predictedPlayer, inputLookup, entity)
                     in SystemAPI.Query<
                         RefRW<PredictedPlayerGhost>,
                         RefRO<PlayerClientCommandInputLookup>>()
                         .WithEntityAccess()
                         .WithAll<Simulate, GhostGameObjectLink>())
            {
                var ghostLink =
                    SystemAPI.ManagedAPI.GetComponent<GhostGameObjectLink>(
                        entity);

                var playerGhost =
                    ghostLink.LinkedInstance.GetComponent<PlayerGhost>();

                var predictedClient =
                    predictedClientInputComponentLookup[
                        inputLookup.ValueRO.ClientCommandInputEntity];

                if (predictedClient.InputCount > 0)
                {
                    // loop through ALL inputs in the batch to find if a scroll occurred
                    float scrollDelta = 0;

                    for (int i = 0;
                        i < predictedClient.InputCount;
                        i++)
                    {
                        var input =
                            commands[
                                predictedClient.BeginInputIndex + i];

                        if (input.PlayerInput.WeaponScrollDelta != 0)
                        {
                            scrollDelta +=
                                input.PlayerInput.WeaponScrollDelta;

                            Debug.Log(
                                $"[Input Test] Scroll input detected! Delta: " +
                                $"{input.PlayerInput.WeaponScrollDelta}");
                        }
                    }

                    if (scrollDelta != 0 &&
                        WeaponManager.Instance != null &&
                        WeaponManager.Instance.WeaponRegistry != null)
                    {
                        var equippedWeapon =
                            WeaponManager.Instance.WeaponRegistry.GetWeaponData(
                                predictedPlayer.ValueRO.EquippedWeaponID);

                        if (equippedWeapon != null &&
                            equippedWeapon.IsPlacementWeapon)
                        {
                            int currentSlot = -1;

                            if (predictedPlayer.ValueRO.SelectedPlacementPrefabIndex ==
                                predictedPlayer.ValueRO.BuildItem0)
                            {
                                currentSlot = 0;
                            }
                            else if (predictedPlayer.ValueRO.SelectedPlacementPrefabIndex ==
                                    predictedPlayer.ValueRO.BuildItem1)
                            {
                                currentSlot = 1;
                            }
                            else if (predictedPlayer.ValueRO.SelectedPlacementPrefabIndex ==
                                    predictedPlayer.ValueRO.BuildItem2)
                            {
                                currentSlot = 2;
                            }

                            if (currentSlot < 0)
                            {
                                if (!IsBuildItemUsed(
                                        predictedPlayer.ValueRO,
                                        0))
                                {
                                    currentSlot = 0;
                                }
                                else if (!IsBuildItemUsed(
                                            predictedPlayer.ValueRO,
                                            1))
                                {
                                    currentSlot = 1;
                                }
                                else if (!IsBuildItemUsed(
                                            predictedPlayer.ValueRO,
                                            2))
                                {
                                    currentSlot = 2;
                                }
                            }

                            if (currentSlot >= 0)
                            {
                                int direction =
                                    scrollDelta > 0 ? 1 : -1;

                                int nextSlot =
                                    FindNextAvailableBuildItemSlot(
                                        predictedPlayer.ValueRO,
                                        currentSlot,
                                        direction);

                                if (nextSlot >= 0)
                                {
                                    int nextPrefabIndex =
                                        GetBuildItemIndex(
                                            predictedPlayer.ValueRO,
                                            nextSlot);

                                    predictedPlayer.ValueRW.SelectedPlacementPrefabIndex =
                                        nextPrefabIndex;

                                    Debug.Log(
                                        $"[Build Mode] Scrolled from slot " +
                                        $"{currentSlot} to slot {nextSlot}. " +
                                        $"Prefab index: {nextPrefabIndex}");
                                }
                            }
                        }
                    }

                    var commandInput =
                        commands[
                            predictedClient.BeginInputIndex +
                            predictedClient.InputCount - 1];

                    predictedPlayer.ValueRW.ControllerState.Shoot =
                        false;

                    var weaponData =
                        WeaponManager.Instance.WeaponRegistry.GetWeaponData(
                            predictedPlayer.ValueRO.EquippedWeaponID);

                    if (weaponData != null)
                    {
                        bool wantsToReload =
                            commandInput.PlayerInput.Reload;

                        bool wantsToShoot =
                            commandInput.PlayerInput.Shoot;

                        bool mustReload =
                            wantsToShoot &&
                            predictedPlayer.ValueRO.CurrentAmmo <= 0;

                        if ((wantsToReload || mustReload) &&
                            !predictedPlayer.ValueRO.ControllerState.IsReloadingState &&
                            predictedPlayer.ValueRO.CurrentAmmo <
                            weaponData.MagazineSize)
                        {
                            predictedPlayer.ValueRW.ControllerState.IsReloadingState =
                                true;

                            predictedPlayer.ValueRW.ReloadTimer =
                                weaponData.ReloadTime;

                            predictedPlayer.ValueRW.LastReloadTick =
                                serverTick;
                        }

                        if (wantsToShoot &&
                            !predictedPlayer.ValueRO.ControllerState.IsReloadingState &&
                            (weaponData.Type == WeaponType.Melee ||
                             (predictedPlayer.ValueRO.CurrentAmmo > 0 &&
                              predictedPlayer.ValueRO.WeaponCooldown >=
                              weaponData.CooldownInMs)))
                        {
                            predictedPlayer.ValueRW.WeaponCooldown =
                                0f;

                            predictedPlayer.ValueRW.LastShotTick =
                                serverTick;

                            if (weaponData.Type != WeaponType.Melee)
                            {
                                predictedPlayer.ValueRW.CurrentAmmo--;
                            }

                            var shooterNetworkId =
                                ghostOwnerLookup[entity].NetworkId;

                            var controllerState =
                                predictedPlayer.ValueRO.ControllerState;

                            quaternion aimRotation =
                                quaternion.Euler(
                                    math.radians(
                                        controllerState.PitchDegrees),
                                    math.radians(
                                        controllerState.YawDegrees),
                                    0f);

                            float3 eyePosition =
                                playerGhost.CameraTarget.position;

                            float3 aimDirection =
                                math.mul(
                                    aimRotation,
                                    new float3(0, 0, 1));

                            float3 shotOriginPosition =
                                eyePosition +
                                aimDirection * 0.5f;

                            if (VisualEffectManager.ServerInstance != null)
                            {
                                VisualEffectManager.ServerInstance.Server_RequestVfx(
                                    shooterNetworkId,
                                    predictedPlayer.ValueRO.EquippedWeaponID);
                            }

                            int selectedIndex =
                                predictedPlayer.ValueRO.SelectedPlacementPrefabIndex;

                            if (weaponData.PlacementGhostPrefabs != null &&
                                weaponData.PlacementGhostPrefabs.Count > 0)
                            {
                                selectedIndex =
                                    math.clamp(
                                        predictedPlayer.ValueRO.SelectedPlacementPrefabIndex,
                                        0,
                                        weaponData.PlacementGhostPrefabs.Count - 1);
                            }

                            if (weaponData.IsPlacementWeapon)
                            {
                                if (weaponData.PlacementGhostPrefabs == null ||
                                    weaponData.PlacementGhostPrefabs.Count == 0)
                                {
                                    continue;
                                }

                                int placementMask =
                                    weaponData.PlacementLayerMask.value != 0
                                        ? weaponData.PlacementLayerMask.value
                                        : LayerMask.GetMask(
                                            "Ground",
                                            "Default");

                                if (UnityEngine.Physics.Raycast(
                                        eyePosition,
                                        aimDirection,
                                        out var placementHit,
                                        weaponData.HitscanRange,
                                        placementMask))
                                {
                                    int usedSlot =
                                        FindBuildItemSlotForPrefab(
                                            predictedPlayer.ValueRO,
                                            selectedIndex);

                                    if (usedSlot < 0)
                                    {
                                        continue;
                                    }

                                    var placementPosition =
                                        placementHit.point +
                                        placementHit.normal *
                                        weaponData.PlacementOffset;

                                    var surfaceRotation =
                                        Quaternion.FromToRotation(
                                            Vector3.up,
                                            placementHit.normal);

                                    var modelCorrection =
                                        Quaternion.Euler(
                                            0f,
                                            0f,
                                            0f);

                                    var placementRotation =
                                        surfaceRotation *
                                        Quaternion.Euler(
                                            0f,
                                            commandInput.PlayerInput.PlacementRotationDegrees,
                                            0f) *
                                        modelCorrection;

                                    selectedIndex =
                                        math.clamp(
                                            predictedPlayer.ValueRO.SelectedPlacementPrefabIndex,
                                            0,
                                            weaponData.PlacementGhostPrefabs.Count - 1);

                                    var selectedPrefab =
                                        weaponData.PlacementGhostPrefabs[
                                            selectedIndex];

                                    if (selectedPrefab.GhostPrefab != null &&
                                        selectedPrefab.GhostGuid.IsValid)
                                    {
                                        placementSpawnList.Add(
                                            (
                                                placementPosition,
                                                placementRotation,
                                                selectedPrefab
                                            ));

                                        MarkBuildItemUsed(
                                            ref predictedPlayer.ValueRW,
                                            usedSlot);

                                        Debug.Log(
                                            "[Server] Purple death orb placed at " +
                                            placementPosition.ToString());

                                        int nextSlot =
                                            FindNextAvailableBuildItemSlot(
                                                predictedPlayer.ValueRO,
                                                usedSlot,
                                                1);

                                        if (nextSlot >= 0)
                                        {
                                            predictedPlayer.ValueRW.SelectedPlacementPrefabIndex =
                                                GetBuildItemIndex(
                                                    predictedPlayer.ValueRO,
                                                    nextSlot);

                                            Debug.Log(
                                                $"[Build Mode] Next available item is slot " +
                                                $"{nextSlot}.");
                                        }
                                        else
                                        {
                                            predictedPlayer.ValueRW.SelectedPlacementPrefabIndex =
                                                -1;

                                            Debug.Log(
                                                "[Build Mode] Player has used all 3 build items.");
                                        }
                                    }
                                }

                                continue;
                            }

                            switch (weaponData.Type)
                            {

                                case WeaponType.Hitscan:
                                {
                                    if (UnityEngine.Physics.Raycast(
                                            eyePosition,
                                            aimDirection,
                                            out var hit,
                                            weaponData.HitscanRange,
                                            s_HitscanLayerMask))
                                    {
                                        if (hit.collider.gameObject.layer ==
                                            LayerMask.NameToLayer(
                                                "ServerPlayer"))
                                        {
                                            if (GhostGameObject.TryFindGhostGameObject(
                                                    hit.collider.gameObject,
                                                    out var hitGhostObject) &&
                                                playerGhostLookup.HasComponent(
                                                    hitGhostObject.LinkedEntity))
                                            {
                                                var targetEntity =
                                                    hitGhostObject.LinkedEntity;

                                                var targetPredictedPlayer =
                                                    playerGhostLookup.GetRefRW(targetEntity);

                                                var targetNetworkId =
                                                    ghostOwnerLookup[targetEntity].NetworkId;

                                                if (shooterNetworkId == targetNetworkId)
                                                {
                                                    // skip hitting self
                                                    continue;
                                                }

                                                var shooterTeam =
                                                    m_PlayerTeamLookup[entity];

                                                var targetTeam =
                                                    m_PlayerTeamLookup[targetEntity];

                                                if (shooterTeam.TeamId ==
                                                    targetTeam.TeamId)
                                                {
                                                    Debug.Log(
                                                        $"[Team Damage] Friendly fire prevented. " +
                                                        $"Player {shooterNetworkId} and target " +
                                                        $"{targetNetworkId} are on the same team.");

                                                    continue;
                                                }

                                                var healthBeforeDamage =
                                                    targetPredictedPlayer.ValueRO.CurrentHealth;

                                                targetPredictedPlayer.ValueRW.CurrentHealth -=
                                                    weaponData.Damage;

                                                targetPredictedPlayer.ValueRW.ControllerState.IsHit =
                                                    true;

                                                targetPredictedPlayer.ValueRW.LastDamageAmount =
                                                    weaponData.Damage;

                                                targetPredictedPlayer.ValueRW.LastHitTick =
                                                    serverTick;

                                                Debug.Log(
                                                    $"[Team Damage] Player {shooterNetworkId} damaged " +
                                                    $"player {targetNetworkId} for " +
                                                    $"{weaponData.Damage} damage.");

                                                if (healthBeforeDamage > 0 &&
                                                    targetPredictedPlayer.ValueRO.CurrentHealth <=
                                                    0)
                                                {
                                                    Debug.Log(
                                                        $"[Server] Player {shooterNetworkId} killed " +
                                                        $"player {targetNetworkId}.");

                                                    if (LeaderboardManager.Instance != null)
                                                    {
                                                        LeaderboardManager.Instance.AddKill(
                                                            shooterNetworkId,
                                                            targetNetworkId);
                                                    }
                                                    else
                                                    {
                                                        Debug.LogWarning(
                                                            "[Server] LeaderboardManager instance not found. " +
                                                            "Cannot add kill.");
                                                    }
                                                }
                                            }
                                        }

                                        if (weaponData.ProjectileHitVfxPrefab != null)
                                        {
                                            vfxSpawnList.Add(
                                                new VfxSpawnData
                                                {
                                                    Position = hit.point,
                                                    Rotation =
                                                        Quaternion.LookRotation(hit.normal),
                                                    Prefab =
                                                        GhostSpawner.FindGhostPrefabEntity(
                                                            weaponData.ProjectileHitVfxPrefab.GhostGuid)
                                                });
                                        }
                                    }

                                    break;
                                }


                                case WeaponType.Projectile:
                                {
                                    var prefabEntity =
                                        GhostSpawner.FindGhostPrefabEntity(
                                            weaponData.ProjectileGhostPrefab.GhostGuid);

                                    Debug.Log(
                                        $"PROJECTILE TEST: {prefabEntity}");

                                    Debug.Log(
                                        $"PROJECTILE NAME: {weaponData.ProjectileGhostPrefab.GhostPrefab.editorAsset?.name}");

                                    Debug.Log(
                                        $"PROJECTILE GUID: {weaponData.ProjectileGhostPrefab.GhostGuid}");

                                    Debug.Log(
                                        $"PROJECTILE ENTITY: {prefabEntity}");

                                    if (prefabEntity != Entity.Null)
                                    {
                                        Vector3 targetPoint;

                                        if (UnityEngine.Physics.Raycast(
                                                eyePosition,
                                                aimDirection,
                                                out RaycastHit aimHit,
                                                1000f,
                                                s_ShootableLayerMask))
                                        {
                                            targetPoint =
                                                aimHit.point;
                                        }
                                        else
                                        {
                                            targetPoint =
                                                eyePosition +
                                                aimDirection * 1000f;
                                        }

                                        Vector3 directionToTarget =
                                            (
                                                targetPoint -
                                                (Vector3)shotOriginPosition
                                            ).normalized;

                                        Quaternion spawnRotation =
                                            Quaternion.LookRotation(
                                                directionToTarget);

                                        projectileSpawnList.Add(
                                            new ProjectileSpawnData
                                            {
                                                Prefab = prefabEntity,
                                                Position = shotOriginPosition,
                                                Rotation = spawnRotation,
                                                OwnerNetworkId =
                                                    ghostOwnerLookup[
                                                        entity].NetworkId,
                                                SpawnTick =
                                                    commandInput.Tick
                                                        .TickIndexForValidTick,
                                                WeaponId =
                                                    predictedPlayer.ValueRO
                                                        .EquippedWeaponID
                                            });
                                    }

                                    break;
                                }

                                case WeaponType.Melee:
                                {
                                    if (!UnityEngine.Physics.Raycast(
                                            eyePosition,
                                            aimDirection,
                                            out RaycastHit hammerGroundHit,
                                            weaponData.HammerGroundRange,
                                            LayerMask.GetMask(
                                                "Ground",
                                                "Default"),
                                            QueryTriggerInteraction.Ignore))
                                    {
                                        Debug.Log(
                                            "[Hammer] Aim at the ground nearby to use the hammer.");

                                        break;
                                    }

                                    Vector3 impactPoint =
                                        hammerGroundHit.point;

                                    Debug.Log(
                                        $"[Hammer] Ground slam at {impactPoint}");

                                    predictedPlayer.ValueRW.ControllerState.JumpFallSpeed =
                                        weaponData.HammerLaunchForce;

                                    predictedPlayer.ValueRW.ControllerState.MovementType =
                                        FirstPersonController.MovementType.Jumping;

                                    predictedPlayer.ValueRW.ControllerState.PreviousMovementType =
                                        FirstPersonController.MovementType.Standing;

                                    predictedPlayer.ValueRW.ControllerState.Jump =
                                        true;

                                    predictedPlayer.ValueRW.ControllerState.Fall =
                                        false;

                                    predictedPlayer.ValueRW.ControllerState.TimeInState =
                                        0f;

                                    float3 selfLaunchDirection =
                                        aimDirection;

                                    selfLaunchDirection.y = 0f;

                                    selfLaunchDirection =
                                        math.normalizesafe(
                                            selfLaunchDirection);

                                    predictedPlayer.ValueRW.AccumulatedMovement +=
                                        selfLaunchDirection *
                                        weaponData.HammerLaunchForce *
                                        deltaTime;

                                    m_HammerLaunchers.Add(entity);

                                    Debug.DrawLine(
                                        eyePosition,
                                        hammerGroundHit.point,
                                        Color.yellow,
                                        1f);

                                    Debug.DrawRay(
                                        impactPoint,
                                        Vector3.up * 2f,
                                        Color.yellow,
                                        1f);

                                    break;
                                }
                            }
                        }
                    }
                }
            }

            foreach (var spawnData in projectileSpawnList)
            {
                GhostSpawner.SpawnGhostPrefab(
                    spawnData.Prefab,
                    spawnData.Position,
                    spawnData.Rotation,
                    GhostGameObject.GenerateRandomHash(),
                    1.0f,
                    (spawnedEntity, ecb2) =>
                    {
                        ecb2.SetComponent(
                            spawnedEntity,
                            new Projectile.ProjectileData
                            {
                                OwnerNetworkId =
                                    spawnData.OwnerNetworkId,

                                SpawnTick =
                                    spawnData.SpawnTick,

                                WeaponID =
                                    spawnData.WeaponId
                            });
                    });
            }

            foreach (var vfxData in vfxSpawnList)
            {
                GhostSpawner.SpawnGhostPrefab(
                    vfxData.Prefab,
                    vfxData.Position,
                    vfxData.Rotation,
                    GhostGameObject.GenerateRandomHash());
            }

            foreach (var placement in placementSpawnList)
            {
                GhostSpawner.SpawnGhostPrefab(
                    placement.Prefab,
                    placement.Position,
                    placement.Rotation,
                    GhostGameObject.GenerateRandomHash());
            }

            predictedClientInputComponentLookup.Update(this);

            foreach (var (predictedPlayer, inputLookup, controllerConsts, entity)
                     in SystemAPI.Query<
                         RefRW<PredictedPlayerGhost>,
                         RefRO<PlayerClientCommandInputLookup>,
                         RefRO<PredictedPlayerControllerConsts>>()
                         .WithAll<Simulate>()
                         .WithEntityAccess())
            {
                var predictedClient =
                    predictedClientInputComponentLookup[
                        inputLookup.ValueRO.ClientCommandInputEntity];

                if (predictedClient.InputCount > 0)
                {
                    float movementDt =
                        deltaTime /
                        predictedClient.InputCount;

                    for (int i = 0;
                         i < predictedClient.InputCount;
                         i++)
                    {
                        var commandInput =
                            commands[
                                i +
                                predictedClient.BeginInputIndex];

                        if (commandInput.TryGetPlayerMovementInput(
                                predictedPlayer.ValueRO.InputIndex,
                                out var input))
                        {
                            FirstPersonController.AccumulateMovement(
                                ref predictedPlayer.ValueRW.ControllerState,
                                ref predictedPlayer.ValueRW.AccumulatedMovement,
                                input,
                                controllerConsts.ValueRO.ControllerConsts,
                                movementDt);
                        }
                    }

                    if (predictedPlayer.ValueRW.ControllerState.JumpTriggered)
                    {
                        predictedPlayer.ValueRW.LastJumpTick =
                            serverTick;

                        predictedPlayer.ValueRW.ControllerState.JumpTriggered =
                            false;
                    }

                    if (predictedPlayer.ValueRW.ControllerState.LandTriggered)
                    {
                        predictedPlayer.ValueRW.LastLandTick =
                            serverTick;

                        predictedPlayer.ValueRW.ControllerState.LandTriggered =
                            false;
                    }
                }
            }

            foreach (var (
                         transform,
                         predictedPlayer,
                         controllerConsts,
                         entity)
                     in SystemAPI.Query<
                         RefRO<LocalTransform>,
                         RefRW<PredictedPlayerGhost>,
                         RefRO<PredictedPlayerControllerConsts>>()
                         .WithAll<
                             Simulate,
                             PlayerControllerLink,
                             GhostGameObjectLink>()
                         .WithNone<GhostGameObjectDeferredActivation>()
                         .WithEntityAccess())
            {
                var controllerLink =
                    SystemAPI.ManagedAPI.GetComponent<PlayerControllerLink>(
                        entity);

                if (math.lengthsq(
                        predictedPlayer.ValueRO.AccumulatedMovement) > 0f)
                {
                    controllerLink.Controller.ApplyMovementUpdate(
                        ref predictedPlayer.ValueRW.ControllerState,
                        controllerConsts.ValueRO.ControllerConsts,
                        predictedPlayer.ValueRO.AccumulatedMovement,
                        deltaTime);
                }

                if (m_HammerLaunchers.Contains(entity) &&
                    predictedPlayer.ValueRO.ControllerState.LandTriggered)
                {
                    var hammerWeaponData =
                        WeaponManager.Instance.WeaponRegistry.GetWeaponData(
                            predictedPlayer.ValueRO.EquippedWeaponID);

                    ResolveHammerLanding(
                        ref predictedPlayer.ValueRW,
                        entity,
                        serverTick,
                        hammerWeaponData);

                    m_HammerLaunchers.Remove(entity);
                }

                predictedPlayer.ValueRW.AccumulatedMovement =
                    float3.zero;
            }

            bool checkForPredictionErrors =
                PlayerPredictionSystem.CheckForPredictionErrors.IsEnabled;

            foreach (var (
                         transform,
                         predictedPlayer,
                         controllerConsts,
                         entity)
                     in SystemAPI.Query<
                         RefRW<LocalTransform>,
                         RefRW<PredictedPlayerGhost>,
                         RefRO<PredictedPlayerControllerConsts>>()
                         .WithAll<
                             Simulate,
                             PlayerControllerLink,
                             GhostGameObjectLink>()
                         .WithEntityAccess())
            {
                transform.ValueRW.Position =
                    predictedPlayer.ValueRO.ControllerState.CurrentPosition;

                transform.ValueRW.Rotation =
                    predictedPlayer.ValueRO.ControllerState.CurrentRotation;

                if (checkForPredictionErrors)
                {
                    if (MovementHistory.TryGetTick(
                            serverTick,
                            out var pos,
                            out var rot))
                    {
                        // compare our calculation with the recorded
                        Debug.Assert(
                            PlayerMovementHistory.HistoryMatches(
                                pos,
                                rot,
                                predictedPlayer.ValueRO.ControllerState.CurrentPosition,
                                predictedPlayer.ValueRO.ControllerState.CurrentRotation),
                            $"[ServerPlayerMovementSystem] Mispredict detected at tick {serverTick} predicted pos {predictedPlayer.ValueRO.ControllerState.CurrentPosition} recorded {(float3)pos}");
                    }
                    else
                    {
                        // this is the first time we've processed this tick
                        // let's add our result
                        MovementHistory.Add(
                            serverTick,
                            predictedPlayer.ValueRO.ControllerState.CurrentPosition,
                            predictedPlayer.ValueRO.ControllerState.CurrentRotation);
                    }
                }
            }

            ecb.Playback(EntityManager);
            ecb.Dispose();

            s_PlayerMovementActive = false;
        }
    }
}