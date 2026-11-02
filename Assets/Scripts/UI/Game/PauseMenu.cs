using Unity.Properties;
using System.Globalization;
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
            public const string PausePanel = "PausePanel";
            public const string AudioSettingsPanel = "AudioSettingsPanel";
            public const string SettingsButton = "SettingsButton";
            public const string MasterVolumeSlider = "MasterVolumeSlider";
            public const string MusicVolumeSlider = "MusicVolumeSlider";
            public const string SFXVolumeSlider = "SFXVolumeSlider";
            public const string MenuVolumeSlider = "MenuVolumeSlider";
            public const string AudioSettingsBackButton = "AudioSettingsBackButton";
        }

        static public bool pauseEnabled = false;

        Button m_ResumeButton;
        Button m_MainMenuButton;
        Button m_QuitButton;
        VisualElement m_PausePanel;
        VisualElement m_AudioSettingsPanel;
        Button m_SettingsButton;
        Button m_AudioSettingsBackButton;
        Slider m_MasterVolumeSlider;
        Slider m_MusicVolumeSlider;
        Slider m_SFXVolumeSlider;
        Slider m_MenuVolumeSlider;

        void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            m_PausePanel = root.Q<VisualElement>(UIElementNames.PausePanel);
            m_AudioSettingsPanel = root.Q<VisualElement>(UIElementNames.AudioSettingsPanel);
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

            m_SettingsButton = root.Q<Button>(UIElementNames.SettingsButton);
            m_SettingsButton.clicked += OnSettingsPressed;

            m_AudioSettingsBackButton = root.Q<Button>(UIElementNames.AudioSettingsBackButton);
            m_AudioSettingsBackButton.clicked += OnAudioSettingsBackPressed;

            m_MasterVolumeSlider = ConfigureVolumeSlider(UIElementNames.MasterVolumeSlider, SoundMixer.soundMasterVol);
            m_MusicVolumeSlider = ConfigureVolumeSlider(UIElementNames.MusicVolumeSlider, SoundMixer.soundMusicVol);
            m_SFXVolumeSlider = ConfigureVolumeSlider(UIElementNames.SFXVolumeSlider, SoundMixer.soundSFXVol);
            m_MenuVolumeSlider = ConfigureVolumeSlider(UIElementNames.MenuVolumeSlider, SoundMixer.soundMenuVol);
            ResetAudioSettingsPanel();
        }

        void OnDisable()
        {
            m_ResumeButton.clicked -= OnResumePressed;
            m_MainMenuButton.clicked -= OnMainMenuPressed;
            m_QuitButton.clicked -= OnQuitPressed;
            m_SettingsButton.clicked -= OnSettingsPressed;
            m_AudioSettingsBackButton.clicked -= OnAudioSettingsBackPressed;
            m_MasterVolumeSlider.UnregisterValueChangedCallback(OnMasterVolumeChanged);
            m_MusicVolumeSlider.UnregisterValueChangedCallback(OnMusicVolumeChanged);
            m_SFXVolumeSlider.UnregisterValueChangedCallback(OnSFXVolumeChanged);
            m_MenuVolumeSlider.UnregisterValueChangedCallback(OnMenuVolumeChanged);
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
            {
                TogglePauseMenuVisibility();
            }

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
            if (!GameSettings.Instance.IsPauseMenuOpen)
            {
                ResetAudioSettingsPanel();
            }
            SetGameplayInputEnabled(!GameSettings.Instance.IsPauseMenuOpen);
        }

        void OnResumePressed()
        {
            ClosePauseMenu();
        }

        void ClosePauseMenu()
        {
            GameSettings.Instance.IsPauseMenuOpen = false;
            ResetAudioSettingsPanel();
            SetGameplayInputEnabled(true);
        }

        Slider ConfigureVolumeSlider(string elementName, ConfigVar configVar)
        {
            var slider = GetComponent<UIDocument>().rootVisualElement.Q<Slider>(elementName);
            slider.value = Mathf.Clamp01(configVar.FloatValue);
            switch (elementName)
            {
                case UIElementNames.MasterVolumeSlider:
                    slider.RegisterValueChangedCallback(OnMasterVolumeChanged);
                    break;
                case UIElementNames.MusicVolumeSlider:
                    slider.RegisterValueChangedCallback(OnMusicVolumeChanged);
                    break;
                case UIElementNames.SFXVolumeSlider:
                    slider.RegisterValueChangedCallback(OnSFXVolumeChanged);
                    break;
                case UIElementNames.MenuVolumeSlider:
                    slider.RegisterValueChangedCallback(OnMenuVolumeChanged);
                    break;
            }
            return slider;
        }

        void ResetAudioSettingsPanel()
        {
            m_AudioSettingsPanel.style.display = DisplayStyle.None;
            m_PausePanel.style.display = DisplayStyle.Flex;
        }

        void OnSettingsPressed()
        {
            m_PausePanel.style.display = DisplayStyle.None;
            m_AudioSettingsPanel.style.display = DisplayStyle.Flex;
            m_MasterVolumeSlider.Focus();
        }

        void OnAudioSettingsBackPressed()
        {
            ResetAudioSettingsPanel();
            m_SettingsButton.Focus();
        }

        static void OnMasterVolumeChanged(ChangeEvent<float> evt) =>
            SoundMixer.soundMasterVol.Value = evt.newValue.ToString(CultureInfo.InvariantCulture);

        static void OnMusicVolumeChanged(ChangeEvent<float> evt) =>
            SoundMixer.soundMusicVol.Value = evt.newValue.ToString(CultureInfo.InvariantCulture);

        static void OnSFXVolumeChanged(ChangeEvent<float> evt) =>
            SoundMixer.soundSFXVol.Value = evt.newValue.ToString(CultureInfo.InvariantCulture);

        static void OnMenuVolumeChanged(ChangeEvent<float> evt) =>
            SoundMixer.soundMenuVol.Value = evt.newValue.ToString(CultureInfo.InvariantCulture);

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
