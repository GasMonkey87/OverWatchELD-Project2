using System.Windows;
using OverWatchELD.ViewModels;

namespace OverWatchELD.Views
{
    public partial class ExpenseReceiptsWindow : Window
    {
        public ExpenseReceiptsWindow()
        {
            InitializeComponent();
            DataContext = new ExpenseReceiptsViewModel();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
