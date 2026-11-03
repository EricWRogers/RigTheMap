using Unity.Collections;
using System.Collections.Generic;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace Unity.MP_FPS
{
    public struct LobbyJoinRpc : IRpcCommand
    {
        public FixedString64Bytes PlayerName;
        public int TeamId;
    }

    public struct LobbyPlayerRpc : IRpcCommand
    {
        public int NetworkId;
        public FixedString64Bytes PlayerName;
        public bool IsReady;
        public int TeamId;
    }

    public struct LobbyPlayerRemovedRpc : IRpcCommand
    {
        public int NetworkId;
    }

    public struct LobbyReadyRpc : IRpcCommand
    {
        public bool IsReady;
    }

    public struct LobbyTeamRpc : IRpcCommand
    {
        public int TeamId;
    }

    public struct LobbyStartGameRpc : IRpcCommand
    {
    }

    public struct LobbyBeginGameRpc : IRpcCommand
    {

    }

    public struct LobbyJoinSent : IComponentData
    {
    }

    public struct LobbyPlayer : IComponentData
    {
        public int NetworkId;
        public FixedString64Bytes PlayerName;
        public bool IsReady;
        public int TeamId;
    }

    public static class LobbyRoster
    {
        public struct PlayerInfo
        {
            public string PlayerName;
            public bool IsReady;
            public int TeamId;
        }

        public static readonly Dictionary<int, PlayerInfo> Players =
            new Dictionary<int, PlayerInfo>();

        public static bool LocalPlayerReady = false;
        public static int LocalNetworkId = -1;
        public static int ChangeVersion {get; set;}

        public static void AddOrUpdatePlayer(
            int networkId,
            string playerName,
            bool isReady,
            int teamId)
        {
            Players[networkId] = new PlayerInfo
            {
                PlayerName = playerName,
                IsReady = isReady,
                TeamId = teamId
            };
            ChangeVersion++;
        }

        public static void RemovePlayer(int networkId)
        {
            if (Players.Remove(networkId))
                ChangeVersion++;
        }

        public static void Clear()
        {
            Players.Clear();
            LocalPlayerReady = false;
            LocalNetworkId = -1;
            ChangeVersion++;
        }

        public static void ToggleReady()
        {
            if (GameSettings.Instance.GameState != GlobalGameState.Lobby)
                return;

            World clientWorld = null;

            foreach (var world in World.All)
            {
                if (world.IsCreated && world.Name == "ClientWorld")
                {
                    clientWorld = world;
                    break;
                }
            }

            if (clientWorld == null)
            {
                Debug.LogWarning("[LOBBY] ClientWorld not found.");
                return;
            }

            var entityManager = clientWorld.EntityManager;
            var rpcEntity = entityManager.CreateEntity();

            LocalPlayerReady = !LocalPlayerReady;

            entityManager.AddComponentData(
                rpcEntity,
                new LobbyReadyRpc
                {
                    IsReady = LocalPlayerReady
                });

            entityManager.AddComponentData(
                rpcEntity,
                new SendRpcCommandRequest());

            Debug.Log(
                $"[LOBBY CLIENT] Ready changed to {LocalPlayerReady}");
        }

        public static void SetTeam(int teamId)
        {
            if (GameSettings.Instance.GameState != GlobalGameState.Lobby)
                return;

            World clientWorld = null;
            foreach (var world in World.All)
            {
                if (world.IsCreated && world.Name == "ClientWorld")
                {
                    clientWorld = world;
                    break;
                }
            }

            if (clientWorld == null)
            {
                Debug.LogWarning("[LOBBY] ClientWorld not found.");
                return;
            }

            GameSettings.Instance.PlayerTeam = teamId;
            var entityManager = clientWorld.EntityManager;
            var rpcEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(
                rpcEntity,
                new LobbyTeamRpc { TeamId = teamId });
            entityManager.AddComponentData(
                rpcEntity,
                new SendRpcCommandRequest());
        }

        public static void RequestStartGame()
        {
            if (GameSettings.Instance.GameState != GlobalGameState.Lobby)
                return;

            if (GameManager.Instance == null ||
                !GameManager.Instance.IsLobbyHost)
                return;

            World clientWorld = null;

            foreach (var world in World.All)
            {
                if (world.IsCreated && world.Name == "ClientWorld")
                {
                    clientWorld = world;
                    break;
                }
            }

            if (clientWorld == null)
            {
                Debug.LogWarning("[LOBBY] ClientWorld not found.");
                return;
            }

            var entityManager = clientWorld.EntityManager;
            var rpcEntity = entityManager.CreateEntity();

            entityManager.AddComponentData(
                rpcEntity,
                new LobbyStartGameRpc());

            entityManager.AddComponentData(
                rpcEntity,
                new SendRpcCommandRequest());

            Debug.Log("[LOBBY CLIENT] Start Game request sent.");
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct LobbyClientSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            if (GameSettings.Instance.GameState != GlobalGameState.Lobby)
                return;

            if (!SystemAPI.TryGetSingletonEntity<NetworkId>(
                    out var connectionEntity))
                return;

            LobbyRoster.LocalNetworkId =
                SystemAPI.GetComponent<NetworkId>(connectionEntity).Value;

            if (SystemAPI.HasComponent<LobbyJoinSent>(connectionEntity))
                return;

            var rpcEntity = state.EntityManager.CreateEntity();

            var lobbyJoin = new LobbyJoinRpc();

            lobbyJoin.PlayerName.CopyFromTruncated(
                GameSettings.Instance.PlayerName);
            lobbyJoin.TeamId = GameSettings.Instance.PlayerTeam;

            state.EntityManager.AddComponentData(
                rpcEntity,
                lobbyJoin);

            state.EntityManager.AddComponentData(
                rpcEntity,
                new SendRpcCommandRequest());

            state.EntityManager.AddComponent<LobbyJoinSent>(
                connectionEntity);

            Debug.Log(
                $"[LOBBY CLIENT] Sent lobby join for " +
                $"{GameSettings.Instance.PlayerName}");
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct LobbyServerSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            var registeredNetworkIds = new HashSet<int>();

            foreach (var connectionEvent in
                     SystemAPI.GetSingleton<NetworkStreamDriver>()
                         .ConnectionEventsForTick)
            {
                if (connectionEvent.State != ConnectionState.State.Disconnected)
                    continue;

                int networkId = connectionEvent.Id.Value;
                bool removed = false;

                foreach (var (lobbyPlayer, lobbyPlayerEntity) in
                         SystemAPI.Query<RefRO<LobbyPlayer>>()
                             .WithEntityAccess())
                {
                    if (lobbyPlayer.ValueRO.NetworkId != networkId)
                        continue;

                    ecb.DestroyEntity(lobbyPlayerEntity);
                    removed = true;
                    break;
                }

                if (!removed)
                    continue;

                Debug.Log(
                    $"[LOBBY SERVER] Removing disconnected player " +
                    $"with NetworkId {networkId}.");

                var removalRpc = ecb.CreateEntity();
                ecb.AddComponent(
                    removalRpc,
                    new LobbyPlayerRemovedRpc
                    {
                        NetworkId = networkId
                    });
                ecb.AddComponent(
                    removalRpc,
                    new SendRpcCommandRequest());
            }

            foreach (var existingPlayer in
                     SystemAPI.Query<RefRO<LobbyPlayer>>())
            {
                registeredNetworkIds.Add(
                    existingPlayer.ValueRO.NetworkId);
            }

            foreach (var (request, receiveRequest, entity) in
                     SystemAPI.Query<
                             RefRO<LobbyJoinRpc>,
                             RefRO<ReceiveRpcCommandRequest>>()
                         .WithEntityAccess())
            {
                var playerName =
                    request.ValueRO.PlayerName.ToString();

                var sourceConnection =
                    receiveRequest.ValueRO.SourceConnection;

                if (!state.EntityManager.HasComponent<NetworkId>(
                        sourceConnection))
                {
                    ecb.DestroyEntity(entity);
                    continue;
                }

                var networkId =
                    state.EntityManager
                        .GetComponentData<NetworkId>(
                            sourceConnection).Value;

                if (GameSettings.Instance.GameState == GlobalGameState.InGame)
                {
                    var beginGameRpc = ecb.CreateEntity();

                    ecb.AddComponent(
                        beginGameRpc,
                        new LobbyBeginGameRpc());

                    ecb.AddComponent(
                        beginGameRpc,
                        new SendRpcCommandRequest
                        {
                            TargetConnection = sourceConnection
                        });

                    Debug.Log(
                        $"[LOBBY SERVER] Late join for NetworkId {networkId}; " +
                        "sending begin game signal.");

                    ecb.DestroyEntity(entity);
                    continue;
                }

                bool alreadyRegistered =
                    registeredNetworkIds.Contains(networkId);

                Debug.Log(
                    $"[LOBBY SERVER] Player joined lobby: " +
                    $"{playerName} (NetworkId {networkId})");

                foreach (var existingPlayer in
                         SystemAPI.Query<RefRO<LobbyPlayer>>())
                {
                    var existingPlayerRpc = ecb.CreateEntity();

                    ecb.AddComponent(
                        existingPlayerRpc,
                        new LobbyPlayerRpc
                        {
                            NetworkId = existingPlayer.ValueRO.NetworkId,
                            PlayerName = existingPlayer.ValueRO.PlayerName,
                            IsReady = existingPlayer.ValueRO.IsReady,
                            TeamId = existingPlayer.ValueRO.TeamId
                        });

                    ecb.AddComponent(
                        existingPlayerRpc,
                        new SendRpcCommandRequest
                        {
                            TargetConnection = sourceConnection
                        });
                }

                if (alreadyRegistered)
                {
                    Debug.Log(
                        $"[LOBBY SERVER] Ignoring duplicate lobby join " +
                        $"for NetworkId {networkId}");
                    ecb.DestroyEntity(entity);
                    continue;
                }

                registeredNetworkIds.Add(networkId);

                var lobbyPlayerEntity = ecb.CreateEntity();

                ecb.AddComponent(
                    lobbyPlayerEntity,
                    new LobbyPlayer
                    {
                        NetworkId = networkId,
                        PlayerName = request.ValueRO.PlayerName,
                        IsReady = false,
                        TeamId = request.ValueRO.TeamId == -1 ||
                                 request.ValueRO.TeamId == 0 ||
                                 request.ValueRO.TeamId == 1
                            ? request.ValueRO.TeamId
                            : -1
                    });

                var newPlayerRpc = ecb.CreateEntity();

                ecb.AddComponent(
                    newPlayerRpc,
                    new LobbyPlayerRpc
                    {
                        NetworkId = networkId,
                        PlayerName = request.ValueRO.PlayerName,
                        IsReady = false,
                        TeamId = request.ValueRO.TeamId == -1 ||
                                 request.ValueRO.TeamId == 0 ||
                                 request.ValueRO.TeamId == 1
                            ? request.ValueRO.TeamId
                            : -1
                    });

                ecb.AddComponent(
                    newPlayerRpc,
                    new SendRpcCommandRequest());

                ecb.DestroyEntity(entity);
            }

            foreach (var (readyRequest, receiveRequest, entity) in
                     SystemAPI.Query<
                             RefRO<LobbyReadyRpc>,
                             RefRO<ReceiveRpcCommandRequest>>()
                         .WithEntityAccess())
            {
                var sourceConnection =
                    receiveRequest.ValueRO.SourceConnection;

                var networkId =
                    state.EntityManager
                        .GetComponentData<NetworkId>(
                            sourceConnection).Value;

                bool newReadyState =
                    readyRequest.ValueRO.IsReady;

                Debug.Log(
                    $"[LOBBY SERVER] Player {networkId} ready: {newReadyState}");

                foreach (var lobbyPlayer in
                         SystemAPI.Query<RefRW<LobbyPlayer>>())
                {
                    if (lobbyPlayer.ValueRO.NetworkId != networkId)
                        continue;

                    lobbyPlayer.ValueRW.IsReady = newReadyState;

                    var updateRpcEntity = ecb.CreateEntity();

                    ecb.AddComponent(
                        updateRpcEntity,
                        new LobbyPlayerRpc
                        {
                            NetworkId = lobbyPlayer.ValueRO.NetworkId,
                            PlayerName = lobbyPlayer.ValueRO.PlayerName,
                            IsReady = newReadyState,
                            TeamId = lobbyPlayer.ValueRO.TeamId
                        });

                    ecb.AddComponent(
                        updateRpcEntity,
                        new SendRpcCommandRequest());

                    break;
                }

                ecb.DestroyEntity(entity);
            }

            foreach (var (teamRequest, receiveRequest, entity) in
                     SystemAPI.Query<
                             RefRO<LobbyTeamRpc>,
                             RefRO<ReceiveRpcCommandRequest>>()
                         .WithEntityAccess())
            {
                var sourceConnection = receiveRequest.ValueRO.SourceConnection;
                if (!state.EntityManager.HasComponent<NetworkId>(sourceConnection))
                {
                    ecb.DestroyEntity(entity);
                    continue;
                }

                int networkId = state.EntityManager.GetComponentData<NetworkId>(sourceConnection).Value;
                int teamId = teamRequest.ValueRO.TeamId == -1 ||
                              teamRequest.ValueRO.TeamId == 0 ||
                              teamRequest.ValueRO.TeamId == 1
                    ? teamRequest.ValueRO.TeamId
                    : -1;

                foreach (var lobbyPlayer in SystemAPI.Query<RefRW<LobbyPlayer>>())
                {
                    if (lobbyPlayer.ValueRO.NetworkId != networkId)
                        continue;

                    lobbyPlayer.ValueRW.TeamId = teamId;
                    var updateRpcEntity = ecb.CreateEntity();
                    ecb.AddComponent(updateRpcEntity, new LobbyPlayerRpc
                    {
                        NetworkId = lobbyPlayer.ValueRO.NetworkId,
                        PlayerName = lobbyPlayer.ValueRO.PlayerName,
                        IsReady = lobbyPlayer.ValueRO.IsReady,
                        TeamId = teamId
                    });
                    ecb.AddComponent(updateRpcEntity, new SendRpcCommandRequest());
                    break;
                }

                ecb.DestroyEntity(entity);
            }

            foreach (var (request, receiveRequest, entity) in
                     SystemAPI.Query<
                             RefRO<LobbyStartGameRpc>,
                             RefRO<ReceiveRpcCommandRequest>>()
                         .WithEntityAccess())
            {
                var sourceConnection =
                    receiveRequest.ValueRO.SourceConnection;

               if (!state.EntityManager.HasComponent<NetworkId>(sourceConnection))
               {
                ecb.DestroyEntity(entity);
                continue;
               }
               int requestingNetworkId = state.EntityManager.GetComponentData<NetworkId>(sourceConnection).Value;

               int hostNetworkId = int.MaxValue;
               int playerCount = 0;
               bool everyoneReady = true;
               bool requesterIsRegistered = false;

               foreach (var player in SystemAPI.Query<RefRO<LobbyPlayer>>())
               {
                int playerId = player.ValueRO.NetworkId;
                playerCount++;

                if (playerId < hostNetworkId)
                    hostNetworkId = playerId;

                if (playerId == requestingNetworkId)
                    requesterIsRegistered = true;

                if (!player.ValueRO.IsReady)
                    everyoneReady = false;
               }

               bool isHost = requesterIsRegistered && requestingNetworkId == hostNetworkId;

               if (!isHost || playerCount == 0 || !everyoneReady)
               {
                Debug.LogWarning("[Lobby server] start game rejected");
                ecb.DestroyEntity(entity);
                continue;
               }

               Debug.Log("[Lobby server]Start game approved");

               var beginGameEntity = ecb.CreateEntity();

               ecb.AddComponent(beginGameEntity, new LobbyBeginGameRpc());
               ecb.AddComponent(beginGameEntity, new SendRpcCommandRequest());

               ecb.DestroyEntity(entity);
            }



            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct LobbyRosterClientSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (player, entity) in
                     SystemAPI.Query<RefRO<LobbyPlayerRpc>>()
                         .WithEntityAccess())
            {
                Debug.Log(
                    $"[LOBBY CLIENT] Roster received: " +
                    $"{player.ValueRO.PlayerName} " +
                    $"(NetworkId {player.ValueRO.NetworkId})");

                LobbyRoster.AddOrUpdatePlayer(
                    player.ValueRO.NetworkId,
                    player.ValueRO.PlayerName.ToString(),
                    player.ValueRO.IsReady,
                    player.ValueRO.TeamId);

                ecb.DestroyEntity(entity);
            }

            foreach (var (player, entity) in
                     SystemAPI.Query<RefRO<LobbyPlayerRemovedRpc>>()
                         .WithEntityAccess())
            {
                Debug.Log(
                    $"[LOBBY CLIENT] Removing disconnected player " +
                    $"with NetworkId {player.ValueRO.NetworkId}.");

                LobbyRoster.RemovePlayer(player.ValueRO.NetworkId);
                ecb.DestroyEntity(entity);
            }

            foreach (var (request, entity) in SystemAPI.Query<RefRO<LobbyBeginGameRpc>>().WithEntityAccess())
            {
                Debug.Log("[Lobby client] Begin game signal recieved");
                if (GameManager.Instance != null)
                {
                    GameManager.Instance.EnterGameplayFromLobby();
                }
                ecb.DestroyEntity(entity);
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }
}