using System;
using System.Globalization;
using System.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace OverWatchELD.Services
{
    public sealed class BolPdfService
    {
        public static BolPdfService Shared { get; } = new();

        private string PdfDir =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OverWatchELD",
                "BOL",
                "pdf");

        private BolPdfService()
        {
            QuestPDF.Settings.License = LicenseType.Community;
        }

        public string CreateOrUpdatePdf(BolAutoCreateService.BolRecord record)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.LoadNumber))
                throw new InvalidOperationException("BOL record missing load number.");

            Directory.CreateDirectory(PdfDir);

            var path = Path.Combine(PdfDir, $"{SafeFile(record.LoadNumber)}.pdf");

            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Margin(30);
                    page.Size(PageSizes.Letter);
                    page.DefaultTextStyle(x => x.FontSize(11));

                    page.Header().Column(col =>
                    {
                        col.Item().Text("Bill of Lading").FontSize(24).Bold();
                        col.Item().Text($"Load Number: {record.LoadNumber}").SemiBold();
                        col.Item().Text("OverWatch ELD");
                    });

                    page.Content().PaddingVertical(12).Column(col =>
                    {
                        col.Spacing(8);

                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Element(CellStyle).Column(c =>
                            {
                                c.Item().Text("Driver").SemiBold();
                                c.Item().Text(record.Driver ?? "");
                            });
                            row.RelativeItem().Element(CellStyle).Column(c =>
                            {
                                c.Item().Text("Truck").SemiBold();
                                c.Item().Text(record.Truck ?? "");
                            });
                        });

                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Element(CellStyle).Column(c =>
                            {
                                c.Item().Text("Cargo").SemiBold();
                                c.Item().Text(record.Cargo ?? "");
                            });
                            row.RelativeItem().Element(CellStyle).Column(c =>
                            {
                                c.Item().Text("Weight").SemiBold();
                                c.Item().Text(record.WeightLbs > 0
                                    ? record.WeightLbs.ToString("N0", CultureInfo.InvariantCulture) + " lbs"
                                    : "");
                            });
                        });

                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Element(CellStyle).Column(c =>
                            {
                                c.Item().Text("Origin").SemiBold();
                                c.Item().Text(record.StartLocation ?? "");
                            });
                            row.RelativeItem().Element(CellStyle).Column(c =>
                            {
                                c.Item().Text("Destination").SemiBold();
                                c.Item().Text(record.EndLocation ?? "");
                            });
                        });

                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Element(CellStyle).Column(c =>
                            {
                                c.Item().Text("Status").SemiBold();
                                c.Item().Text(record.Status ?? "");
                            });
                            row.RelativeItem().Element(CellStyle).Column(c =>
                            {
                                c.Item().Text("Created").SemiBold();
                                c.Item().Text(record.CreatedUtc == default
                                    ? ""
                                    : record.CreatedUtc.LocalDateTime.ToString("yyyy-MM-dd hh:mm tt", CultureInfo.InvariantCulture));
                            });
                        });

                        col.Item().Element(CellStyle).Column(c =>
                        {
                            c.Item().Text("Notes").SemiBold();
                            c.Item().Text(record.Notes ?? "");
                        });
                    });

                    page.Footer().AlignCenter().Text(txt =>
                    {
                        txt.Span("Generated by OverWatch ELD ");
                        txt.Span(DateTime.Now.ToString("yyyy-MM-dd hh:mm tt", CultureInfo.InvariantCulture));
                    });
                });
            }).GeneratePdf(path);

            return path;
        }

        private static QuestPDF.Infrastructure.IContainer CellStyle(QuestPDF.Infrastructure.IContainer container)
        {
            return container
                .Border(1)
                .BorderColor(Colors.Grey.Lighten2)
                .Padding(10);
        }

        private static string SafeFile(string input)
        {
            var bad = Path.GetInvalidFileNameChars();
            var chars = input.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                for (var j = 0; j < bad.Length; j++)
                {
                    if (chars[i] == bad[j])
                    {
                        chars[i] = '_';
                        break;
                    }
                }
            }

            var safe = new string(chars).Trim();
            return string.IsNullOrWhiteSpace(safe) ? "bol" : safe;
        }
    }
}
