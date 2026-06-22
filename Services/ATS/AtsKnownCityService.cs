using System;
using System.Collections.Generic;
using System.Linq;

namespace OverWatchELD.Services.ATS
{
    public static class AtsKnownCityService
    {
        private static readonly List<AtsKnownCity> _cities = new()
        {
            new("Camp Verde","AZ"), new("Clifton","AZ"), new("Ehrenberg","AZ"), new("Flagstaff","AZ"), new("Grand Canyon Village","AZ"), new("Holbrook","AZ"), new("Kayenta","AZ"), new("Kingman","AZ"), new("Page","AZ"), new("Phoenix","AZ"), new("San Simon","AZ"), new("Show Low","AZ"), new("Sierra Vista","AZ"), new("Tucson","AZ"), new("Yuma","AZ"),
            new("El Dorado","AR"), new("Fayetteville","AR"), new("Fort Smith","AR"), new("Harrison","AR"), new("Hot Springs","AR"), new("Jonesboro","AR"), new("Little Rock","AR"), new("Pine Bluff","AR"), new("Springdale","AR"), new("Texarkana","AR"),
            new("Bakersfield","CA"), new("Barstow","CA"), new("Blythe","CA"), new("Carlsbad","CA"), new("El Centro","CA"), new("Eureka","CA"), new("Fresno","CA"), new("Hilt","CA"), new("Hornbrook","CA"), new("Huron","CA"), new("Los Angeles","CA"), new("Modesto","CA"), new("Oakland","CA"), new("Oxnard","CA"), new("Redding","CA"), new("Sacramento","CA"), new("San Diego","CA"), new("San Francisco","CA"), new("San Jose","CA"), new("San Rafael","CA"), new("Santa Maria","CA"), new("Santa Cruz","CA"), new("Stockton","CA"), new("Truckee","CA"), new("Ukiah","CA"),
            new("Alamosa","CO"), new("Burlington","CO"), new("Colorado Springs","CO"), new("Denver","CO"), new("Durango","CO"), new("Fort Collins","CO"), new("Grand Junction","CO"), new("Lamar","CO"), new("Montrose","CO"), new("Pueblo","CO"), new("Rangely","CO"), new("Steamboat Springs","CO"), new("Sterling","CO"),
            new("Boise","ID"), new("Coeur d'Alene","ID"), new("Grangeville","ID"), new("Idaho Falls","ID"), new("Ketchum","ID"), new("Lewiston","ID"), new("Nampa","ID"), new("Pocatello","ID"), new("Salmon","ID"), new("Sandpoint","ID"), new("Twin Falls","ID"),
            new("Burlington","IA"), new("Cedar Rapids","IA"), new("Council Bluffs","IA"), new("Davenport","IA"), new("Des Moines","IA"), new("Dubuque","IA"), new("Fort Dodge","IA"), new("Iowa City","IA"), new("Mason City","IA"), new("Ottumwa","IA"), new("Sioux City","IA"), new("Waterloo","IA"),
            new("Colby","KS"), new("Dodge City","KS"), new("Emporia","KS"), new("Garden City","KS"), new("Hays","KS"), new("Hutchinson","KS"), new("Junction City","KS"), new("Kansas City","KS"), new("Marysville","KS"), new("Phillipsburg","KS"), new("Pittsburg","KS"), new("Salina","KS"), new("Topeka","KS"), new("Wichita","KS"),
            new("Alexandria","LA"), new("Baton Rouge","LA"), new("Houma","LA"), new("Lafayette","LA"), new("Lake Charles","LA"), new("Monroe","LA"), new("Morgan City","LA"), new("New Orleans","LA"), new("Port Fourchon","LA"), new("Shreveport","LA"), new("Slidell","LA"),
            new("Cape Girardeau","MO"), new("Columbia","MO"), new("Jefferson City","MO"), new("Joplin","MO"), new("Kansas City","MO"), new("Kirksville","MO"), new("Maryville","MO"), new("Poplar Bluff","MO"), new("Rolla","MO"), new("Springfield","MO"), new("St. Joseph","MO"), new("St. Louis","MO"),
            new("Billings","MT"), new("Bozeman","MT"), new("Butte","MT"), new("Glasgow","MT"), new("Glendive","MT"), new("Great Falls","MT"), new("Havre","MT"), new("Helena","MT"), new("Kalispell","MT"), new("Laurel","MT"), new("Lewistown","MT"), new("Miles City","MT"), new("Missoula","MT"), new("Sidney","MT"), new("Thompson Falls","MT"),
            new("Alliance","NE"), new("Chadron","NE"), new("Columbus","NE"), new("Grand Island","NE"), new("Lincoln","NE"), new("McCook","NE"), new("Norfolk","NE"), new("North Platte","NE"), new("Omaha","NE"), new("Scottsbluff","NE"), new("Sidney","NE"), new("Valentine","NE"),
            new("Carson City","NV"), new("Elko","NV"), new("Ely","NV"), new("Jackpot","NV"), new("Las Vegas","NV"), new("Pioche","NV"), new("Primm","NV"), new("Reno","NV"), new("Tonopah","NV"), new("Winnemucca","NV"),
            new("Alamogordo","NM"), new("Albuquerque","NM"), new("Artesia","NM"), new("Carlsbad","NM"), new("Clovis","NM"), new("Farmington","NM"), new("Gallup","NM"), new("Hobbs","NM"), new("Las Cruces","NM"), new("Raton","NM"), new("Roswell","NM"), new("Santa Fe","NM"), new("Socorro","NM"), new("Tucumcari","NM"),
            new("Ardmore","OK"), new("Clinton","OK"), new("Enid","OK"), new("Guymon","OK"), new("Idabel","OK"), new("Lawton","OK"), new("McAlester","OK"), new("Oklahoma City","OK"), new("Tulsa","OK"), new("Woodward","OK"),
            new("Astoria","OR"), new("Bend","OR"), new("Burns","OR"), new("Coos Bay","OR"), new("Eugene","OR"), new("Klamath Falls","OR"), new("Lakeview","OR"), new("Medford","OR"), new("Newport","OR"), new("Ontario","OR"), new("Pendleton","OR"), new("Portland","OR"), new("Salem","OR"), new("The Dalles","OR"),
            new("Abilene","TX"), new("Amarillo","TX"), new("Austin","TX"), new("Beaumont","TX"), new("Brownsville","TX"), new("Corpus Christi","TX"), new("Dalhart","TX"), new("Dallas","TX"), new("Del Rio","TX"), new("El Paso","TX"), new("Fort Stockton","TX"), new("Fort Worth","TX"), new("Galveston","TX"), new("Houston","TX"), new("Huntsville","TX"), new("Laredo","TX"), new("Longview","TX"), new("Lubbock","TX"), new("Lufkin","TX"), new("McAllen","TX"), new("Odessa","TX"), new("San Angelo","TX"), new("San Antonio","TX"), new("Texarkana","TX"), new("Tyler","TX"), new("Van Horn","TX"), new("Victoria","TX"), new("Waco","TX"), new("Wichita Falls","TX"),
            new("Cedar City","UT"), new("Logan","UT"), new("Moab","UT"), new("Ogden","UT"), new("Price","UT"), new("Provo","UT"), new("Salina","UT"), new("Salt Lake City","UT"), new("St. George","UT"), new("Vernal","UT"),
            new("Aberdeen","WA"), new("Bellingham","WA"), new("Colville","WA"), new("Everett","WA"), new("Grand Coulee","WA"), new("Kennewick","WA"), new("Longview","WA"), new("Olympia","WA"), new("Omak","WA"), new("Port Angeles","WA"), new("Seattle","WA"), new("Spokane","WA"), new("Tacoma","WA"), new("Vancouver","WA"), new("Wenatchee","WA"), new("Yakima","WA"),
            new("Casper","WY"), new("Cheyenne","WY"), new("Cody","WY"), new("Evanston","WY"), new("Gillette","WY"), new("Jackson","WY"), new("Laramie","WY"), new("Rawlins","WY"), new("Riverton","WY"), new("Rock Springs","WY"), new("Sheridan","WY")
        };

        public static IReadOnlyList<AtsKnownCity> All => _cities;

        public static bool IsKnownCity(string? city, string? state)
        {
            if (string.IsNullOrWhiteSpace(city)) return false;
            return _cities.Any(c =>
                string.Equals(c.City, city.Trim(), StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(state) || string.Equals(c.State, state.Trim(), StringComparison.OrdinalIgnoreCase)));
        }

        public static string TokenFor(AtsKnownCity city)
        {
            return $"{city.City}_{city.State}".ToLowerInvariant()
                .Replace(" ", "_")
                .Replace(".", "")
                .Replace("'", "")
                .Replace("-", "_");
        }
    }

    public sealed record AtsKnownCity(string City, string State);
}
