#if UNITY_ANDROID
using System.IO;
using System.Xml;
using UnityEditor.Android;
using UnityEngine;

namespace Layers.Unity.Editor
{
    /// <summary>
    /// Android post-build processor for the Layers SDK.
    /// Modifies the exported Gradle project's AndroidManifest.xml to add
    /// deep link intent filters to the main UnityPlayerActivity.
    ///
    /// Reads configuration from a <see cref="LayersSettings"/> ScriptableObject
    /// in any Resources folder. If no settings asset exists, the processor is skipped.
    ///
    /// Intent filter behavior:
    ///   - https/http schemes get android:autoVerify="true" for App Links
    ///   - Custom schemes (e.g., "myapp") do not get autoVerify
    ///   - All filters include ACTION_VIEW + CATEGORY_DEFAULT + CATEGORY_BROWSABLE
    ///   - Existing intent filters are preserved; duplicates are skipped
    /// </summary>
    public class LayersAndroidPostBuildProcessor : IPostGenerateGradleAndroidProject
    {
        /// <summary>
        /// Callback order. Runs after Unity's own processing (order 0).
        /// </summary>
        public int callbackOrder => 100;

        // XML namespace for android: attributes
        private const string AndroidNs = "http://schemas.android.com/apk/res/android";

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            var settings = LayersSettings.Instance;
            if (settings == null)
            {
                Debug.Log("[Layers] No LayersSettings asset found in Resources. " +
                          "Skipping Android post-build processing. " +
                          "Create one via Assets > Create > Layers > Settings.");
                return;
            }

            if (settings.intentFilters == null || settings.intentFilters.Length == 0)
                return;

            // Unity exports the Gradle project with the manifest at:
            //   <path>/src/main/AndroidManifest.xml
            string manifestPath = Path.Combine(path, "src", "main", "AndroidManifest.xml");

            if (!File.Exists(manifestPath))
            {
                Debug.LogWarning(
                    $"[Layers] AndroidManifest.xml not found at expected path: {manifestPath}");
                return;
            }

            var doc = new XmlDocument();
            doc.PreserveWhitespace = true;
            doc.Load(manifestPath);

            var nsMgr = new XmlNamespaceManager(doc.NameTable);
            nsMgr.AddNamespace("android", AndroidNs);

            // Find the main Activity (UnityPlayerActivity or the launcher activity)
            XmlNode mainActivity = FindMainActivity(doc, nsMgr);

            if (mainActivity == null)
            {
                Debug.LogWarning(
                    "[Layers] Could not find main Activity in AndroidManifest.xml. " +
                    "Deep link intent filters were not added.");
                return;
            }

            int added = 0;

            foreach (var filter in settings.intentFilters)
            {
                if (string.IsNullOrWhiteSpace(filter.scheme))
                {
                    Debug.LogWarning(
                        "[Layers] Intent filter missing required 'scheme' field. Skipping.");
                    continue;
                }

                // Idempotency: skip if an identical intent filter already exists
                if (IntentFilterExists(mainActivity, nsMgr, filter))
                    continue;

                AddIntentFilter(doc, mainActivity, filter);
                added++;
            }

            if (added > 0)
            {
                doc.Save(manifestPath);
                Debug.Log($"[Layers] Added {added} intent filter(s) to AndroidManifest.xml.");
            }
            else
            {
                Debug.Log("[Layers] All intent filters already present. No changes needed.");
            }
        }

        /// <summary>
        /// Find the main/launcher Activity node in the manifest.
        /// Looks for an activity with a LAUNCHER intent filter first,
        /// then falls back to com.unity3d.player.UnityPlayerActivity.
        /// </summary>
        private static XmlNode FindMainActivity(XmlDocument doc, XmlNamespaceManager nsMgr)
        {
            // Try to find the activity with LAUNCHER category (the real main activity)
            XmlNodeList activities = doc.SelectNodes(
                "//activity", nsMgr);

            if (activities == null)
                return null;

            foreach (XmlNode activity in activities)
            {
                // Check for a LAUNCHER intent filter
                XmlNodeList intentFilters = activity.SelectNodes("intent-filter", nsMgr);
                if (intentFilters != null)
                {
                    foreach (XmlNode intentFilter in intentFilters)
                    {
                        XmlNode category = intentFilter.SelectSingleNode(
                            $"category[@android:name='android.intent.category.LAUNCHER']",
                            nsMgr);
                        if (category != null)
                            return activity;
                    }
                }
            }

            // Fallback: find UnityPlayerActivity by name
            XmlNode unityActivity = doc.SelectSingleNode(
                "//activity[contains(@android:name, 'UnityPlayerActivity')]", nsMgr);

            return unityActivity;
        }

