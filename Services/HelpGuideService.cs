using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace OverWatchELD.Services
{
    public static class HelpGuideService
    {
        private const string GuideFileName = "OverWatch_ELD_Help_Center_Guide.docx";

        public static string GuidePath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Docs", GuideFileName);

        public static void OpenGuide(Window? owner = null)
        {
            try
            {
                if (!File.Exists(GuidePath))
                {
                    MessageBox.Show(
                        owner,
                        "The OverWatch ELD guide could not be found.\n\nExpected location:\n" + GuidePath,
                        "OverWatch ELD Guide",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = GuidePath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    owner,
                    "Unable to open the OverWatch ELD guide.\n\n" + ex.Message,
                    "OverWatch ELD Guide",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
