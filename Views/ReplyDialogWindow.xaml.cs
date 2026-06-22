using System.Windows;

namespace OverWatchELD.Views
{
    public partial class ReplyDialogWindow : Window
    {
        public string ReplyText { get; private set; } = "";

        public ReplyDialogWindow()
        {
            InitializeComponent();
        }

        private void Send_Click(object sender, RoutedEventArgs e)
        {
            ReplyText = ReplyBox.Text ?? "";
            DialogResult = true;
            Close();
        }
    }
}
