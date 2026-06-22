using System.Windows;
using System.Windows.Controls;
using OverWatchELD.ViewModels;

namespace OverWatchELD.Views
{
    public partial class MessagesView : UserControl
    {
        public MessagesView()
        {
            InitializeComponent();

            Loaded += (_, __) =>
            {
                // If parent didn't set DataContext, make it self-contained
                if (DataContext == null)
                    DataContext = new MessagesViewModel();

                (DataContext as MessagesViewModel)?.Start();
            };

            Unloaded += (_, __) =>
            {
                (DataContext as MessagesViewModel)?.Stop();
            };
        }

        private MessagesViewModel? Vm => DataContext as MessagesViewModel;

        private void ApplyRecipient_Click(object sender, RoutedEventArgs e)
        {
            Vm?.ApplyRecipient();
        }

        private async void Send_Click(object sender, RoutedEventArgs e)
        {
            if (Vm == null) return;

            // If user typed To: but didn't Apply, do it automatically
            if (!string.IsNullOrWhiteSpace(Vm.RecipientText) && Vm.SelectedConversation == null)
                Vm.ApplyRecipient();

            await Vm.SendFromComposerAsync();
        }

        private void Attach_Click(object sender, RoutedEventArgs e) { /* no-op for now */ }

        private void ClearAttach_Click(object sender, RoutedEventArgs e) { /* no-op for now */ }
    }
}
