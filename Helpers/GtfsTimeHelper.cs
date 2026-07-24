namespace ulasım_veri_servisi.Helpers
{
    public static class GtfsTimeHelper
    {
        /// <summary>
        /// "HH:MM:SS" formatındaki GTFS zamanını saniyeye çevirir. (Örn: "25:30:00" -> 91800)
        /// </summary>
        public static int? ParseGtfsTimeToSeconds(string timeStr)
        {
            if (string.IsNullOrWhiteSpace(timeStr))
                return null;

            var parts = timeStr.Split(':');
            if (parts.Length != 3)
                return null;

            if (int.TryParse(parts[0], out int hours) &&
                int.TryParse(parts[1], out int minutes) &&
                int.TryParse(parts[2], out int seconds))
            {
                return (hours * 3600) + (minutes * 60) + seconds;
            }

            return null;
        }
    }
}
