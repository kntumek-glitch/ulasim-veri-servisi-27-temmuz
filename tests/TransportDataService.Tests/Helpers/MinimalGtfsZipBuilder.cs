using System.IO.Compression;
using System.Text;

namespace TransportDataService.Tests.Helpers;

public static class MinimalGtfsZipBuilder
{
    public static byte[] Build(IDictionary<string, string>? overrides = null)
    {
        var stops = Enumerable.Range(1, 11).Select(i => $"S{i},Stop {i},38.4000,27.1000");
        var trips = Enumerable.Range(1, 11).Select(i => $"R1,WD,T{i},0,0,0,SH1,Test Headsign {i}");
        var stopTimes = Enumerable.Range(1, 101).Select(i => $"T1,25:30:45,25:31:00,S1,{i},1");

        var files = new Dictionary<string, string>
        {
            ["agency.txt"] = "agency_id,agency_name,agency_url,agency_timezone,agency_lang,agency_phone\n1,Test Agency,http://test.com,Europe/Istanbul,tr,123456",
            ["routes.txt"] = "route_id,agency_id,route_short_name,route_long_name,route_type,route_desc,route_color,route_text_color\nR1,1,1,Test Route,3,,000000,FFFFFF",
            ["stops.txt"] = "stop_id,stop_name,stop_lat,stop_lon\n" + string.Join("\n", stops),
            ["trips.txt"] = "route_id,service_id,trip_id,direction_id,wheelchair_accessible,bikes_allowed,shape_id,trip_headsign\n" + string.Join("\n", trips),
            ["stop_times.txt"] = "trip_id,arrival_time,departure_time,stop_id,stop_sequence,timepoint\n" + string.Join("\n", stopTimes),
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
                if (content == null) continue;
                var entry = archive.CreateEntry(name);
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write(content);
            }
        }

        return ms.ToArray();
    }
}
