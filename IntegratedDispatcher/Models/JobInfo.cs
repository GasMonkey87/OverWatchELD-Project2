namespace ATS.Dispatcher.Models
{
    public class JobInfo
    {
        public string Cargo { get; set; } = "";
        public string SourceCity { get; set; } = "";
        public string DestinationCity { get; set; } = "";
        public double DistanceKm { get; set; }
        public double Income { get; set; }
        public string Company { get; set; } = "";
        public string FilePath { get; set; } = "";
    }
}
