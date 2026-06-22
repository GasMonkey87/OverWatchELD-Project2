using System;
using System.Windows;
using ATS_ELD.ViewModels;

namespace ATS_ELD
{
    public partial class MainWindow : Window
    {
        private DispatchInboxTabViewModel? _inboxVm;

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Wire Messaging tab DataContext (separate from dashboard/clocks)
                if (_inboxVm == null)
                {
                    var app = (App)Application.Current;
                    _inboxVm = new DispatchInboxTabViewModel(app.DispatchInbox);

                    MessagingTab.DataContext = _inboxVm;
                    MessagingTabItem.DataContext = _inboxVm;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Messaging init failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
