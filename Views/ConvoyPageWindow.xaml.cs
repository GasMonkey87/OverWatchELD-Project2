using OverWatchELD.Models.Convoy;
using OverWatchELD.Services.Convoy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OverWatchELD.Views
{
    public partial class ConvoyPageWindow : Window
    {
        private readonly ConvoyStore _store = new();
        private List<ConvoyEvent> _items = new();

        private sealed class ConvoyHistoryRow
        {
            public string Id { get; set; } = "";
            public string Title { get; set; } = "";
            public string DateDisplay { get; set; } = "";
            public string TimeDisplay { get; set; } = "";
            public string Status { get; set; } = "";
            public int AttendeeCount { get; set; }
        }

        public ConvoyPageWindow()
        {
            InitializeComponent();
            RefreshGrid();
        }

        private void RefreshGrid()
        {
            _items = _store.LoadAll()
                .OrderByDescending(x => x.UpdatedUtc)
                .ThenByDescending(x => x.CreatedUtc)
                .ToList();

            ConvoyHistoryGrid.ItemsSource = _items.Select(x => new ConvoyHistoryRow
            {
                Id = x.Id,
                Title = x.Title,
                DateDisplay = x.DateDisplay,
                TimeDisplay = x.TimeDisplay,
                Status = x.Status,
                AttendeeCount = x.Attendees?.Count ?? 0
            }).ToList();

            if (_items.Count == 0)
            {
                LoadDetails(null);
                FooterTextBlock.Text = "No convoys saved yet.";
                return;
            }

            if (ConvoyHistoryGrid.SelectedItem == null)
                ConvoyHistoryGrid.SelectedIndex = 0;
        }

        private ConvoyEvent? GetSelectedConvoy()
        {
            if (ConvoyHistoryGrid.SelectedItem is not ConvoyHistoryRow row)
                return null;

            return _items.FirstOrDefault(x => x.Id == row.Id);
        }

        private void LoadDetails(ConvoyEvent? item)
        {
            if (item == null)
            {
                SelectedStatusBadgeText.Text = "No Convoy Selected";
                SelectedStatusBadgeText.Background = GetStatusBrush("Planned");
                DetailTitleText.Text = "No convoy selected";
                DetailStartText.Text = "--";
                DetailDestinationText.Text = "--";
                DetailDateTimeText.Text = "--";
                DetailAttendeesText.Text = "Attendees: 0";
                DetailMetaText.Text = "--";
                DetailNotesTextBox.Text = "";
                return;
            }

            var status = string.IsNullOrWhiteSpace(item.Status) ? "Planned" : item.Status;

            SelectedStatusBadgeText.Text = status;
            SelectedStatusBadgeText.Background = GetStatusBrush(status);

            DetailTitleText.Text = string.IsNullOrWhiteSpace(item.Title) ? "Untitled Convoy" : item.Title;
            DetailStartText.Text = string.IsNullOrWhiteSpace(item.StartLocation) ? "--" : item.StartLocation;
            DetailDestinationText.Text = string.IsNullOrWhiteSpace(item.Destination) ? "--" : item.Destination;
            DetailDateTimeText.Text = $"{item.DateDisplay} • {item.TimeDisplay}".Trim(' ', '•');
            DetailAttendeesText.Text = $"Attendees: {item.Attendees?.Count ?? 0}";
            DetailMetaText.Text =
                $"Meet: {Safe(item.MeetTime)}\n" +
                $"Departure: {Safe(item.DepartureTime)}\n" +
                $"Server: {Safe(item.Server)}\n" +
                $"Lead Driver: {Safe(item.LeadDriver)}";
            DetailNotesTextBox.Text = string.IsNullOrWhiteSpace(item.Notes) ? "No notes." : item.Notes;
        }

        private static string Safe(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? "--" : value.Trim();
        }

        private static Brush GetStatusBrush(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return new SolidColorBrush(Color.FromRgb(90, 90, 90));

            var s = status.Trim();

            if (s.Equals("Planned", StringComparison.OrdinalIgnoreCase))
                return new SolidColorBrush(Color.FromRgb(176, 132, 0));

            if (s.Equals("Live", StringComparison.OrdinalIgnoreCase))
                return new SolidColorBrush(Color.FromRgb(34, 139, 34));

            if (s.Equals("Completed", StringComparison.OrdinalIgnoreCase))
                return new SolidColorBrush(Color.FromRgb(90, 90, 90));

            return new SolidColorBrush(Color.FromRgb(58, 83, 109));
        }

        private void ConvoyHistoryGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            LoadDetails(GetSelectedConvoy());
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            RefreshGrid();
            FooterTextBlock.Text = "Convoy list refreshed.";
        }

        private void NewConvoy_Click(object sender, RoutedEventArgs e)
        {
            var win = new CreateConvoyWindow
            {
                Owner = this
            };

            win.ShowDialog();
            RefreshGrid();
            FooterTextBlock.Text = "Convoy editor opened.";
        }

        private void EditSelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = GetSelectedConvoy();
            if (selected == null)
            {
                FooterTextBlock.Text = "Select a convoy first.";
                return;
            }

            var win = new CreateConvoyWindow(selected.Id)
            {
                Owner = this
            };

            win.ShowDialog();
            RefreshGrid();
            FooterTextBlock.Text = "Convoy edited.";
        }

        private void OpenAttendees_Click(object sender, RoutedEventArgs e)
        {
            var selected = GetSelectedConvoy();
            if (selected == null)
            {
                FooterTextBlock.Text = "Select a convoy first.";
                return;
            }

            var win = new ConvoyAttendeesWindow(selected.Id)
            {
                Owner = this
            };

            win.ShowDialog();
            RefreshGrid();
            FooterTextBlock.Text = "Attendees opened.";
        }

        private void MarkPlanned_Click(object sender, RoutedEventArgs e)
        {
            SetStatus("Planned");
        }

        private void MarkLive_Click(object sender, RoutedEventArgs e)
        {
            SetStatus("Live");
        }

        private void MarkCompleted_Click(object sender, RoutedEventArgs e)
        {
            SetStatus("Completed");
        }

        private void SetStatus(string status)
        {
            var selected = GetSelectedConvoy();
            if (selected == null)
            {
                FooterTextBlock.Text = "Select a convoy first.";
                return;
            }

            selected.Status = status;
            selected.UpdatedUtc = DateTimeOffset.UtcNow;
            _store.Save(selected);
            RefreshGrid();
            FooterTextBlock.Text = $"Convoy marked {status}.";
        }

        private void DeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = GetSelectedConvoy();
            if (selected == null)
            {
                FooterTextBlock.Text = "Select a convoy first.";
                return;
            }

            var confirm = MessageBox.Show(
                $"Delete convoy?\n\n{selected.Title}",
                "Delete Convoy",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
                return;

            var all = _store.LoadAll();
            all.RemoveAll(x => x.Id == selected.Id);
            _store.SaveAll(all);

            RefreshGrid();
            FooterTextBlock.Text = "Convoy deleted.";
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}