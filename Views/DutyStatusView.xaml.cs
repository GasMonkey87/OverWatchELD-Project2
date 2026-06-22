using System.Windows.Controls;
using OverWatchELD.ViewModels;

namespace OverWatchELD.Views
{
    public partial class DutyStatusView : UserControl
    {
        public DutyStatusView()
        {
            InitializeComponent();
            DataContext ??= new DutyStatusViewModel();
        }
    }
}
