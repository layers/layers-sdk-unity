using System;
using System.Collections.Generic;

namespace Layers.Unity.Internal
{
    /// <summary>
    /// The one query-decoding rule every Layers SDK implements.
    ///
    /// URL query components — deep-link query strings, Play Install Referrer
    /// strings — are <c>application/x-www-form-urlencoded</c>. Ad platforms
    /// emit <c>utm_campaign=running+shoes</c> meaning "running shoes", so:
    ///
    /// <list type="bullet">
    ///   <item><c>+</c> decodes to a space</item>
    ///   <item><c>%XX</c> decodes to the byte 0xXX (then UTF-8)</item>
    ///   <item>it is ONE pass: neither output feeds the other, so <c>%2B</c>
    ///         decodes to a literal <c>+</c> and stops there</item>
    /// </list>
    ///
    /// The rule applies to the QUERY component only. Path and fragment are
    /// RFC 3986, where <c>+</c> is an ordinary character.
    /// </summary>
    /// <remarks>
    /// Why this is hand-rolled rather than a BCL/Unity call — the three
    /// candidates all fail on something:
    ///
    /// <list type="bullet">
    ///   <item><c>Uri.UnescapeDataString</c> is RFC 3986 percent-decoding and
    ///   leaves <c>+</c> alone. Correct for a path, wrong for a query. It is
    ///   still the right primitive for the percent half, so it is used below —
    ///   after <c>+</c> has been handled.</item>
    ///
    ///   <item><c>HttpUtility.ParseQueryString</c> has the right semantics but
    ///   lives in <c>System.Web</c>, which is outside Unity's default .NET
    ///   Standard 2.1 API-compatibility profile, so referencing it would break
    ///   consumers who have not switched to the .NET Framework profile. It also
    ///   returns a <c>NameValueCollection</c> that joins duplicate keys with
    ///   commas, which is not the shape any other Layers SDK produces.</item>
    ///
    ///   <item><c>UnityWebRequest.UnEscapeURL</c> would drag
    ///   <c>UnityEngine.Networking</c> into a pure string parser, and its
    ///   handling of <c>+</c> and malformed escapes has changed across Unity
    ///   versions — the exact property this rule needs to be stable on.</item>
    /// </list>
    /// </remarks>
    internal static class FormUrlDecoding
    {
        /// <summary>
        /// Decode one form-urlencoded token from a query component.
        /// </summary>
        /// <remarks>
        /// The order is load-bearing. <c>+</c> is swapped for its own escape on
        /// the STILL-ENCODED token, so percent-decoding runs exactly once over
        /// the result. Percent-decoding first and then swapping <c>+</c> for a
        /// space would turn <c>%2B</c> into a space — a double decode that
        /// silently corrupts every campaign name containing a real plus sign.
        ///
        /// Never throws: a malformed escape falls back to the raw token.
        /// </remarks>
        internal static string DecodeComponent(string encoded)
        {
            if (string.IsNullOrEmpty(encoded)) return encoded ?? string.Empty;

            try
            {
                return Uri.UnescapeDataString(encoded.Replace("+", "%20"));
            }
            catch (Exception)
            {
                return encoded;
            }
        }

        /// <summary>
        /// Parse a raw, still-encoded query component (<c>a=1&amp;b=two+words</c>)
        /// into decoded key/value pairs. A leading <c>?</c> is stripped. A pair
        /// with no <c>=</c> maps to <c>""</c>. Duplicate keys: last wins,
        /// preserving the behaviour the Unity SDK has always had.
        /// </summary>
        internal static Dictionary<string, string> ParseQuery(string encodedQuery)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(encodedQuery)) return result;

            var raw = encodedQuery;
            if (raw.StartsWith("?")) raw = raw.Substring(1);
            if (raw.Length == 0) return result;

            var pairs = raw.Split('&');
            for (int i = 0; i < pairs.Length; i++)
            {
                var pair = pairs[i];
                if (pair.Length == 0) continue;

                int eqIndex = pair.IndexOf('=');
                if (eqIndex < 0)
                {
                    result[DecodeComponent(pair)] = "";
                }
                else
                {
                    result[DecodeComponent(pair.Substring(0, eqIndex))] =
                        DecodeComponent(pair.Substring(eqIndex + 1));
                }
            }

            return result;
        }
    }
}
