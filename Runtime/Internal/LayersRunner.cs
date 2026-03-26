using UnityEngine;

namespace Layers.Unity.Internal
{
    /// <summary>
    /// Hidden MonoBehaviour singleton that hosts coroutines for periodic flush and
    /// remote config polling, and forwards Unity lifecycle callbacks to the main
    /// <see cref="LayersSDK"/> class.
    ///
    /// Created lazily on first access. The GameObject is marked with
    /// <see cref="HideFlags.HideAndDontSave"/> so it does not appear in the
    /// hierarchy and survives scene loads via <see cref="Object.DontDestroyOnLoad"/>.
    /// </summary>
    internal class LayersRunner : MonoBehaviour
    {
        private static LayersRunner _instance;
        private NetworkReachability _lastReachability;

        internal static LayersRunner Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("[Layers]");
                    go.hideFlags = HideFlags.HideAndDontSave;
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<LayersRunner>();
                }
                return _instance;
            }
        }

        private void Start()
        {
            _lastReachability = Application.internetReachability;
        }

        private void Update()
        {
            var current = Application.internetReachability;
            if (_lastReachability == NetworkReachability.NotReachable
                && current != NetworkReachability.NotReachable)
            {
                // Went from offline to online — flush queued events
                LayersSDK.OnReconnected();
            }
            _lastReachability = current;
        }

        /// <summary>
        /// Called by Unity when the app is paused (backgrounded) or resumed (foregrounded).
        /// On mobile platforms this fires when the app enters/exits the background.
        /// </summary>
        private void OnApplicationPause(bool paused)
        {
            if (paused)
                LayersSDK.OnBackgrounded();
            else
                LayersSDK.OnForegrounded();
        }

        /// <summary>
        /// Called by Unity when the application is about to quit.
        /// Triggers a synchronous shutdown to persist queued events.
        /// </summary>
        private void OnApplicationQuit()
        {
            LayersSDK.OnQuitting();
        }

        private void OnDestroy()
        {
            _instance = null;
        }
    }
}
