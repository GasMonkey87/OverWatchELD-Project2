using System.Windows;

namespace OverWatchELD.Views
{
    public partial class ShiftDebugWindow : Window
    {
        public ShiftDebugWindow(object vm)
        {
            InitializeComponent();
            DataContext = vm;
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
