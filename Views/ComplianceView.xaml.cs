using System.Windows.Controls;
using OverWatchELD.ViewModels;

namespace OverWatchELD.Views
{
    public partial class ComplianceView : UserControl
    {
        public ComplianceView()
        {
            InitializeComponent();
            DataContext = new ComplianceViewModel();
        }
    }
}