using OverWatchELD.Models.Events;
using OverWatchELD.Services;
using OverWatchELD.Services.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OverWatchELD.Views
{
    public partial class EventPageWindow : Window
    {
        private readonly EventStore _store = new();
        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

        private List<EventItem> _all = new();
        private DateTime _displayMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
        private DateTime _selectedDate = DateTime.Today;

        private sealed class EventListRow
        {
            public string Id { get; set; } = "";
            public string Title { get; set; } = "";
            public string Meta { get; set; } = "";
        }

        public EventPageWindow()
        {
            InitializeComponent();
            RefreshAll();
        }

        private void RefreshAll()
        {
            _all = _store.LoadAll()
                .OrderBy(x => x.EventDate)
                .ThenBy(x => x.TimeDisplay)
                .ToList();

            BuildCalendar();
            LoadSelectedDateEvents();
            LoadUpcomingEvents();
            LoadDetails(null);

            FooterTextBlock.Text = "Events refreshed.";
        }

        private void BuildCalendar()
        {
            MonthTitleText.Text = _displayMonth.ToString("MMMM yyyy");
            CalendarDaysGrid.Children.Clear();

            var firstDay = _displayMonth;
            var start = firstDay.AddDays(-(int)firstDay.DayOfWeek);

            for (int i = 0; i < 42; i++)
            {
                var day = start.AddDays(i);
                var hasEvent = _all.Any(x => x.EventDate.Date == day.Date);
                var isCurrentMonth = day.Month == _displayMonth.Month;
                var isSelected = day.Date == _selectedDate.Date;
                var isToday = day.Date == DateTime.Today;

                var border = new Border
                {
                    Margin = new Thickness(2),
                    CornerRadius = new CornerRadius(8),
                    Background = isSelected
                        ? new SolidColorBrush(Color.FromRgb(42, 78, 120))
                        : isCurrentMonth
                            ? new SolidColorBrush(Color.FromRgb(24, 24, 24))
                            : new SolidColorBrush(Color.FromRgb(16, 16, 16)),
                    BorderBrush = isToday
                        ? new SolidColorBrush(Color.FromRgb(111, 207, 151))
                        : new SolidColorBrush(Color.FromRgb(42, 42, 42)),
                    BorderThickness = isToday ? new Thickness(2) : new Thickness(1),
                    Padding = new Thickness(6),
                    Tag = day.Date
                };

                var stack = new StackPanel();

                stack.Children.Add(new TextBlock
                {
                    Text = day.Day.ToString(),
                    Foreground = isCurrentMonth ? Brushes.White : new SolidColorBrush(Color.FromRgb(120, 120, 120)),
                    FontWeight = FontWeights.SemiBold
                });

                if (hasEvent)
                {
                    stack.Children.Add(new TextBlock
                    {
                        Text = "• Event",
                        Foreground = new SolidColorBrush(Color.FromRgb(111, 207, 151)),
                        FontSize = 11,
                        Margin = new Thickness(0, 4, 0, 0)
                    });
                }

                border.Child = stack;
                border.MouseLeftButtonUp += CalendarDay_Click;

                CalendarDaysGrid.Children.Add(border);
            }

            SelectedDateLabelText.Text = _selectedDate.ToString("dddd, MMMM d, yyyy");
        }

        private void CalendarDay_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is DateTime dt)
            {
                _selectedDate = dt;
                BuildCalendar();
                LoadSelectedDateEvents();
            }
        }

        private void LoadSelectedDateEvents()
        {
            SelectedDateLabelText.Text = _selectedDate.ToString("dddd, MMMM d, yyyy");

            var items = _all
                .Where(x => x.EventDate.Date == _selectedDate.Date)
                .OrderBy(x => x.TimeDisplay)
                .Select(x => new EventListRow
                {
                    Id = x.Id,
                    Title = x.Title,
                    Meta = $"{x.EventType} • {x.TimeDisplay} • {x.Location}"
                })
                .ToList();

            SelectedDateEventsList.ItemsSource = items;
        }

        private void LoadUpcomingEvents()
        {
            var today = DateTime.Today;

            var items = _all
                .Where(x => x.EventDate.Date >= today)
                .OrderBy(x => x.EventDate)
                .ThenBy(x => x.TimeDisplay)
                .Take(12)
                .Select(x => new EventListRow
                {
                    Id = x.Id,
                    Title = x.Title,
                    Meta = $"{x.EventDate:MMM d} • {x.TimeDisplay} • {x.EventType}"
                })
                .ToList();

            UpcomingEventsList.ItemsSource = items;
        }

        private EventItem? ResolveSelectedEvent()
        {
            if (SelectedDateEventsList.SelectedItem is EventListRow row1)
                return _store.GetById(row1.Id);

            if (UpcomingEventsList.SelectedItem is EventListRow row2)
                return _store.GetById(row2.Id);

            return null;
        }

        private void LoadDetails(EventItem? item)
        {
            if (item == null)
            {
                DetailTitleText.Text = "No event selected";
                DetailMetaText.Text = "--";
                DetailAttendeesText.Text = "Attendees: 0";
                DetailNotesTextBox.Text = "";
                return;
            }

            DetailTitleText.Text = item.Title;
            DetailMetaText.Text =
                $"Type: {Safe(item.EventType)}\n" +
                $"Date: {item.EventDate:dddd, MMMM d, yyyy}\n" +
                $"Time: {Safe(item.TimeDisplay)}\n" +
                $"Location: {Safe(item.Location)}\n" +
                $"Host: {Safe(item.Host)}\n" +
                $"Status: {Safe(item.Status)}";

            DetailAttendeesText.Text = $"Attendees: {item.Attendees?.Count ?? 0}";
            DetailNotesTextBox.Text = string.IsNullOrWhiteSpace(item.Notes) ? "No notes." : item.Notes;
        }

        private static string Safe(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? "--" : value.Trim();
        }

        private void PrevMonth_Click(object sender, RoutedEventArgs e)
        {
            _displayMonth = _displayMonth.AddMonths(-1);
            BuildCalendar();
        }

        private void NextMonth_Click(object sender, RoutedEventArgs e)
        {
            _displayMonth = _displayMonth.AddMonths(1);
            BuildCalendar();
        }

        private void Today_Click(object sender, RoutedEventArgs e)
        {
            _selectedDate = DateTime.Today;
            _displayMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            BuildCalendar();
            LoadSelectedDateEvents();
            FooterTextBlock.Text = "Jumped to today.";
        }

        private void CreateEvent_Click(object sender, RoutedEventArgs e)
        {
            var win = new CreateEventWindow
            {
                Owner = this
            };

            win.ShowDialog();
            RefreshAll();
            FooterTextBlock.Text = "Event editor opened.";
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            RefreshAll();
        }

        private void SelectedDateEventsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SelectedDateEventsList.SelectedItem is EventListRow row)
            {
                UpcomingEventsList.SelectedItem = null;
                LoadDetails(_store.GetById(row.Id));
            }
        }

        private void UpcomingEventsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (UpcomingEventsList.SelectedItem is EventListRow row)
            {
                SelectedDateEventsList.SelectedItem = null;
                LoadDetails(_store.GetById(row.Id));
            }
        }

        private void EditEvent_Click(object sender, RoutedEventArgs e)
        {
            var selected = ResolveSelectedEvent();
            if (selected == null)
            {
                FooterTextBlock.Text = "Select an event first.";
                return;
            }

            var win = new CreateEventWindow(selected.Id)
            {
                Owner = this
            };

            win.ShowDialog();
            RefreshAll();
            FooterTextBlock.Text = "Event edited.";
        }

        private void DeleteEvent_Click(object sender, RoutedEventArgs e)
        {
            var selected = ResolveSelectedEvent();
            if (selected == null)
            {
                FooterTextBlock.Text = "Select an event first.";
                return;
            }

            var confirm = MessageBox.Show(
                $"Delete event?\n\n{selected.Title}",
                "Delete Event",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
                return;

            var all = _store.LoadAll();
            all.RemoveAll(x => x.Id == selected.Id);
            _store.SaveAll(all);

            RefreshAll();
            FooterTextBlock.Text = "Event deleted.";
        }

        private async void PostEvent_Click(object sender, RoutedEventArgs e)
        {
            var selected = ResolveSelectedEvent();
            if (selected == null)
            {
                FooterTextBlock.Text = "Select an event first.";
                return;
            }

            try
            {
                var cfg = VtcConfigService.Load(forceReload: true);
                var baseUrl = (cfg.BotApiBaseUrl ?? "").Trim().TrimEnd('/');
                var guildId = (cfg.Discord?.GuildId ?? "").Trim();

                if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(guildId))
                {
                    FooterTextBlock.Text = "BotApiBaseUrl or GuildId missing.";
                    return;
                }

                var payload = new
                {
                    guildId = guildId,
                    text =
                        $"📅 **{selected.Title}**\n" +
                        $"Type: {Safe(selected.EventType)}\n" +
                        $"Date: {selected.EventDate:dddd, MMMM d, yyyy}\n" +
                        $"Time: {Safe(selected.TimeDisplay)}\n" +
                        $"Location: {Safe(selected.Location)}\n" +
                        $"Host: {Safe(selected.Host)}\n" +
                        $"Status: {Safe(selected.Status)}\n" +
                        $"Attending: {selected.Attendees?.Count ?? 0}\n" +
                        $"{Safe(selected.Notes)}",
                    author = "OverWatch Events"
                };

                var json = JsonSerializer.Serialize(payload);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                var resp = await _http.PostAsync($"{baseUrl}/api/vtc/announcements/post", content);

                if (!resp.IsSuccessStatusCode)
                {
                    FooterTextBlock.Text = "Post to Discord failed.";
                    return;
                }

                FooterTextBlock.Text = "Event posted to Discord.";
            }
            catch (Exception ex)
            {
                FooterTextBlock.Text = "Post failed: " + ex.Message;
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}