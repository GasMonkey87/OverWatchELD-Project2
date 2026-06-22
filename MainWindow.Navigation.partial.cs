using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace ATS_ELD
{
    public partial class MainWindow : Window
    {
        /// <summary>
        /// Central navigation entry point used by Dashboard tiles.
        /// Works even if your content host has a different name.
        /// </summary>
        public void Navigate(string destination)
        {
            destination = (destination ?? "").Trim();

            // Map route -> view type names (try multiple candidates)
            string[] typeCandidates = destination.ToLowerInvariant() switch
            {
                "logs" => new[] { "ATS_ELD.Views.LogsView", "ATS_ELD.Views.LogsPage" },
                "unsignedlogs" or "unsigned" => new[] { "ATS_ELD.Views.UnsignedLogsView", "ATS_ELD.Views.LogsView" },
                "messages" => new[] { "ATS_ELD.Views.MessagesView", "ATS_ELD.Views.MessagesPage" },
                "support" => new[] { "ATS_ELD.Views.SupportView", "ATS_ELD.Views.SupportPage" },
                "documents" => new[] { "ATS_ELD.Views.DocumentsView", "ATS_ELD.Views.DocumentsPage", "ATS_ELD.Views.DocsView" },
                "dashboard" => new[] { "ATS_ELD.Views.DashboardView", "ATS_ELD.Views.DashboardPage" },
                _ => new[] { "ATS_ELD.Views.DashboardView" }
            };

            // Create view instance (first type that exists)
            var view = CreateFirstAvailableView(typeCandidates);

            if (view == null)
            {
                // If a target doesn't exist (like Documents), fall back to Dashboard
                view = CreateFirstAvailableView(new[] { "ATS_ELD.Views.DashboardView" });

                MessageBox.Show(
                    $"Navigation target not found: {destination}\n\n" +
                    $"Tried: {string.Join(", ", typeCandidates)}\n\n" +
                    $"Fell back to Dashboard.",
                    "Navigation",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            if (view == null)
            {
                MessageBox.Show("Unable to create any view instance for navigation.",
                    "Navigation", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 1) If your MainWindow has a Frame host anywhere, navigate it
            var frame = FindVisualChild<Frame>(this);
            if (frame != null)
            {
                frame.Navigate(view);
                return;
            }

            // 2) If your MainWindow has a ContentControl host anywhere, set Content
            var cc = FindVisualChild<ContentControl>(this);
            if (cc != null)
            {
                cc.Content = view;
                return;
            }

            // 3) If you use a TabControl pattern, try selecting by header/tag/name
            var tabs = FindVisualChild<TabControl>(this);
            if (tabs != null)
            {
                var key = destination.ToLowerInvariant();
                var match =
                    tabs.Items.OfType<TabItem>().FirstOrDefault(t =>
                        (t.Tag?.ToString() ?? "").ToLowerInvariant() == key ||
                        (t.Header?.ToString() ?? "").ToLowerInvariant().Contains(key));

                if (match != null)
                {
                    tabs.SelectedItem = match;
                    return;
                }
            }

            MessageBox.Show(
                "No navigation host found in MainWindow.\n\n" +
                "I looked for:\n- Frame\n- ContentControl\n- TabControl\n\n" +
                "If your host is custom, paste MainWindow.xaml and I’ll bind it exactly.",
                "Navigation",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        private object? CreateFirstAvailableView(string[] fullTypeNames)
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();

                foreach (var name in fullTypeNames)
                {
                    // Try exact match inside current assembly first
                    var t = asm.GetType(name, throwOnError: false, ignoreCase: false);

                    // If not found, also try case-insensitive as a last resort
                    t ??= asm.GetTypes().FirstOrDefault(x =>
                        string.Equals(x.FullName, name, StringComparison.OrdinalIgnoreCase));

                    if (t == null) continue;

                    // Create instance
                    var obj = Activator.CreateInstance(t);
                    if (obj != null) return obj;
                }
            }
            catch { }

            return null;
        }

        private static T? FindVisualChild<T>(DependencyObject root) where T : DependencyObject
        {
            try
            {
                int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
                for (int i = 0; i < count; i++)
                {
                    var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                    if (child is T typed) return typed;

                    var nested = FindVisualChild<T>(child);
                    if (nested != null) return nested;
                }
            }
            catch { }
            return null;
        }
    }
}
