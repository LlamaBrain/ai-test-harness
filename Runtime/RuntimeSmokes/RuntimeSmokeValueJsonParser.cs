#if UNITY_EDITOR || DEVELOPMENT_BUILD || SMOKE_HARNESS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LlamaBrainLabs.Ath.RuntimeSmokes
{
    /// <summary>Small bounded JSON reader for the closed RuntimeSmoke assertion value algebra.</summary>
    public static class RuntimeSmokeValueJsonParser
    {
        private const int MaximumDepth = 64;
        private const int MaximumInputLength = 1024 * 1024;

        public static RuntimeSmokeValue Parse(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            if (json.Length > MaximumInputLength) throw new FormatException("Assertion JSON exceeds the maximum size.");
            var parser = new Parser(json);
            var value = parser.ReadValue(0);
            parser.SkipWhitespace();
            if (!parser.AtEnd) throw parser.Error("Unexpected content after the JSON value.");
            return value;
        }

        private sealed class Parser
        {
            private readonly string _text;
            private int _position;

            public Parser(string text) { _text = text; }
            public bool AtEnd => _position >= _text.Length;

            public RuntimeSmokeValue ReadValue(int depth)
            {
                SkipWhitespace();
                if (depth > MaximumDepth) throw Error("JSON nesting exceeds the maximum depth.");
                if (AtEnd) throw Error("Expected a JSON value.");
                switch (_text[_position])
                {
                    case 'n': ReadLiteral("null"); return RuntimeSmokeValue.Null();
                    case 't': ReadLiteral("true"); return RuntimeSmokeValue.From(true);
                    case 'f': ReadLiteral("false"); return RuntimeSmokeValue.From(false);
                    case '"': return RuntimeSmokeValue.From(ReadString());
                    case '[': return ReadArray(depth + 1);
                    case '{': return ReadMap(depth + 1);
                    default: return ReadNumber();
                }
            }

            public void SkipWhitespace()
            {
                while (!AtEnd && (_text[_position] == ' ' || _text[_position] == '\t' || _text[_position] == '\r' || _text[_position] == '\n')) _position++;
            }

            public FormatException Error(string message) => new FormatException(message + " At character " + _position.ToString(CultureInfo.InvariantCulture) + ".");

            private RuntimeSmokeValue ReadArray(int depth)
            {
                _position++;
                SkipWhitespace();
                var values = new List<RuntimeSmokeValue>();
                if (Take(']')) return RuntimeSmokeValue.FromArray(values);
                while (true)
                {
                    values.Add(ReadValue(depth));
                    SkipWhitespace();
                    if (Take(']')) break;
                    if (!Take(',')) throw Error("Expected ',' or ']' in array.");
                }
                return RuntimeSmokeValue.FromArray(values);
            }

            private RuntimeSmokeValue ReadMap(int depth)
            {
                _position++;
                SkipWhitespace();
                var values = new List<KeyValuePair<string, RuntimeSmokeValue>>();
                var keys = new HashSet<string>(StringComparer.Ordinal);
                if (Take('}')) return RuntimeSmokeValue.FromMap(values);
                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || _text[_position] != '"') throw Error("Map keys must be JSON strings.");
                    var key = ReadString();
                    if (!keys.Add(key)) throw Error("Duplicate ordinal map key: " + key);
                    SkipWhitespace();
                    if (!Take(':')) throw Error("Expected ':' after map key.");
                    values.Add(new KeyValuePair<string, RuntimeSmokeValue>(key, ReadValue(depth)));
                    SkipWhitespace();
                    if (Take('}')) break;
                    if (!Take(',')) throw Error("Expected ',' or '}' in map.");
                }
                return RuntimeSmokeValue.FromMap(values);
            }

            private RuntimeSmokeValue ReadNumber()
            {
                var start = _position;
                if (Take('-') && AtEnd) throw Error("Invalid number.");
                if (Take('0'))
                {
                    if (!AtEnd && IsDigit(_text[_position])) throw Error("Leading zeroes are not valid JSON numbers.");
                }
                else
                {
                    if (AtEnd || _text[_position] < '1' || _text[_position] > '9') throw Error("Invalid JSON number.");
                    while (!AtEnd && IsDigit(_text[_position])) _position++;
                }

                var isDecimal = false;
                if (Take('.'))
                {
                    isDecimal = true;
                    if (AtEnd || !IsDigit(_text[_position])) throw Error("Fraction requires at least one digit.");
                    while (!AtEnd && IsDigit(_text[_position])) _position++;
                }
                if (Take('e') || Take('E'))
                {
                    isDecimal = true;
                    if (!Take('+')) Take('-');
                    if (AtEnd || !IsDigit(_text[_position])) throw Error("Exponent requires at least one digit.");
                    while (!AtEnd && IsDigit(_text[_position])) _position++;
                }

                var token = _text.Substring(start, _position - start);
                if (!isDecimal)
                {
                    if (!long.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
                        throw Error("Integer is outside the signed 64-bit range.");
                    return RuntimeSmokeValue.From(integer);
                }
                if (!decimal.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                    throw Error("Decimal is invalid, non-finite, or outside the supported range.");
                return RuntimeSmokeValue.From(number);
            }

            private string ReadString()
            {
                if (!Take('"')) throw Error("Expected a JSON string.");
                var builder = new StringBuilder();
                while (!AtEnd)
                {
                    var character = _text[_position++];
                    if (character == '"') return builder.ToString();
                    if (character < 0x20) throw Error("Unescaped control character in string.");
                    if (character != '\\')
                    {
                        if (char.IsHighSurrogate(character))
                        {
                            if (AtEnd || !char.IsLowSurrogate(_text[_position])) throw Error("Unpaired high surrogate in string.");
                            builder.Append(character).Append(_text[_position++]);
                            continue;
                        }
                        if (char.IsLowSurrogate(character)) throw Error("Unpaired low surrogate in string.");
                        builder.Append(character);
                        continue;
                    }
                    if (AtEnd) throw Error("Incomplete string escape.");
                    switch (_text[_position++])
                    {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case '/': builder.Append('/'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        case 'u': AppendUnicodeEscape(builder); break;
                        default: throw Error("Unknown string escape.");
                    }
                }
                throw Error("Unterminated JSON string.");
            }

            private void AppendUnicodeEscape(StringBuilder builder)
            {
                var first = ReadUnicodeCodeUnit();
                if (char.IsLowSurrogate(first)) throw Error("Unpaired low surrogate escape.");
                builder.Append(first);
                if (!char.IsHighSurrogate(first)) return;
                if (_position + 2 > _text.Length || _text[_position] != '\\' || _text[_position + 1] != 'u')
                    throw Error("Unpaired high surrogate escape.");
                _position += 2;
                var second = ReadUnicodeCodeUnit();
                if (!char.IsLowSurrogate(second)) throw Error("High surrogate escape must be followed by a low surrogate.");
                builder.Append(second);
            }

            private char ReadUnicodeCodeUnit()
            {
                if (_position + 4 > _text.Length) throw Error("Incomplete Unicode escape.");
                var token = _text.Substring(_position, 4);
                if (!ushort.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                    throw Error("Invalid Unicode escape.");
                _position += 4;
                return (char)code;
            }

            private static bool IsDigit(char character) => character >= '0' && character <= '9';

            private void ReadLiteral(string literal)
            {
                if (_position + literal.Length > _text.Length || string.Compare(_text, _position, literal, 0, literal.Length, StringComparison.Ordinal) != 0)
                    throw Error("Invalid JSON literal.");
                _position += literal.Length;
            }

            private bool Take(char expected)
            {
                if (AtEnd || _text[_position] != expected) return false;
                _position++;
                return true;
            }
        }
    }
}
#endif
