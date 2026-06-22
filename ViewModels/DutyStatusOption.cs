using OverWatchELD.Models;

namespace OverWatchELD.ViewModels
{
    /// <summary>
    /// Simple (Value, Text) pair for binding DutyStatus dropdowns.
    /// </summary>
    public sealed class DutyStatusOption
    {
        public DutyStatus Value { get; }
        public string Text { get; }

        public DutyStatusOption(DutyStatus value, string text)
        {
            Value = value;
            Text = text;
        }

        public override string ToString() => Text;
    }
}
