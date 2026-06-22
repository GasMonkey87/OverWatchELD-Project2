using System;
using System.Collections.Generic;
using System.Linq;
using OverWatchELD.Models;
using DutyStatus = OverWatchELD.Models.DutyStatus;

namespace OverWatchELD.Services
{
    public static class HosCalculator2
    {
        public readonly record struct Snapshot(
            TimeSpan BreakRemaining,
            TimeSpan DriveRemaining,
            TimeSpan ShiftRemaining,
            TimeSpan CycleRemaining,
            TimeSpan BreakLimit,
            TimeSpan DriveLimit,
            TimeSpan ShiftLimit,
            TimeSpan CycleLimit);

        private static readonly TimeSpan BreakDrivingLimit = TimeSpan.FromHours(8);
        private static readonly TimeSpan DriveLimit = TimeSpan.FromHours(11);
        private static readonly TimeSpan ShiftLimit = TimeSpan.FromHours(14);
        private static readonly TimeSpan CycleLimit = TimeSpan.FromHours(70);

        private static readonly TimeSpan ShiftReset = TimeSpan.FromHours(10);
        private static readonly TimeSpan CycleRestart = TimeSpan.FromHours(34);

        public static Snapshot ComputeSnapshot()
        {
            var nowUtc = EldClock.UtcNow;
            var historyStartUtc = nowUtc.AddDays(-14);

            List<DutyEvent> events;

            try
            {
                events = DatabaseService.GetDutyEvents(historyStartUtc, nowUtc)
                    .OrderBy(e => e.StartUtc)
                    .ToList();
            }
            catch
            {
                return FullSnapshot();
            }

            if (events.Count == 0)
                return FullSnapshot();

            var latestReset = events
                .Where(IsClockResetEvent)
                .OrderByDescending(e => e.StartUtc)
                .FirstOrDefault();

            DateTimeOffset? resetStartUtc = null;

            if (latestReset != null)
            {
                resetStartUtc = latestReset.StartUtc;

                events = events
                    .Where(e => (e.EndUtc ?? nowUtc) >= resetStartUtc.Value)
                    .OrderBy(e => e.StartUtc)
                    .ToList();
            }

            if (events.Count == 0)
                return FullSnapshot();

            var shiftStartUtc = FindLastConsecutiveOffEndUtc(events, nowUtc, ShiftReset) ?? events.First().StartUtc;
            var cycleStartUtc = FindLastConsecutiveOffEndUtc(events, nowUtc, CycleRestart) ?? nowUtc.AddDays(-8);

            if (resetStartUtc.HasValue)
            {
                if (shiftStartUtc < resetStartUtc.Value)
                    shiftStartUtc = resetStartUtc.Value;

                if (cycleStartUtc < resetStartUtc.Value)
                    cycleStartUtc = resetStartUtc.Value;
            }

            var eightDayStart = nowUtc.AddDays(-8);
            if (cycleStartUtc < eightDayStart)
                cycleStartUtc = eightDayStart;

            var driveSinceBreak = DrivingSinceLast30MinNonDriving(events, shiftStartUtc, nowUtc);

            var drivingInShift = SumStatus(events, shiftStartUtc, nowUtc, DutyStatus.Driving);
            var onDutyInShift = SumOnDuty(events, shiftStartUtc, nowUtc);
            var onDutyInCycle = SumOnDuty(events, cycleStartUtc, nowUtc);

            return new Snapshot(
                BreakDrivingLimit - driveSinceBreak,
                DriveLimit - drivingInShift,
                ShiftLimit - onDutyInShift,
                CycleLimit - onDutyInCycle,
                BreakDrivingLimit,
                DriveLimit,
                ShiftLimit,
                CycleLimit);
        }

        private static Snapshot FullSnapshot()
        {
            return new Snapshot(
                BreakDrivingLimit,
                DriveLimit,
                ShiftLimit,
                CycleLimit,
                BreakDrivingLimit,
                DriveLimit,
                ShiftLimit,
                CycleLimit);
        }

