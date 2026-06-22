using System;
using System.Windows;
using System.Windows.Controls;
using OverWatchELD.Models.Convoy;
using OverWatchELD.Services.Convoy;
using OverWatchELD.Services;

namespace OverWatchELD.Views
{
    public partial class CreateConvoyWindow : Window
    {
        private readonly ConvoyStore _store = new();
        private ConvoyEvent _current;

        public CreateConvoyWindow() : this(null)
        {
        }

        public CreateConvoyWindow(string? convoyId)
        {
            InitializeComponent();

            _current = _store.GetById(convoyId) ?? _store.GetLatest() ?? new ConvoyEvent();
            DriverDropdownService.Bind(LeadDriverTextBox, _current.LeadDriver, includeUnassigned: false);
            LoadIntoForm(_current);
        }

        private void LoadIntoForm(ConvoyEvent convoy)
        {
            TitleTextBox.Text = convoy.Title;
            StartLocationTextBox.Text = convoy.StartLocation;
            DestinationTextBox.Text = convoy.Destination;
            DateDisplayTextBox.Text = convoy.DateDisplay;
            TimeDisplayTextBox.Text = convoy.TimeDisplay;
            MeetTimeTextBox.Text = convoy.MeetTime;
            DepartureTimeTextBox.Text = convoy.DepartureTime;
            ServerTextBox.Text = convoy.Server;
            DriverDropdownService.Select(LeadDriverTextBox, convoy.LeadDriver);
            NotesTextBox.Text = convoy.Notes;

            SelectStatus(convoy.Status);
        }

        private void SelectStatus(string? status)
        {
            var target = (status ?? "").Trim();

            for (int i = 0; i < StatusComboBox.Items.Count; i++)
            {
                if (StatusComboBox.Items[i] is ComboBoxItem item &&
                    string.Equals((item.Content?.ToString() ?? "").Trim(), target, StringComparison.OrdinalIgnoreCase))
                {
                    StatusComboBox.SelectedIndex = i;
                    return;
                }
            }

            StatusComboBox.SelectedIndex = 0;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var title = (TitleTextBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                StatusTextBlock.Text = "Convoy title is required.";
                return;
            }

            _current.Title = title;
            _current.StartLocation = (StartLocationTextBox.Text ?? "").Trim();
            _current.Destination = (DestinationTextBox.Text ?? "").Trim();
            _current.DateDisplay = (DateDisplayTextBox.Text ?? "").Trim();
            _current.TimeDisplay = (TimeDisplayTextBox.Text ?? "").Trim();
            _current.MeetTime = (MeetTimeTextBox.Text ?? "").Trim();
            _current.DepartureTime = (DepartureTimeTextBox.Text ?? "").Trim();
            _current.Server = (ServerTextBox.Text ?? "").Trim();
            _current.LeadDriver = DriverDropdownService.SelectedName(LeadDriverTextBox);
            _current.Notes = (NotesTextBox.Text ?? "").Trim();
            _current.Status = (StatusComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString()?.Trim() ?? "Planned";
            _current.UpdatedUtc = DateTimeOffset.UtcNow;

            _store.Save(_current);
            StatusTextBlock.Text = "Convoy saved.";
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}