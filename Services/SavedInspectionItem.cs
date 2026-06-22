namespace OverWatchELD.Services
{
    /// <summary>
    /// Small list item used by InspectionStore to show saved logs in the UI.
    /// Some earlier patches referenced this but the type was missing.
    /// </summary>
    public sealed class SavedInspectionItem
    {
        public string Path { get; set; } = "";
        public string Title { get; set; } = "";
    }
}