        /// <summary>
        /// Check if an intent filter with the same scheme/host/pathPrefix already exists
        /// on the given activity. Prevents duplicate entries on repeated builds.
        /// </summary>
        private static bool IntentFilterExists(
            XmlNode activity,
            XmlNamespaceManager nsMgr,
            AndroidIntentFilter filter)
        {
            XmlNodeList intentFilters = activity.SelectNodes("intent-filter", nsMgr);
            if (intentFilters == null)
                return false;

            foreach (XmlNode existing in intentFilters)
            {
                XmlNodeList dataNodes = existing.SelectNodes("data", nsMgr);
                if (dataNodes == null || dataNodes.Count == 0)
                    continue;

                foreach (XmlNode data in dataNodes)
                {
                    string existingScheme = GetAndroidAttr(data, "scheme");
                    string existingHost = GetAndroidAttr(data, "host");
                    string existingPathPrefix = GetAndroidAttr(data, "pathPrefix");

                    bool schemeMatch = existingScheme == filter.scheme;
                    bool hostMatch = NullOrEmpty(existingHost) && NullOrEmpty(filter.host)
                                     || existingHost == filter.host;
                    bool pathMatch = NullOrEmpty(existingPathPrefix) && NullOrEmpty(filter.pathPrefix)
                                     || existingPathPrefix == filter.pathPrefix;

                    if (schemeMatch && hostMatch && pathMatch)
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Add a deep link intent filter to the activity element.
        /// Follows the same pattern as the Expo config plugin:
        ///   - ACTION_VIEW
        ///   - CATEGORY_DEFAULT + CATEGORY_BROWSABLE
        ///   - data element with scheme, host, pathPrefix
        ///   - android:autoVerify="true" for https/http schemes (App Links)
        /// </summary>
        private static void AddIntentFilter(
            XmlDocument doc,
            XmlNode activity,
            AndroidIntentFilter filter)
        {
            XmlElement intentFilterEl = doc.CreateElement("intent-filter");

            // App Links verification for https/http schemes
            bool isHttpScheme = filter.scheme == "https" || filter.scheme == "http";
            if (isHttpScheme)
            {
                XmlAttribute autoVerify = doc.CreateAttribute(
                    "android", "autoVerify", AndroidNs);
                autoVerify.Value = "true";
                intentFilterEl.Attributes.Append(autoVerify);
            }

            // <action android:name="android.intent.action.VIEW" />
            XmlElement action = doc.CreateElement("action");
            SetAndroidAttr(doc, action, "name", "android.intent.action.VIEW");
            intentFilterEl.AppendChild(action);

            // <category android:name="android.intent.category.DEFAULT" />
            XmlElement categoryDefault = doc.CreateElement("category");
            SetAndroidAttr(doc, categoryDefault, "name", "android.intent.category.DEFAULT");
            intentFilterEl.AppendChild(categoryDefault);

            // <category android:name="android.intent.category.BROWSABLE" />
            XmlElement categoryBrowsable = doc.CreateElement("category");
            SetAndroidAttr(doc, categoryBrowsable, "name", "android.intent.category.BROWSABLE");
            intentFilterEl.AppendChild(categoryBrowsable);

            // <data android:scheme="..." android:host="..." android:pathPrefix="..." />
            XmlElement data = doc.CreateElement("data");
            SetAndroidAttr(doc, data, "scheme", filter.scheme);

            if (!string.IsNullOrWhiteSpace(filter.host))
                SetAndroidAttr(doc, data, "host", filter.host);

            if (!string.IsNullOrWhiteSpace(filter.pathPrefix))
                SetAndroidAttr(doc, data, "pathPrefix", filter.pathPrefix);

            intentFilterEl.AppendChild(data);

            activity.AppendChild(intentFilterEl);
        }

        // ── Helpers ─────────────────────────────────────────────────────

        private static void SetAndroidAttr(
            XmlDocument doc, XmlElement element, string name, string value)
        {
            XmlAttribute attr = doc.CreateAttribute("android", name, AndroidNs);
            attr.Value = value;
            element.Attributes.Append(attr);
        }

        private static string GetAndroidAttr(XmlNode node, string name)
        {
            XmlAttribute attr = node.Attributes?[$"android:{name}", AndroidNs]
                                ?? node.Attributes?[$"android:{name}"];
            return attr?.Value;
        }

        private static bool NullOrEmpty(string s)
        {
            return string.IsNullOrEmpty(s);
        }
    }
}
#endif
