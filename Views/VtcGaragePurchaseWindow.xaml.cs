using System.Windows;
using OverWatchELD.ViewModels;

namespace OverWatchELD.Views
{
    public partial class VtcGaragePurchaseWindow : Window
    {
        public VtcGaragePurchaseWindow()
        {
            InitializeComponent();
            DataContext = new VtcGaragePurchaseWindowViewModel();
        }
    }
}