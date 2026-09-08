using UnityEngine;
using System;
using Gameplay.Leaderboard;
using Unity.Burst;
using Unity.CharacterController;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Physics;
using Random = Unity.Mathematics.Random;
using Unity.Transforms;
using Collider = UnityEngine.Collider;

namespace Unity.MP_FPS
{
    /// <summary>
    /// Processes client join requests and spawns a character for each client.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [BurstCompile]
    public partial struct ServerGameSystem : ISystem
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init()
        {
            _overlapColliders = new Collider[16];
        }

        private static Collider[] _overlapColliders = new Collider[16];
        private ComponentLookup<JoinedClient> _joinedClientLookup;

        // Phase 3: Round state
        private bool _roundOver;
        private int _winningTeam;

        // Lives each player starts with
        private const int StartingLives = 3;

        private bool _wasBuildMode;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PlayerEntityPrefabs>();
            state.RequireForUpdate<PhysicsWorldSingleton>();
            state.RequireForUpdate<NetworkStreamDriver>();
            state.RequireForUpdate<ClientsMap>();

            _roundOver = false;
            _winningTeam = -1;
            _wasBuildMode = true;

            Entity randomEntity = state.EntityManager.CreateEntity();

            state.EntityManager.AddComponentData(
                randomEntity,
                new FixedRandom
                {
                    Random = Random.CreateFromIndex(
                        (uint)DateTime.Now.Millisecond),
                });

            var mapSingleton =
                state.EntityManager.CreateSingletonBuffer<ClientsMap>();

            state.EntityManager.GetBuffer<ClientsMap>(mapSingleton)
                .Add(default); //The server NetworkId is 0

