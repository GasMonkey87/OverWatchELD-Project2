using OverWatchELD.Models.Economy;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OverWatchELD.Services.Economy
{
    public static class EconomyStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private static string EconomyFolder
        {
            get
            {
                var folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "OverWatchELD",
                    "Economy");

                Directory.CreateDirectory(folder);
                return folder;
            }
        }

        private static string AccountPath => Path.Combine(EconomyFolder, "company_account.json");
        private static string TransactionsPath => Path.Combine(EconomyFolder, "economy_transactions.json");

        public static CompanyAccount LoadAccount()
        {
            try
            {
                if (!File.Exists(AccountPath))
                {
                    var seeded = new CompanyAccount();
                    SaveAccount(seeded);
                    return seeded;
                }

                var json = File.ReadAllText(AccountPath);
                var account = JsonSerializer.Deserialize<CompanyAccount>(json, JsonOptions) ?? new CompanyAccount();
                return account;
            }
            catch
            {
                return new CompanyAccount();
            }
        }

        public static void SaveAccount(CompanyAccount account)
        {
            try
            {
                account.UpdatedUtc = DateTime.UtcNow;
                File.WriteAllText(AccountPath, JsonSerializer.Serialize(account, JsonOptions));
            }
            catch
            {
            }
        }

        public static List<EconomyTransaction> LoadTransactions()
        {
            try
            {
                if (!File.Exists(TransactionsPath))
                    return new List<EconomyTransaction>();

                var json = File.ReadAllText(TransactionsPath);
                return JsonSerializer.Deserialize<List<EconomyTransaction>>(json, JsonOptions) ?? new List<EconomyTransaction>();
            }
            catch
            {
                return new List<EconomyTransaction>();
            }
        }

        public static void SaveTransactions(List<EconomyTransaction> rows)
        {
            try
            {
                rows = rows
                    .Where(x => x != null)
                    .OrderByDescending(x => x.CreatedUtc)
                    .Take(10000)
                    .ToList();

                File.WriteAllText(TransactionsPath, JsonSerializer.Serialize(rows, JsonOptions));
            }
            catch
            {
            }
        }

        public static EconomyTransaction AddTransaction(EconomyTransaction tx)
        {
            var account = LoadAccount();
            var rows = LoadTransactions();

            account.Balance += tx.Amount;

            if (tx.Amount >= 0)
                account.LifetimeRevenue += tx.Amount;
            else
                account.LifetimeExpenses += Math.Abs(tx.Amount);

            tx.BalanceAfter = account.Balance;
            tx.CreatedUtc = tx.CreatedUtc == default ? DateTime.UtcNow : tx.CreatedUtc;

            rows.Insert(0, tx);

            SaveAccount(account);
            SaveTransactions(rows);

            return tx;
        }

        public static bool HasTransactionForLoad(string? loadNumber, string type)
        {
            if (string.IsNullOrWhiteSpace(loadNumber))
                return false;

            return LoadTransactions().Any(x =>
                string.Equals(x.LoadNumber, loadNumber.Trim(), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.Type, type, StringComparison.OrdinalIgnoreCase));
        }

        public static EconomySummary BuildSummary()
        {
            var account = LoadAccount();
            var rows = LoadTransactions();

            var now = DateTime.UtcNow;
            var today = now.Date;
            var weekStart = today.AddDays(-(int)today.DayOfWeek);
            var monthStart = new DateTime(today.Year, today.Month, 1);

            decimal RevenueSince(DateTime cutoff) =>
                rows.Where(x => x.CreatedUtc >= cutoff && x.Amount > 0).Sum(x => x.Amount);

            decimal ExpensesSince(DateTime cutoff) =>
                rows.Where(x => x.CreatedUtc >= cutoff && x.Amount < 0).Sum(x => Math.Abs(x.Amount));

            return new EconomySummary
            {
                Balance = account.Balance,
                LifetimeRevenue = account.LifetimeRevenue,
                LifetimeExpenses = account.LifetimeExpenses,
                LifetimeProfit = account.LifetimeProfit,

                TodayRevenue = RevenueSince(today),
                TodayExpenses = ExpensesSince(today),

                WeekRevenue = RevenueSince(weekStart),
                WeekExpenses = ExpensesSince(weekStart),

                MonthRevenue = RevenueSince(monthStart),
                MonthExpenses = ExpensesSince(monthStart),

                DeliveredLoadsToday = rows.Count(x =>
                    x.CreatedUtc >= today &&
                    string.Equals(x.Type, "LoadPayout", StringComparison.OrdinalIgnoreCase)),

                TransactionsToday = rows.Count(x => x.CreatedUtc >= today),
                TotalTransactions = rows.Count,
                GeneratedUtc = now
            };
        }
    }
}
