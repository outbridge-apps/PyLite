using System;
using System.Collections.Generic;

namespace Outbridge.PyLite.Modules.Support
{
    // Newtonsoft turns an escaped surrogate that is not half of a pair into U+FFFD, where CPython keeps it
    // (json.loads('"\\ud800"') is '\ud800'). The text says, in order, what each U+FFFD inside its strings
    // stands for (a lone surrogate, or U+FFFD itself), and the strings the reader hands back, which come in
    // the same order, get those back. Built only when the text has a lone surrogate escape at all.
    internal sealed class LoneSurrogates
    {
        private readonly List<char> _sources;
        private int _next;

        private LoneSurrogates(List<char> sources)
        {
            _sources = sources;
        }

        internal static LoneSurrogates Find(string s)
        {
            if (s.IndexOf("\\u", StringComparison.Ordinal) < 0)
                return null;
            var sources = new List<char>();
            bool lone = false, inString = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (!inString)
                {
                    inString = c == '"';
                    continue;
                }
                if (c == '"')
                {
                    inString = false;
                    continue;
                }
                if (c == '�')
                {
                    sources.Add(c);
                    continue;
                }
                if (c != '\\' || i + 1 >= s.Length)
                    continue;
                i++;
                if (s[i] != 'u')
                    continue;
                int cp = Hex4(s, i + 1);
                if (cp < 0)
                    continue;   // the reader refuses the escape
                i += 4;
                if (cp >= 0xD800 && cp <= 0xDBFF && i + 2 < s.Length && s[i + 1] == '\\' && s[i + 2] == 'u')
                {
                    int low = Hex4(s, i + 3);
                    if (low >= 0xDC00 && low <= 0xDFFF)
                    {
                        i += 6;   // a pair: one astral character
                        continue;
                    }
                }
                if (cp >= 0xD800 && cp <= 0xDFFF)
                {
                    sources.Add((char)cp);
                    lone = true;
                }
                else if (cp == 0xFFFD)
                {
                    sources.Add('�');
                }
            }
            return lone ? new LoneSurrogates(sources) : null;
        }

        internal string Restore(string value)
        {
            if (value.IndexOf('�') < 0)
                return value;
            char[] chars = value.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (chars[i] == '�' && _next < _sources.Count)
                    chars[i] = _sources[_next++];
            }
            return new string(chars);
        }

        private static int Hex4(string s, int at)
        {
            if (at + 4 > s.Length)
                return -1;
            int v = 0;
            for (int k = 0; k < 4; k++)
            {
                char c = s[at + k];
                int d = c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;
                if (d < 0)
                    return -1;
                v = v * 16 + d;
            }
            return v;
        }
    }
}
