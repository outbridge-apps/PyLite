using System;
using System.Collections.Generic;
using System.Globalization;

namespace Outbridge.PyLite.Modules.Support
{
    // tzdata's zone abbreviations for ZoneInfo.tzname(): the alphabetic ones tzdata still carries, keyed
    // by IANA name (aliases spelled out where they differ); every other zone answers tzdata's numeric
    // form (+04, +0530, -03), which is what modern tzdata prints for them as well.
    internal static class ZoneAbbrev
    {
        private static readonly Dictionary<string, string[]> Table = Build();

        // (standard, daylight); daylight null => the zone has no DST abbreviation, use the numeric form
        internal static string For(string key, bool daylight, long offsetUs)
        {
            string[] pair;
            if (Table.TryGetValue(key, out pair))
            {
                if (!daylight)
                    return pair[0];
                if (pair[1] != null)
                    return pair[1];
            }
            return Numeric(offsetUs);
        }

        internal static string Numeric(long offsetUs)
        {
            long minutes = Math.Abs(offsetUs) / 60000000L;
            string s = (offsetUs < 0 ? "-" : "+") + (minutes / 60).ToString("00", CultureInfo.InvariantCulture);
            if (minutes % 60 != 0)
                s += (minutes % 60).ToString("00", CultureInfo.InvariantCulture);
            return s;
        }

