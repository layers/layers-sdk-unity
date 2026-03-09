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
    /// Supports: strings, ints, longs, floats, doubles, bools, nulls,
    /// nested Dictionary&lt;string, object&gt;, and IList (arrays/lists).
    /// </summary>
    internal static class JsonHelper
    {
        /// <summary>
        /// Serialize a Dictionary&lt;string, object&gt; to a JSON string.
        /// Returns "{}" for null or empty dictionaries.
        /// </summary>
        internal static string Serialize(Dictionary<string, object> dict)
        {
            if (dict == null || dict.Count == 0) return "{}";

            var sb = new StringBuilder(256);
            SerializeObject(sb, dict);
            return sb.ToString();
        }

        private static void SerializeObject(StringBuilder sb, Dictionary<string, object> dict)
        {
            sb.Append('{');
            bool first = true;
            foreach (var kvp in dict)
            {
                if (!first) sb.Append(',');
                first = false;
                SerializeString(sb, kvp.Key);
                sb.Append(':');
                SerializeValue(sb, kvp.Value);
            }
            sb.Append('}');
        }

        private static void SerializeValue(StringBuilder sb, object value)
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
                    sb.Append(f.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case double d:
                    sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case Dictionary<string, object> nested:
                    SerializeObject(sb, nested);
                    break;
                case IList list:
                    SerializeArray(sb, list);
                    break;
                default:
                    // Fallback: treat as string
                    SerializeString(sb, value.ToString());
                    break;
            }
        }

        private static void SerializeArray(StringBuilder sb, IList list)
        {
            sb.Append('[');
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(',');
                SerializeValue(sb, list[i]);
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
