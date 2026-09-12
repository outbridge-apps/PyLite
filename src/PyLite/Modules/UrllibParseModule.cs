using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules
{
    // urllib.parse: a port of Lib/urllib/parse.py's str functions (quote/unquote, urlencode, parse_qs,
    // urlsplit/urlparse/urlunsplit/urlunparse/urljoin/urldefrag). The results are namedtuples with
    // CPython's extra properties (hostname/port/username/password, geturl). bytes inputs are not taken.
    public static class UrllibParseModule
    {
        private const string SchemeChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789+-.";
        private const string AlwaysSafe = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_.-~";
        private static readonly HashSet<string> UsesRelative = new HashSet<string>(StringComparer.Ordinal)
        {
            "", "ftp", "http", "gopher", "nntp", "imap", "wais", "file", "https", "shttp", "mms", "prospero", "rtsp", "rtspu",
            "sftp", "svn", "svn+ssh", "ws", "wss",
        };
        private static readonly HashSet<string> UsesNetloc = new HashSet<string>(StringComparer.Ordinal)
        {
            "", "ftp", "http", "gopher", "nntp", "telnet", "imap", "wais", "file", "mms", "https", "shttp", "snews", "prospero",
            "rtsp", "rtspu", "rsync", "svn", "svn+ssh", "sftp", "nfs", "git", "git+ssh", "ws", "wss", "itms-services",
        };
        private static readonly HashSet<string> UsesParams = new HashSet<string>(StringComparer.Ordinal)
        {
            "", "ftp", "hdl", "prospero", "http", "imap", "https", "shttp", "rtsp", "rtspu", "sip", "sips", "mms", "sftp", "tel",
        };

        private static readonly NamedTupleInfo ParseInfo = ResultInfo("ParseResult",
            new[] { "scheme", "netloc", "path", "params", "query", "fragment" }, true);
        private static readonly NamedTupleInfo SplitInfo = ResultInfo("SplitResult",
            new[] { "scheme", "netloc", "path", "query", "fragment" }, true);
        private static readonly NamedTupleInfo DefragInfo = ResultInfo("DefragResult", new[] { "url", "fragment" }, false);

        public static ModuleValue Create(EvalContext ctx)
        {
            // CPython exports these three as its extension point: appending to uses_relative and
            // uses_netloc is the documented way to teach urljoin a scheme of its own. So urljoin reads
            // THESE lists, not a private copy — it used to read a C# set the exported list was only a
            // snapshot of, which made an append silently do nothing. A module instance is built per RUN
            // (ModuleRegistry.Import caches in the EvalContext), so one script's append cannot reach the
            // next one.
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal)
            {
                ["quote"] = BuiltinFunctionValue.Make("quote", (s, a, kw, c) => QuoteFn(c, a, kw, "quote", false)),
                ["quote_plus"] = BuiltinFunctionValue.Make("quote_plus", (s, a, kw, c) => QuoteFn(c, a, kw, "quote_plus", true)),
                ["unquote"] = BuiltinFunctionValue.Make("unquote", (s, a, kw, c) => UnquoteFn(c, a, kw, "unquote", false)),
                ["unquote_plus"] = BuiltinFunctionValue.Make("unquote_plus", (s, a, kw, c) => UnquoteFn(c, a, kw, "unquote_plus", true)),
                ["urlencode"] = BuiltinFunctionValue.Make("urlencode", UrlEncode),
                ["parse_qs"] = BuiltinFunctionValue.Make("parse_qs", (s, a, kw, c) => ParseQs(c, a, kw, true)),
                ["parse_qsl"] = BuiltinFunctionValue.Make("parse_qsl", (s, a, kw, c) => ParseQs(c, a, kw, false)),
                ["urlsplit"] = BuiltinFunctionValue.Make("urlsplit", (s, a, kw, c) => SplitFn(c, a, kw, false)),
                ["urlparse"] = BuiltinFunctionValue.Make("urlparse", (s, a, kw, c) => SplitFn(c, a, kw, true)),
                ["urlunsplit"] = BuiltinFunctionValue.Make("urlunsplit", (s, a, kw, c) => c.Values.Str(UnsplitFn(c, a, "urlunsplit", 5))),
                ["urlunparse"] = BuiltinFunctionValue.Make("urlunparse", (s, a, kw, c) => c.Values.Str(UnsplitFn(c, a, "urlunparse", 6))),
                ["urljoin"] = BuiltinFunctionValue.Make("urljoin", UrlJoin),
                ["urldefrag"] = BuiltinFunctionValue.Make("urldefrag", UrlDefrag),
                ["unwrap"] = BuiltinFunctionValue.Make("unwrap", Unwrap),
                // CPython caches parse results and hands out a way to drop them; nothing is cached here,
                // so the call is a no-op that exists to keep the surface callable.
                ["clear_cache"] = BuiltinFunctionValue.Make("clear_cache", (s, a, kw, c) =>
                {
                    Args.None(c, a, kw, "clear_cache");
                    return c.Values.None;
                }),
                ["ParseResult"] = ResultType(ctx, ParseInfo),
                ["SplitResult"] = ResultType(ctx, SplitInfo),
                ["DefragResult"] = ResultType(ctx, DefragInfo),
                ["uses_relative"] = StrList(ctx, UsesRelative),
                ["uses_netloc"] = StrList(ctx, UsesNetloc),
                ["uses_params"] = StrList(ctx, UsesParams),
                ["scheme_chars"] = ctx.Values.Str(SchemeChars),
            };
            return ctx.Values.Module("urllib.parse", m);
        }

        // unwrap('<URL:scheme://host/path>') -> 'scheme://host/path'.
        private static ScriptValue Unwrap(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            Args.Exactly(ctx, a, kw, "unwrap", 1);
            string url = StrArg(ctx, a[0], "unwrap", "url").Trim();
            if (url.Length >= 2 && url[0] == '<' && url[url.Length - 1] == '>')
                url = url.Substring(1, url.Length - 2).Trim();
            if (url.StartsWith("URL:", StringComparison.Ordinal))
                url = url.Substring(4).Trim();
            return ctx.Values.Str(url);
        }

        // Membership in one of the LIVE scheme lists, read off this run's module instance rather than a
        // private copy. Reaching it through the import cache instead of a closure is what lets geturl()
        // honour the lists too: its slot table is built once per process and can close over nothing.
        // The module dictionary is read-only, so the list OBJECT is stable and only its contents move.
        // The scan is charged in proportion to the list, so a script that appends a hundred thousand
        // schemes pays for them on every call.
        private static bool Has(EvalContext ctx, string listName, string scheme)
        {
            ScriptValue v;
            if (!ctx.Modules.Import("urllib.parse", ctx).Members.TryGetValue(listName, out v))
                return false;
            ListValue list = v as ListValue;
            if (list == null)
                return false;
            ctx.Budget.Step(1 + list.Items.Count / 16);
            foreach (ScriptValue item in list.Items)
            {
                StrValue s = item as StrValue;
                if (s != null && string.Equals(s.Value, scheme, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static ListValue StrList(EvalContext ctx, HashSet<string> items)
        {
            ListValue l = ctx.Values.List(items.Count);
            foreach (string s in items)
                l.Add(ctx.Values.Str(s), ctx);
            return l;
        }

        // ---- result types ----

        private static NamedTupleInfo ResultInfo(string name, string[] fields, bool netlocProps)
        {
            var extra = new Dictionary<string, SlotDescriptor>(StringComparer.Ordinal)
            {
                ["geturl"] = SlotDescriptor.MakeMethod("geturl", (self, a, kw, c) => c.Values.Str(GetUrl((NamedTupleValue)self, c))),
            };
            if (netlocProps)
            {
                extra["hostname"] = SlotDescriptor.MakeProperty("hostname", (self, c) => Hostname((NamedTupleValue)self, c));
                extra["port"] = SlotDescriptor.MakeProperty("port", (self, c) => Port((NamedTupleValue)self, c));
                extra["username"] = SlotDescriptor.MakeProperty("username", (self, c) => UserInfo((NamedTupleValue)self, c, 0));
                extra["password"] = SlotDescriptor.MakeProperty("password", (self, c) => UserInfo((NamedTupleValue)self, c, 1));
            }
            return new NamedTupleInfo(name, fields, NamedTupleValue.BuildInstanceType("urllib.parse." + name, fields, extra));
        }

        private static TypeValue ResultType(EvalContext ctx, NamedTupleInfo info)
        {
            var statics = new Dictionary<string, ScriptValue>(StringComparer.Ordinal)
            {
                ["_fields"] = NamedTupleValue.FieldsTuple(info, ctx),
            };
            return ctx.Values.Type(info.TypeName, (s, a, k, c) => NamedTupleValue.Construct(info, a, k, c), info.InstanceType, statics);
        }

        private static NamedTupleValue Result(EvalContext ctx, NamedTupleInfo info, params string[] parts)
        {
            var items = new ScriptValue[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                items[i] = ctx.Values.Str(parts[i]);
            ctx.Values.PreCharge(48 + 16 * parts.Length);
            return new NamedTupleValue(info, items);
        }

        private static string Field(NamedTupleValue r, string name, EvalContext ctx)
        {
            int idx;
            if (!r.NtInfo.FieldIndex.TryGetValue(name, out idx))
                return "";
            StrValue s = r.Items[idx] as StrValue;
            if (s == null)
                throw Raise.TypeError(ctx, r.NtInfo.TypeName + " field '" + name + "' must be str, not " + r.Items[idx].PyTypeName);
            return s.Value;
        }

        // The result types are constructible by the script, so a field may hold anything: Field raises.
        private static string GetUrl(NamedTupleValue r, EvalContext ctx)
        {
            string fragment = Field(r, "fragment", ctx);
            if (ReferenceEquals(r.NtInfo, DefragInfo))
            {
                string url = Field(r, "url", ctx);
                return fragment.Length > 0 ? url + "#" + fragment : url;
            }
            string parameters = ReferenceEquals(r.NtInfo, ParseInfo) ? Field(r, "params", ctx) : "";
            return Unsplit(ctx, Field(r, "scheme", ctx), Field(r, "netloc", ctx), Field(r, "path", ctx), parameters,
                Field(r, "query", ctx), fragment);
        }

        // _hostinfo: the host[:port] after the last '@', with IPv6 brackets honoured.
        private static void HostInfo(string netloc, out string host, out string port)
        {
            int at = netloc.LastIndexOf('@');
            string hostinfo = at >= 0 ? netloc.Substring(at + 1) : netloc;
            port = null;
            if (hostinfo.StartsWith("[", StringComparison.Ordinal))
            {
                int close = hostinfo.IndexOf(']');
                host = close >= 0 ? hostinfo.Substring(0, close + 1) : hostinfo;
                string rest = close >= 0 ? hostinfo.Substring(close + 1) : "";
                if (rest.StartsWith(":", StringComparison.Ordinal))
                    port = rest.Substring(1);
                return;
            }
            int colon = hostinfo.IndexOf(':');
            if (colon >= 0)
            {
                host = hostinfo.Substring(0, colon);
                port = hostinfo.Substring(colon + 1);
            }
            else
                host = hostinfo;
        }

        private static ScriptValue Hostname(NamedTupleValue r, EvalContext ctx)
        {
            string host, port;
            HostInfo(Field(r, "netloc", ctx), out host, out port);
            if (host.Length == 0)
                return ctx.Values.None;
            string h = host.ToLowerInvariant();
            if (h.Length > 1 && h[0] == '[' && h[h.Length - 1] == ']')
                h = h.Substring(1, h.Length - 2);   // bracketed IPv6 literal answers without the brackets
            return ctx.Values.Str(h);
        }

        private static ScriptValue Port(NamedTupleValue r, EvalContext ctx)
        {
            string host, port;
            HostInfo(Field(r, "netloc", ctx), out host, out port);
            if (port == null || port.Length == 0)
                return ctx.Values.None;
            int p;
            if (!int.TryParse(port, NumberStyles.None, CultureInfo.InvariantCulture, out p))
                throw Raise.ValueError(ctx, "Port could not be cast to integer value as " + ctx.Values.Str(port).Repr(ctx));
            if (p > 65535)
                throw Raise.ValueError(ctx, "Port out of range 0-65535");
            return ctx.Values.Int(p);
        }

        private static ScriptValue UserInfo(NamedTupleValue r, EvalContext ctx, int which)
        {
            string netloc = Field(r, "netloc", ctx);
            int at = netloc.LastIndexOf('@');
            if (at < 0)
                return ctx.Values.None;
            string userinfo = netloc.Substring(0, at);
            int colon = userinfo.IndexOf(':');
            if (which == 0)
                return ctx.Values.Str(colon >= 0 ? userinfo.Substring(0, colon) : userinfo);
            return colon >= 0 ? (ScriptValue)ctx.Values.Str(userinfo.Substring(colon + 1)) : ctx.Values.None;
        }

        // ---- urlsplit / urlparse ----

        private static string StrArg(EvalContext ctx, ScriptValue v, string fn, string name)
        {
            StrValue s = v as StrValue;
            if (s == null)
                throw Raise.TypeError(ctx, fn + "() argument '" + name + "' must be str, not " + v.PyTypeName);
            return s.Value;
        }

        private static ScriptValue SplitFn(EvalContext ctx, ScriptValue[] a, KwArgs kw, bool withParams)
        {
            string fn = withParams ? "urlparse" : "urlsplit";
            Args.Between(ctx, a, fn, 1, 3);
            ScriptValue urlV = a[0], schemeV = a.Length > 1 ? a[1] : null, allowV = a.Length > 2 ? a[2] : null;
            ScriptValue v;
            if (kw.TryGet("url", out v))
                urlV = v;
            if (kw.TryGet("scheme", out v))
                schemeV = v;
            if (kw.TryGet("allow_fragments", out v))
                allowV = v;
            string url = StrArg(ctx, urlV, fn, "url");
            string scheme = schemeV == null ? "" : StrArg(ctx, schemeV, fn, "scheme");
            bool allowFragments = allowV == null || allowV.IsTruthy(ctx);
            string netloc, path, query, fragment;
            Split(ctx, url, ref scheme, allowFragments, out netloc, out path, out query, out fragment);
            if (!withParams)
                return Result(ctx, SplitInfo, scheme, netloc, path, query, fragment);
            string parameters = "";
            if (Has(ctx, "uses_params", scheme) && path.IndexOf(';') >= 0)
                SplitParams(path, out path, out parameters);
            return Result(ctx, ParseInfo, scheme, netloc, path, parameters, query, fragment);
        }

        private static void Split(EvalContext ctx, string url, ref string scheme, bool allowFragments,
            out string netloc, out string path, out string query, out string fragment)
        {
            ctx.Budget.ChargeLinear(url.Length);
            url = StripUnsafe(url.TrimStart(' ', '\t', '\n', '\r', '\f', '\v', '\0', '\x01', '\x02', '\x03', '\x04', '\x05',
                '\x06', '\x07', '\x08', '\x0e', '\x0f', '\x10', '\x11', '\x12', '\x13', '\x14', '\x15', '\x16', '\x17', '\x18',
                '\x19', '\x1a', '\x1b', '\x1c', '\x1d', '\x1e', '\x1f'));
            scheme = StripUnsafe(scheme.Trim(' ', '\t', '\n', '\r', '\f', '\v'));
            netloc = query = fragment = "";
            int i = url.IndexOf(':');
            if (i > 0 && url[0] < 128 && char.IsLetter(url[0]))
            {
                bool ok = true;
                for (int k = 0; k < i && ok; k++)
                    ok = SchemeChars.IndexOf(url[k]) >= 0;
                if (ok)
                {
                    scheme = url.Substring(0, i).ToLowerInvariant();
                    url = url.Substring(i + 1);
                }
            }
            if (url.StartsWith("//", StringComparison.Ordinal))
            {
                int delim = url.Length;
                foreach (char c in "/?#")
                {
                    int w = url.IndexOf(c, 2);
                    if (w >= 0 && w < delim)
                        delim = w;
                }
                netloc = url.Substring(2, delim - 2);
                url = url.Substring(delim);
                bool open = netloc.IndexOf('[') >= 0, close = netloc.IndexOf(']') >= 0;
                if (open != close)
                    throw Raise.ValueError(ctx, "Invalid IPv6 URL");
            }
            if (allowFragments)
            {
                int h = url.IndexOf('#');
                if (h >= 0)
                {
                    fragment = url.Substring(h + 1);
                    url = url.Substring(0, h);
                }
            }
            int q = url.IndexOf('?');
            if (q >= 0)
            {
                query = url.Substring(q + 1);
                url = url.Substring(0, q);
            }
            path = url;
        }

        private static string StripUnsafe(string s)
        {
            return s.IndexOfAny(new[] { '\t', '\r', '\n' }) < 0 ? s : s.Replace("\t", "").Replace("\r", "").Replace("\n", "");
        }

        private static void SplitParams(string url, out string path, out string parameters)
        {
            int i;
            if (url.IndexOf('/') >= 0)
            {
                i = url.IndexOf(';', url.LastIndexOf('/'));
                if (i < 0)
                {
                    path = url;
                    parameters = "";
                    return;
                }
            }
            else
                i = url.IndexOf(';');
            path = url.Substring(0, i);
            parameters = url.Substring(i + 1);
        }

        // ---- urlunsplit / urlunparse ----

        private static string UnsplitFn(EvalContext ctx, ScriptValue[] a, string fn, int arity)
        {
            Args.Exactly(ctx, a, fn, 1);
            var parts = new List<string>();
            IScriptIterator it = a[0] is StrValue ? null : TryIter(ctx, a[0]);
            if (it == null)
                throw Raise.TypeError(ctx, fn + "() argument must be a sequence of str, not " + a[0].PyTypeName);
            ScriptValue item;
            while (it.MoveNext(ctx, out item))
            {
                StrValue s = item as StrValue;
                if (s == null)
                    throw Raise.TypeError(ctx, fn + "() components must be str, not " + item.PyTypeName);
                parts.Add(s.Value);
            }
            if (parts.Count != arity)
                throw Raise.ValueError(ctx, fn + "() takes a " + arity + "-item sequence (" + parts.Count + " given)");
            if (arity == 5)
                return Unsplit(ctx, parts[0], parts[1], parts[2], "", parts[3], parts[4]);
            return Unsplit(ctx, parts[0], parts[1], parts[2], parts[3], parts[4], parts[5]);
        }

        private static string Unsplit(EvalContext ctx, string scheme, string netloc, string url, string parameters, string query, string fragment)
        {
            if (parameters.Length > 0)
                url = url + ";" + parameters;
            if (netloc.Length > 0 || (scheme.Length > 0 && Has(ctx, "uses_netloc", scheme) && !url.StartsWith("//", StringComparison.Ordinal)))
            {
                if (url.Length > 0 && url[0] != '/')
                    url = "/" + url;
                url = "//" + netloc + url;
            }
            if (scheme.Length > 0)
                url = scheme + ":" + url;
            if (query.Length > 0)
                url = url + "?" + query;
            if (fragment.Length > 0)
                url = url + "#" + fragment;
            return url;
        }

        // ---- urljoin / urldefrag ----

        private static ScriptValue UrlJoin(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            Args.Between(ctx, a, "urljoin", 2, 3);
            string baseUrl = StrArg(ctx, a[0], "urljoin", "base");
            string url = StrArg(ctx, a[1], "urljoin", "url");
            ScriptValue av = a.Length > 2 ? a[2] : null;
            ScriptValue v;
            if (kw.TryGet("allow_fragments", out v))
                av = v;
            bool allow = av == null || av.IsTruthy(ctx);
            if (baseUrl.Length == 0)
                return ctx.Values.Str(url);
            if (url.Length == 0)
                return ctx.Values.Str(baseUrl);

            string bscheme = "", bnetloc, bpath, bquery, bfragment, bparams = "";
            Split(ctx, baseUrl, ref bscheme, allow, out bnetloc, out bpath, out bquery, out bfragment);
            if (Has(ctx, "uses_params", bscheme) && bpath.IndexOf(';') >= 0)
                SplitParams(bpath, out bpath, out bparams);
            string scheme = bscheme, netloc, path, query, fragment, parameters = "";
            Split(ctx, url, ref scheme, allow, out netloc, out path, out query, out fragment);
            if (Has(ctx, "uses_params", scheme) && path.IndexOf(';') >= 0)
                SplitParams(path, out path, out parameters);

            if (scheme != bscheme || !Has(ctx, "uses_relative", scheme))
                return ctx.Values.Str(url);
            if (Has(ctx, "uses_netloc", scheme))
            {
                if (netloc.Length > 0)
                    return ctx.Values.Str(Unsplit(ctx, scheme, netloc, path, parameters, query, fragment));
                netloc = bnetloc;
            }
            if (path.Length == 0 && parameters.Length == 0)
            {
                path = bpath;
                parameters = bparams;
                if (query.Length == 0)
                    query = bquery;
                return ctx.Values.Str(Unsplit(ctx, scheme, netloc, path, parameters, query, fragment));
            }

            var baseParts = new List<string>(bpath.Split('/'));
            if (baseParts[baseParts.Count - 1].Length != 0)
                baseParts.RemoveAt(baseParts.Count - 1);
            List<string> segments;
            if (path.StartsWith("/", StringComparison.Ordinal))
                segments = new List<string>(path.Split('/'));
            else
            {
                segments = new List<string>(baseParts);
                segments.AddRange(path.Split('/'));
                // drop the empty inner segments that would double the slashes on re-joining
                var kept = new List<string> { segments[0] };
                for (int k = 1; k < segments.Count - 1; k++)
                    if (segments[k].Length > 0)
                        kept.Add(segments[k]);
                if (segments.Count > 1)
                    kept.Add(segments[segments.Count - 1]);
                segments = kept;
            }
            var resolved = new List<string>();
            foreach (string seg in segments)
            {
                if (seg == "..")
                {
                    if (resolved.Count > 0)
                        resolved.RemoveAt(resolved.Count - 1);
                }
                else if (seg != ".")
                    resolved.Add(seg);
            }
            string last = segments[segments.Count - 1];
            if (last == "." || last == "..")
                resolved.Add("");
            string joined = string.Join("/", resolved);
            return ctx.Values.Str(Unsplit(ctx, scheme, netloc, joined.Length > 0 ? joined : "/", parameters, query, fragment));
        }

        private static ScriptValue UrlDefrag(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            Args.Exactly(ctx, a, "urldefrag", 1);
            string url = StrArg(ctx, a[0], "urldefrag", "url");
            if (url.IndexOf('#') < 0)
                return Result(ctx, DefragInfo, url, "");
            string scheme = "", netloc, path, query, fragment, parameters = "";
            Split(ctx, url, ref scheme, true, out netloc, out path, out query, out fragment);
            if (Has(ctx, "uses_params", scheme) && path.IndexOf(';') >= 0)
                SplitParams(path, out path, out parameters);
            return Result(ctx, DefragInfo, Unsplit(ctx, scheme, netloc, path, parameters, query, ""), fragment);
        }

        // ---- quote / unquote ----

        private static ScriptValue QuoteFn(EvalContext ctx, ScriptValue[] a, KwArgs kw, string fn, bool plus)
        {
            Args.Between(ctx, a, fn, 1, 4);
            ScriptValue sv = a[0], safeV = a.Length > 1 ? a[1] : null, encV = a.Length > 2 ? a[2] : null, errV = a.Length > 3 ? a[3] : null;
            ScriptValue v;
            if (kw.TryGet("safe", out v))
                safeV = v;
            if (kw.TryGet("encoding", out v))
                encV = v;
            if (kw.TryGet("errors", out v))
                errV = v;
            // quote defaults safe to '/', quote_plus to '' — a path separator is not safe in a query value.
            string safe = safeV != null ? StrArg(ctx, safeV, fn, "safe") : (plus ? "" : "/");
            byte[] data;
            BytesValue bv = sv as BytesValue;
            if (bv != null)
                data = bv.Data;
            else
            {
                string s = StrArg(ctx, sv, fn, "string");
                string enc = encV == null || encV.Kind == ValueKind.None ? "utf-8" : StrArg(ctx, encV, fn, "encoding");
                string errors = errV == null || errV.Kind == ValueKind.None ? "strict" : StrArg(ctx, errV, fn, "errors");
                data = BytesCodec.Encode(ctx, s, BytesCodec.Normalize(ctx, ctx.Values.Str(enc)), errors);
            }
            ctx.Budget.ChargeLinear(data.Length);   // the quoting pass; the codec charged its own
            if (plus)
                safe = safe + " ";
            // a table, not a scan of safe per byte: safe is script-sized, and CPython caches the same way
            var ok = new bool[128];
            foreach (char ch in AlwaysSafe)
                ok[ch] = true;
            foreach (char ch in safe)
            {
                if (ch < 128)
                    ok[ch] = true;
            }
            var sb = new StringBuilder(data.Length + 8);
            foreach (byte b in data)
            {
                if (b < 128 && ok[b])
                    sb.Append(plus && b == ' ' ? '+' : (char)b);
                else
                    sb.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
            ctx.Values.EnsureStrLen(sb.Length);
            return ctx.Values.Str(sb.ToString());
        }

        private static ScriptValue UnquoteFn(EvalContext ctx, ScriptValue[] a, KwArgs kw, string fn, bool plus)
        {
            Args.Between(ctx, a, fn, 1, 3);
            ScriptValue encV = a.Length > 1 ? a[1] : null, errV = a.Length > 2 ? a[2] : null;
            ScriptValue v;
            if (kw.TryGet("encoding", out v))
                encV = v;
            if (kw.TryGet("errors", out v))
                errV = v;
            string s = StrArg(ctx, a[0], fn, "string");
            string enc = encV == null || encV.Kind == ValueKind.None ? "utf-8" : StrArg(ctx, encV, fn, "encoding");
            string errors = errV == null || errV.Kind == ValueKind.None ? "replace" : StrArg(ctx, errV, fn, "errors");
            if (plus)
                s = s.Replace('+', ' ');
            return ctx.Values.Str(Unquote(ctx, s, enc, errors));
        }

        // %XX runs become bytes and are decoded together; a lone or malformed '%' stays as it is.
        private static string Unquote(EvalContext ctx, string s, string enc, string errors)
        {
            ctx.Budget.ChargeLinear(s.Length);
            if (s.IndexOf('%') < 0)
                return s;
            string codec = BytesCodec.Normalize(ctx, ctx.Values.Str(enc));
            var sb = new StringBuilder(s.Length);
            var pending = new List<byte>();
            int i = 0;
            while (i < s.Length)
            {
                if (s[i] == '%' && i + 2 < s.Length + 0 && i + 2 <= s.Length - 1 && IsHex(s[i + 1]) && IsHex(s[i + 2]))
                {
                    pending.Add((byte)Convert.ToInt32(s.Substring(i + 1, 2), 16));
                    i += 3;
                    continue;
                }
                if (pending.Count > 0)
                {
                    sb.Append(BytesCodec.Decode(ctx, pending.ToArray(), codec, errors));
                    pending.Clear();
                }
                sb.Append(s[i]);
                i++;
            }
            if (pending.Count > 0)
                sb.Append(BytesCodec.Decode(ctx, pending.ToArray(), codec, errors));
            ctx.Values.EnsureStrLen(sb.Length);
            return sb.ToString();
        }

        private static bool IsHex(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        }

        // ---- urlencode / parse_qs ----

        private static ScriptValue UrlEncode(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            Args.Between(ctx, a, "urlencode", 1, 6);
            ScriptValue query = a[0];
            ScriptValue doseqV = a.Length > 1 ? a[1] : null, safeV = a.Length > 2 ? a[2] : null;
            ScriptValue encV = a.Length > 3 ? a[3] : null, errV = a.Length > 4 ? a[4] : null, viaV = a.Length > 5 ? a[5] : null;
            ScriptValue v;
            if (kw.TryGet("doseq", out v))
                doseqV = v;
            if (kw.TryGet("safe", out v))
                safeV = v;
            if (kw.TryGet("encoding", out v))
                encV = v;
            if (kw.TryGet("errors", out v))
                errV = v;
            if (kw.TryGet("quote_via", out v))
                viaV = v;
            bool doseq = doseqV != null && doseqV.IsTruthy(ctx);
            string safe = safeV == null ? "" : StrArg(ctx, safeV, "urlencode", "safe");
            ScriptValue encArg = encV ?? ctx.Values.None, errArg = errV ?? ctx.Values.None;
            bool plus = true;
            if (viaV != null && viaV.Kind != ValueKind.None)
            {
                BuiltinFunctionValue via = viaV as BuiltinFunctionValue;
                if (via != null && via.Name == "quote")
                    plus = false;
                else if (via == null || via.Name != "quote_plus")
                    throw Raise.TypeError(ctx, "quote_via must be urllib.parse.quote or quote_plus in this dialect");
            }

            var pairs = new List<KeyValuePair<ScriptValue, ScriptValue>>();
            DictValue d = query as DictValue;
            if (d != null)
            {
                var t = d.Table;
                for (int p = 0; p < t.EntriesUsed; p++)
                {
                    long h;
                    ScriptValue k, val;
                    if (t.TryGetEntryAt(p, out h, out k, out val))
                        pairs.Add(new KeyValuePair<ScriptValue, ScriptValue>(k, val));
                }
            }
            else
            {
                IScriptIterator it = query is StrValue || query is BytesValue ? null : TryIter(ctx, query);
                if (it == null)
                    throw Raise.TypeError(ctx, "not a valid non-string sequence or mapping object");
                ScriptValue item;
                while (it.MoveNext(ctx, out item))
                {
                    TupleValue pair = item as TupleValue;
                    ListValue lpair = item as ListValue;
                    if (pair != null && pair.Items.Length == 2)
                        pairs.Add(new KeyValuePair<ScriptValue, ScriptValue>(pair.Items[0], pair.Items[1]));
                    else if (lpair != null && lpair.Items.Count == 2)
                        pairs.Add(new KeyValuePair<ScriptValue, ScriptValue>(lpair.Items[0], lpair.Items[1]));
                    else
                        throw Raise.TypeError(ctx, "not a valid non-string sequence or mapping object");
                }
            }

            var sb = new StringBuilder();
            foreach (KeyValuePair<ScriptValue, ScriptValue> kv in pairs)
            {
                string k = QuoteValue(ctx, kv.Key, safe, encArg, errArg, plus);
                ScriptValue val = kv.Value;
                if (!doseq || val is StrValue || val is BytesValue)
                {
                    AppendPair(sb, k, QuoteValue(ctx, val, safe, encArg, errArg, plus));
                    continue;
                }
                IScriptIterator it = TryIter(ctx, val);
                if (it == null)
                {
                    AppendPair(sb, k, QuoteValue(ctx, val, safe, encArg, errArg, plus));
                    continue;
                }
                ScriptValue elem;
                while (it.MoveNext(ctx, out elem))
                    AppendPair(sb, k, QuoteValue(ctx, elem, safe, encArg, errArg, plus));
            }
            ctx.Values.EnsureStrLen(sb.Length);
            return ctx.Values.Str(sb.ToString());
        }

        private static IScriptIterator TryIter(EvalContext ctx, ScriptValue v)
        {
            return PyOps.TryGetIterator(v, ctx);
        }

        private static void AppendPair(StringBuilder sb, string k, string v)
        {
            if (sb.Length > 0)
                sb.Append('&');
            sb.Append(k).Append('=').Append(v);
        }

        private static string QuoteValue(EvalContext ctx, ScriptValue v, string safe, ScriptValue enc, ScriptValue err, bool plus)
        {
            ScriptValue input = v is StrValue || v is BytesValue ? v : ctx.Values.Str(v.Str(ctx));
            var args = new[] { input, ctx.Values.Str(safe), enc, err };
            return ((StrValue)QuoteFn(ctx, args, KwArgs.Empty, plus ? "quote_plus" : "quote", plus)).Value;
        }

        private static ScriptValue ParseQs(EvalContext ctx, ScriptValue[] a, KwArgs kw, bool asDict)
        {
            string fn = asDict ? "parse_qs" : "parse_qsl";
            Args.Between(ctx, a, fn, 1, 7);
            string qs = StrArg(ctx, a[0], fn, "qs");
            ScriptValue keepV = a.Length > 1 ? a[1] : null, strictV = a.Length > 2 ? a[2] : null;
            ScriptValue encV = a.Length > 3 ? a[3] : null, errV = a.Length > 4 ? a[4] : null;
            ScriptValue maxV = a.Length > 5 ? a[5] : null, sepV = a.Length > 6 ? a[6] : null;
            ScriptValue v;
            if (kw.TryGet("keep_blank_values", out v))
                keepV = v;
            if (kw.TryGet("strict_parsing", out v))
                strictV = v;
            if (kw.TryGet("encoding", out v))
                encV = v;
            if (kw.TryGet("errors", out v))
                errV = v;
            if (kw.TryGet("max_num_fields", out v))
                maxV = v;
            if (kw.TryGet("separator", out v))
                sepV = v;
            bool keepBlank = keepV != null && keepV.IsTruthy(ctx);
            bool strict = strictV != null && strictV.IsTruthy(ctx);
            string enc = encV == null || encV.Kind == ValueKind.None ? "utf-8" : StrArg(ctx, encV, fn, "encoding");
            string errors = errV == null || errV.Kind == ValueKind.None ? "replace" : StrArg(ctx, errV, fn, "errors");
            string sep = sepV == null || sepV.Kind == ValueKind.None ? "&" : StrArg(ctx, sepV, fn, "separator");
            if (sep.Length == 0)
                throw Raise.ValueError(ctx, "Separator must be of type string or bytes.");
            bool hasMax = maxV != null && maxV.Kind != ValueKind.None;   // a negative limit is exceeded by any field
            int maxFields = hasMax ? Coerce.ToInt32(ctx, maxV, "max_num_fields") : 0;

            string[] fields = qs.Split(new[] { sep }, StringSplitOptions.None);
            if (hasMax && fields.Length > maxFields && qs.Length > 0)
                throw Raise.ValueError(ctx, "Max number of fields exceeded");
            var pairs = new List<KeyValuePair<string, string>>();
            foreach (string field in fields)
            {
                if (field.Length == 0 && !strict)
                    continue;
                int eq = field.IndexOf('=');
                string name, value;
                if (eq >= 0)
                {
                    name = field.Substring(0, eq);
                    value = field.Substring(eq + 1);
                }
                else
                {
                    if (strict)
                        throw Raise.ValueError(ctx, "bad query field: " + ctx.Values.Str(field).Repr(ctx));
                    if (!keepBlank)
                        continue;
                    name = field;
                    value = "";
                }
                if (value.Length == 0 && !keepBlank)
                    continue;
                name = Unquote(ctx, name.Replace('+', ' '), enc, errors);
                value = Unquote(ctx, value.Replace('+', ' '), enc, errors);
                pairs.Add(new KeyValuePair<string, string>(name, value));
            }
            if (!asDict)
            {
                ListValue list = ctx.Values.List(pairs.Count);
                foreach (KeyValuePair<string, string> p in pairs)
                    list.Add(ctx.Values.Tuple(new ScriptValue[] { ctx.Values.Str(p.Key), ctx.Values.Str(p.Value) }), ctx);
                return list;
            }
            DictValue result = ctx.Values.Dict(pairs.Count);
            foreach (KeyValuePair<string, string> p in pairs)
            {
                StrValue key = ctx.Values.Str(p.Key);
                ScriptValue existing;
                if (result.TryGet(key, ctx, out existing))
                    ((ListValue)existing).Add(ctx.Values.Str(p.Value), ctx);
                else
                {
                    ListValue l = ctx.Values.List(1);
                    l.Add(ctx.Values.Str(p.Value), ctx);
                    result.SetItem(key, l, ctx);
                }
            }
            return result;
        }
    }
}
