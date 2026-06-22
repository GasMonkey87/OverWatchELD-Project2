using OverWatchELD.Services;
using OverWatchELD.ViewModels;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace OverWatchELD.Views
{
    public partial class DispatchInboxTabView : UserControl
    {
        private readonly DispatcherTimer _pollTimer =
            new() { Interval = TimeSpan.FromSeconds(10) };

        private bool _openingThread;
        private int _lastKnownMessageCount;

        public DispatchInboxTabView()
        {
            InitializeComponent();

            if (DataContext == null)
                DataContext = new DispatchInboxTabViewModel();

            Loaded += DispatchInboxTabView_Loaded;
            Unloaded += DispatchInboxTabView_Unloaded;

            _pollTimer.Tick += PollTimer_Tick;
        }

        private async void DispatchInboxTabView_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            if (DataContext is DispatchInboxTabViewModel vm)
            {
                await vm.InitializeAsync();
                await vm.RefreshAsync(keepSelection: true);

                _lastKnownMessageCount =
                    vm.SelectedMessages.Count;

                ScrollMessagesToBottom(force: true);
            }

            try
            {
                _pollTimer.Start();
            }
            catch
            {
            }
        }

        private void DispatchInboxTabView_Unloaded(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                _pollTimer.Stop();
            }
            catch
            {
            }
        }

        private async void PollTimer_Tick(
            object? sender,
            EventArgs e)
        {
            if (_openingThread)
                return;

            if (DataContext is not DispatchInboxTabViewModel vm)
                return;

            var oldCount = vm.SelectedMessages.Count;
            var wasAtBottom = IsMessagesAtBottom();

            await vm.RefreshAsync(keepSelection: true);

            var newCount = vm.SelectedMessages.Count;

            if (newCount > oldCount)
            {
                var newest =
                    vm.SelectedMessages.LastOrDefault();

                if (newest != null && !newest.IsMine)
                {
                    DashboardToastService.Message(
                        newest.SenderName,
                        newest.Body);
                }
            }

            _lastKnownMessageCount = newCount;

            if (wasAtBottom)
                ScrollMessagesToBottom(force: true);
        }

        private async void Refresh_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (DataContext is DispatchInboxTabViewModel vm)
            {
                await vm.RefreshAsync(keepSelection: true);
                ScrollMessagesToBottom(force: true);
            }
        }

        private void SearchBox_TextChanged(
            object sender,
            TextChangedEventArgs e)
        {
            if (DataContext is DispatchInboxTabViewModel vm)
            {
                vm.SearchText = SearchBox.Text ?? "";
                vm.ApplyThreadFilter();
            }
        }

        private async void ThreadsList_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (_openingThread)
                return;

            if (DataContext is not DispatchInboxTabViewModel vm)
                return;

            try
            {
                _openingThread = true;

                await vm.OpenSelectedThreadAsync();

                if (vm.SelectedThread != null)
                {
                    vm.SelectedThread.UnreadCount = 0;

                    foreach (var msg in vm.SelectedThread.SourceMessages)
                        msg.IsRead = true;
                }

                ScrollMessagesToBottom(force: true);
            }
            finally
            {
                _openingThread = false;
            }
        }

        private async void Send_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (DataContext is not DispatchInboxTabViewModel vm)
                return;

            var text =
                (ComposeBox.Text ?? "").Trim();

            if (string.IsNullOrWhiteSpace(text))
                return;

            var ok = await vm.SendAsync(text);

            if (ok)
            {
                ComposeBox.Text = "";
                ScrollMessagesToBottom(force: true);
            }
        }

        private async void MarkThreadRead_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (DataContext is not DispatchInboxTabViewModel vm)
                return;

            await vm.MarkSelectedThreadReadAsync();

            if (vm.SelectedThread != null)
            {
                vm.SelectedThread.UnreadCount = 0;

                foreach (var msg in vm.SelectedThread.SourceMessages)
                    msg.IsRead = true;
            }
        }

        private async void DeleteThread_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (DataContext is not DispatchInboxTabViewModel vm)
                return;

            await vm.ClearSelectedThreadAsync();
        }

        private void Profile_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (DataContext is not DispatchInboxTabViewModel vm)
                return;

            var row = vm.SelectedThread;

            if (row == null)
            {
                MessageBox.Show(
                    "No conversation selected.",
                    "Profile",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            MessageBox.Show(
                $"Driver: {row.DisplayName}\n" +
                $"Discord: {row.DiscordNameDisplay}\n" +
                $"Role: {row.RoleDisplay}\n" +
                $"Unread: {row.UnreadCount}\n" +
                $"Last Message: {row.LastMessageTimeDisplay}\n" +
                $"Thread Key: {row.ThreadKey}",
                "Conversation Profile",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private async void NewMessage_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (DataContext is not DispatchInboxTabViewModel vm)
                return;

            if (vm.CurrentUserIsDispatchLike)
            {
                var prompt =
                    new NewDispatchMessageWindow(vm.GetDriverSelections())
                    {
                        Owner = Window.GetWindow(this)
                    };

                if (prompt.ShowDialog() == true)
                {
                    await vm.StartNewConversationAsync(
                        prompt.TargetDriverName,
                        prompt.TargetDiscordUserId,
                        prompt.MessageBody,
                        prompt.AttachmentPath,
                        prompt.AttachmentFileName);

                    ScrollMessagesToBottom(force: true);
                }

                return;
            }

            var text =
                Microsoft.VisualBasic.Interaction.InputBox(
                    "Enter your message to Dispatch:",
                    "Message Dispatch",
                    "");

            if (string.IsNullOrWhiteSpace(text))
                return;

            await vm.StartNewConversationAsync(
                "Dispatch",
                "",
                text.Trim());

            ScrollMessagesToBottom(force: true);
        }

        private void ScrollMessagesToBottom(bool force = false)
        {
            try
            {
                if (MessagesList.Items.Count == 0)
                    return;

                if (!force && !IsMessagesAtBottom())
                    return;

                MessagesList.ScrollIntoView(
                    MessagesList.Items[
                        MessagesList.Items.Count - 1]);
            }
            catch
            {
            }
        }

        private bool IsMessagesAtBottom()
        {
            try
            {
                var viewer =
                    FindScrollViewer(MessagesList);

                if (viewer == null)
                    return true;

                return viewer.VerticalOffset >=
                       viewer.ScrollableHeight - 5;
            }
            catch
            {
                return true;
            }
        }

        private static ScrollViewer? FindScrollViewer(
            DependencyObject parent)
        {
            try
            {
                for (int i = 0;
                     i < VisualTreeHelper.GetChildrenCount(parent);
                     i++)
                {
                    var child =
                        VisualTreeHelper.GetChild(parent, i);

                    if (child is ScrollViewer sv)
                        return sv;

                    var result =
                        FindScrollViewer(child);

                    if (result != null)
                        return result;
                }
            }
            catch
            {
            }

            return null;
        }
    }
}