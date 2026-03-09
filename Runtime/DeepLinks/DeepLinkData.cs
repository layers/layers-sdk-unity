using System.Collections.Generic;

namespace Layers.Unity
{
    /// <summary>
    /// Parsed deep link URL components.
    /// Populated by <see cref="DeepLinksModule.ParseUrl"/> or automatically
    /// when a deep link is received and auto-tracking is enabled.
    /// </summary>
    public class DeepLinkData
    {
        /// <summary>The original, unparsed URL string.</summary>
        public string RawUrl { get; set; }

        /// <summary>URL scheme (e.g. "myapp", "https").</summary>
        public string Scheme { get; set; }

        /// <summary>URL host (e.g. "open", "myapp.com").</summary>
        public string Host { get; set; }

        /// <summary>URL path (e.g. "/product/123"). Empty string if no path.</summary>
        public string Path { get; set; }

        /// <summary>All query parameters parsed from the URL.</summary>
        public Dictionary<string, string> QueryParameters { get; set; }

        /// <summary>
        /// Attribution data extracted from the query parameters (UTM params + click IDs).
        /// </summary>
        public AttributionData Attribution { get; set; }
    }
}
