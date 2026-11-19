using Unity.Entities;
using Unity.Netcode;
using UnityEngine;
using Unity.Transforms;
using Unity.Collections;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Gameplay.Leaderboard;

namespace Unity.MP_FPS.UI
{
    [RequireComponent(typeof(UIDocument))]
    public class RespawnScreen : MonoBehaviour
    {
        public Camera RespawnCamera;

        [Header("Spectator Camera")]
        public Camera SpectatorCamera;
        [SerializeField] private float spectatorSpeed = 10f;
        [SerializeField] private float spectatorSprintSpeed = 20f;
        [SerializeField] private float spectatorMouseSensitivity = 2f;

        private bool m_IsSpectating = false;
        private float m_SpectatorPitch;
        private float m_SpectatorYaw;
        private int m_LocalTeamId = -1;

        private VisualElement m_RespawnScreen;
        private Label m_RespawnTimerLabel;

        private World m_ClientWorld;
        private EntityManager m_EntityManager;
        private EntityQuery m_LocalPlayerQuery;
        private EntityQuery m_LocalConnectionQuery;

        private float m_RespawnCountdown;
        private const float RESPAWN_DURATION = 5.0f;
        private bool m_ShowBuildModeMessage;

        private void Awake()
        {
            RespawnCamera.gameObject.SetActive(false);
            SpectatorCamera.gameObject.SetActive(false);
        }

        void OnEnable()
        {
            m_RespawnScreen = GetComponent<UIDocument>().rootVisualElement;
            m_RespawnTimerLabel = m_RespawnScreen.Q<Label>("RespawnMessage");
            LeaderboardManager.BuildModeChanged += OnBuildModeChanged;
            LeaderboardManager.PlayerELiminated += OnPlayerEliminated;
        }

        private void OnDisable()
        {
            LeaderboardManager.BuildModeChanged -= OnBuildModeChanged;
            LeaderboardManager.PlayerELiminated -= OnPlayerEliminated;
        }

        private void OnBuildModeChanged(bool isBuildMode)
        {
            m_ShowBuildModeMessage = isBuildMode;
            Debug.Log($"[Spectator] BuildModeChanged received: {isBuildMode}, Spectating: {m_IsSpectating}");
            if (isBuildMode)
            {
                m_IsSpectating = false;
                SpectatorCamera.gameObject.SetActive(false);
                Debug.Log($"[Spectator] Spectator disabled, build mode started.");
            }
        }
        
        private void OnPlayerEliminated(int eliminatedNetworkId)
        {
            if (LeaderboardManager.Instance != null && LeaderboardManager.Instance.CurrentPhase != LeaderboardManager.RoundPhase.Fighting)
            {
                Debug.Log("[Spectator] Ignoiring elimination outside fighting");
                return;
            }
            if (m_ClientWorld == null || !m_ClientWorld.IsCreated)
                return;

            if (m_LocalConnectionQuery.IsEmptyIgnoreFilter)
                return;
            
            Entity connectionEntity = m_LocalConnectionQuery.GetSingletonEntity();
            int localNetworkId = m_EntityManager.GetComponentData<NetworkId>(connectionEntity).Value;

            if (eliminatedNetworkId != localNetworkId)
                return;
            
            Debug.Log("[Spectator] Enabling spectator mode! Frame: {Time.frameCount}");
            MoveCameraToSpawnPoint();
            m_IsSpectating = true;
            //swithc to spectator
            RespawnCamera.gameObject.SetActive(false);
            SpectatorCamera.gameObject.SetActive(true);
            //set rotation
            Vector3 angles = SpectatorCamera.transform.eulerAngles;
            m_SpectatorYaw = angles.y;
            m_SpectatorPitch = Mathf.DeltaAngle(0f, angles.x);
            Debug.Log($"[Spectator] Camera active: {SpectatorCamera.gameObject.activeInHierarchy}");
        }
        private void InitializeEcs()
        {
            foreach (var world in World.All)
            {
                if (world.IsClient())
                {
                    m_ClientWorld = world;
                    m_EntityManager = world.EntityManager;
                    break;
                }
            }

            if (m_EntityManager != null)
            {
                m_LocalPlayerQuery = m_EntityManager.CreateEntityQuery(
                    ComponentType.ReadOnly<PredictedPlayerGhost>(),
                    ComponentType.ReadOnly<GhostOwnerIsLocal>()
                );
                m_LocalConnectionQuery = m_EntityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>(),ComponentType.ReadOnly<NetworkStreamConnection>());
            }
        }

