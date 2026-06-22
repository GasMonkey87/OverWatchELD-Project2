using System;
using System.Collections.Generic;
using System.Linq;
using OverWatchELD.Models;

namespace OverWatchELD.Services
{
    public static class ComplianceService
    {
        // US property-carrying defaults
        private static readonly TimeSpan MaxDrive = TimeSpan.FromHours(11);
        private static readonly TimeSpan MaxShift = TimeSpan.FromHours(14);

        // Break: 30 min non-driving required before exceeding 8 hours of driving time
        private static readonly TimeSpan BreakRuleDrive = TimeSpan.FromHours(8);
        private static readonly TimeSpan BreakQualMin = TimeSpan.FromMinutes(30);

        private static readonly TimeSpan CycleLimit = TimeSpan.FromHours(70);
        private static readonly TimeSpan CycleWindow = TimeSpan.FromDays(8);

        private static readonly TimeSpan ShiftResetMin = TimeSpan.FromHours(10);
        private static readonly TimeSpan CycleResetMin = TimeSpan.FromHours(34);

        public static HosSnapshot Compute(DateTimeOffset nowUtc, List<DutyEvent> events, DutyStatus currentStatus, DateTimeOffset currentStatusStartUtc)
        {
            nowUtc = nowUtc.ToUniversalTime();

            var list = Normalize(events, currentStatus, currentStatusStartUtc, nowUtc);

            // -----------------------------
            // Detect true reset anchors
            // -----------------------------
            var tenHourResetEnd = FindLastConsecutiveRestEnd(list, nowUtc, ShiftResetMin, IsRestForShiftReset);
            var thirtyFourResetEnd = FindLastConsecutiveRestEnd(list, nowUtc, CycleResetMin, IsRestForCycleReset);

            // Split sleeper (8/2 OR 7/3)
            var split = FindLatestSplitPair(list, nowUtc);

            // Choose anchor for shift/driving clocks:
            // Prefer a true 10-hour reset end if it exists and is after the split anchor.
            // Else if split pair exists, use split anchor.
            // Else fallback to last event start.
            DateTimeOffset anchorUtc;
            if (tenHourResetEnd != null && (split == null || tenHourResetEnd.Value >= split.AnchorEndUtc))
                anchorUtc = tenHourResetEnd.Value;
            else if (split != null)
                anchorUtc = split.AnchorEndUtc;
            else
                anchorUtc = list.LastOrDefault()?.StartUtc ?? (nowUtc - TimeSpan.FromDays(1));

            // -----------------------------
            // Cycle clock (70/8) w/ 34 reset
            // -----------------------------
            // If a 34h reset exists, cycle used is from that reset end.
            // Otherwise, use rolling 8-day window.
            DateTimeOffset cycleAnchorUtc;
            if (thirtyFourResetEnd != null)
            {
                cycleAnchorUtc = thirtyFourResetEnd.Value;
            }
            else
            {
                cycleAnchorUtc = nowUtc - CycleWindow;
            }

            // But never sum earlier than rolling window start (keeps behavior stable)
            var rollingStart = nowUtc - CycleWindow;
            if (cycleAnchorUtc < rollingStart) cycleAnchorUtc = rollingStart;

            // Cycle includes OnDuty + Driving + YardMove (PC does NOT count)
            var onDutyInCycle = SumDurations(list, cycleAnchorUtc, nowUtc, IsOnDutyForCycle);
            var cycleRemaining = CycleLimit - onDutyInCycle;

            // -----------------------------
            // Drive (11-hour) since anchor
            // -----------------------------
            var drivingSinceAnchor = SumDurations(list, anchorUtc, nowUtc, s => s == DutyStatus.Driving);
            var driveRemaining = MaxDrive - drivingSinceAnchor;

            // -----------------------------
            // Shift (14-hour) since anchor
            // Split sleeper: exclude qualifying rest period(s) from 14-hour calculation
            // In Motive-style behavior, once the SECOND qualifying period completes, it is excluded from the 14.
            // -----------------------------
            var elapsedSinceAnchor = nowUtc - anchorUtc;

            var excludedForSplit = TimeSpan.Zero;
            if (split != null && split.AnchorEndUtc == anchorUtc)
            {
                excludedForSplit = split.ExcludedFrom14;
            }

            var effectiveShiftElapsed = elapsedSinceAnchor - excludedForSplit;
            if (effectiveShiftElapsed < TimeSpan.Zero) effectiveShiftElapsed = TimeSpan.Zero;

            var shiftRemaining = MaxShift - effectiveShiftElapsed;

            // -----------------------------
            // 30-minute break rule
            // Required before exceeding 8 hours of DRIVING time since last 30+ min NON-DRIVING break.
            // Qualifying break = 30+ minutes continuous non-driving.
            // (Off/SB/PC/OnDuty not driving/YardMove all qualify because they are non-driving)
            // -----------------------------
            var lastQualBreakEnd = FindLastQualifyingBreakEnd(list, nowUtc) ?? anchorUtc;
            var drivingSinceBreak = SumDurations(list, lastQualBreakEnd, nowUtc, s => s == DutyStatus.Driving);
            var breakRemaining = BreakRuleDrive - drivingSinceBreak;
            var isBreakDue = drivingSinceBreak > TimeSpan.Zero && breakRemaining <= TimeSpan.Zero;

            // Violations
            var driveViolation = driveRemaining < TimeSpan.Zero;
            var shiftViolation = shiftRemaining < TimeSpan.Zero;
            var cycleViolation = cycleRemaining < TimeSpan.Zero;
            var breakViolation = isBreakDue;

            var shouldPulse =
                driveViolation || shiftViolation || cycleViolation || isBreakDue ||
                driveRemaining <= TimeSpan.FromMinutes(30) ||
                shiftRemaining <= TimeSpan.FromMinutes(30) ||
                breakRemaining <= TimeSpan.FromMinutes(30) ||
                cycleRemaining <= TimeSpan.FromMinutes(60);

            var notes = BuildNotes(split, driveViolation, shiftViolation, breakViolation, cycleViolation, tenHourResetEnd, thirtyFourResetEnd);

            // Used % (0..1)
            var driveUsedPct = Clamp01(drivingSinceAnchor.TotalSeconds / MaxDrive.TotalSeconds);
            var shiftUsedPct = Clamp01(effectiveShiftElapsed.TotalSeconds / MaxShift.TotalSeconds);
            var breakUsedPct = Clamp01(drivingSinceBreak.TotalSeconds / BreakRuleDrive.TotalSeconds);
            var cycleUsedPct = Clamp01(onDutyInCycle.TotalSeconds / CycleLimit.TotalSeconds);

            return new HosSnapshot
            {
                ShiftRemaining = shiftRemaining,
                DriveRemaining = driveRemaining,
                CycleRemaining = cycleRemaining,
                BreakRemaining = breakRemaining,

                BreakRequired = isBreakDue,
                IsBreakDue = isBreakDue,

                DriveViolation = driveViolation,
                ShiftViolation = shiftViolation,
                BreakViolation = breakViolation,
                CycleViolation = cycleViolation,

                ShouldPulse = shouldPulse,
                Notes = notes,

                DriveUsedPct = driveUsedPct,
                ShiftUsedPct = shiftUsedPct,
                BreakUsedPct = breakUsedPct,
                CycleUsedPct = cycleUsedPct
            };
        }

