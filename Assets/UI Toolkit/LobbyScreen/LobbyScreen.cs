using System.Collections.Generic;
using UnityEngine;
using Unity.Properties;
using UnityEngine.UIElements;


namespace Unity.MP_FPS.Client
{
    public class LobbyScreen : MonoBehaviour
    {
        static class UIElementNames
        {
            public const string LobbyTitle = "LobbyTitle";
            public const string JoinCode = "JoinCode";
            public const string PlayerList = "PlayerList";
            public const string PlayersHeading = "PlayerHeading";
            public const string PlayerRosterScrollView = "PlayerRosterScrollView";
            public const string StartGameButton = "StartGameButton";
            public const string ReadyButton = "ReadyButton";
        }

        VisualElement m_Root;
        VisualElement m_PlayerList;
        Label m_LobbyTitle;
        Label m_JoinCode;
        Button m_StartGameButton;
        Button m_ReadyButton;
        int m_LastRosterChangeVersion = -1;

        void Update()
        {
            if (GameSettings.Instance.GameState != GlobalGameState.Lobby)
                return;

            PauseMenu.pauseEnabled = true;

            if (m_LastRosterChangeVersion != LobbyRoster.ChangeVersion)
            {
                RefreshPlayerList();
                m_LastRosterChangeVersion = LobbyRoster.ChangeVersion;
            }
            UpdateStartGameButton();
            if (m_JoinCode != null)
                m_JoinCode.text = $"Join Code: {ConnectionSettings.Instance.SessionCode}";

            if (m_ReadyButton != null)
            {
                m_ReadyButton.text = LobbyRoster.LocalPlayerReady ? "Cancel" : "Ready";
            }
        }

        void RefreshPlayerList()
        {
            if (m_PlayerList == null)
                return;

            // Remove previously generated player labels.
            var oldPlayers =
                m_PlayerList.Query<VisualElement>(className: "lobby-player").ToList();

            foreach (var label in oldPlayers)
            {
                label.RemoveFromHierarchy();
            }

            // Create one label for every player in the lobby.
            foreach (var player in LobbyRoster.Players)
            {
                var playerRow = new VisualElement();
                playerRow.AddToClassList("lobby-player");
                playerRow.style.flexDirection = FlexDirection.Row;
                playerRow.style.alignItems = Align.Center;
                playerRow.style.marginTop = 8;
                playerRow.style.width = Length.Percent(100);
                playerRow.style.flexShrink = 1;

                var playerNameLabel = new Label(player.Value.PlayerName);
                playerNameLabel.style.fontSize = 30;
                playerNameLabel.style.color = Color.white;
                playerNameLabel.style.whiteSpace = WhiteSpace.Normal;
                playerNameLabel.style.flexGrow = 1;
                playerNameLabel.style.flexShrink = 1;
                playerNameLabel.style.minWidth = 0;
                playerRow.Add(playerNameLabel);

                var readyLabel = new Label(player.Value.IsReady ? "Ready" : "Not Ready");
                readyLabel.style.width = 180;
                readyLabel.style.fontSize = 30;
                readyLabel.style.color = Color.white;
                readyLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                playerRow.Add(readyLabel);

                if (player.Key == LobbyRoster.LocalNetworkId)
                {
                    playerRow.Add(CreateTeamSelection(player.Value.TeamId));
                }
                else
                {
                    playerRow.Add(CreateTeamDisplay(player.Value.TeamId));
                }

                
                m_PlayerList.Add(playerRow);
            }
        }

        VisualElement CreateTeamSelection(int teamId)
        {
            var teamColumn = CreateTeamColumn();
            var teamLabel = CreateTeamLabel();
            var teamSelection = new DropdownField(
                new List<string> { "Auto", "Green", "Blue" },
                TeamName(teamId));

            teamSelection.style.flexShrink = 0;
            teamSelection.style.width = 180;
            teamSelection.style.fontSize = 24;
            teamSelection.style.alignSelf = Align.Center;
            teamSelection.RegisterValueChangedCallback(changeEvent =>
            {
                LobbyRoster.SetTeam(changeEvent.newValue switch
                {
                    "Green" => 1,
                    "Blue" => 0,
                    _ => -1
                });
            });

            teamColumn.Add(teamLabel);
            teamColumn.Add(teamSelection);
            return teamColumn;
        }

