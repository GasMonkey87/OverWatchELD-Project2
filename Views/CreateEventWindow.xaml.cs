using OverWatchELD.Models.Events;
using OverWatchELD.Services.Events;
using System;
using System.Windows;
using System.Windows.Controls;

namespace OverWatchELD.Views
{
    public partial class CreateEventWindow : Window
    {
        private readonly EventStore _store = new();
        private EventItem _current;

        public CreateEventWindow() : this(null)
        {
        }

        public CreateEventWindow(string? eventId)
        {
            InitializeComponent();

            _current = _store.GetById(eventId) ?? new EventItem();
            LoadIntoForm(_current);
        }

        private void LoadIntoForm(EventItem item)
        {
            TitleTextBox.Text = item.Title;
            LocationTextBox.Text = item.Location;
            HostTextBox.Text = item.Host;
            TimeTextBox.Text = item.TimeDisplay;
            NotesTextBox.Text = item.Notes;
            EventDatePicker.SelectedDate = item.EventDate == default ? DateTime.Today : item.EventDate;

            SelectCombo(EventTypeComboBox, item.EventType, 0);
            SelectCombo(StatusComboBox, item.Status, 0);
        }

        private static void SelectCombo(ComboBox combo, string? value, int fallbackIndex)
        {
            var target = (value ?? "").Trim();

            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (combo.Items[i] is ComboBoxItem item &&
                    string.Equals((item.Content?.ToString() ?? "").Trim(), target, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }

            combo.SelectedIndex = fallbackIndex;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var title = (TitleTextBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                StatusTextBlock.Text = "Event title is required.";
                return;
            }

            _current.Title = title;
            _current.EventType = (EventTypeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString()?.Trim() ?? "Event";
            _current.Status = (StatusComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString()?.Trim() ?? "Planned";
            _current.EventDate = EventDatePicker.SelectedDate ?? DateTime.Today;
            _current.TimeDisplay = (TimeTextBox.Text ?? "").Trim();
            _current.Location = (LocationTextBox.Text ?? "").Trim();
            _current.Host = (HostTextBox.Text ?? "").Trim();
            _current.Notes = (NotesTextBox.Text ?? "").Trim();
            _current.UpdatedUtc = DateTimeOffset.UtcNow;

            _store.Save(_current);
            StatusTextBlock.Text = "Event saved.";
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}