using System.IO.Compression;
using System.Text;

namespace TransportDataService.Tests.Helpers;

public static class MinimalGtfsZipBuilder
{
    public static byte[] Build(IDictionary<string, string>? overrides = null)
    {
        var files = new Dictionary<string, string>
        {
            ["agency.txt"] = "agency_id,agency_name,agency_url,agency_timezone\n1,Test Agency,http://test.com,Europe/Istanbul",
            ["routes.txt"] = "route_id,route_short_name,route_long_name,route_type\nR1,1,Test Route,3",
            ["stops.txt"] = "stop_id,stop_name,stop_lat,stop_lon\nS1,Stop 1,38.4000,27.1000",
            ["trips.txt"] = "route_id,service_id,trip_id,direction_id\nR1,WD,T1,0",
            ["stop_times.txt"] = "trip_id,arrival_time,departure_time,stop_id,stop_sequence\nT1,25:30:45,25:31:00,S1,1",
            ["calendar.txt"] = "service_id,monday,tuesday,wednesday,thursday,friday,saturday,sunday,start_date,end_date\nWD,1,1,1,1,1,0,0,20260101,20261231"
        };

        if (overrides != null)
        {
            foreach (var (key, value) in overrides)
                files[key] = value;
        }

        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in files)
            {
                var entry = archive.CreateEntry(name);
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write(content);
            }
        }

        return ms.ToArray();
    }
}
