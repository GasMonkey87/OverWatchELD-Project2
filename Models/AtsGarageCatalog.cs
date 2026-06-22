using System.Collections.Generic;

namespace OverWatchELD.Models
{
    public static class AtsGarageCatalog
    {
        public static List<VtcGarage> Create()
        {
            return new List<VtcGarage>
            {
                Garage("albuquerque", "Albuquerque", "NM", -106.6504, 35.0844),
                Garage("amarillo", "Amarillo", "TX", -101.8313, 35.2219),
                Garage("atlanta", "Atlanta", "GA", -84.3880, 33.7490),
                Garage("austin", "Austin", "TX", -97.7431, 30.2672),
                Garage("bakersfield", "Bakersfield", "CA", -119.0187, 35.3733),
                Garage("billings", "Billings", "MT", -108.5007, 45.7833),
                Garage("boise", "Boise", "ID", -116.2023, 43.6150),
                Garage("casper", "Casper", "WY", -106.3131, 42.8501),
                Garage("cheyenne", "Cheyenne", "WY", -104.8202, 41.1400),
                Garage("colorado_springs", "Colorado Springs", "CO", -104.8214, 38.8339),
                Garage("dallas", "Dallas", "TX", -96.7970, 32.7767),
                Garage("denver", "Denver", "CO", -104.9903, 39.7392),
                Garage("el_paso", "El Paso", "TX", -106.4850, 31.7619),
                Garage("eugene", "Eugene", "OR", -123.0868, 44.0521),
                Garage("farmington", "Farmington", "NM", -108.2187, 36.7281),
                Garage("flagstaff", "Flagstaff", "AZ", -111.6513, 35.1983),
                Garage("fort_worth", "Fort Worth", "TX", -97.3308, 32.7555),
                Garage("fresno", "Fresno", "CA", -119.7871, 36.7378),
                Garage("houston", "Houston", "TX", -95.3698, 29.7604),
                Garage("idaho_falls", "Idaho Falls", "ID", -112.0339, 43.4917),
                Garage("kansas_city", "Kansas City", "KS", -94.5786, 39.0997),
                Garage("las_vegas", "Las Vegas", "NV", -115.1398, 36.1699),
                Garage("los_angeles", "Los Angeles", "CA", -118.2437, 34.0522),
                Garage("medford", "Medford", "OR", -122.8719, 42.3265),
                Garage("miami", "Miami", "FL", -80.1918, 25.7617),
                Garage("oklahoma_city", "Oklahoma City", "OK", -97.5164, 35.4676),
                Garage("omaha", "Omaha", "NE", -95.9345, 41.2565),
                Garage("phoenix", "Phoenix", "AZ", -112.0740, 33.4484),
                Garage("portland", "Portland", "OR", -122.6765, 45.5231),
                Garage("reno", "Reno", "NV", -119.8138, 39.5296),
                Garage("sacramento", "Sacramento", "CA", -121.4944, 38.5816),
                Garage("salt_lake_city", "Salt Lake City", "UT", -111.8910, 40.7608),
                Garage("san_antonio", "San Antonio", "TX", -98.4936, 29.4241),
                Garage("san_diego", "San Diego", "CA", -117.1611, 32.7157),
                Garage("san_francisco", "San Francisco", "CA", -122.4194, 37.7749),
                Garage("seattle", "Seattle", "WA", -122.3321, 47.6062),
                Garage("spokane", "Spokane", "WA", -117.4260, 47.6588),
                Garage("tucson", "Tucson", "AZ", -110.9747, 32.2226),
                Garage("wichita", "Wichita", "KS", -97.3301, 37.6872),
                Garage("yuma", "Yuma", "AZ", -114.6277, 32.6927),

                // Illinois DLC / VTC garage ownership support.
                // These are available to the ELD garage ownership system so VTCs can
                // purchase and manage garages in Illinois cities.
                Garage("chicago", "Chicago", "IL", -87.6298, 41.8781),
                Garage("springfield_il", "Springfield", "IL", -89.6501, 39.7817),
                Garage("rockford", "Rockford", "IL", -89.0937, 42.2711),
                Garage("peoria", "Peoria", "IL", -89.5890, 40.6936),
                Garage("champaign", "Champaign", "IL", -88.2434, 40.1164),
                Garage("bloomington_il", "Bloomington", "IL", -88.9937, 40.4842),
                Garage("moline", "Moline", "IL", -90.5151, 41.5067),
                Garage("quincy_il", "Quincy", "IL", -91.4099, 39.9356),
                Garage("effingham", "Effingham", "IL", -88.5434, 39.1200),
                Garage("marion_il", "Marion", "IL", -88.9331, 37.7306),
                Garage("east_st_louis", "East St. Louis", "IL", -90.1509, 38.6245),

                // Extra Illinois manual-coverage garages used by the ELD VTC economy.
                Garage("joliet", "Joliet", "IL", -88.0817, 41.5250),
                Garage("decatur_il", "Decatur", "IL", -88.9548, 39.8403)
            };
        }

        private static VtcGarage Garage(string id, string city, string state, double mapX, double mapY)
        {
            var g = new VtcGarage
            {
                Id = id,
                CityToken = id,
                CityName = city,
                State = state,
                Size = "Small",
                TruckCapacity = 3,
                IsOwned = false,
                MapX = mapX,
                MapY = mapY,
                AssignedTruckNumbers = new List<string>()
            };

            g.ApplyEconomyDefaults();

            return g;
        }
    }
}
