using System.Windows;
using OverWatchELD.ViewModels;

namespace OverWatchELD.Views
{
    public partial class LoadHistoryWindow : Window
    {
        public LoadHistoryWindow()
        {
            InitializeComponent();
            DataContext = new LoadHistoryViewModel();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
