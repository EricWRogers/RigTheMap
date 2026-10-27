using System.Collections.Generic;
using System.IO;
using Unity.Properties;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    [RequireComponent(typeof(UIDocument))]
    public class MainMenu : MonoBehaviour
    {
        static class UIElementNames
        {
            public const string HidingBackground = "HidingBackground";
            public const string NameInputField = "PlayerNameField";
            public const string MapInputField = "MapNameField";
            public const string CreateGame = "CreateGame";
            public const string JoinGame = "JoinGame";
            public const string JoinCodeField = "JoinCodeField";
            public const string QuitGame = "QuitButton";
            public const string SurveyButton = "SurveyButton";
        }

        VisualElement m_MainMenu;
        DropdownField m_MapNameField;
        Button m_CreateGameButton;
        Button m_JoinGameButton;
        TextField m_JoinCodeField;
        Button m_QuitButton;
        Button m_SurveyButton;

        void OnEnable()
        {
            m_MainMenu = GetComponent<UIDocument>().rootVisualElement;

            m_MainMenu.SetBinding("style.display", new DataBinding
            {
                dataSource = GameSettings.Instance,
                dataSourcePath = new PropertyPath(GameSettings.MainMenuStylePropertyName),
                bindingMode = BindingMode.ToTarget,
            });

            var nameInputField = m_MainMenu.Q<TextField>(UIElementNames.NameInputField);
            nameInputField.SetBinding("value", new DataBinding
            {
                dataSource = GameSettings.Instance,
                dataSourcePath = new PropertyPath(nameof(GameSettings.PlayerName)),
                bindingMode = BindingMode.TwoWay,
            });

            m_MapNameField = m_MainMenu.Q<DropdownField>(UIElementNames.MapInputField);
            m_MapNameField.choices = GetBuildSceneNames();
            m_MapNameField.SetBinding("value", new DataBinding
            {
                dataSource = GameSettings.Instance,
                dataSourcePath = new PropertyPath(nameof(GameSettings.MapName)),
                bindingMode = BindingMode.TwoWay,
            });

            m_JoinCodeField = m_MainMenu.Q<TextField>(UIElementNames.JoinCodeField);
            m_JoinCodeField.SetBinding("value", new DataBinding
            {
                dataSource = ConnectionSettings.Instance,
                dataSourcePath = new PropertyPath(nameof(ConnectionSettings.SessionCode)),
                bindingMode = BindingMode.TwoWay,
            });

            m_CreateGameButton = m_MainMenu.Q<Button>(UIElementNames.CreateGame);
            m_CreateGameButton.clicked += OnCreateGamePressed;

            m_JoinGameButton = m_MainMenu.Q<Button>(UIElementNames.JoinGame);
            m_JoinGameButton.clicked += OnJoinGamePressed;

            m_QuitButton = m_MainMenu.Q<Button>(UIElementNames.QuitGame);
            m_QuitButton.clicked += OnQuitPressed;

            m_SurveyButton = m_MainMenu.Q<Button>(UIElementNames.SurveyButton);
            m_SurveyButton.clicked += OnSurveyPressed;

            var hidingBackground = m_MainMenu.Q<VisualElement>(UIElementNames.HidingBackground);
            hidingBackground.SetBinding("style.display", new DataBinding
            {
                dataSource = GameSettings.Instance,
                dataSourcePath = new PropertyPath(GameSettings.MainMenuSceneLoadedPropertyName),
                bindingMode = BindingMode.ToTarget,
            });

        }

        static List<string> GetBuildSceneNames()
        {
            var sceneNames = new List<string>();
            for (var buildIndex = 0; buildIndex < SceneManager.sceneCountInBuildSettings; buildIndex++)
            {
                var scenePath = SceneUtility.GetScenePathByBuildIndex(buildIndex);
                if (string.IsNullOrEmpty(scenePath))
                {
                    Debug.LogWarning($"Build scene at index {buildIndex} has no path and will not be available as a map.");
                    continue;
                }

                sceneNames.Add(Path.GetFileNameWithoutExtension(scenePath));
            }

            // Debug Scenes to remove
            #if !UNITY_EDITOR
            sceneNames.Remove("MainMenu");
            sceneNames.Remove("GameResourcesSubScene");
            sceneNames.Remove("Persistents");
            sceneNames.Remove("SpawnPointsSubScene");
            sceneNames.Remove("CamsScene");
            sceneNames.Remove("ChloesScene");
            sceneNames.Remove("FarmSpawnPoints");
            sceneNames.Remove("ElijahsScene");
            #endif
                

            if (sceneNames.Count == 0)
                Debug.LogError("No scenes are enabled in Build Settings, so no maps are available.");

            return sceneNames;
        }

        void OnDisable()
        {
            m_CreateGameButton.clicked -= OnCreateGamePressed;
            m_JoinGameButton.clicked -= OnJoinGamePressed;
            m_QuitButton.clicked -= OnQuitPressed;
            m_SurveyButton.clicked -= OnSurveyPressed;
        }

        void Update()
        {
            if (GameSettings.Instance.GameState == GlobalGameState.MainMenu &&
                (!UnityEngine.Cursor.visible || UnityEngine.Cursor.lockState != CursorLockMode.None))
            {
                Utils.SetCursorVisible(true);
            }

            if (!GameInput.Actions.UI.Navigate.WasPressedThisFrame() ||
                GameInput.Actions.UI.Navigate.activeControl?.device is not Gamepad ||
                m_MainMenu.panel?.focusController.focusedElement is Button ||
                !m_CreateGameButton.enabledInHierarchy)
            {
                return;
            }

            m_CreateGameButton.Focus();
            Utils.SetCursorVisible(true);
        }

        void Start()
        {
            QualitySettings.vSyncCount = 1;
        }

        static void OnCreateGamePressed() => GameManager.Instance.StartGameAsync(CreationType.CreateRelay);

        void OnJoinGamePressed()
        {
            ConnectionSettings.Instance.SessionCode =
                (ConnectionSettings.Instance.SessionCode ?? string.Empty).Trim().ToUpperInvariant();

            if (!ConnectionSettings.Instance.IsSessionCodeFormatValid)
            {
                Debug.LogWarning("A valid six-character join code is required.");
                return;
            }

            GameManager.Instance.StartGameAsync(CreationType.JoinRelay);
        }

        static void OnQuitPressed() => GameManager.Instance.QuitAsync();

        static void OnSurveyPressed()
        {
            Application.OpenURL("https://forms.gle/fATHHYG63Cs5Knuo9");
        }
    }
}