        VisualElement CreateTeamDisplay(int teamId)
        {
            var teamColumn = CreateTeamColumn();
            var teamLabel = CreateTeamLabel();
            var teamValue = new Label(TeamName(teamId));
            teamValue.style.width = 180;
            teamValue.style.fontSize = 32;
            teamValue.style.color = Color.white;
            teamValue.style.unityTextAlign = TextAnchor.MiddleCenter;
            teamColumn.Add(teamLabel);
            teamColumn.Add(teamValue);
            return teamColumn;
        }

        static VisualElement CreateTeamColumn()
        {
            var teamColumn = new VisualElement();
            teamColumn.style.width = 280;
            teamColumn.style.flexShrink = 0;
            teamColumn.style.flexDirection = FlexDirection.Row;
            teamColumn.style.alignItems = Align.Center;
            teamColumn.style.marginLeft = StyleKeyword.Auto;
            return teamColumn;
        }

        static Label CreateTeamLabel()
        {
            var teamLabel = new Label("Team:");
            teamLabel.style.width = 100;
            teamLabel.style.fontSize = 24;
            teamLabel.style.color = Color.white;
            teamLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            return teamLabel;
        }

        void UpdateStartGameButton()
        {
            if (m_StartGameButton == null)
                return;

            bool isHost = GameManager.Instance != null && GameManager.Instance.IsLobbyHost;

            //only the host can see start game
            m_StartGameButton.style.display = isHost ? DisplayStyle.Flex : DisplayStyle.None;

            //require eveyone to be ready
            bool everyoneReady = LobbyRoster.Players.Count > 0;

            foreach (var player in LobbyRoster.Players.Values)
            {
                if (!player.IsReady)
                {
                    everyoneReady = false;
                    break;
                }
            }
            m_StartGameButton.SetEnabled(isHost && everyoneReady);
        }

        

        void OnEnable()
        {
            m_Root = GetComponent<UIDocument>().rootVisualElement;

            // Show this UI only while in the Lobby state
            m_Root.SetBinding("style.display", new DataBinding
            {
                dataSource = GameSettings.Instance,
                dataSourcePath = new PropertyPath(GameSettings.LobbyStylePropertyName),
                bindingMode = BindingMode.ToTarget,
            });

            m_LobbyTitle = m_Root.Q<Label>(UIElementNames.LobbyTitle);
            m_JoinCode = m_Root.Q<Label>(UIElementNames.JoinCode);
            var playerRosterScrollView =
                m_Root.Q<ScrollView>(UIElementNames.PlayerRosterScrollView);
            m_PlayerList = playerRosterScrollView?.contentContainer;
            m_StartGameButton = m_Root.Q<Button>(UIElementNames.StartGameButton);
            m_ReadyButton = m_Root.Q<Button>(UIElementNames.ReadyButton);

            if (m_LobbyTitle != null)
                m_LobbyTitle.text = "Game Lobby";

            if (m_JoinCode != null)
                m_JoinCode.text = $"Join Code: {ConnectionSettings.Instance.SessionCode}";

            if (m_StartGameButton != null)
                m_StartGameButton.clicked += OnStartGamePressed;

            if (m_ReadyButton != null)
                m_ReadyButton.clicked += OnReadyPressed;
            
            if (m_ReadyButton != null)
            {
                m_ReadyButton.text = "Ready";
            }

            m_LastRosterChangeVersion = -1;

        }

       

        void OnDisable()
        {
            if (m_StartGameButton != null)
                m_StartGameButton.clicked -= OnStartGamePressed;

            if (m_ReadyButton != null)
                m_ReadyButton.clicked -= OnReadyPressed;
            LobbyRoster.Clear();
            // m_LastRosterCount = -1;

        }

        static string TeamName(int teamId) => teamId switch
        {
            0 => "Blue",
            1 => "Green",
            _ => "Auto"
        };

        void OnStartGamePressed()
        {
            LobbyRoster.RequestStartGame();
        }
      void OnReadyPressed()
        {
            LobbyRoster.ToggleReady();

            if (m_ReadyButton != null)
            {
                m_ReadyButton.text =
                    LobbyRoster.LocalPlayerReady
                        ? "Cancel"
                        : "Ready";
            }
        }
    }
}