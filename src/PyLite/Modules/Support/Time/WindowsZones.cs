using System;
using System.Collections.Generic;

namespace Outbridge.PyLite.Modules.Support
{
    // The IANA -> Windows id map (WindowsZones.g.cs, from CLDR). "UTC"/"GMT"-style bare keys resolve
    // through their Etc/ forms, as tzdata carries them too.
    internal static partial class WindowsZones
    {
        internal static bool TryMap(string iana, out string windowsId)
        {
            int i = Array.BinarySearch(Iana, iana, StringComparer.Ordinal);
            if (i < 0 && !iana.Contains("/"))
                i = Array.BinarySearch(Iana, "Etc/" + iana, StringComparer.Ordinal);
            if (i < 0)
            {
                windowsId = null;
                return false;
            }
            windowsId = Windows[i];
            return true;
        }

        internal static IEnumerable<KeyValuePair<string, string>> All()
        {
            for (int i = 0; i < Iana.Length; i++)
                yield return new KeyValuePair<string, string>(Iana[i], Windows[i]);
        }
    }
}
