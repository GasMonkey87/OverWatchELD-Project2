
using System.Text.Json;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("ATS Map Extractor Tool");

        var atsPath = args.Length > 0
            ? args[0]
            : @"C:\Program Files (x86)\Steam\steamapps\common\American Truck Simulator";

        var outDir = Path.Combine(Directory.GetCurrentDirectory(), "LiveMap", "data");
        Directory.CreateDirectory(outDir);

        var pois = new[]
        {
            new { name="Truck Stop", type="truckstop", lon=-105.27, lat=40.52 },
            new { name="Freight Depot", type="depot", lon=-105.10, lat=40.60 },
            new { name="Garage", type="garage", lon=-105.05, lat=40.45 },
            new { name="Weigh Station", type="weigh", lon=-105.35, lat=40.63 },
            new { name="Rest Area", type="rest", lon=-105.20, lat=40.39 }
        };

        var roads = new[]
        {
            new {
                type="highway",
                points=new[]{
                    new {lon=-105.60,lat=40.50},
                    new {lon=-105.30,lat=40.55},
                    new {lon=-105.00,lat=40.60}
                }
            }
        };

        File.WriteAllText(Path.Combine(outDir,"ats_pois.json"),
            JsonSerializer.Serialize(pois,new JsonSerializerOptions{WriteIndented=true}));

        File.WriteAllText(Path.Combine(outDir,"ats_roads.json"),
            JsonSerializer.Serialize(roads,new JsonSerializerOptions{WriteIndented=true}));

        Console.WriteLine("Generated ATS data in LiveMap/data");
    }
}
