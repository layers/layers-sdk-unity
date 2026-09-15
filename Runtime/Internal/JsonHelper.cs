using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Layers.Unity.Internal
{
    /// <summary>
    /// Minimal JSON serializer and deserializer for Dictionary&lt;string, object&gt;.
    /// Unity's JsonUtility does not support dictionaries, and we avoid external dependencies.
    ///
    /// Serializes with their JSON types: strings, chars, every built-in integer
    /// and floating-point type (including <c>decimal</c> and the unsigned/short
    /// widths), bools, enums, <c>DateTime</c>/<c>DateTimeOffset</c>, nulls, any
    /// <c>IDictionary</c> (nested objects), and any <c>IEnumerable</c> —
    /// <c>IList</c>, arrays, <c>HashSet</c>, <c>Queue</c>, LINQ results (JSON
    /// arrays).
    ///
    /// Types are preserved on the wire. Every other SDK hands the Rust core
    /// typed JSON — Swift via <c>JSONSerialization</c>, Kotlin via
    /// <c>org.json</c>, Flutter via <c>jsonEncode</c> — and the core parses
    /// the blob with serde_json into a <c>serde_json::Value</c>. Anything this
    /// serializer sends through the <c>default</c> case arrives as a JSON
    /// STRING and is stored as one, which is how Unity ended up with
    /// <c>"1.50"</c> where the other platforms had <c>1.5</c>. Keep the
    /// <c>default</c> case for genuinely unknown objects only; a type that has
    /// a JSON representation gets a case of its own.
    /// </summary>
    internal static class JsonHelper
    {
        /// <summary>
        /// How deep nesting may go before a value is replaced by <c>null</c>.
        ///
        /// Recursion here is driven by caller data, and a caller can hand us a
        /// cycle — <c>map["self"] = map</c> is one line, and an object graph
        /// walked into a property bag can close a loop without anyone
        /// intending it. Unbounded recursion overflows the stack, and a
        /// <c>StackOverflowException</c> cannot be caught in .NET: it takes
        /// the whole player down, not just the event. A depth cap turns that
        /// crash into one lost property. Nothing legitimate nests 32 deep —
        /// the ingest pipeline flattens two or three.
        /// </summary>
        private const int MaxDepth = 32;

        /// <summary>
        /// Serialize a Dictionary&lt;string, object&gt; to a JSON string.
        /// Returns "{}" for null or empty dictionaries.
        /// </summary>
        internal static string Serialize(Dictionary<string, object> dict)
        {
            if (dict == null || dict.Count == 0) return "{}";

            var sb = new StringBuilder(256);
            SerializeObject(sb, dict, 0);
            return sb.ToString();
        }

        /// <summary>
        /// Serialize an arbitrary value to a JSON string. Supports the same
        /// type set as <see cref="Serialize(Dictionary{string, object})"/> —
        /// strings, chars, every numeric width, bools, enums, dates, null,
        /// nested dictionaries, and any enumerable.
        /// Used by the Tier 1 user-property mutators (<c>append</c>,
        /// <c>union</c>) which need to JSON-encode a single scalar / array.
        /// </summary>
        internal static string SerializeAny(object value)
        {
            if (value == null) return "null";
            var sb = new StringBuilder(64);
            SerializeValue(sb, value, 0);
            return sb.ToString();
        }

        private static void SerializeObject(StringBuilder sb, Dictionary<string, object> dict, int depth)
        {
            sb.Append('{');
            bool first = true;
            foreach (var kvp in dict)
            {
                if (!first) sb.Append(',');
                first = false;
                SerializeString(sb, kvp.Key ?? string.Empty);
                sb.Append(':');
                SerializeValue(sb, kvp.Value, depth);
            }
            sb.Append('}');
        }

        private static void SerializeValue(StringBuilder sb, object value, int depth)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }

            switch (value)
            {
                case string s:
                    SerializeString(sb, s);
                    break;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    break;
                case int i:
                    sb.Append(i.ToString(CultureInfo.InvariantCulture));
                    break;
                case long l:
                    sb.Append(l.ToString(CultureInfo.InvariantCulture));
                    break;
                case float f:
                    // NaN / ±Infinity have no JSON representation. Emitting the
                    // "R" text ("NaN", "Infinity") would produce a body serde_json
                    // rejects, failing the whole batch — null loses one property
                    // instead of every event in it.
                    sb.Append(float.IsNaN(f) || float.IsInfinity(f)
                        ? "null"
                        : f.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case double d:
                    sb.Append(double.IsNaN(d) || double.IsInfinity(d)
                        ? "null"
                        : d.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case decimal m:
                    sb.Append(m.ToString(CultureInfo.InvariantCulture));
                    break;
                case uint ui:
                    sb.Append(ui.ToString(CultureInfo.InvariantCulture));
                    break;
                case ulong ul:
                    sb.Append(ul.ToString(CultureInfo.InvariantCulture));
                    break;
                case short sh:
                    sb.Append(sh.ToString(CultureInfo.InvariantCulture));
                    break;
                case ushort us:
                    sb.Append(us.ToString(CultureInfo.InvariantCulture));
                    break;
                case byte by:
                    sb.Append(by.ToString(CultureInfo.InvariantCulture));
                    break;
                case sbyte sby:
                    sb.Append(sby.ToString(CultureInfo.InvariantCulture));
                    break;
                case char ch:
                    SerializeString(sb, ch.ToString());
                    break;
                case Enum e:
                    SerializeEnum(sb, e);
                    break;
                case DateTime dt:
                    // Round-trip ISO 8601 with a "Z" suffix, e.g.
                    // "2024-01-15T10:30:00.0000000Z".
                    SerializeString(sb, ToUtc(dt).ToString("o", CultureInfo.InvariantCulture));
                    break;
                case DateTimeOffset dto:
                    // Round-trip ISO 8601 with an explicit offset, always
                    // "+00:00" once normalized, e.g.
                    // "2024-01-15T10:30:00.0000000+00:00". Both forms are RFC
                    // 3339 and parse to the same instant; the suffix differs
                    // only because "o" spells UTC differently for the two types.
                    SerializeString(sb, dto.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
                    break;
                case Dictionary<string, object> nested:
                    if (depth >= MaxDepth) { sb.Append("null"); break; }
                    SerializeObject(sb, nested, depth + 1);
                    break;
                case IDictionary map:
                    // Dictionary<string, string>, Dictionary<string, int>,
                    // Hashtable, a hand-rolled IDictionary — anything a caller
                    // reasonably passes as a nested object. Without this case
                    // the generic ones fell to ToString() and shipped their
                    // TYPE NAME
                    // ("System.Collections.Generic.Dictionary`2[...]") as the
                    // property value.
                    if (depth >= MaxDepth) { sb.Append("null"); break; }
                    SerializeDictionary(sb, map, depth + 1);
                    break;
                case IList list:
                    if (depth >= MaxDepth) { sb.Append("null"); break; }
                    SerializeArray(sb, list, depth + 1);
                    break;
                case IEnumerable seq:
                    // HashSet<T>, Queue<T>, Stack<T>, a LINQ iterator — all
                    // sequences with an obvious JSON array form, none of them
                    // an IList. They too shipped their type name before.
                    // `string` is IEnumerable<char> but is matched by the
                    // `case string` above, so it never lands here.
                    if (depth >= MaxDepth) { sb.Append("null"); break; }
                    SerializeEnumerable(sb, seq, depth + 1);
                    break;
                default:
                    // Fallback: treat as string
                    SerializeString(sb, value.ToString());
                    break;
            }
        }

        /// <summary>
        /// Normalize a <see cref="DateTime"/> to UTC without inventing an
        /// offset.
        ///
        /// <c>ToUniversalTime()</c> treats <c>DateTimeKind.Unspecified</c> as
        /// LOCAL and shifts by the device's timezone — and Unspecified is what
        /// <c>new DateTime(2024, 1, 15)</c>, <c>DateTime.Parse</c> and every
        /// date read back out of PlayerPrefs or a save file gives you. A plain
        /// calendar date would therefore arrive as the previous day for a
        /// player in Tokyo and the same day for one in New York, from
        /// identical game code. Unspecified is stamped UTC instead, so the
        /// wire value matches what the caller wrote.
        /// </summary>
        private static DateTime ToUtc(DateTime dt)
        {
            switch (dt.Kind)
            {
                case DateTimeKind.Utc:
                    return dt;
                case DateTimeKind.Local:
                    return dt.ToUniversalTime();
                default:
                    return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            }
        }

        /// <summary>
        /// Serialize an enum by NAME where it has one — reordering an enum
        /// must not silently repoint historical analytics rows — and as a
        /// NUMBER where it does not.
        ///
        /// A value with no declared member (<c>(Tier)42</c>, which C# permits
        /// for any enum) has no name to send. <c>ToString()</c> yields the
        /// digits "42", and quoting those is exactly the defect this file
        /// exists to fix: a number arriving as a JSON string.
        ///
        /// A <c>[Flags]</c> enum is a SET, so it serializes as an array of the
        /// names that are set. A combination no set of declared members covers
        /// falls back to the number.
        /// </summary>
        private static void SerializeEnum(StringBuilder sb, Enum e)
        {
            Type enumType = e.GetType();

            if (enumType.IsDefined(typeof(FlagsAttribute), false))
            {
                string flags = e.ToString();
                // ToString() falls back to the numeric text when the value has
                // bits no declared member covers. A leading digit or sign is
                // how that fallback announces itself.
                if (flags.Length == 0 || char.IsDigit(flags[0]) || flags[0] == '-')
                {
                    AppendEnumNumber(sb, e, enumType);
                    return;
                }

                sb.Append('[');
                string[] names = flags.Split(',');
                for (int i = 0; i < names.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    SerializeString(sb, names[i].Trim());
                }
                sb.Append(']');
                return;
            }

            if (Enum.IsDefined(enumType, e))
            {
                SerializeString(sb, e.ToString());
                return;
            }

            AppendEnumNumber(sb, e, enumType);
        }

        private static void AppendEnumNumber(StringBuilder sb, Enum e, Type enumType)
        {
            object number = Convert.ChangeType(
                e, Enum.GetUnderlyingType(enumType), CultureInfo.InvariantCulture);
            sb.Append(Convert.ToString(number, CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Serialize any <see cref="IDictionary"/> as a JSON object.
        ///
        /// Iterating with <see cref="IDictionaryEnumerator"/> and reading
        /// <c>.Key</c>/<c>.Value</c> is deliberate. <c>foreach (DictionaryEntry
        /// entry in map)</c> casts <c>IEnumerator.Current</c> to
        /// <c>DictionaryEntry</c>, which holds for the BCL dictionaries and
        /// throws <c>InvalidCastException</c> on a hand-written
        /// <c>IDictionary</c> whose enumerator yields something else — turning
        /// one odd property into a thrown exception inside the caller's game.
        ///
        /// Keys are stringified rather than required to be strings. JSON object
        /// keys are strings, and this is what JavaScript does
        /// (<c>JSON.stringify({1: "x"})</c> is <c>{"1":"x"}</c>). Refusing the
        /// whole map over one non-string key would send its TYPE NAME instead,
        /// which is the defect, not the fix.
        /// </summary>
        private static void SerializeDictionary(StringBuilder sb, IDictionary map, int depth)
        {
            sb.Append('{');
            bool first = true;
            IDictionaryEnumerator entries = map.GetEnumerator();
            while (entries.MoveNext())
            {
                if (!first) sb.Append(',');
                first = false;
                SerializeString(sb, Convert.ToString(entries.Key, CultureInfo.InvariantCulture) ?? string.Empty);
                sb.Append(':');
                SerializeValue(sb, entries.Value, depth);
            }
            sb.Append('}');
        }

        private static void SerializeArray(StringBuilder sb, IList list, int depth)
        {
            sb.Append('[');
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(',');
                SerializeValue(sb, list[i], depth);
            }
            sb.Append(']');
        }

        private static void SerializeEnumerable(StringBuilder sb, IEnumerable seq, int depth)
        {
            sb.Append('[');
            bool first = true;
            foreach (object item in seq)
            {
                if (!first) sb.Append(',');
                first = false;
                SerializeValue(sb, item, depth);
            }
            sb.Append(']');
        }

        private static void SerializeString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '\b':
                        sb.Append("\\b");
                        break;
                    case '\f':
                        sb.Append("\\f");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u");
                            sb.Append(((int)c).ToString("X4"));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');
        }

        // ── Minimal JSON Deserializer ────────────────────────────────────

        /// <summary>
        /// Deserialize a JSON string into a Dictionary&lt;string, object&gt;.
        /// Values are typed as: string, double (all numbers), bool, null,
        /// Dictionary&lt;string, object&gt; (nested objects), or List&lt;object&gt; (arrays).
        /// Returns null if the input is null, empty, or not a valid JSON object.
        /// </summary>
        internal static Dictionary<string, object> Deserialize(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;

            var parser = new JsonParser(json);
            object result = parser.ParseValue();
            return result as Dictionary<string, object>;
        }

        /// <summary>
        /// Simple recursive-descent JSON parser. Not optimized for huge inputs,
        /// but sufficient for remote config payloads (typically a few KB).
        /// </summary>
        private class JsonParser
        {
            private readonly string _json;
            private int _pos;

            internal JsonParser(string json)
            {
                _json = json;
                _pos = 0;
            }

            internal object ParseValue()
            {
                SkipWhitespace();
                if (_pos >= _json.Length) return null;

                char c = _json[_pos];

                switch (c)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return ParseString();
                    case 't':
                    case 'f': return ParseBool();
                    case 'n': return ParseNull();
                    default:
                        if (c == '-' || (c >= '0' && c <= '9'))
                            return ParseNumber();
                        return null;
                }
            }

            private Dictionary<string, object> ParseObject()
            {
                var dict = new Dictionary<string, object>();
                _pos++; // skip '{'
                SkipWhitespace();

                if (_pos < _json.Length && _json[_pos] == '}')
                {
                    _pos++;
                    return dict;
                }

                while (_pos < _json.Length)
                {
                    SkipWhitespace();
                    if (_pos >= _json.Length || _json[_pos] != '"') break;

                    string key = ParseString();
                    SkipWhitespace();

                    if (_pos >= _json.Length || _json[_pos] != ':') break;
                    _pos++; // skip ':'

                    object value = ParseValue();
                    dict[key] = value;

                    SkipWhitespace();
                    if (_pos < _json.Length && _json[_pos] == ',')
                    {
                        _pos++;
                        continue;
                    }
                    break;
                }

                if (_pos < _json.Length && _json[_pos] == '}')
                    _pos++;

                return dict;
            }

            private List<object> ParseArray()
            {
                var list = new List<object>();
                _pos++; // skip '['
                SkipWhitespace();

                if (_pos < _json.Length && _json[_pos] == ']')
                {
                    _pos++;
                    return list;
                }

                while (_pos < _json.Length)
                {
                    object value = ParseValue();
                    list.Add(value);

                    SkipWhitespace();
                    if (_pos < _json.Length && _json[_pos] == ',')
                    {
                        _pos++;
                        continue;
                    }
                    break;
                }

                if (_pos < _json.Length && _json[_pos] == ']')
                    _pos++;

                return list;
            }

            private string ParseString()
            {
                _pos++; // skip opening '"'
                var sb = new StringBuilder();

                while (_pos < _json.Length)
                {
                    char c = _json[_pos];

                    if (c == '"')
                    {
                        _pos++;
                        return sb.ToString();
                    }

                    if (c == '\\')
                    {
                        _pos++;
                        if (_pos >= _json.Length) break;
                        char esc = _json[_pos];
                        switch (esc)
                        {
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            case '/': sb.Append('/'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'u':
                                if (_pos + 4 < _json.Length)
                                {
                                    string hex = _json.Substring(_pos + 1, 4);
                                    if (int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int codepoint))
                                        sb.Append((char)codepoint);
                                    _pos += 4;
                                }
                                break;
                            default: sb.Append(esc); break;
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    _pos++;
                }

                return sb.ToString();
            }

            private double ParseNumber()
            {
                int start = _pos;

                if (_pos < _json.Length && _json[_pos] == '-') _pos++;

                while (_pos < _json.Length && _json[_pos] >= '0' && _json[_pos] <= '9') _pos++;

                if (_pos < _json.Length && _json[_pos] == '.')
                {
                    _pos++;
                    while (_pos < _json.Length && _json[_pos] >= '0' && _json[_pos] <= '9') _pos++;
                }

                if (_pos < _json.Length && (_json[_pos] == 'e' || _json[_pos] == 'E'))
                {
                    _pos++;
                    if (_pos < _json.Length && (_json[_pos] == '+' || _json[_pos] == '-')) _pos++;
                    while (_pos < _json.Length && _json[_pos] >= '0' && _json[_pos] <= '9') _pos++;
                }

                string numStr = _json.Substring(start, _pos - start);
                if (double.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double result))
                    return result;
                return 0;
            }

            private bool ParseBool()
            {
                if (_pos + 4 <= _json.Length && _json.Substring(_pos, 4) == "true")
                {
                    _pos += 4;
                    return true;
                }
                if (_pos + 5 <= _json.Length && _json.Substring(_pos, 5) == "false")
                {
                    _pos += 5;
                    return false;
                }
                return false;
            }

            private object ParseNull()
            {
                if (_pos + 4 <= _json.Length && _json.Substring(_pos, 4) == "null")
                {
                    _pos += 4;
                    return null;
                }
                return null;
            }

            private void SkipWhitespace()
            {
                while (_pos < _json.Length)
                {
                    char c = _json[_pos];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
                        _pos++;
                    else
                        break;
                }
            }
        }
    }
}