        // =========================
        // Predicates (FMCSA meanings)
        // =========================
        private static bool IsOnDutyForCycle(DutyStatus s)
            => s == DutyStatus.OnDuty || s == DutyStatus.Driving || s == DutyStatus.YardMove;

        // Rest that can qualify for 10-hour reset (Motive treats PC as Off-duty)
        private static bool IsRestForShiftReset(DutyStatus s)
            => s == DutyStatus.OffDuty || s == DutyStatus.Sleeper || s == DutyStatus.PersonalConveyance;

        // Rest that can qualify for 34-hour reset (Motive treats PC as Off-duty)
        private static bool IsRestForCycleReset(DutyStatus s)
            => s == DutyStatus.OffDuty || s == DutyStatus.Sleeper || s == DutyStatus.PersonalConveyance;

        private static bool IsRestForSplit(DutyStatus s)
            => s == DutyStatus.OffDuty || s == DutyStatus.Sleeper || s == DutyStatus.PersonalConveyance;

        private static bool IsNonDriving(DutyStatus s)
            => s != DutyStatus.Driving;

        // =========================
        // Timeline normalization
        // =========================
        private static List<DutyEvent> Normalize(IEnumerable<DutyEvent> events, DutyStatus currentStatus, DateTimeOffset currentStatusStartUtc, DateTimeOffset nowUtc)
        {
            var list = (events ?? Enumerable.Empty<DutyEvent>())
                .Where(e => e != null)
                .Select(e => new DutyEvent
                {
                    Id = e.Id,
                    StartUtc = e.StartUtc.ToUniversalTime(),
                    EndUtc = e.EndUtc?.ToUniversalTime(),
                    Status = e.Status,
                    LocationText = e.LocationText,
                    Notes = e.Notes,
                    Source = e.Source,
                    IsEdited = e.IsEdited,
                    EditedAtUtc = e.EditedAtUtc?.ToUniversalTime(),
                    EditReason = e.EditReason,
                    Lat = e.Lat,
                    Lon = e.Lon
                })
                .OrderBy(e => e.StartUtc)
                .ToList();

            foreach (var e in list)
            {
                if (e.StartUtc > nowUtc) e.StartUtc = nowUtc;
                if (e.EndUtc != null && e.EndUtc > nowUtc) e.EndUtc = nowUtc;
            }

            // Ensure a live "open" event exists for current status
            var open = list.LastOrDefault(e => e.EndUtc == null);
            if (open == null || open.Status != currentStatus)
            {
                list.Add(new DutyEvent
                {
                    StartUtc = currentStatusStartUtc.ToUniversalTime(),
                    EndUtc = null,
                    Status = currentStatus,
                    Source = "live"
                });
            }

            list = list.OrderBy(e => e.StartUtc).ToList();

            // Repair overlaps + drop invalid + merge same-status adjacent
            for (int i = 0; i < list.Count; i++)
            {
                var cur = list[i];
                var curEnd = cur.EndUtc ?? nowUtc;

                if (i + 1 < list.Count)
                {
                    var next = list[i + 1];
                    if (next.StartUtc < curEnd)
                    {
                        curEnd = next.StartUtc;
                        cur.EndUtc = curEnd;
                    }
                }

                if (curEnd <= cur.StartUtc)
                {
                    list.RemoveAt(i);
                    i--;
                    continue;
                }

                if (i > 0)
                {
                    var prev = list[i - 1];
                    var prevEnd = prev.EndUtc ?? nowUtc;

                    if (prev.Status == cur.Status && cur.StartUtc <= prevEnd)
                    {
                        var mergedEnd = (cur.EndUtc ?? nowUtc) > prevEnd ? (cur.EndUtc ?? nowUtc) : prevEnd;
                        prev.EndUtc = (mergedEnd == nowUtc && cur.EndUtc == null) ? null : mergedEnd;

                        list.RemoveAt(i);
                        i--;
                        continue;
                    }
                }
            }

            return list;
        }

