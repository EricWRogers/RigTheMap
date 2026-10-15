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
            public const string PlayerList = "PlayerList";
            public const string PlayersHeading = "PlayerHeading";
            public const string StartGameButton = "StartGameButton";
            public const string ReadyButton = "ReadyButton";
        }

        VisualElement m_Root;
        VisualElement m_PlayerList;
        Label m_LobbyTitle;
        Button m_StartGameButton;
        Button m_ReadyButton;
        int m_LastRosterCount = -1;

        void Update()
        {
            if (GameSettings.Instance.GameState != GlobalGameState.Lobby)
                return;

            RefreshPlayerList();
            UpdateStartGameButton();
        }

        void RefreshPlayerList()
        {
            if (m_PlayerList == null)
                return;

            // Remove previously generated player labels.
            var oldPlayers =
                m_PlayerList.Query<Label>(className: "lobby-player").ToList();

            foreach (var label in oldPlayers)
            {
                label.RemoveFromHierarchy();
            }

            // Create one label for every player in the lobby.
            foreach (var player in LobbyRoster.Players)
            {
                var playerLabel = new Label($"{player.Value.PlayerName}  " + $"{(player.Value.IsReady ? "Ready" : "Not Ready")}");

                playerLabel.AddToClassList("lobby-player");

                playerLabel.style.fontSize = 40;
                playerLabel.style.marginTop = 8;
                playerLabel.style.color = Color.white;

                m_PlayerList.Add(playerLabel);
            }
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
            m_PlayerList = m_Root.Q<VisualElement>(UIElementNames.PlayerList);
            m_StartGameButton = m_Root.Q<Button>(UIElementNames.StartGameButton);
            m_ReadyButton = m_Root.Q<Button>(UIElementNames.ReadyButton);

            if (m_LobbyTitle != null)
                m_LobbyTitle.text = $"{GameSettings.Instance.SessionName}'s Lobby";

            if (m_StartGameButton != null)
                m_StartGameButton.clicked += OnStartGamePressed;

            if (m_ReadyButton != null)
                m_ReadyButton.clicked += OnReadyPressed;
        }

       

        void OnDisable()
        {
            if (m_StartGameButton != null)
                m_StartGameButton.clicked -= OnStartGamePressed;

            if (m_ReadyButton != null)
                m_ReadyButton.clicked -= OnReadyPressed;
            LobbyRoster.Clear();
            m_LastRosterCount = -1;

        }

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