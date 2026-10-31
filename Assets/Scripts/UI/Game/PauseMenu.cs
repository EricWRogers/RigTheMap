using Unity.Properties;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Users;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Unity.MP_FPS
{
    [RequireComponent(typeof(UIDocument))]
    public class PauseMenu : MonoBehaviour
    {
        static class UIElementNames
        {
            public const string ResumeButton = "ResumeButton";
            public const string MainMenuButton = "MainMenuButton";
            public const string QuitButton = "QuitButton";
        }

        static public bool pauseEnabled = false;

        Button m_ResumeButton;
        Button m_MainMenuButton;
        Button m_QuitButton;

        void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            SetGameplayInputEnabled(!GameSettings.Instance.IsPauseMenuOpen);

            root.SetBinding("style.display", new DataBinding
            {
                dataSource = GameSettings.Instance,
                dataSourcePath = new PropertyPath(GameSettings.PauseMenuStylePropertyName),
                bindingMode = BindingMode.ToTarget,
            });
          
            m_ResumeButton = root.Q<Button>(UIElementNames.ResumeButton);
            m_ResumeButton.clicked += OnResumePressed;

            m_MainMenuButton = root.Q<Button>(UIElementNames.MainMenuButton);
            m_MainMenuButton.clicked += OnMainMenuPressed;
            m_MainMenuButton.SetEnabled(GameManager.CanUseMainMenu);

            m_QuitButton = root.Q<Button>(UIElementNames.QuitButton);
            m_QuitButton.clicked += OnQuitPressed;
        }

        void OnDisable()
        {
            m_ResumeButton.clicked -= OnResumePressed;
            m_MainMenuButton.clicked -= OnMainMenuPressed;
            m_QuitButton.clicked -= OnQuitPressed;
        }

        void Update()
        {
            if(pauseEnabled)
            {

            bool pausePressed =
                Keyboard.current != null &&
                Keyboard.current.escapeKey.wasPressedThisFrame;

            if (!pausePressed &&
                Gamepad.current != null &&
                Gamepad.current.startButton.wasPressedThisFrame)
            {
                pausePressed = true;
            }

            if (pausePressed)
                TogglePauseMenuVisibility();

            }
        }

        void TogglePauseMenuVisibility()
        {
            if(EventSystem.current != null)
            {
                var panelRaycaster = transform.parent?.GetComponentInChildren<PanelRaycaster>();

                if(panelRaycaster != null)
                {
                    EventSystem.current.SetSelectedGameObject(panelRaycaster.gameObject);
                }

            }

            // EventSystem.current.SetSelectedGameObject(transform.parent.GetComponentInChildren<PanelRaycaster>().gameObject);
            GameSettings.Instance.IsPauseMenuOpen = !GameSettings.Instance.IsPauseMenuOpen;
            SetGameplayInputEnabled(!GameSettings.Instance.IsPauseMenuOpen);
        }

        static void OnResumePressed()
        {
            GameSettings.Instance.IsPauseMenuOpen = false;
            SetGameplayInputEnabled(true);
        }

        static void SetGameplayInputEnabled(bool enabled)
        {
            if (InputUser.all.Count == 0)
            {
                return;
            }

            var user = InputSystemManager.GetFirstInputUser();
            if (user.actions is not InputSystem_Actions controls)
            {
                return;
            }

            if (enabled)
            {
                controls.Player.Enable();
                controls.FPS.Enable();
            }
            else
            {
                controls.Player.Disable();
                controls.FPS.Disable();
            }
        }

        static void OnMainMenuPressed()
        {
            pauseEnabled = false;
            SetGameplayInputEnabled(true);
            GameManager.Instance.ReturnToMainMenuAsync();
            Utils.SetCursorVisible(true);  
            LobbyRoster.Clear();          
        }

        static void OnQuitPressed() => GameManager.Instance.QuitAsync();
    }
}