        private static TimeSpan SumDurations(List<DutyEvent> list, DateTimeOffset fromUtc, DateTimeOffset toUtc, Func<DutyStatus, bool> include)
        {
            if (toUtc <= fromUtc) return TimeSpan.Zero;

            TimeSpan total = TimeSpan.Zero;

            foreach (var e in list)
            {
                var end = e.EndUtc ?? toUtc;
                if (end <= fromUtc) continue;
                if (e.StartUtc >= toUtc) break;

                var segStart = e.StartUtc < fromUtc ? fromUtc : e.StartUtc;
                var segEnd = end > toUtc ? toUtc : end;
                if (segEnd <= segStart) continue;

                if (include(e.Status))
                    total += (segEnd - segStart);
            }

            return total;
        }

        // =========================
        // Consecutive rest detection (10h + 34h)
        // =========================
        private static DateTimeOffset? FindLastConsecutiveRestEnd(
            List<DutyEvent> list,
            DateTimeOffset nowUtc,
            TimeSpan minDuration,
            Func<DutyStatus, bool> isRest)
        {
            // Find end of last continuous REST block >= minDuration.
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var e = list[i];
                var end = e.EndUtc ?? nowUtc;

                if (!isRest(e.Status))
                    continue;

                var blockStart = e.StartUtc;
                var blockEnd = end;

                int j = i - 1;
                while (j >= 0)
                {
                    var prev = list[j];
                    var prevEnd = prev.EndUtc ?? nowUtc;

                    if (prevEnd < blockStart) break; // gap ends block

                    if (isRest(prev.Status))
                    {
                        if (prev.StartUtc < blockStart) blockStart = prev.StartUtc;
                        j--;
                        continue;
                    }

                    break;
                }

                if ((blockEnd - blockStart) >= minDuration)
                    return blockEnd;
            }