        private static bool IsClockResetEvent(DutyEvent e)
        {
            var notes = e.Notes ?? "";
            var reason = e.EditReason ?? "";
            var source = e.Source ?? "";

            return notes.Contains("Clocks Reset Used", StringComparison.OrdinalIgnoreCase)
                || notes.Contains("HOS Reset Applied", StringComparison.OrdinalIgnoreCase)
                || reason.Contains("Driver requested HOS clock reset", StringComparison.OrdinalIgnoreCase)
                || source.Contains("clock-reset", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsOffLike(DutyStatus s) =>
            s == DutyStatus.OffDuty ||
            s == DutyStatus.SleeperBerth ||
            s == DutyStatus.PersonalConveyance;

        private static bool IsOnDuty(DutyStatus s) =>
            s == DutyStatus.Driving ||
            s == DutyStatus.OnDuty ||
            s == DutyStatus.YardMove;

        private static TimeSpan SumStatus(List<DutyEvent> events, DateTimeOffset winStartUtc, DateTimeOffset nowUtc, DutyStatus status)
        {
            TimeSpan total = TimeSpan.Zero;

            foreach (var ev in events)
            {
                if (IsClockResetEvent(ev))
                    continue;

                var end = ev.EndUtc ?? nowUtc;
                var dur = Overlap(ev.StartUtc, end, winStartUtc, nowUtc);

                if (dur <= TimeSpan.Zero)
                    continue;

                if (ev.Status == status)
                    total += dur;
            }

            return total;
        }

        private static TimeSpan SumOnDuty(List<DutyEvent> events, DateTimeOffset winStartUtc, DateTimeOffset nowUtc)
        {
            TimeSpan total = TimeSpan.Zero;

            foreach (var ev in events)
            {
                if (IsClockResetEvent(ev))
                    continue;

                var end = ev.EndUtc ?? nowUtc;
                var dur = Overlap(ev.StartUtc, end, winStartUtc, nowUtc);

                if (dur <= TimeSpan.Zero)
                    continue;

                if (IsOnDuty(ev.Status))
                    total += dur;
            }

            return total;
        }

        private static DateTimeOffset? FindLastConsecutiveOffEndUtc(List<DutyEvent> events, DateTimeOffset nowUtc, TimeSpan required)
        {
            if (events.Count == 0)
                return null;

            DateTimeOffset curEnd = nowUtc;
            TimeSpan acc = TimeSpan.Zero;

            for (int i = events.Count - 1; i >= 0; i--)
            {
                var ev = events[i];

                if (IsClockResetEvent(ev))
                    return ev.StartUtc;

                var evEnd = ev.EndUtc ?? nowUtc;
                var segEnd = evEnd < curEnd ? evEnd : curEnd;
                var segStart = ev.StartUtc;

                if (segEnd <= segStart)
                    continue;

                if (IsOffLike(ev.Status))
                {
                    var seg = segEnd - segStart;
                    acc += seg;

                    if (acc >= required)
                        return segEnd;

                    curEnd = segStart;
                }
                else
                {
                    acc = TimeSpan.Zero;
                    curEnd = ev.StartUtc;
                }
            }

            return null;
        }

        private static TimeSpan DrivingSinceLast30MinNonDriving(List<DutyEvent> events, DateTimeOffset winStartUtc, DateTimeOffset nowUtc)
        {
            var ordered = events
                .Where(e => !IsClockResetEvent(e))
                .Where(e => (e.EndUtc ?? nowUtc) > winStartUtc)
                .OrderBy(e => e.StartUtc)
                .ToList();

            if (ordered.Count == 0)
                return TimeSpan.Zero;

            DateTimeOffset breakCutoff = winStartUtc;

            for (int i = ordered.Count - 1; i >= 0; i--)
            {
                var ev = ordered[i];
                var end = ev.EndUtc ?? nowUtc;

                var seg = Overlap(ev.StartUtc, end, winStartUtc, nowUtc);
                if (seg <= TimeSpan.Zero)
                    continue;

                if (ev.Status != DutyStatus.Driving && seg >= TimeSpan.FromMinutes(30))
                {
                    breakCutoff = end < nowUtc ? end : nowUtc;
                    break;
                }
            }

            TimeSpan drive = TimeSpan.Zero;

            foreach (var ev in ordered)
            {
                if (ev.Status != DutyStatus.Driving)
                    continue;

                var end = ev.EndUtc ?? nowUtc;
                var dur = Overlap(ev.StartUtc, end, breakCutoff, nowUtc);

                if (dur > TimeSpan.Zero)
                    drive += dur;
            }

            return drive;
        }

        private static TimeSpan Overlap(DateTimeOffset a0, DateTimeOffset a1, DateTimeOffset b0, DateTimeOffset b1)
        {
            var start = a0 > b0 ? a0 : b0;
            var end = a1 < b1 ? a1 : b1;
            return end > start ? end - start : TimeSpan.Zero;
        }
    }
}