using System.Windows.Controls;
using OverWatchELD.ViewModels;

namespace OverWatchELD.Views
{
    public partial class DriverPerformanceView : UserControl
    {
        public DriverPerformanceView()
        {
            InitializeComponent();
            DataContext = new DriverPerformanceViewModel();
        }
    }
}