            return null;
        }

        // =========================
        // 30-min break detection
        // =========================
        private static DateTimeOffset? FindLastQualifyingBreakEnd(List<DutyEvent> list, DateTimeOffset nowUtc)
        {
            // Qualifying break: continuous NON-DRIVING block >= 30 minutes.
            // We keep the most recent qualifying block end.
            DateTimeOffset? blockStart = null;
            DateTimeOffset? blockEnd = null;
            DateTimeOffset? lastQualifiedEnd = null;

            foreach (var e in list.OrderBy(x => x.StartUtc))
            {
                var end = e.EndUtc ?? nowUtc;

                if (!IsNonDriving(e.Status))
                {
                    // driving ends any non-driving block; evaluate it
                    if (blockStart != null && blockEnd != null && (blockEnd.Value - blockStart.Value) >= BreakQualMin)
                        lastQualifiedEnd = blockEnd.Value;

                    blockStart = null;
                    blockEnd = null;
                    continue;
                }

                // Non-driving segment extends/starts block
                if (blockStart == null)
                {
                    blockStart = e.StartUtc;
                    blockEnd = end;
                }
                else
                {
                    if (e.StartUtc <= blockEnd)
                        blockEnd = end > blockEnd ? end : blockEnd;
                    else
                    {
                        // gap: evaluate previous block
                        if (blockEnd != null && (blockEnd.Value - blockStart.Value) >= BreakQualMin)
                            lastQualifiedEnd = blockEnd.Value;

                        blockStart = e.StartUtc;
                        blockEnd = end;
                    }
                }
            }

            // final block
            if (blockStart != null && blockEnd != null && (blockEnd.Value - blockStart.Value) >= BreakQualMin)
                lastQualifiedEnd = blockEnd.Value;

            return lastQualifiedEnd;
        }

        // =========================
        // SPLIT SLEEPER (8/2 and 7/3)
        // =========================
        private sealed class RestBlock
        {
            public DateTimeOffset StartUtc;
            public DateTimeOffset EndUtc;
            public TimeSpan Duration => EndUtc - StartUtc;
            public bool HasSleeper; // any SB inside
        }

        private sealed class SplitResult
        {
            public DateTimeOffset AnchorEndUtc;  // End of FIRST qualifying period (chronologically)
            public TimeSpan ExcludedFrom14;      // Duration of SECOND qualifying period excluded from 14
            public string Rule = "";
        }

        private static SplitResult? FindLatestSplitPair(List<DutyEvent> list, DateTimeOffset nowUtc)
        {
            var blocks = BuildRestBlocks(list, nowUtc);
            if (blocks.Count < 2) return null;

            // Pick the most recent "second" block and find an earlier "first" block forming a valid pair.
            for (int second = blocks.Count - 1; second >= 1; second--)
            {
                var b2 = blocks[second];

                for (int first = second - 1; first >= 0; first--)
                {
                    var b1 = blocks[first];
                    if (b1.EndUtc > b2.StartUtc) continue;

                    // FMCSA property-carrying (2020 rule): 8/2 and 7/3
                    var r82 = TryMakeSplit(b1, b2, sleeperMin: TimeSpan.FromHours(8), otherMin: TimeSpan.FromHours(2), ruleName: "8/2");
                    if (r82 != null) return r82;

                    var r73 = TryMakeSplit(b1, b2, sleeperMin: TimeSpan.FromHours(7), otherMin: TimeSpan.FromHours(3), ruleName: "7/3");
                    if (r73 != null) return r73;
                }
            }

            return null;
        }

