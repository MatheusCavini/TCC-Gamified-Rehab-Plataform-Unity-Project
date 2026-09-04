using UnityEngine;
using UnityEngine.SceneManagement;

namespace HalMenu.Core
{
    /// <summary>
    /// Bootstrap-scene singleton responsible for scene transitions. The Bootstrap
    /// scene (holding this + ROSConnection + DeviceRegistryService + CalibrationService
    /// + GameCatalogService) is never unloaded, so the ROS connection and all
    /// subscriptions survive the menu &lt;-&gt; game scene switch.
    /// </summary>
    public class SceneLoader : MonoBehaviour
    {
        public static SceneLoader Instance { get; private set; }
        public const string MenuSceneName = "MenuScreen";

        private string _activeContentScene;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            LoadMenu();
        }

        public void LoadMenu() => SwapContentScene(MenuSceneName);

        public void LoadGame(string gameSceneName) => SwapContentScene(gameSceneName);

        private void SwapContentScene(string sceneName)
        {
            if (_activeContentScene == sceneName) return;

            if (!string.IsNullOrEmpty(_activeContentScene))
                SceneManager.UnloadSceneAsync(_activeContentScene);

            SceneManager.LoadScene(sceneName, LoadSceneMode.Additive);
            _activeContentScene = sceneName;
        }
    }
}
