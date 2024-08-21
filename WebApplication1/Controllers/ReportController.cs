using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using WebApplication1.Data;
using WebApplication1.Models;
using WebApplication1.Services;

namespace WebApplication1.Controllers
{
    public class ReportsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<CryptoController> _logger;

        public ReportsController(ApplicationDbContext context, ILogger<CryptoController> logger)
        {
            _context = context;
            _logger = logger;
        }

        [HttpGet("api/reports/performance")]
        public async Task<IActionResult> GetPerformanceReport(DateTime startDate, DateTime endDate, int page =1, int pageSize =10)
        {
            var accountId = HttpContext.Session.GetInt32("AccountId");

            if (accountId == null)
            {
                return Unauthorized(); // No AccountId in session
            }

            var ordersQuery = _context.Orders.Where(o => o.AccountId == accountId);

            if (startDate != DateTime.MinValue && endDate != DateTime.MinValue)
            {
                ordersQuery = ordersQuery.Where(o => o.Timestamp >= startDate && o.Timestamp <= endDate);
            }
            _logger.LogInformation($"Filtering orders from {startDate} to {endDate} for account {accountId}");

            var allOrders = await ordersQuery.ToListAsync();

            var totalOrders = allOrders.Count;
            var wins = allOrders.Count(o => o.Roi > 0);
            var losses = allOrders.Count(o => o.Roi <= 0);
            var averageRoi = allOrders.Average(o => o.Roi) ?? 0;
            var totalProfitLoss = allOrders.Sum(o => o.ProfitAndLoss);
            var bestTrade = allOrders.OrderByDescending(o => o.Roi).FirstOrDefault()?.Symbol ?? "N/A";
            var worstTrade = allOrders.OrderBy(o => o.Roi).FirstOrDefault()?.Symbol ?? "N/A";

            // Apply pagination after calculating the overall statistics
            var paginatedOrders = allOrders
                .OrderBy(o => o.Timestamp)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            var totalPages = (int)Math.Ceiling(totalOrders / (double)pageSize);

            //&& o.Status == OrderStatus.Closed?

            var report = new PerformanceReport
            {
                TotalTrades = totalOrders,
                Wins = wins,
                Losses = losses,
                AverageRoi = averageRoi,
                TotalProfitLoss = totalProfitLoss,
                BestTrade = bestTrade,
                WorstTrade = worstTrade,
                TradeSymbols = paginatedOrders.Select(o => o.Symbol).ToList(),
                RoiValues = paginatedOrders.Select(o => o.Roi ?? 0).ToList(),
                ProfitLossValues = paginatedOrders.Select(o => o.ProfitAndLoss).ToList(),
                Orders = paginatedOrders,
                TotalPages = totalPages,
                CurrentPage = page
            };

            return Ok(report);
        }

        [HttpGet("api/reports/balance-history")]
        public async Task<IActionResult> GetBalanceHistory(DateTime startDate, DateTime endDate)
        {
            var accountId = HttpContext.Session.GetInt32("AccountId");

            if (accountId == null)
            {
                return Unauthorized(); // No AccountId in session
            }

            
            var balanceHistory = new List<object>();


            var lastTransactionBeforeStartDate = await _context.Transactions
                .Where(t => t.AccountId == accountId && t.TransactionDate < startDate)
                .OrderByDescending(t => t.TransactionDate)
                .FirstOrDefaultAsync();

            decimal initialBalance = lastTransactionBeforeStartDate?.BalanceAtTransaction ?? 0;

            // Get all transactions within the date range
            var transactions = await _context.Transactions
                .Where(t => t.AccountId == accountId && t.TransactionDate >= startDate && t.TransactionDate <= endDate)
                .OrderBy(t => t.TransactionDate)
                .ToListAsync();

            var allDates = Enumerable.Range(0, 1 + endDate.Subtract(startDate).Days)
                .Select(offset => startDate.AddDays(offset))
                .ToList();

            decimal runningBalance = initialBalance;
            int transactionIndex = 0;

            // Iterate over all dates and calculate the balance for each date
            foreach (var date in allDates)
            {
                // If there are transactions for this date, update the running balance
                while (transactionIndex < transactions.Count && transactions[transactionIndex].TransactionDate.Date == date.Date)
                {
                    runningBalance = transactions[transactionIndex].BalanceAtTransaction;
                    transactionIndex++;
                }

                // Add the date and balance to the history
                balanceHistory.Add(new
                {
                    date = date.ToString("yyyy-MM-dd"),
                    balance = runningBalance
                });
            }

            return Ok(balanceHistory);
        }

    }

    public class PerformanceReport
    {
        public int TotalTrades { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
        public decimal AverageRoi { get; set; }
        public decimal TotalProfitLoss { get; set; }
        public string BestTrade { get; set; }
        public string WorstTrade { get; set; }
        public List<string> TradeSymbols { get; set; }
        public List<decimal> RoiValues { get; set; }
        public List<decimal> ProfitLossValues { get; set; }
        public List<Order> Orders { get; set; } = new List<Order>();

        public int TotalPages { get; set; }
        public int CurrentPage { get; set; }   
    }

    public class BalanceHistoryEntry
    {
        public DateTime Date { get; set; }
        public decimal Balance { get; set; }
    }
}
