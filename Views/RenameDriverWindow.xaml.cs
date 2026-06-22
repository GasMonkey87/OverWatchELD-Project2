using System.Windows;

namespace OverWatchELD.Views
{
    public partial class RenameDriverWindow : Window
    {
        public string NewDriverName { get; private set; } = "";

        public RenameDriverWindow(string currentName)
        {
            InitializeComponent();
            NameBox.Text = currentName ?? "";
            NameBox.SelectAll();
            NameBox.Focus();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var value = (NameBox.Text ?? "").Trim();

            if (string.IsNullOrWhiteSpace(value))
            {
                MessageBox.Show(
                    "Please enter a valid driver name.",
                    "Change Name",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            NewDriverName = value;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}