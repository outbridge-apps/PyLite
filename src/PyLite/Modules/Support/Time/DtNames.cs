namespace Outbridge.PyLite.Modules.Support
{
    // Hardcoded English day/month name tables. Ordinal, locale-independent; shared by
    // ctime/asctime/strftime/strptime. Day tables are indexed Mon=0..Sun=6 (date.weekday numbering).
    internal static class DtNames
    {
        public static readonly string[] AbbrDays = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
        public static readonly string[] FullDays = { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };
        public static readonly string[] AbbrMonths = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
        public static readonly string[] FullMonths =
        {
            "January", "February", "March", "April", "May", "June",
            "July", "August", "September", "October", "November", "December",
        };
    }
}
