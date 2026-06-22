using System;

namespace OverWatchELD.ViewModels
{
    public partial class DispatchInboxTabViewModel
    {
        private string _replyText = "";

        public string ReplyText
        {
            get => _replyText;
            set
            {
                if (_replyText == value) return;
                _replyText = value;
                OnPropertyChanged(nameof(ReplyText));

                // If your ReplyCommand supports CanExecute changes, refresh it safely.
                try
                {
                    // Covers common patterns without hard dependency
                    var mi = ReplyCommand?.GetType().GetMethod("RaiseCanExecuteChanged");
                    mi?.Invoke(ReplyCommand, Array.Empty<object>());
                }
                catch { }
            }
        }
    }
}