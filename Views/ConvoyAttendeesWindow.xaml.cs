using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using OverWatchELD.Models.Convoy;
using OverWatchELD.Services.Convoy;

namespace OverWatchELD.Views
{
    public partial class ConvoyAttendeesWindow : Window
    {
        private readonly ConvoyStore _store = new();
        private readonly ConvoyEvent _convoy;
        private readonly ObservableCollection<ConvoyAttendee> _attendees = new();

        public ConvoyAttendeesWindow() : this(null)
        {
        }

        public ConvoyAttendeesWindow(string? convoyId)
        {
            InitializeComponent();

            _convoy = _store.GetById(convoyId) ?? _store.GetLatest() ?? new ConvoyEvent { Title = "No Convoy Created" };
            ConvoyTitleTextBlock.Text = $"{_convoy.Title} — Attendees";

            foreach (var attendee in _convoy.Attendees)
                _attendees.Add(attendee);

            AttendeesGrid.ItemsSource = _attendees;
        }

        private void AddAttendee_Click(object sender, RoutedEventArgs e)
        {
            var name = (AttendeeNameTextBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                StatusTextBlock.Text = "Attendee name is required.";
                return;
            }

            _attendees.Add(new ConvoyAttendee
            {
                Name = name,
                Truck = (AttendeeTruckTextBox.Text ?? "").Trim(),
                Role = (AttendeeRoleComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString()?.Trim() ?? "Driver",
                Status = (AttendeeStatusComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString()?.Trim() ?? "Attending"
            });

            Persist();
            AttendeeNameTextBox.Clear();
            AttendeeTruckTextBox.Clear();
            StatusTextBlock.Text = "Attendee added.";
        }

        private void RemoveSelected_Click(object sender, RoutedEventArgs e)
        {
            if (AttendeesGrid.SelectedItem is not ConvoyAttendee attendee)
            {
                StatusTextBlock.Text = "Select an attendee first.";
                return;
            }

            _attendees.Remove(attendee);
            Persist();
            StatusTextBlock.Text = "Attendee removed.";
        }

        private void Persist()
        {
            _convoy.Attendees = _attendees.ToList();
            _convoy.UpdatedUtc = DateTimeOffset.UtcNow;
            _store.Save(_convoy);
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}