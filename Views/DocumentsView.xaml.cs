using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace OverWatchELD.Views
{
    public partial class DocumentsView : UserControl
    {
        public DocumentsView()
        {
            InitializeComponent();

            // Simple self-binding so we can bind AboutText/ReadmeText without a full VM
            DataContext = this;

            // Ensure correct initial panel
            try { NavList.SelectedIndex = 0; } catch { }
        }

        // About + Readme strings (bound in XAML)
        public string AboutText => BuildAboutText();

        public string ReadmeText =>
@"OverWatch ELD is a realistic Electronic Logging Device (ELD)-style app built for American Truck Simulator roleplay and VTC operations.

WHAT IT DOES
• Duty status control (Off Duty / Sleeper / Driving / On Duty)
• Real-time HOS clocks (shift/drive/break/cycle)
• Daily logs + inspection workflows
• Live telemetry-based auto logging (engine/speed/location where available)
• Fleet Manager tracking (fuel, mileage, damage) by driver
• Optional Discord posting (weekly fleet reports / exports when configured)

REQUIREMENTS (FOR LIVE FEATURES)
• ATS running
• Telemetry Server (Funbit) on localhost:25555

VTC TOOLS
• Performance (driver stats)
• Fleet Manager (driver + truck aggregation)
• Roster (VTC members)
• Messages (dispatch flow)

NOTES
• Telemetry fields vary by plugin build; some values are best-effort.
• This is a simulation tool for roleplay—not a real-world certified ELD.";

        private static string BuildAboutText()
        {
            // Version: pull from entry assembly if available; fallback to 2.0
            string ver = "2.0";
            try
            {
                var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
                var v = asm.GetName().Version;
                if (v != null) ver = $"{v.Major}.{v.Minor}.{v.Build}";
            }
            catch { }

            return
$@"OverWatch ELD
Version: {ver}
Creator: GasMonkey Creations
© 2026 GasMonkeyCreations. All rights reserved.";
        }

        private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                var idx = NavList.SelectedIndex;
                PanelDocuments.Visibility = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
                PanelBol.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;
                PanelAbout.Visibility = idx == 2 ? Visibility.Visible : Visibility.Collapsed;
                PanelReadme.Visibility = idx == 3 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch { }
        }

        private void OpenBol_Click(object sender, RoutedEventArgs e)
        {
            try { NavList.SelectedIndex = 1; } catch { }
        }

        private void OpenReadme_Click(object sender, RoutedEventArgs e)
        {
            try { NavList.SelectedIndex = 3; } catch { }
        }

        // ✅ BOL button -> BolWindow
        private void Bol_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new BolWindow();
                win.Owner = Window.GetWindow(this);
                win.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                win.ShowDialog();
            }
            catch (Exception ex)
            {
                try
                {
                    MessageBox.Show("Unable to open BOL window.\n\n" + ex.Message,
                        "OverWatch ELD", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch { }
            }
        }
    }
}