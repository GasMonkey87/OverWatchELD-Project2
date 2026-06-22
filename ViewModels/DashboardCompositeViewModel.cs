using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows;
using System.Windows.Controls;

namespace ATS_ELD.ViewModels
{
    /// <summary>
    /// Single composite VM for the Dashboard screen.
    /// Handles duty status + top navigation buttons (Logs / Messages / Support).
    /// </summary>
    public partial class DashboardCompositeViewModel : ObservableObject
    {
        public DutyStatusViewModel Duty { get; } = new DutyStatusViewModel();

        public IRelayCommand GoLogsCommand { get; }
        public IRelayCommand GoMessagesCommand { get; }
        public IRelayCommand GoSupportCommand { get; }

        public DashboardCompositeViewModel()
        {
            GoLogsCommand = new RelayCommand(() => SelectTab("ComplianceTab"));
            GoMessagesCommand = new RelayCommand(() => SelectTab("MessagesTab"));
            GoSupportCommand = new RelayCommand(() => SelectTab("SupportTab"));
        }

        private static void SelectTab(string tabName)
        {
            if (Application.Current?.MainWindow is not Window window)
                return;

            if (window.FindName(tabName) is TabItem tab)
            {
                if (FindParentTabControl(tab) is TabControl tc)
                    tc.SelectedItem = tab;
            }
        }

        private static TabControl? FindParentTabControl(DependencyObject child)
        {
            DependencyObject parent = child;
            while (parent != null)
            {
                if (parent is TabControl tc)
                    return tc;

                parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
            }
            return null;
        }
    }
}
