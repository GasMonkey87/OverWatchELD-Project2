using OverWatchELD.ViewModels;
using OverWatchELD.Services;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Forms = System.Windows.Forms;

namespace OverWatchELD.Views
{
    public partial class SupportView : UserControl
    {
        public SupportView()
        {
            InitializeComponent();

            // Force this view to use the correct ViewModel.
            if (DataContext is not SupportViewModel)
                DataContext = new SupportViewModel();
        }

        private void BrowseTelemetryExe_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new Forms.OpenFileDialog
            {
                Title = "Select Telemetry Server EXE",
                Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*",
                CheckFileExists = true,
                CheckPathExists = true,
                Multiselect = false
            };

            if (DataContext is SupportViewModel vm &&
                !string.IsNullOrWhiteSpace(vm.TelemetryExePath))
            {
                var dir = Path.GetDirectoryName(vm.TelemetryExePath);
                if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                    dialog.InitialDirectory = dir;
            }

            if (dialog.ShowDialog() != Forms.DialogResult.OK)
                return;

            if (DataContext is SupportViewModel model)
            {
                model.TelemetryExePath = dialog.FileName;
                model.SaveTelemetrySettingsFromView();

                MessageBox.Show(
                    "Telemetry executable saved:\n\n" + dialog.FileName,
                    "OverWatch ELD",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        private void OpenGuide_Click(object sender, RoutedEventArgs e)
        {
            HelpGuideService.OpenGuide(Window.GetWindow(this));
        }

    }
}