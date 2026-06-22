using System;
using System.Windows;

namespace OverWatchELD.Models
{
    /// <summary>
    /// Driver daily log certification (local day).
    /// Used to track whether a day's logs have been reviewed and signed.
    /// </summary>
    public class DailyLogCertification
    {
        public long Id { get; set; }

        /// <summary>
        /// Local log day in yyyy-MM-dd format (driver's local time).
        /// </summary>
        public string LogDateLocal { get; set; } = "";

        private bool ConfirmAttestation(string title)
        {
            var msg =
                "By signing, you certify these logs are true and correct.\n\n" +
                "This action will mark the selected day(s) as certified.";
            var result = MessageBox.Show(msg, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            return result == MessageBoxResult.OK;
        }

        public DateTimeOffset SignedAtUtc { get; set; }
        public string? DriverName { get; set; }
        public string? Signature { get; set; }

        /// <summary>
        /// If true, driver certified the logs are complete/true.
        /// </summary>
        public bool Certified { get; set; } = true;

        /// <summary>
        /// Optional certification text shown to driver.
        /// </summary>
        public string? CertificationText { get; set; }
    }
}
