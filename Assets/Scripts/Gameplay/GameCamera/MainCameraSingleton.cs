using UnityEngine;

namespace Unity.MP_FPS
{
    /// <summary>
    /// This class allows the <see cref="MainCameraSystem"/> to sync its position to the current player character position in the Client World.
    /// </summary>
    [RequireComponent(typeof(Camera), typeof(AudioListener))]
    public class MainCameraSingleton : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init()
        {
            if (Instance != null)
            {
                Destroy(Instance);
                Instance = null;
            }
        }
        
        public static Camera Instance;
        public AudioListener AudioListener { get; private set; } = null;

        void Awake()
        {
            var camera = GetComponent<Camera>();

            if (Instance != null && Instance != camera)
            {
                Instance.enabled = false;

                var existingAudioListener = Instance.GetComponent<AudioListener>();
                if (existingAudioListener != null)
                    existingAudioListener.enabled = false;
            }

            Instance = camera;
            AudioListener = GetComponent<AudioListener>();
        }

        void OnDestroy()
        {
            if (Instance == GetComponent<Camera>())
            {
                Instance = null;
            }
        }
    }
}