            _joinedClientLookup =
                state.GetComponentLookup<JoinedClient>();
        }

        [BurstDiscard]
        public void OnUpdate(ref SystemState state)
        {
            _joinedClientLookup.Update(ref state);

            var ecb =
                SystemAPI.GetSingletonRW<
                    BeginSimulationEntityCommandBufferSystem.Singleton>()
                    .ValueRW
                    .CreateCommandBuffer(state.WorldUnmanaged);

            var clientsMap =
                SystemAPI.GetSingletonBuffer<ClientsMap>();

            var gameplayMapsEntity =
                SystemAPI.GetSingletonEntity<ClientsMap>();

            var connectionEventsForTick =
                SystemAPI.GetSingleton<NetworkStreamDriver>()
                    .ConnectionEventsForTick;

            RefreshClientsMap(
                ref state,
                ecb,
                clientsMap,
                connectionEventsForTick);

            if (!SystemAPI.TryGetSingleton(
                    out PlayerEntityPrefabs playerEntityPrefabs))
            {
                return;
            }

            if (SystemAPI.HasSingleton<DisableCharacterDynamicContacts>())
            {
                state.EntityManager.DestroyEntity(
                    SystemAPI.GetSingletonEntity<
                        DisableCharacterDynamicContacts>());
            }

            HandleJoinRequests(
                ref state,
                gameplayMapsEntity,
                playerEntityPrefabs,
                ecb);

            HandleRoundPhaseTransition(
                ref state,
                ecb);

            HandlePlayerDeathAndRespawn(
                ref state,
                ecb);
        }

        void RefreshClientsMap(
            ref SystemState state,
            EntityCommandBuffer ecb,
            DynamicBuffer<ClientsMap> clientsMap,
            NativeArray<NetCodeConnectionEvent>.ReadOnly connectionEventsForTick)
        {
            //Maintain the clients map size
            foreach (var evt in connectionEventsForTick)
            {
                if (evt.State == ConnectionState.State.Connected)
                {
                    var lengthNeeded = evt.Id.Value + 1;

                    if (clientsMap.Length < lengthNeeded)
                    {
                        clientsMap.Resize(
                            lengthNeeded,
                            NativeArrayOptions.ClearMemory);
                    }

                    clientsMap.ElementAt(evt.Id.Value)
                        .ConnectionEntity = evt.ConnectionEntity;
                }

                if (evt.State == ConnectionState.State.Disconnected)
                {
                    var networkId = evt.Id.Value;

                    Debug.Log(
                        $"[Server] Client with NetworkId {networkId} has disconnected.");

                    // Find and destroy the player character entity by querying for its GhostOwner.
                    foreach (var (ghostOwner, entity) in
                             SystemAPI.Query<RefRO<GhostOwner>>()
                                 .WithEntityAccess())
                    {
                        if (ghostOwner.ValueRO.NetworkId == networkId)
                        {
                            Debug.Log(
                                $"[Server] Found and destroying PlayerEntity {entity} for disconnected client {networkId}.");

                            ecb.DestroyEntity(entity);
                            break;
                        }
                    }

                    // Find and destroy the clientInputEntity by querying for its PlayerCommandTarget.
                    foreach (var (commandTarget, entity) in
                             SystemAPI.Query<RefRO<PlayerCommandTarget>>()
                                 .WithEntityAccess())
                    {
                        if (commandTarget.ValueRO.NetworkId == networkId)
                        {
                            Debug.Log(
                                $"[Server] Found and destroying ClientInputEntity {entity} for disconnected client {networkId}.");

                            ecb.DestroyEntity(entity);
                            break;
                        }
                    }

                    RemovePlayerFromLeaderboard(networkId);

                    clientsMap.ElementAt(networkId) = default;
                }
            }

            // Entities created via ECB have temporary Entity IDs. Need to refresh this index lookup. So patch them.
            for (var i = clientsMap.Length - 1; i >= 0; --i)
            {
                ref var map = ref clientsMap.ElementAt(i);

                if (map.OwnerNetworkId.Value == default)
                {
                    break;
                }

                ref var dest =
                    ref clientsMap.ElementAt(
                        map.OwnerNetworkId.Value);

                Patch(
                    map.PlayerEntity,
                    ref dest.PlayerEntity);

                Patch(
                    map.CharacterControllerEntity,
                    ref dest.CharacterControllerEntity);

                map = default;

                static void Patch(
                    Entity possibleRemapValue,
                    ref Entity destination)
                {
                    if (possibleRemapValue != Entity.Null)
                    {
                        destination = possibleRemapValue;
                    }
                }

                ;
            }
        }

        Entity GetChildWithComponent<T>(
            EntityManager em,
            Entity parentEntity)
            where T : unmanaged, IComponentData
        {
            if (!em.HasComponent<Child>(parentEntity))
            {
                return Entity.Null;
            }

            var children =
                em.GetBuffer<Child>(parentEntity);

            foreach (var child in children)
            {
                if (em.HasComponent<T>(child.Value))
                {
                    return child.Value;
                }
            }

            return Entity.Null;
        }

        [BurstDiscard]
        private void AddDeathToLeaderboard(int networkId)
        {
            LeaderboardManager.Instance.AddDeath(networkId);
        }

        [BurstDiscard]
        private void AddPlayerToLeaderboard(
            int networkId,
            FixedString64Bytes playerName)
        {
            LeaderboardManager.AddPlayer(
                networkId,
                playerName);
        }

        [BurstDiscard]
        private void RemovePlayerFromLeaderboard(int networkId)
        {
            LeaderboardManager.Instance.RemovePlayer(networkId);
        }

        private void HandleRoundPhaseTransition(
            ref SystemState state,
            EntityCommandBuffer ecb)
        {
            if (LeaderboardManager.Instance == null)
            {
                return;
            }

            bool isBuildMode =
                LeaderboardManager.Instance.CurrentPhase ==
                LeaderboardManager.RoundPhase.BuildMode;

            if (_wasBuildMode && !isBuildMode)
            {
                _roundOver = false;
                _winningTeam = -1;

                var connections =
                    SystemAPI.QueryBuilder()
                        .WithAll<NetworkId, JoinedClient>()
                        .Build();

                using var connectionEntities =
                    connections.ToEntityArray(Allocator.Temp);

                foreach (var connectionEntity in connectionEntities)
                {
                    if (!SystemAPI.Exists(connectionEntity))
                    {
                        continue;
                    }

                    var networkId =
                        SystemAPI.GetComponent<NetworkId>(
                            connectionEntity);

                    var joinedClient =
                        SystemAPI.GetComponent<JoinedClient>(
                            connectionEntity);

                    if (!joinedClient.HasSpawned)
                    {
                        continue;
                    }

                    if (joinedClient.PlayerEntity != Entity.Null &&
                        SystemAPI.Exists(joinedClient.PlayerEntity))
                    {
                        if (SystemAPI.HasComponent<
                                PlayerClientCommandInputLookup>(
                                joinedClient.PlayerEntity))
                        {
                            var inputLookup =
                                SystemAPI.GetComponent<
                                    PlayerClientCommandInputLookup>(
                                    joinedClient.PlayerEntity);

                            if (inputLookup.ClientCommandInputEntity !=
                                Entity.Null &&
                                SystemAPI.Exists(
                                    inputLookup.ClientCommandInputEntity))
                            {
                                ecb.DestroyEntity(
                                    inputLookup.ClientCommandInputEntity);
                            }
                        }

                        ecb.DestroyEntity(
                            joinedClient.PlayerEntity);
                    }

                    SpawnPlayerCharacter(
                        ref state,
                        ecb,
                        connectionEntity,
                        joinedClient.PlayerName,
                        joinedClient.CharacterIndex,
                        joinedClient.TeamId,
                        true);

                    Debug.Log(
                        $"[Round] Player {networkId.Value} switched from Build Mode to Fighting.");
                }
            }

            _wasBuildMode = isBuildMode;
        }

        private void SpawnPlayerCharacter(
            ref SystemState state,
            EntityCommandBuffer ecb,
            Entity connectionEntity,
            FixedString64Bytes playerName,
            int characterIndex,
            int teamId = -1,
            bool resetLives = false)
        {
            var playerEntityPrefabs =
                SystemAPI.GetSingleton<PlayerEntityPrefabs>();

            var ownerNetworkId =
                SystemAPI.GetComponent<NetworkId>(
                    connectionEntity);

            if (teamId < 0)
            {
                teamId = GetTeamForNewPlayer(ref state);
            }

            // Instantiate the client input entity
            var clientInputEntity =
                ecb.Instantiate(
                    playerEntityPrefabs.ClientInputEntityPrefab);

            ecb.SetComponent(
                clientInputEntity,
                new GhostOwner
                {
                    NetworkId = ownerNetworkId.Value
                });

            ecb.SetComponent(
                connectionEntity,
                new CommandTarget
                {
                    targetEntity = clientInputEntity
                });

            ecb.AddBuffer<ClientCommandInput>(
                clientInputEntity);

            ecb.SetComponent(
                clientInputEntity,
                new PlayerCommandTarget
                {
                    NetworkId = ownerNetworkId.Value
                });

            bool isBuildMode =
                LeaderboardManager.Instance != null &&
                LeaderboardManager.Instance.CurrentPhase ==
                LeaderboardManager.RoundPhase.BuildMode;

            var weaponId = isBuildMode
                ? (uint)(
                    WeaponManager.Instance.WeaponRegistry.Weapons.Count - 1)
                : (uint)UnityEngine.Random.Range(
                    0,
                    WeaponManager.Instance.WeaponRegistry.Weapons.Count - 1);

            characterIndex = (int)weaponId;

            // Instantiate the player entity for the current round phase.
            var playerEntityPrefab =
                isBuildMode &&
                playerEntityPrefabs.PlayerBuildEntityPrefab != Entity.Null
                    ? playerEntityPrefabs.PlayerBuildEntityPrefab
                    : characterIndex switch
                    {
                        0 => playerEntityPrefabs.PlayerRifleEntityPrefab,
                        1 => playerEntityPrefabs.PlayerShotgunEntityPrefab,
                        //2 => playerEntityPrefabs.PlayerSharkEntityPrefab,
                        2 => playerEntityPrefabs.PlayerHammerEntityPrefab,
                        _ => playerEntityPrefabs.PlayerShotgunEntityPrefab
                    };

            var playerEntity =
                ecb.Instantiate(playerEntityPrefab);

            // Keep the working team assignment.
            ecb.SetComponent(
                playerEntity,
                new PlayerTeam
                {
                    TeamId = teamId
                });

            var weaponData =
                WeaponManager.Instance.WeaponRegistry.GetWeaponData(
                    weaponId);

            var magazineSize =
                weaponData != null
                    ? weaponData.MagazineSize
                    : 30;

            ecb.SetComponent(
                playerEntity,
                new GhostOwner
                {
                    NetworkId = ownerNetworkId.Value
                });

            ecb.AddComponent(
                playerEntity,
                new PlayerClientCommandInputLookup
                {
                    ClientCommandInputEntity =
                        clientInputEntity
                });

            ecb.SetComponent(
                playerEntity,
                new PredictedPlayerGhost
                {
                    InputIndex = 0,
                    MaxHealth = 100f,
                    CurrentHealth = 100f,
                    EquippedWeaponID = weaponId,
                    CurrentAmmo = magazineSize
                });

            ecb.AddComponent(
                playerEntity,
                new PlayerCharacterInitialized());

            ecb.SetComponentEnabled<PlayerCharacterInitialized>(
                playerEntity,
                false);

            if (FindSpawnPoint(
                    ref state,
                    out var spawnPoint))
            {
                ecb.SetComponent(
                    playerEntity,
                    new LocalTransform
                    {
                        Position = spawnPoint.Position,
                        Rotation = spawnPoint.Rotation,
                        Scale = 1.0f
                    });
            }

            ecb.SetComponent(
                playerEntity,
                new GhostGameObjectGuid
                {
                    Guid =
                        GhostGameObject.GenerateRandomHash()
                });

            ecb.SetComponent(
                playerEntity,
                new PlayerGhost.PlayerData
                {
                    Name = playerName
                });

            // Update the clients map
            var clientsMap =
                SystemAPI.GetSingletonBuffer<ClientsMap>();

            clientsMap.ElementAt(
                    ownerNetworkId.Value)
                .PlayerEntity = playerEntity;

            if (!SystemAPI.HasComponent<JoinedClient>(
                    connectionEntity))
            {
                ecb.AddComponent(
                    connectionEntity,
                    new JoinedClient
                    {
                        PlayerEntity = playerEntity,
                        PlayerName = playerName,
                        CharacterIndex = characterIndex,
                        TeamId = teamId,
                        Lives = StartingLives,
                        HasSpawned = true
                    });

                Debug.Log(
                    $"[Lives] Player {ownerNetworkId.Value} joined with {StartingLives} lives on team {teamId}.");
            }
            else
            {
                var joinedClient =
                    SystemAPI.GetComponent<JoinedClient>(
                        connectionEntity);

                int lives =
                    resetLives
                        ? StartingLives
                        : joinedClient.Lives;

                ecb.SetComponent(
                    connectionEntity,
                    new JoinedClient
                    {
                        PlayerEntity = playerEntity,
                        PlayerName = joinedClient.PlayerName,
                        CharacterIndex = characterIndex,
                        TeamId = joinedClient.TeamId,
                        Lives = lives,
                        HasSpawned = true
                    });

                Debug.Log(
                    $"[Lives] Player {ownerNetworkId.Value} spawned with {lives} lives remaining.");
            }

            ecb.AppendToBuffer(
                connectionEntity,
                new LinkedEntityGroup
                {
                    Value = playerEntity
                });

            if (!SystemAPI.HasComponent<NetworkStreamInGame>(
                    connectionEntity))
            {
                ecb.AddComponent<NetworkStreamInGame>(
                    connectionEntity);
            }
        }

        void HandlePlayerDeathAndRespawn(
            ref SystemState state,
            EntityCommandBuffer ecb)
        {
            var clientsMap =
                SystemAPI.GetSingletonBuffer<ClientsMap>();

            foreach (var (playerGhost, ghostOwner, initialized, entity) in
                     SystemAPI.Query<
                         RefRO<PredictedPlayerGhost>,
                         RefRO<GhostOwner>,
                         EnabledRefRO<PlayerCharacterInitialized>>()
                         .WithEntityAccess())
            {
                if (!initialized.ValueRO)
                {
                    continue;
                }

                if (playerGhost.ValueRO.CurrentHealth > 0)
                {
                    continue;
                }

                var networkId =
                    ghostOwner.ValueRO.NetworkId;

                if (networkId < 0 ||
                    networkId >= clientsMap.Length)
                {
                    continue;
                }

                var connectionEntity =
                    clientsMap[networkId].ConnectionEntity;

                if (connectionEntity == Entity.Null ||
                    !SystemAPI.Exists(connectionEntity))
                {
                    continue;
                }

                if (!_joinedClientLookup.HasComponent(
                        connectionEntity))
                {
                    continue;
                }

                var joinedClient =
                    _joinedClientLookup[connectionEntity];

                if (!joinedClient.HasSpawned)
                {
                    continue;
                }

                if (!SystemAPI.HasComponent<PendingRespawn>(
                        connectionEntity))
                {
                    joinedClient.Lives--;

                    ecb.SetComponent(
                        connectionEntity,
                        joinedClient);

                    AddDeathToLeaderboard(networkId);

                    Debug.Log(
                        $"[Lives] Player {networkId} died. " +
                        $"Lives remaining: {joinedClient.Lives}");

                    if (joinedClient.Lives > 0 &&
                        !_roundOver)
                    {
                        ecb.AddComponent(
                            connectionEntity,
                            new PendingRespawn
                            {
                                RespawnTimer = 5f
                            });

                        Debug.Log(
                            $"[Lives] Player {networkId} will respawn in 5 seconds.");
                    }
                    else
                    {
                        Debug.Log(
                            $"[Lives] Player {networkId} has been eliminated.");
                    }
                }

                if (SystemAPI.HasComponent<
                        PlayerClientCommandInputLookup>(
                        entity))
                {
                    var inputLookup =
                        SystemAPI.GetComponent<
                            PlayerClientCommandInputLookup>(
                            entity);

                    if (SystemAPI.Exists(
                            inputLookup.ClientCommandInputEntity))
                    {
                        ecb.DestroyEntity(
                            inputLookup.ClientCommandInputEntity);
                    }
                }

                ecb.DestroyEntity(entity);
            }

            // Phase 3: Check whether an entire team has been eliminated.
            CheckForRoundEnd(ref state, ecb);

            // --- Part 2: Countdown Timers and Respawn Players ---
            if (!_roundOver)
            {
                foreach (var (pendingRespawn, connection, entity) in
                         SystemAPI.Query<
                             RefRW<PendingRespawn>,
                             RefRO<NetworkId>>()
                             .WithEntityAccess())
                {
                    pendingRespawn.ValueRW.RespawnTimer -=
                        SystemAPI.Time.DeltaTime;

                    if (pendingRespawn.ValueRO.RespawnTimer <= 0f)
                    {
                        Debug.Log(
                            $"[Server] Respawning player for connection {entity}.");

                        if (_joinedClientLookup.HasComponent(
                                entity))
                        {
                            var joinedClientData =
                                _joinedClientLookup[entity];

                            if (joinedClientData.Lives > 0)
                            {
                                SpawnPlayerCharacter(
                                    ref state,
                                    ecb,
                                    entity,
                                    joinedClientData.PlayerName,
                                    joinedClientData.CharacterIndex,
                                    joinedClientData.TeamId);

                                ecb.RemoveComponent<PendingRespawn>(
                                    entity);
                            }
                            else
                            {
                                ecb.RemoveComponent<PendingRespawn>(
                                    entity);
                            }
                        }
                        else
                        {
                            Debug.LogError(
                                $"Connection entity {entity} is pending respawn but has no JoinedClient data!");

                            ecb.RemoveComponent<PendingRespawn>(
                                entity);
                        }
                    }
                }
            }
            else
            {
                // Round is over, so remove any pending respawn timers.
                foreach (var (pendingRespawn, entity) in
                         SystemAPI.Query<
                             RefRO<PendingRespawn>>()
                             .WithEntityAccess())
                {
                    ecb.RemoveComponent<PendingRespawn>(
                        entity);
                }
            }
        }

        private void CheckForRoundEnd(
            ref SystemState state,
            EntityCommandBuffer ecb)
        {
            if (_roundOver)
            {
                return;
            }

            bool team0HasLives = false;
            bool team1HasLives = false;

            bool team0Exists = false;
            bool team1Exists = false;

            foreach (var joinedClient in
                     SystemAPI.Query<RefRO<JoinedClient>>())
            {
                int teamId =
                    joinedClient.ValueRO.TeamId;

                int lives =
                    joinedClient.ValueRO.Lives;

                if (teamId == 0)
                {
                    team0Exists = true;

                    if (lives > 0)
                    {
                        team0HasLives = true;
                    }
                }
                else if (teamId == 1)
                {
                    team1Exists = true;

                    if (lives > 0)
                    {
                        team1HasLives = true;
                    }
                }
            }

            if (!team0Exists || !team1Exists)
            {
                return;
            }

            if (!team0HasLives && team1HasLives)
            {
                EndRound(
                    ref state,
                    ecb,
                    1);
            }
            else if (!team1HasLives && team0HasLives)
            {
                EndRound(
                    ref state,
                    ecb,
                    0);
            }
        }

        private void EndRound(
            ref SystemState state,
            EntityCommandBuffer ecb,
            int winningTeam)
        {
            if (_roundOver)
            {
                return;
            }

            _roundOver = true;
            _winningTeam = winningTeam;

            string winnerName =
                winningTeam == 0
                    ? "RED"
                    : "BLUE";

            Debug.Log("========================================");
            Debug.Log("[ROUND] ROUND OVER!");
            Debug.Log($"[ROUND] WINNING TEAM: {winnerName}");
            Debug.Log("========================================");

            foreach (var (pendingRespawn, entity) in
                     SystemAPI.Query<
                         RefRO<PendingRespawn>>()
                         .WithEntityAccess())
            {
                ecb.RemoveComponent<PendingRespawn>(
                    entity);
            }
        }

        void HandleJoinRequests(
            ref SystemState state,
            Entity gameplayMapsEntity,
            PlayerEntityPrefabs playerEntityPrefabs,
            EntityCommandBuffer ecb)
        {
            foreach (var (request, rpcReceive, entity) in
                     SystemAPI.Query<
                         RefRO<ClientJoinRequestRpc>,
                         RefRW<ReceiveRpcCommandRequest>>()
                         .WithEntityAccess())
            {
                if (SystemAPI.HasComponent<NetworkId>(
                        rpcReceive.ValueRW.SourceConnection) &&
                    !SystemAPI.HasComponent<NetworkStreamInGame>(
                        rpcReceive.ValueRW.SourceConnection))
                {
                    SpawnPlayerCharacter(
                        ref state,
                        ecb,
                        rpcReceive.ValueRW.SourceConnection,
                        request.ValueRO.PlayerName,
                        request.ValueRO.CharacterIndex);

                    var ownerNetworkId =
                        SystemAPI.GetComponent<NetworkId>(
                            rpcReceive.ValueRW.SourceConnection);

                    AddPlayerToLeaderboard(
                        ownerNetworkId.Value,
                        request.ValueRO.PlayerName);
                }

                ecb.DestroyEntity(entity);
            }
        }

        [BurstDiscard]
        private bool FindSpawnPoint(
            ref SystemState state,
            out LocalToWorld spawnPoint)
        {
            var spawnPointsQuery =
                SystemAPI.QueryBuilder()
                    .WithAll<SpawnPoint, LocalToWorld>()
                    .Build();

            var spawnPoints =
                spawnPointsQuery.ToComponentDataArray<LocalToWorld>(
                    Allocator.Temp);

            ref FixedRandom random =
                ref SystemAPI.GetSingletonRW<FixedRandom>()
                    .ValueRW;

            if (spawnPoints.Length == 0)
            {
                spawnPoint = default;
                spawnPoints.Dispose();
                return false;
            }

            // Shuffle the list to ensure that if multiple points have the same low number of players, the choice among them is still random.
            for (int i = spawnPoints.Length - 1; i > 0; i--)
            {
                int k =
                    random.Random.NextInt(
                        0,
                        i + 1);

                (spawnPoints[k], spawnPoints[i]) =
                    (spawnPoints[i], spawnPoints[k]);
            }

            int bestSpawnPointIndex = 0;
            int minColliderCount = int.MaxValue;

            for (int i = 0; i < spawnPoints.Length; i++)
            {
                int numColliders =
                    UnityEngine.Physics.OverlapSphereNonAlloc(
                        spawnPoints[i].Position,
                        2f,
                        _overlapColliders,
                        LayerMask.GetMask("ServerPlayer"));

                if (numColliders == 0)
                {
                    bestSpawnPointIndex = i;
                    break;
                }

                if (numColliders < minColliderCount)
                {
                    minColliderCount = numColliders;
                    bestSpawnPointIndex = i;
                }
            }

            spawnPoint =
                spawnPoints[bestSpawnPointIndex];

            spawnPoints.Dispose();

            return true;
        }

        private int GetTeamForNewPlayer(
            ref SystemState state)
        {
            int redPlayers = 0;
            int bluePlayers = 0;

            foreach (var playerTeam in
                     SystemAPI.Query<RefRO<PlayerTeam>>())
            {
                if (playerTeam.ValueRO.TeamId == 0)
                {
                    redPlayers++;
                }
                else if (playerTeam.ValueRO.TeamId == 1)
                {
                    bluePlayers++;
                }
            }

            if (redPlayers <= bluePlayers)
            {
                return 0;
            }

            return 1;
        }
    }
}