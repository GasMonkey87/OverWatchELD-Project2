using System.Windows;
using OverWatchELD.ViewModels;

namespace OverWatchELD.Views
{
    public partial class VtcGarageWindow : Window
    {
        public VtcGarageWindow()
        {
            InitializeComponent();
            DataContext = new VtcGarageWindowViewModel();
        }

        private void OpenAddGarage_Click(object sender, RoutedEventArgs e)
        {
            var win = new VtcGaragePurchaseWindow
            {
                Owner = this
            };

            win.ShowDialog();

            if (DataContext is VtcGarageWindowViewModel vm)
            {
                vm.ReloadGarages();
            }
        }
    }
}