        private static SplitResult? TryMakeSplit(RestBlock first, RestBlock second, TimeSpan sleeperMin, TimeSpan otherMin, string ruleName)
        {
            var d1 = first.Duration;
            var d2 = second.Duration;

            if ((d1 + d2) < TimeSpan.FromHours(10))
                return null;

            // One period must be sleeper-qualified; the other must be rest-qualified
            bool firstSleeperOk = first.HasSleeper && d1 >= sleeperMin;
            bool secondSleeperOk = second.HasSleeper && d2 >= sleeperMin;

            bool firstOtherOk = d1 >= otherMin;   // off/sb/pc is rest
            bool secondOtherOk = d2 >= otherMin;

            if (firstSleeperOk && secondOtherOk)
            {
                return new SplitResult
                {
                    AnchorEndUtc = first.EndUtc,
                    ExcludedFrom14 = d2,
                    Rule = ruleName
                };
            }

            // If the "sleeper" is second, we still anchor at the END of the first block chronologically.
            // We exclude the second block from 14 once completed.
            if (secondSleeperOk && firstOtherOk)
            {
                return new SplitResult
                {
                    AnchorEndUtc = first.EndUtc,
                    ExcludedFrom14 = d2,
                    Rule = ruleName
                };
            }

            return null;
        }

        private static List<RestBlock> BuildRestBlocks(List<DutyEvent> list, DateTimeOffset nowUtc)
        {
            var blocks = new List<RestBlock>();
            RestBlock? cur = null;

            foreach (var e in list.OrderBy(x => x.StartUtc))
            {
                var end = e.EndUtc ?? nowUtc;
                if (end <= e.StartUtc) continue;

                if (!IsRestForSplit(e.Status))
                {
                    if (cur != null)
                    {
                        blocks.Add(cur);
                        cur = null;
                    }
                    continue;
                }

                if (cur == null)
                {
                    cur = new RestBlock { StartUtc = e.StartUtc, EndUtc = end };
                }
                else
                {
                    if (e.StartUtc <= cur.EndUtc)
                        cur.EndUtc = end > cur.EndUtc ? end : cur.EndUtc;
                    else
                    {
                        blocks.Add(cur);
                        cur = new RestBlock { StartUtc = e.StartUtc, EndUtc = end };
                    }
                }

                if (e.Status == DutyStatus.Sleeper) cur.HasSleeper = true;
            }

            if (cur != null) blocks.Add(cur);

            return blocks;
        }

        // =========================
        // Notes + misc
        // =========================
        private static string BuildNotes(
            SplitResult? split,
            bool driveV,
            bool shiftV,
            bool breakV,
            bool cycleV,
            DateTimeOffset? tenResetEnd,
            DateTimeOffset? cycleResetEnd)
        {
            var parts = new List<string>();

            if (split != null) parts.Add($"Split sleeper active ({split.Rule}).");
            if (tenResetEnd != null) parts.Add($"Last 10-hour reset ended {tenResetEnd.Value.LocalDateTime:MMM d h:mm tt}.");
            if (cycleResetEnd != null) parts.Add($"Last 34-hour reset ended {cycleResetEnd.Value.LocalDateTime:MMM d h:mm tt}.");

            if (driveV) parts.Add("11-hour driving limit exceeded.");
            if (shiftV) parts.Add("14-hour window exceeded.");
            if (breakV) parts.Add("30-minute break required (8 hours driving time since last qualifying break).");
            if (cycleV) parts.Add("70-hour/8-day cycle limit exceeded.");

            return string.Join(" ", parts);
        }

        private static double Clamp01(double v)
        {
            if (v < 0) return 0;
            if (v > 1) return 1;
            return v;
        }
    }
}
