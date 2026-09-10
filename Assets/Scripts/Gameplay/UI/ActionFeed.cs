using UnityEngine;
using UnityEngine.UIElements;
using Unity.Entities;
using Unity.NetCode;
using Unity.Collections;
using Gameplay.Leaderboard;

namespace Unity.MP_FPS.UI
{
    [RequireComponent(typeof(UIDocument))]
    public class ActionFeed : MonoBehaviour
    {
        public static ActionFeed Instance { get; private set; }

        // Name of the container element in your UXML
        private const string KillFeedContainerName = "ActionFeedContainer";
        private const string RoundStatusName = "RoundStatus";
        private const string TeamStatusName = "TeamStatus";
        private const string MatchWinnerName = "MatchWinner";
        // Name of the USS class for styling individual kill messages
        private const string KillFeedEntryClassName = "action-feed-entry";

        // Duration in milliseconds
        private const long MessageDurationMs = 4000;

        private VisualElement _rootElement;
        private VisualElement _actionFeedContainer;
        private Label _roundStatus;
        private Label _teamStatus;
        private Label _matchWinner;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this.gameObject);
                return;
            }

            Instance = this;
        }

        void OnEnable()
        {
            _rootElement = GetComponent<UIDocument>().rootVisualElement;
            if (_rootElement == null)
            {
                Debug.LogError("KillFeedUI: Could not find root VisualElement.");
                return;
            }

            _actionFeedContainer = _rootElement.Q<VisualElement>(KillFeedContainerName);
            _roundStatus = _rootElement.Q<Label>(RoundStatusName);
            _teamStatus = _rootElement.Q<Label>(TeamStatusName);
            _matchWinner = _rootElement.Q<Label>(MatchWinnerName);

            if (_actionFeedContainer == null)
            {
                Debug.LogError($"KillFeedUI: Could not find VisualElement named '{KillFeedContainerName}'.");
                return;
            }

            if (_roundStatus == null)
            {
                Debug.LogError($"KillFeedUI: Could not find VisualElement named '{RoundStatusName}'.");
                return;
            }

            if (_teamStatus == null)
            {
                Debug.LogError($"KillFeedUI: Could not find VisualElement named '{TeamStatusName}'.");
                return;
            }

            if (_matchWinner == null)
            {
                Debug.LogError($"KillFeedUI: Could not find VisualElement named '{MatchWinnerName}'.");
                return;
            }
        }

        private void Update() {
            if (LeaderboardManager.Instance == null) 
               return;

            var leaderboard = LeaderboardManager.Instance;

            UpdateRoundUI(leaderboard);
            UpdateTeamUI(leaderboard);
        }

        private void UpdateRoundUI(LeaderboardManager leaderboard)
        {
            if (_roundStatus == null) return;

            switch (leaderboard.CurrentPhase)
            {
                case LeaderboardManager.RoundPhase.Fighting:
                    _roundStatus.text = $"Round {leaderboard.CurrentRound}";

                    if (_matchWinner != null)
                        _matchWinner.text ="";
                    
                    break;
                case LeaderboardManager.RoundPhase.BuildMode:
                    _roundStatus.text = $"Build Mode\nNext Round In: {Mathf.CeilToInt(leaderboard.BuildTimer)}";

                    if (_matchWinner != null)
                        _matchWinner.text = "";

                    break;
                case LeaderboardManager.RoundPhase.MatchOver:
                    _roundStatus.text = $"Match Over";

                    UpdateWinnerText(leaderboard);

                    break;
                
            }
        }
        private void UpdateTeamUI(LeaderboardManager leaderboard)
        {
            if (_teamStatus == null)
                return;

            _teamStatus.text = "TEAM: --";

            var world = ClientServerBootstrap.ClientWorld;

            if (world == null)
                return;

            var entityManager = world.EntityManager;

            var networkIdQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<NetworkId>()
            );

            if (networkIdQuery.IsEmptyIgnoreFilter)
            {
                networkIdQuery.Dispose();
                return;
            }

            var localNetworkId =
                networkIdQuery.GetSingleton<NetworkId>().Value;

            networkIdQuery.Dispose();

            var playerQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PlayerTeam>(),
                ComponentType.ReadOnly<GhostOwner>(),
                ComponentType.ReadOnly<PredictedPlayerGhost>()
            );

            if (playerQuery.IsEmptyIgnoreFilter)
            {
                playerQuery.Dispose();
                return;
            }

            using (var entities =
                   playerQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (var entity in entities)
                {
                    if (!entityManager.Exists(entity))
                        continue;

                    var ghostOwner =
                        entityManager.GetComponentData<GhostOwner>(entity);

                    if (ghostOwner.NetworkId != localNetworkId)
                        continue;

                    var playerTeam =
                        entityManager.GetComponentData<PlayerTeam>(entity);

                    if (playerTeam.TeamId == 0)
                    {
                        _teamStatus.text = "TEAM: BLUE";
                    }
                    else if (playerTeam.TeamId == 1)
                    {
                        _teamStatus.text = "TEAM: GREEN";
                    }

                    playerQuery.Dispose();
                    return;
                }
            }

            playerQuery.Dispose();
        }

        private void UpdateWinnerText(LeaderboardManager leaderboard)
        {
            if (_matchWinner == null)
                return;

            if (leaderboard.LastWinningTeamId == 0)
            {
                _matchWinner.text = "BLUE TEAM WINS!";
            }
            else if (leaderboard.LastWinningTeamId == 1)
            {
                _matchWinner.text = "GREEN TEAM WINS!";
            }
            else
            {
                _matchWinner.text = "WINNER: UNKNOWN";
            }
        }


        public void AnnouncePlayerJoined(string playerName)
        {
            if (_actionFeedContainer == null) return;

            var killLabel = new Label($"{playerName} joined");
            killLabel.AddToClassList(KillFeedEntryClassName); // Apply USS style

            _actionFeedContainer.Add(killLabel);

            killLabel.schedule.Execute(() =>
            {
                if (killLabel.parent == _actionFeedContainer)
                {
                    killLabel.RemoveFromHierarchy();
                }
            }).StartingIn(MessageDurationMs);
        }

        public void AnnounceKill(string killer, string victim)
        {
            if (_actionFeedContainer == null) return;

            var killLabel = new Label($"{killer} killed {victim}");
            killLabel.AddToClassList(KillFeedEntryClassName);

            _actionFeedContainer.Add(killLabel);

            killLabel.schedule.Execute(() =>
            {
                if (killLabel.parent == _actionFeedContainer)
                {
                    killLabel.RemoveFromHierarchy();
                }
            }).StartingIn(MessageDurationMs);
        }
    }
}