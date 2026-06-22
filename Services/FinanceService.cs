using OverWatchELD.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OverWatchELD.Services
{
    public sealed class FinanceService
    {
        private readonly string _filePath;

        public FinanceService()
        {
            _filePath = Path.Combine(AppContext.BaseDirectory, "finance_ledger.json");
        }

        public List<FinanceEntry> LoadAll()
        {
            try
            {
                if (!File.Exists(_filePath))
                    return new List<FinanceEntry>();

                var json = File.ReadAllText(_filePath);
                var items = JsonSerializer.Deserialize<List<FinanceEntry>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return items ?? new List<FinanceEntry>();
            }
            catch
            {
                return new List<FinanceEntry>();
            }
        }

        public void SaveAll(List<FinanceEntry> entries)
        {
            var json = JsonSerializer.Serialize(entries.OrderByDescending(x => x.DateUtc).ToList(),
                new JsonSerializerOptions { WriteIndented = true });

            File.WriteAllText(_filePath, json);
        }

        public void Add(FinanceEntry entry)
        {
            var items = LoadAll();
            items.Add(entry);
            SaveAll(items);
        }

        public void Remove(string id)
        {
            var items = LoadAll();
            items.RemoveAll(x => x.Id == id);
            SaveAll(items);
        }

        public decimal GetTotalIncome() =>
            LoadAll()
                .Where(x => string.Equals(x.EntryType, "Income", StringComparison.OrdinalIgnoreCase))
                .Sum(x => x.Amount);

        public decimal GetTotalExpenses() =>
            LoadAll()
                .Where(x => string.Equals(x.EntryType, "Expense", StringComparison.OrdinalIgnoreCase))
                .Sum(x => x.Amount);

        public decimal GetBalance() => GetTotalIncome() - GetTotalExpenses();

        public decimal GetCategoryTotal(string category) =>
            LoadAll()
                .Where(x => string.Equals(x.Category, category, StringComparison.OrdinalIgnoreCase))
                .Sum(x => x.Amount);
    }
}