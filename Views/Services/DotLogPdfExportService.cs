using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;

using PdfContainer = QuestPDF.Infrastructure.IContainer;

namespace OverWatchELD.Services
{
    public static class DotLogPdfExportService
    {
        public static void Export(IEnumerable<DateTime> days, string fileName)
        {
            var selectedDays = days?.OrderBy(d => d).ToList()
                              ?? throw new ArgumentNullException(nameof(days));

            if (selectedDays.Count == 0)
                throw new InvalidOperationException("No days selected.");

            QuestPDF.Settings.License = LicenseType.Community;

            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.Letter);
                    page.Margin(20);

                    page.Header()
                        .Text("OverWatch ELD - DOT Compliance Log")
                        .SemiBold()
                        .FontSize(18);

                    page.Content()
                        .Column(col =>
                        {
                            col.Spacing(10);

                            col.Item().Text($"Generated: {DateTime.Now:g}");

                            foreach (var day in selectedDays)
                            {
                                col.Item().Element(c => BuildDayBlock(c, day));
                            }
                        });

                    page.Footer()
                        .AlignCenter()
                        .Text(x =>
                        {
                            x.Span("OverWatch ELD • DOT Log Export • ");
                            x.CurrentPageNumber();
                            x.Span(" / ");
                            x.TotalPages();
                        });
                });
            })
            .GeneratePdf(fileName);
        }

        // ---------------- DAY BLOCK ----------------

        private static void BuildDayBlock(PdfContainer container, DateTime day)
        {
            container
                .Border(1)
                .Padding(8)
                .Column(col =>
                {
                    col.Spacing(6);

                    col.Item()
                        .Background(Colors.Grey.Lighten3)
                        .Padding(4)
                        .Text($"DATE: {day:yyyy-MM-dd} ({day:dddd})")
                        .SemiBold();

                    col.Item().Element(c => BuildGrid(c, day));
                });
        }

        // ---------------- DOT 24H GRID ----------------

        private static void BuildGrid(PdfContainer container, DateTime day)
        {
            container.Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(60);
                    columns.RelativeColumn();
                });

                table.Header(header =>
                {
                    header.Cell().Element(CellStyle).Text("Time");
                    header.Cell().Element(CellStyle).Text("Status");
                });

                var blocks = BuildQuarterBlocks(day);

                foreach (var b in blocks)
                {
                    table.Cell().Element(CellStyle).Text(b.Time);
                    table.Cell().Element(CellStyle).Text(b.Status);
                }
            });
        }

        private static PdfContainer CellStyle(PdfContainer c)
        {
            return c.BorderBottom(1).Padding(2);
        }

        // ---------------- 15 MIN BLOCKS ----------------

        private static List<DotBlock> BuildQuarterBlocks(DateTime day)
        {
            var list = new List<DotBlock>();

            var start = day.Date;

            for (int i = 0; i < 96; i++)
            {
                var time = start.AddMinutes(i * 15);

                list.Add(new DotBlock
                {
                    Time = time.ToString("HH:mm"),
                    Status = GetFakeStatus(i)
                });
            }

            return list;
        }

        // ---------------- STATUS (TEMP PLACEHOLDER) ----------------

        private static string GetFakeStatus(int i)
        {
            if (i < 20) return "Off Duty";
            if (i < 40) return "Sleeper";
            if (i < 70) return "Driving";
            return "On Duty";
        }

        private sealed class DotBlock
        {
            public string Time { get; set; } = "";
            public string Status { get; set; } = "";
        }
    }
}