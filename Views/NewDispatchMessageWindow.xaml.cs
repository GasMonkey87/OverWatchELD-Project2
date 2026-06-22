using Microsoft.Win32;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace OverWatchELD.Views
{
    public partial class NewDispatchMessageWindow : Window
    {
        public sealed class DriverPickItem
        {
            public string DriverName { get; set; } = "";
            public string DiscordUserId { get; set; } = "";
            public string Role { get; set; } = "";

            public string DisplayText =>
                string.IsNullOrWhiteSpace(Role)
                    ? DriverName
                    : $"{DriverName} ({Role})";
        }

        public string TargetDriverName { get; private set; } = "";
        public string TargetDiscordUserId { get; private set; } = "";
        public string MessageBody { get; private set; } = "";

        public string AttachmentPath { get; private set; } = "";
        public string AttachmentFileName { get; private set; } = "";

        public NewDispatchMessageWindow(IEnumerable<DriverPickItem> drivers)
        {
            InitializeComponent();

            var list = drivers?
                .Where(x => !string.IsNullOrWhiteSpace(x.DriverName))
                .OrderBy(x => x.DriverName)
                .ToList() ?? new List<DriverPickItem>();

            DriverCombo.ItemsSource = list;

            if (list.Count > 0)
                DriverCombo.SelectedIndex = 0;
            else
            {
                SelectedDriverText.Text = "Driver: No drivers available";
                SelectedDiscordText.Text = "Discord ID: --";
            }

            AttachmentText.Text = "No attachment selected";
        }

        private void DriverCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DriverCombo.SelectedItem is not DriverPickItem selected)
            {
                SelectedDriverText.Text = "Driver: --";
                SelectedDiscordText.Text = "Discord ID: --";
                return;
            }

            SelectedDriverText.Text = $"Driver: {selected.DriverName}";
            SelectedDiscordText.Text = string.IsNullOrWhiteSpace(selected.DiscordUserId)
                ? "Discord ID: Missing"
                : $"Discord ID: {selected.DiscordUserId}";
        }

        private void Attach_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Attach File",
                Filter = "All supported files|*.png;*.jpg;*.jpeg;*.pdf;*.txt;*.doc;*.docx;*.xls;*.xlsx;*.zip|All files|*.*",
                Multiselect = false
            };

            if (dlg.ShowDialog(this) != true)
                return;

            AttachmentPath = dlg.FileName;
            AttachmentFileName = Path.GetFileName(dlg.FileName);
            AttachmentText.Text = AttachmentFileName;
        }

        private void ClearAttachment_Click(object sender, RoutedEventArgs e)
        {
            AttachmentPath = "";
            AttachmentFileName = "";
            AttachmentText.Text = "No attachment selected";
        }

        private void Send_Click(object sender, RoutedEventArgs e)
        {
            if (DriverCombo.SelectedItem is not DriverPickItem selected)
            {
                MessageBox.Show("Select a driver.", "New Message", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            TargetDriverName = (selected.DriverName ?? "").Trim();
            TargetDiscordUserId = (selected.DiscordUserId ?? "").Trim();
            MessageBody = (MessageInput.Text ?? "").Trim();

            if (string.IsNullOrWhiteSpace(TargetDriverName))
            {
                MessageBox.Show("Selected driver is invalid.", "New Message", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(TargetDiscordUserId))
            {
                MessageBox.Show("That driver does not have a Discord ID linked yet.", "New Message", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(MessageBody) && string.IsNullOrWhiteSpace(AttachmentPath))
            {
                MessageBox.Show("Enter a message or attach a file.", "New Message", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

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