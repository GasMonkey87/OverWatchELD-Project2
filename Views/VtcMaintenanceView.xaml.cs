using OverWatchELD.Models;
using OverWatchELD.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OverWatchELD.Views
{
    public partial class VtcMaintenanceView : UserControl
    {
        public VtcMaintenanceView() : this("Driver")
        {
        }

        public VtcMaintenanceView(string currentRole)
        {
            InitializeComponent();
            DataContext = new VtcMaintenanceViewModel();
        }

        private void RequestsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (DataContext is not VtcMaintenanceViewModel vm)
                    return;

                if (vm.SelectedMaintenanceRequest is not MaintenanceRequestTicket ticket)
                    return;

                var owner = Window.GetWindow(this);

                var win = new MaintenanceRequestFixWindow(ticket)
                {
                    Owner = owner
                };

                var result = win.ShowDialog();

                if (result == true)
                    vm.Load();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Unable to open maintenance request: " + ex.Message,
                    "Maintenance Request",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}