using Microsoft.Win32;
using Microsoft.Web.WebView2.Wpf;
using OverWatchELD.Models.Media;
using OverWatchELD.Services.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace OverWatchELD.Views.Media
{
    public sealed class MediaPlayerWindow : Window
    {
        private readonly MediaElement _player = new();
        private readonly ListBox _trackList = new();
        private readonly TextBlock _nowPlayingText = new();
        private readonly TextBlock _statusText = new();
        private readonly Slider _volumeSlider = new();
        private readonly Slider _positionSlider = new();
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };

        private readonly WebView2 _spotifyWebView = new();
        private readonly WebView2 _appleWebView = new();
        private readonly TextBlock _spotifyStatusText = new();
        private readonly TextBlock _appleStatusText = new();

        private List<MediaTrack> _tracks = new();
        private bool _isDragging;
        private int _currentIndex = -1;

        public MediaPlayerWindow()
        {
            Title = "OverWatch ELD Media Player";
            Width = 1080;
            Height = 720;
            MinWidth = 900;
            MinHeight = 600;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Brush("#07111F");

            Content = BuildLayout();

            _player.LoadedBehavior = MediaState.Manual;
            _player.UnloadedBehavior = MediaState.Manual;
            _player.Volume = 0.75;

            _player.MediaOpened += (_, _) =>
            {
                if (_player.NaturalDuration.HasTimeSpan)
                    _positionSlider.Maximum = _player.NaturalDuration.TimeSpan.TotalSeconds;
            };

            _player.MediaEnded += (_, _) => PlayNext();
            _player.MediaFailed += (_, e) => SetStatus("Playback failed: " + e.ErrorException.Message);

            Loaded += async (_, _) =>
            {
                LoadAtsMusic();
                await InitializeStreamingTabsAsync();
            };

            Closed += (_, _) =>
            {
                try { _timer.Stop(); _player.Stop(); } catch { }
            };

            _timer.Tick += (_, _) => UpdatePosition();
            _timer.Start();
        }

        private UIElement BuildLayout()
        {
            var root = new Grid { Margin = new Thickness(18) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };

            var title = new TextBlock
            {
                Text = "Media Player",
                FontSize = 28,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(title, Dock.Left);
            header.Children.Add(title);

            var closeTop = Button("Close", (_, _) => Close());
            DockPanel.SetDock(closeTop, Dock.Right);
            header.Children.Add(closeTop);

            Grid.SetRow(header, 0);
            root.Children.Add(header);

            var tabs = new TabControl
            {
                Background = Brush("#07111F"),
                BorderBrush = Brush("#263E5C"),
                Foreground = Brushes.White
            };

            tabs.Resources[typeof(TabItem)] = new Style(typeof(TabItem))
            {
                Setters =
                {
                    new Setter(Control.BackgroundProperty, Brush("#102038")),
                    new Setter(Control.ForegroundProperty, Brushes.White),
                    new Setter(Control.BorderBrushProperty, Brush("#263E5C")),
                    new Setter(Control.PaddingProperty, new Thickness(18, 8, 18, 8)),
                    new Setter(Control.FontWeightProperty, FontWeights.SemiBold)
                },
                Triggers =
                {
                    new Trigger
                    {
                        Property = TabItem.IsSelectedProperty,
                        Value = true,
                        Setters =
                        {
                            new Setter(Control.BackgroundProperty, Brush("#163B65")),
                            new Setter(Control.ForegroundProperty, Brushes.White),
                            new Setter(Control.BorderBrushProperty, Brush("#4A91D0"))
                        }
                    }
                }
            };

            tabs.Items.Add(new TabItem { Header = "Local ATS Music", Content = BuildLocalMusicTab() });
            tabs.Items.Add(new TabItem { Header = "Spotify", Content = BuildSpotifyTab() });
            tabs.Items.Add(new TabItem { Header = "Apple Music", Content = BuildAppleMusicTab() });

            Grid.SetRow(tabs, 1);
            root.Children.Add(tabs);

            var footer = new TextBlock
            {
                Text = "Local music plays directly through OverWatch ELD. Spotify and Apple Music play inside embedded WebView2 tabs after login.",
                Foreground = Brush("#9FB3CC"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 12, 0, 0)
            };
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);

            return root;
        }

        private UIElement BuildLocalMusicTab()
        {
            var root = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var sourcePanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            sourcePanel.Children.Add(Button("ATS Music Folder", (_, _) => LoadAtsMusic()));
            sourcePanel.Children.Add(Button("Choose Folder", (_, _) => ChooseFolder()));
            sourcePanel.Children.Add(Button("Open ATS Music Folder", (_, _) => MediaProviderAuthService.OpenAtsMusicFolder()));
            Grid.SetRow(sourcePanel, 0);
            root.Children.Add(sourcePanel);

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.85, GridUnitType.Star) });

            var left = new Border
            {
                Background = Brush("#0D1A2B"),
                BorderBrush = Brush("#263E5C"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(12),
                Child = _trackList
            };

            _trackList.Background = Brush("#0D1A2B");
            _trackList.Foreground = Brushes.White;
            _trackList.BorderBrush = Brush("#263E5C");
            _trackList.DisplayMemberPath = "DisplayName";
            _trackList.MouseDoubleClick += (_, _) => PlaySelected();

            body.Children.Add(left);

            var right = new Border
            {
                Background = Brush("#0D1A2B"),
                BorderBrush = Brush("#263E5C"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(18)
            };
            Grid.SetColumn(right, 2);

            var panel = new StackPanel();

            _nowPlayingText.Text = "No track playing";
            _nowPlayingText.FontSize = 22;
            _nowPlayingText.FontWeight = FontWeights.Bold;
            _nowPlayingText.Foreground = Brushes.White;
            _nowPlayingText.TextWrapping = TextWrapping.Wrap;
            panel.Children.Add(_nowPlayingText);

            _statusText.Text = "Load music from Documents\\American Truck Simulator\\music or choose a folder.";
            _statusText.Foreground = Brush("#9FB3CC");
            _statusText.TextWrapping = TextWrapping.Wrap;
            _statusText.Margin = new Thickness(0, 8, 0, 18);
            panel.Children.Add(_statusText);

            _positionSlider.Minimum = 0;
            _positionSlider.Maximum = 1;
            _positionSlider.Margin = new Thickness(0, 0, 0, 12);
            _positionSlider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) => _isDragging = true));
            _positionSlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) =>
            {
                _isDragging = false;
                SeekToSlider();
            }));
            panel.Children.Add(_positionSlider);

            var controls = new WrapPanel();
            controls.Children.Add(Button("⏮ Previous", (_, _) => PlayPrevious()));
            controls.Children.Add(Button("▶ Play", (_, _) => PlaySelectedOrCurrent()));
            controls.Children.Add(Button("⏸ Pause", (_, _) => Pause()));
            controls.Children.Add(Button("⏹ Stop", (_, _) => Stop()));
            controls.Children.Add(Button("⏭ Next", (_, _) => PlayNext()));
            panel.Children.Add(controls);

            panel.Children.Add(new TextBlock
            {
                Text = "Volume",
                Foreground = Brush("#9FB3CC"),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 18, 0, 6)
            });

            _volumeSlider.Minimum = 0;
            _volumeSlider.Maximum = 1;
            _volumeSlider.Value = 0.75;
            _volumeSlider.ValueChanged += (_, _) => _player.Volume = _volumeSlider.Value;
            panel.Children.Add(_volumeSlider);

            right.Child = panel;
            body.Children.Add(right);

            Grid.SetRow(body, 1);
            root.Children.Add(body);

            return root;
        }

        private UIElement BuildSpotifyTab()
        {
            var root = BuildStreamingTabRoot(
                title: "Spotify inside OverWatch ELD",
                subtitle: "Log into Spotify below. Use Spotify's web player controls to play playlists inside this window.",
                webView: _spotifyWebView,
                statusText: _spotifyStatusText,
                reloadHandler: async (_, _) => await NavigateSpotifyAsync(),
                openExternalHandler: (_, _) => OpenExternal("https://open.spotify.com/"));

            return root;
        }

        private UIElement BuildAppleMusicTab()
        {
            var root = BuildStreamingTabRoot(
                title: "Apple Music inside OverWatch ELD",
                subtitle: "Log into Apple Music below. Use Apple Music web controls to play playlists inside this window.",
                webView: _appleWebView,
                statusText: _appleStatusText,
                reloadHandler: async (_, _) => await NavigateAppleMusicAsync(),
                openExternalHandler: (_, _) => OpenExternal("https://music.apple.com/"));

            return root;
        }

        private UIElement BuildStreamingTabRoot(
            string title,
            string subtitle,
            WebView2 webView,
            TextBlock statusText,
            RoutedEventHandler reloadHandler,
            RoutedEventHandler openExternalHandler)
        {
            var root = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = new Border
            {
                Background = Brush("#0D1A2B"),
                BorderBrush = Brush("#263E5C"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 10)
            };

            var headerStack = new StackPanel();
            headerStack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = subtitle,
                Foreground = Brush("#9FB3CC"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0)
            });
            header.Child = headerStack;

            Grid.SetRow(header, 0);
            root.Children.Add(header);

            var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            actions.Children.Add(Button("Login / Reload", reloadHandler));
            actions.Children.Add(Button("Open in Browser", openExternalHandler));
            actions.Children.Add(Button("Back", (_, _) => { if (webView.CanGoBack) webView.GoBack(); }));
            actions.Children.Add(Button("Forward", (_, _) => { if (webView.CanGoForward) webView.GoForward(); }));
            actions.Children.Add(Button("Refresh", (_, _) => webView.Reload()));

            statusText.Text = "Initializing embedded player...";
            statusText.Foreground = Brush("#9FB3CC");
            statusText.VerticalAlignment = VerticalAlignment.Center;
            statusText.Margin = new Thickness(8, 8, 0, 0);
            actions.Children.Add(statusText);

            Grid.SetRow(actions, 1);
            root.Children.Add(actions);

            var browserHost = new Border
            {
                Background = Brushes.Black,
                BorderBrush = Brush("#263E5C"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(0),
                Child = webView
            };

            Grid.SetRow(browserHost, 2);
            root.Children.Add(browserHost);

            return root;
        }

        private async System.Threading.Tasks.Task InitializeStreamingTabsAsync()
        {
            await InitializeWebViewAsync(_spotifyWebView, _spotifyStatusText, "Spotify");
            await InitializeWebViewAsync(_appleWebView, _appleStatusText, "Apple Music");

            await NavigateSpotifyAsync();
            await NavigateAppleMusicAsync();
        }

        private static async System.Threading.Tasks.Task InitializeWebViewAsync(WebView2 webView, TextBlock statusText, string name)
        {
            try
            {
                await webView.EnsureCoreWebView2Async();
                webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                statusText.Text = name + " ready.";
            }
            catch (Exception ex)
            {
                statusText.Text = name + " WebView2 failed: " + ex.Message;
            }
        }

        private async System.Threading.Tasks.Task NavigateSpotifyAsync()
        {
            try
            {
                await _spotifyWebView.EnsureCoreWebView2Async();
                _spotifyWebView.Source = new Uri("https://open.spotify.com/");
                _spotifyStatusText.Text = "Spotify loaded. Log in and play a playlist here.";
            }
            catch (Exception ex)
            {
                _spotifyStatusText.Text = "Spotify failed: " + ex.Message;
            }
        }

        private async System.Threading.Tasks.Task NavigateAppleMusicAsync()
        {
            try
            {
                await _appleWebView.EnsureCoreWebView2Async();
                _appleWebView.Source = new Uri("https://music.apple.com/");
                _appleStatusText.Text = "Apple Music loaded. Log in and play a playlist here.";
            }
            catch (Exception ex)
            {
                _appleStatusText.Text = "Apple Music failed: " + ex.Message;
            }
        }

        private static void OpenExternal(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch
            {
            }
        }

        private Button Button(string text, RoutedEventHandler handler)
        {
            var btn = new Button
            {
                Content = text,
                Height = 38,
                MinWidth = 112,
                Margin = new Thickness(0, 0, 8, 8),
                Background = Brush("#163B65"),
                BorderBrush = Brush("#4A91D0"),
                Foreground = Brushes.White,
                Padding = new Thickness(12, 4, 12, 4)
            };

            btn.Click += handler;
            return btn;
        }

        private void LoadAtsMusic()
        {
            _tracks = LocalMusicLibraryService.LoadAtsMusicFolder();
            _trackList.ItemsSource = _tracks;
            SetStatus($"Loaded {_tracks.Count:N0} tracks from ATS music folder: {LocalMusicLibraryService.AtsMusicFolder}");
        }

        private void ChooseFolder()
        {
            var dlg = new OpenFolderDialog
            {
                Title = "Choose Music Folder"
            };

            if (dlg.ShowDialog(this) != true)
                return;

            _tracks = LocalMusicLibraryService.LoadFolder(dlg.FolderName);
            _trackList.ItemsSource = _tracks;

            var cfg = MediaProviderAuthService.Load();
            cfg.LastLocalMusicFolder = dlg.FolderName;
            MediaProviderAuthService.Save(cfg);

            SetStatus($"Loaded {_tracks.Count:N0} tracks from: {dlg.FolderName}");
        }

        private void PlaySelectedOrCurrent()
        {
            if (_trackList.SelectedItem is MediaTrack)
            {
                PlaySelected();
                return;
            }

            if (_currentIndex >= 0)
            {
                _player.Play();
                return;
            }

            if (_tracks.Count > 0)
            {
                _trackList.SelectedIndex = 0;
                PlaySelected();
            }
        }

        private void PlaySelected()
        {
            if (_trackList.SelectedItem is not MediaTrack track)
                return;

            _currentIndex = _tracks.FindIndex(x => string.Equals(x.FullPath, track.FullPath, StringComparison.OrdinalIgnoreCase));
            Play(track);
        }

        private void Play(MediaTrack track)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(track.FullPath) || !File.Exists(track.FullPath))
                {
                    SetStatus("Track file not found.");
                    return;
                }

                _player.Stop();
                _player.Source = new Uri(track.FullPath, UriKind.Absolute);
                _player.Play();

                _nowPlayingText.Text = track.DisplayName;
                SetStatus($"Playing from {track.Source}");
            }
            catch (Exception ex)
            {
                SetStatus("Could not play track: " + ex.Message);
            }
        }

        private void Pause()
        {
            try { _player.Pause(); SetStatus("Paused."); } catch { }
        }

        private void Stop()
        {
            try
            {
                _player.Stop();
                _positionSlider.Value = 0;
                SetStatus("Stopped.");
            }
            catch { }
        }

        private void PlayNext()
        {
            if (_tracks.Count == 0)
                return;

            _currentIndex++;
            if (_currentIndex >= _tracks.Count)
                _currentIndex = 0;

            _trackList.SelectedIndex = _currentIndex;
            Play(_tracks[_currentIndex]);
        }

        private void PlayPrevious()
        {
            if (_tracks.Count == 0)
                return;

            _currentIndex--;
            if (_currentIndex < 0)
                _currentIndex = _tracks.Count - 1;

            _trackList.SelectedIndex = _currentIndex;
            Play(_tracks[_currentIndex]);
        }

        private void UpdatePosition()
        {
            if (_isDragging)
                return;

            try
            {
                if (_player.NaturalDuration.HasTimeSpan)
                {
                    _positionSlider.Maximum = _player.NaturalDuration.TimeSpan.TotalSeconds;
                    _positionSlider.Value = _player.Position.TotalSeconds;
                }
            }
            catch
            {
            }
        }

        private void SeekToSlider()
        {
            try
            {
                _player.Position = TimeSpan.FromSeconds(_positionSlider.Value);
            }
            catch
            {
            }
        }

        private void SetStatus(string text)
        {
            _statusText.Text = text;
        }

        private static SolidColorBrush Brush(string hex)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }
    }
}