        private static Dictionary<string, string[]> Build()
        {
            var t = new Dictionary<string, string[]>(StringComparer.Ordinal);
            Add(t, "CET", "CEST", "Europe/Amsterdam", "Europe/Andorra", "Europe/Belgrade", "Europe/Berlin", "Europe/Bratislava",
                "Europe/Brussels", "Europe/Budapest", "Europe/Busingen", "Europe/Copenhagen", "Europe/Gibraltar", "Europe/Ljubljana",
                "Europe/Luxembourg", "Europe/Madrid", "Europe/Malta", "Europe/Monaco", "Europe/Oslo", "Europe/Paris", "Europe/Podgorica",
                "Europe/Prague", "Europe/Rome", "Europe/San_Marino", "Europe/Sarajevo", "Europe/Skopje", "Europe/Stockholm",
                "Europe/Tirane", "Europe/Vaduz", "Europe/Vatican", "Europe/Vienna", "Europe/Warsaw", "Europe/Zagreb", "Europe/Zurich",
                "Africa/Ceuta", "Arctic/Longyearbyen", "Atlantic/Jan_Mayen", "CET", "MET", "Poland");
            Add(t, "CET", null, "Africa/Algiers", "Africa/Tunis");
            Add(t, "EET", "EEST", "Europe/Athens", "Europe/Bucharest", "Europe/Chisinau", "Europe/Helsinki", "Europe/Kiev", "Europe/Kyiv",
                "Europe/Mariehamn", "Europe/Riga", "Europe/Sofia", "Europe/Tallinn", "Europe/Uzhgorod", "Europe/Vilnius",
                "Europe/Zaporozhye", "Asia/Nicosia", "Europe/Nicosia", "Asia/Famagusta", "Asia/Beirut", "Africa/Cairo", "Egypt", "EET");
            Add(t, "EET", null, "Europe/Kaliningrad", "Africa/Tripoli", "Libya");
            Add(t, "IST", "IDT", "Asia/Jerusalem", "Asia/Tel_Aviv", "Israel");
            Add(t, "WET", "WEST", "Europe/Lisbon", "Portugal", "Atlantic/Canary", "Atlantic/Faroe", "Atlantic/Faeroe", "Atlantic/Madeira", "WET");
            Add(t, "GMT", "BST", "Europe/London", "Europe/Belfast", "Europe/Guernsey", "Europe/Isle_of_Man", "Europe/Jersey", "GB", "GB-Eire");
            Add(t, "GMT", "IST", "Europe/Dublin", "Eire");
            Add(t, "MSK", null, "Europe/Moscow", "Europe/Simferopol", "W-SU");
            Add(t, "GMT", null, "Africa/Abidjan", "Africa/Accra", "Africa/Bamako", "Africa/Banjul", "Africa/Bissau", "Africa/Conakry",
                "Africa/Dakar", "Africa/Freetown", "Africa/Lome", "Africa/Monrovia", "Africa/Nouakchott", "Africa/Ouagadougou",
                "Africa/Sao_Tome", "Atlantic/Reykjavik", "Atlantic/St_Helena", "Iceland", "Etc/GMT", "GMT", "Greenwich", "Etc/Greenwich");
            Add(t, "UTC", null, "Etc/UTC", "UTC", "Etc/UCT", "UCT", "Etc/Universal", "Universal", "Etc/Zulu", "Zulu");
            Add(t, "EST", "EDT", "America/New_York", "America/Detroit", "America/Toronto", "America/Montreal", "America/Nassau",
                "America/Indiana/Indianapolis", "America/Indianapolis", "America/Kentucky/Louisville", "America/Louisville",
                "America/Iqaluit", "America/Port-au-Prince", "America/Grand_Turk", "US/Eastern", "EST5EDT", "US/Michigan");
            Add(t, "EST", null, "America/Cancun", "America/Jamaica", "America/Panama", "America/Cayman", "Jamaica", "EST");
            Add(t, "CST", "CDT", "America/Chicago", "America/Winnipeg", "America/Menominee", "America/Indiana/Knox", "America/Matamoros",
                "America/North_Dakota/Center", "America/Rankin_Inlet", "America/Resolute", "America/Havana", "Cuba", "US/Central", "CST6CDT");
            Add(t, "CST", null, "America/Mexico_City", "America/Regina", "America/Guatemala", "America/Costa_Rica", "America/El_Salvador",
                "America/Managua", "America/Tegucigalpa", "America/Belize", "America/Merida", "America/Monterrey", "America/Bahia_Banderas",
                "America/Chihuahua", "America/Swift_Current", "Mexico/General", "Canada/Saskatchewan");
            Add(t, "MST", "MDT", "America/Denver", "America/Edmonton", "America/Boise", "America/Cambridge_Bay", "America/Inuvik",
                "America/Yellowknife", "America/Ojinaga", "America/Ciudad_Juarez", "US/Mountain", "Navajo", "MST7MDT", "Canada/Mountain");
            Add(t, "MST", null, "America/Phoenix", "America/Hermosillo", "America/Mazatlan", "America/Creston", "America/Dawson_Creek",
                "America/Fort_Nelson", "America/Whitehorse", "America/Dawson", "US/Arizona", "MST");
            Add(t, "PST", "PDT", "America/Los_Angeles", "America/Vancouver", "America/Tijuana", "US/Pacific", "PST8PDT", "Canada/Pacific");
            Add(t, "AKST", "AKDT", "America/Anchorage", "America/Juneau", "America/Sitka", "America/Nome", "America/Yakutat", "America/Metlakatla", "US/Alaska");
            Add(t, "HST", null, "Pacific/Honolulu", "US/Hawaii", "HST");
            Add(t, "HST", "HDT", "America/Adak", "US/Aleutian");
            Add(t, "AST", "ADT", "America/Halifax", "America/Glace_Bay", "America/Moncton", "America/Goose_Bay", "Atlantic/Bermuda",
                "America/Thule", "Canada/Atlantic");
            Add(t, "AST", null, "America/Puerto_Rico", "America/Santo_Domingo", "America/Barbados", "America/Martinique",
                "America/Port_of_Spain", "America/Curacao", "America/Aruba", "America/Anguilla", "America/Antigua", "America/Dominica",
                "America/Grenada", "America/Guadeloupe", "America/St_Kitts", "America/St_Lucia", "America/St_Vincent", "America/St_Thomas",
                "America/Tortola", "America/Montserrat", "America/Marigot", "America/St_Barthelemy", "America/Kralendijk",
                "America/Lower_Princes", "America/Blanc-Sablon");
            Add(t, "NST", "NDT", "America/St_Johns", "Canada/Newfoundland");
            Add(t, "IST", null, "Asia/Kolkata", "Asia/Calcutta");
            Add(t, "PKT", null, "Asia/Karachi");
            Add(t, "JST", null, "Asia/Tokyo", "Japan");
            Add(t, "KST", null, "Asia/Seoul", "Asia/Pyongyang", "ROK");
            Add(t, "CST", null, "Asia/Shanghai", "Asia/Chongqing", "Asia/Chungking", "Asia/Harbin", "Asia/Taipei", "Asia/Macau", "Asia/Macao", "PRC", "ROC");
            Add(t, "HKT", null, "Asia/Hong_Kong", "Hongkong");
            Add(t, "WIB", null, "Asia/Jakarta", "Asia/Pontianak");
            Add(t, "WITA", null, "Asia/Makassar", "Asia/Ujung_Pandang");
            Add(t, "WIT", null, "Asia/Jayapura");
            Add(t, "PST", null, "Asia/Manila");
            Add(t, "AEST", "AEDT", "Australia/Sydney", "Australia/Melbourne", "Australia/Hobart", "Australia/Canberra", "Australia/ACT",
                "Australia/NSW", "Australia/Victoria", "Australia/Tasmania", "Australia/Currie", "Antarctica/Macquarie");
            Add(t, "AEST", null, "Australia/Brisbane", "Australia/Lindeman", "Australia/Queensland");
            Add(t, "ACST", "ACDT", "Australia/Adelaide", "Australia/Broken_Hill", "Australia/South", "Australia/Yancowinna");
            Add(t, "ACST", null, "Australia/Darwin", "Australia/North");
            Add(t, "AWST", null, "Australia/Perth", "Australia/West");
            Add(t, "NZST", "NZDT", "Pacific/Auckland", "NZ", "Antarctica/McMurdo", "Antarctica/South_Pole");
            Add(t, "ChST", null, "Pacific/Guam", "Pacific/Saipan");
            Add(t, "SST", null, "Pacific/Pago_Pago", "Pacific/Midway", "US/Samoa");
            Add(t, "SAST", null, "Africa/Johannesburg", "Africa/Maseru", "Africa/Mbabane");
            Add(t, "EAT", null, "Africa/Nairobi", "Africa/Addis_Ababa", "Africa/Asmara", "Africa/Asmera", "Africa/Dar_es_Salaam",
                "Africa/Djibouti", "Africa/Kampala", "Africa/Mogadishu", "Indian/Antananarivo", "Indian/Comoro", "Indian/Mayotte");
            Add(t, "WAT", null, "Africa/Lagos", "Africa/Kinshasa", "Africa/Luanda", "Africa/Bangui", "Africa/Brazzaville", "Africa/Douala",
                "Africa/Libreville", "Africa/Malabo", "Africa/Niamey", "Africa/Porto-Novo", "Africa/Ndjamena");
            Add(t, "CAT", null, "Africa/Harare", "Africa/Maputo", "Africa/Lusaka", "Africa/Blantyre", "Africa/Bujumbura", "Africa/Gaborone",
                "Africa/Kigali", "Africa/Lubumbashi", "Africa/Windhoek", "Africa/Khartoum", "Africa/Juba");
            return t;
        }

        private static void Add(Dictionary<string, string[]> t, string std, string dst, params string[] keys)
        {
            var pair = new[] { std, dst };
            foreach (string k in keys)
                t[k] = pair;
        }
    }
}
