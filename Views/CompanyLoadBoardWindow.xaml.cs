using System.Windows;
using OverWatchELD.ViewModels;

namespace OverWatchELD.Views
{
    public partial class CompanyLoadBoardWindow : Window
    {
        public CompanyLoadBoardWindow()
        {
            InitializeComponent();
            DataContext = new CompanyLoadBoardViewModel();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
