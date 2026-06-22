using System;
using System.Collections.Generic;
using System.Linq;
using DutyStatus = OverWatchELD.Models.DutyStatus;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OverWatchELD.Models;
using OverWatchELD.Services;

namespace OverWatchELD.ViewModels
{
    public partial class EditDutyDialogViewModel : ObservableObject
    {
        private readonly DutyEvent _original;

        // Bound fields
        [ObservableProperty] private DateTimeOffset startUtc;
        [ObservableProperty] private DateTimeOffset? endUtc;
        [ObservableProperty] private DutyStatus status;
        [ObservableProperty] private string notes = "";
        [ObservableProperty] private string locationText = "";

        // DOT correction note (required)
        [ObservableProperty] private string correctionNote = "";

        // For dialog result
        public bool? DialogResult { get; private set; }
        public DutyEvent Edited { get; private set; } = new DutyEvent();

        public IReadOnlyList<DutyStatusOption> StatusOptions { get; } = new[]
        {
            new DutyStatusOption(DutyStatus.OffDuty, "OFF DUTY"),
            new DutyStatusOption(DutyStatus.Sleeper, "SLEEPER"),
            new DutyStatusOption(DutyStatus.Driving, "DRIVING"),
            new DutyStatusOption(DutyStatus.OnDuty, "ON DUTY"),
            new DutyStatusOption(DutyStatus.PersonalConveyance, "PERSONAL CONVEYANCE"),
            new DutyStatusOption(DutyStatus.YardMove, "YARD MOVE"),
        };

        public EditDutyDialogViewModel(DutyEvent ev)
        {
            _original = ev ?? throw new ArgumentNullException(nameof(ev));

            StartUtc = ev.StartUtc;
            EndUtc = ev.EndUtc;
            Status = ev.Status;
            Notes = ev.Notes ?? "";
            LocationText = ev.LocationText ?? "";
        }

        public bool IsDrivingLocked => _original.Status == DutyStatus.Driving;

        [RelayCommand]
        private void Save()
        {
            // DOT rule: cannot edit driving
            if (IsDrivingLocked || Status == DutyStatus.Driving)
            {
                DialogResult = false;
                return;
            }

            if (string.IsNullOrWhiteSpace(CorrectionNote))
            {
                // keep dialog open; UI can show validation message if you want
                return;
            }

            // basic sanity
            var s = StartUtc;
            var e = EndUtc;

            if (e.HasValue && e.Value < s)
                e = s;

            // Build edited record
            var edited = new DutyEvent
            {
                Id = _original.Id,
                StartUtc = s,
                EndUtc = e,
                Status = Status,
                LocationText = string.IsNullOrWhiteSpace(LocationText) ? null : LocationText.Trim(),

                // Store correction note inside Notes so we don't need DB schema changes
                Notes = BuildCorrectionNotes(_original.Notes, Notes, CorrectionNote),

                Lat = _original.Lat,
                Lon = _original.Lon,
                Source = _original.Source,

                IsEdited = true,
                EditedAtUtc = DateTimeOffset.UtcNow,
                EditReason = CorrectionNote.Trim()
            };

            // Persist
            if (edited.Id > 0)
                DatabaseService.UpdateDutyEvent(edited);

            Edited = edited;
            DialogResult = true;
        }

        [RelayCommand]
        private void Cancel()
        {
            DialogResult = false;
        }

        private static string BuildCorrectionNotes(string? oldNotes, string newNotes, string correctionNote)
        {
            // Keep existing notes, append correction marker in DOT-friendly way
            // Format example:
            // "Original note... | CORRECTION: <note> | USER: <notes>"
            var baseText = (oldNotes ?? "").Trim();
            var userText = (newNotes ?? "").Trim();
            var corr = (correctionNote ?? "").Trim();

            string combined = baseText;

            if (!string.IsNullOrWhiteSpace(corr))
            {
                if (!string.IsNullOrWhiteSpace(combined)) combined += " | ";
                combined += $"CORRECTION: {corr}";
            }

            if (!string.IsNullOrWhiteSpace(userText))
            {
                if (!string.IsNullOrWhiteSpace(combined)) combined += " | ";
                combined += $"USER: {userText}";
            }

            return combined;
        }
    }
}
