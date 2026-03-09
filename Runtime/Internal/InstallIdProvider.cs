using UnityEngine;

namespace Layers.Unity.Internal
{
    /// <summary>
    /// Generates and persists a unique install ID via PlayerPrefs.
    /// The install ID survives app restarts but not app reinstalls.
    /// </summary>
    internal static class InstallIdProvider
    {
        private const string Key = "layers_install_id";
        private static string _cachedId;

        internal static string GetOrCreate()
        {
            if (_cachedId != null) return _cachedId;

            _cachedId = PlayerPrefs.GetString(Key, null);
            if (string.IsNullOrEmpty(_cachedId))
            {
                _cachedId = System.Guid.NewGuid().ToString();
                PlayerPrefs.SetString(Key, _cachedId);
                PlayerPrefs.Save();
            }
            return _cachedId;
        }
    }
}