        void LateUpdate()
        {
            if (GameSettings.Instance.GameState != GlobalGameState.InGame)
            {
                m_RespawnScreen.style.display = DisplayStyle.None;
                return;
            }

            if (m_ClientWorld == null || !m_ClientWorld.IsCreated)
            {
                InitializeEcs();
                if (m_ClientWorld == null) return;
            }

            bool isPlayerAlive = m_LocalPlayerQuery.HasSingleton<PredictedPlayerGhost>();

            if (isPlayerAlive && !m_IsSpectating)
            {
                if (m_IsSpectating)
                {
                    Debug.Log("[Spectator] Spectator mode is being disabled because the local player ghost still exists!");
                }
                m_IsSpectating = false;
                //remember players team before character is destroyed
                using (var playerEntities = m_LocalPlayerQuery.ToEntityArray(Unity.Collections.Allocator.Temp))
                {
                    if (playerEntities.Length == 1)
                    {
                        Entity playerEntity = playerEntities[0];

                        if (m_EntityManager.HasComponent<PlayerTeam>(playerEntity))
                        {
                            m_LocalTeamId = m_EntityManager.GetComponentData<PlayerTeam>(playerEntity).TeamId;
                        }
                    }
                }
                RespawnCamera.gameObject.SetActive(false);
                // Player is alive, hide the respawn screen
                if (m_RespawnScreen.style.display == DisplayStyle.Flex)
                {
                    m_RespawnScreen.style.display = DisplayStyle.None;
                }
            }
            else
            {
                RespawnCamera.gameObject.SetActive(!m_IsSpectating);
                SpectatorCamera.gameObject.SetActive(m_IsSpectating);
                // spectator mode
                if (m_IsSpectating)
                {
                    m_RespawnScreen.style.display = DisplayStyle.None;
                    UpdateSpectatorCamera();
                    return;
                }
                // Player is dead, show the respawn screen and update the timer
                if (m_RespawnScreen.style.display == DisplayStyle.None)
                {
                    // This is the first frame death is detected, start the countdown
                    m_RespawnCountdown = RESPAWN_DURATION;
                    m_RespawnScreen.style.display = DisplayStyle.Flex;
                }

                m_RespawnCountdown -= Time.deltaTime;
                if (m_RespawnCountdown < 0)
                {
                    m_RespawnCountdown = 0;
                }

                string message = m_ShowBuildModeMessage
                    ? $"BUILD MODE START IN {Mathf.CeilToInt(m_RespawnCountdown)}"
                    : $"RESPAWNING IN {Mathf.CeilToInt(m_RespawnCountdown)}";

                m_RespawnTimerLabel.text = message;            
            }
            UpdateSpectatorCamera();
        }
        
        private void UpdateSpectatorCamera()
        {
            if (!m_IsSpectating)
                return;

            //Mouse look
            if (Mouse.current != null)
            {
                Vector2 mouseDelta = Mouse.current.delta.ReadValue();

                m_SpectatorYaw += mouseDelta.x * spectatorMouseSensitivity * 0.1f;
                m_SpectatorPitch -= mouseDelta.y * spectatorMouseSensitivity * 0.1f;
                m_SpectatorPitch = Mathf.Clamp(m_SpectatorPitch, -90f, 90f);
            }

            SpectatorCamera.transform.rotation = Quaternion.Euler(
                m_SpectatorPitch,
                m_SpectatorYaw,
                0f
            );

            
            if (Keyboard.current == null)
                return;

            Keyboard keyboard = Keyboard.current;

            float horizontal = 0f;
            float forward = 0f;
            float vertical = 0f;

            //WASD movement
            if (keyboard.aKey.isPressed)
                horizontal -= 1f;

            if (keyboard.dKey.isPressed)
                horizontal += 1f;

            if (keyboard.wKey.isPressed)
                forward += 1f;

            if (keyboard.sKey.isPressed)
                forward -= 1f;

            //Fly up and down
            if (keyboard.spaceKey.isPressed)
                vertical += 1f;

            if (keyboard.leftCtrlKey.isPressed)
                vertical -= 1f;

            Vector3 movement =
                SpectatorCamera.transform.right * horizontal +
                SpectatorCamera.transform.forward * forward +
                Vector3.up * vertical;

            //Sprint 
            float speed = keyboard.leftShiftKey.isPressed
                ? spectatorSprintSpeed
                : spectatorSpeed;

            SpectatorCamera.transform.position +=
                movement.normalized * speed * Time.deltaTime;
        }

        private void MoveCameraToSpawnPoint()
        {
            if (m_ClientWorld == null || !m_ClientWorld.IsCreated)
                return;

            if (m_LocalTeamId < 0)
                return;
            
            using (EntityQuery spawnQuery = m_EntityManager.CreateEntityQuery(ComponentType.ReadOnly<SpawnPoint>(),ComponentType.ReadOnly<LocalToWorld>()))
            {
                using (var spawnEntities = spawnQuery.ToEntityArray(Allocator.Temp))
                {
                    foreach (Entity spawnEntity in spawnEntities)
                    {
                        SpawnPoint spawnPoint = m_EntityManager.GetComponentData<SpawnPoint>(spawnEntity);
                        if (spawnPoint.TeamId != m_LocalTeamId)
                            continue;
                        
                        LocalToWorld spawnTransform = m_EntityManager.GetComponentData<LocalToWorld>(spawnEntity);
                        SpectatorCamera.transform.SetPositionAndRotation((Vector3)spawnTransform.Position + Vector3.up * 2f, (Quaternion)spawnTransform.Rotation);
                        Debug.Log($"[Spectator] Camera moved to Team {m_LocalTeamId} spawn point.");
                        return;
                    }
                }
            }
            Debug.Log($"[Spectator] No spawn point found.");
        }
    }
}