using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using WebApplication1.Data;
using WebApplication1.Models;
using WebApplication1.Services.Interfaces;

namespace WebApplication1.Services
{
    public class BinanceService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<BinanceService> _logger;
        private readonly ApplicationDbContext _context;
        private readonly TradingStrategyContext _strategyContext;

        // Dictionary to store the last known signals
        private readonly Dictionary<string, TradingSignal> _lastKnownSignals = new Dictionary<string, TradingSignal>();

        public BinanceService(ILogger<BinanceService> logger, ApplicationDbContext context)
        {
            _logger = logger;
            _context = context;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            _strategyContext = new TradingStrategyContext();
        }

        public void SetStrategy(ITradingStrategy strategy)
        {
            _strategyContext.SetStrategy(strategy);
        }

        public async Task SaveOrderAsync(Order order)
        {
            _context.Orders.Add(order);
            await _context.SaveChangesAsync();
        }

        public async Task<List<Order>> GetOrdersAsync(int accountId, int page, int pageSize)
        {
            return await _context.Orders
                .Where(o => o.AccountId == accountId && o.Status == OrderStatus.Active)
                .OrderBy(o => o.Timestamp)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        public async Task UpdateOrderAsync(Order order)
        {
            _context.Orders.Update(order);
            await _context.SaveChangesAsync();
        }

        //Update ROI
        public async Task UpdateOrdersRoiAsync(string symbol, decimal newPrice)
        {
            var orders = _context.Orders
         .Where(o => o.Symbol == symbol && o.Status == OrderStatus.Active)
         .ToList();

            foreach (var order in orders)
            {
                try
                {
                    if (order.EntryPrice != 0)
                    {
                        if (order.Action == "Buy")
                        {
                            order.Roi = ((newPrice - order.EntryPrice) / order.EntryPrice) * 100;
                        }
                        else if (order.Action == "Sell")
                        {
                            order.Roi = ((order.EntryPrice - newPrice) / order.EntryPrice) * 100;
                        }

                        order.ProfitAndLoss = order.Roi.HasValue ? (order.OrderPrice * order.Roi.Value) / 100 : 0;
                        // Check if the order should be closed
                        if (order.Roi <= -100 || order.ProfitAndLoss <= -order.OrderPrice)
                        {
                            order.Status = OrderStatus.Closed;
                            order.ClosedTime = DateTime.Now;
                        }
                    }
                    else
                    {
                        order.Roi = 0;
                    }

                    _context.Orders.Update(order);
                    _logger.LogInformation($"Calculated ROI for {order.Symbol}: {order.Roi}%");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error updating ROI for Order ID: {order.Id}, Symbol: {order.Symbol}");
                    throw; // Re-throw the exception to allow for higher-level handling if needed
                }
            }
            await _context.SaveChangesAsync();
        }

        public async Task HandleNewPriceAsync(string symbol, decimal newPrice)
        {

            _logger.LogInformation($"HandleNewPriceAsync called for symbol: {symbol}, newPrice: {newPrice}");

            await Task.Run(() => UpdateOrdersRoiAsync(symbol, newPrice));
        }

        public bool TryGetLastKnownPrice(string symbol, out decimal currentPrice)
        {
            if (_lastKnownSignals.TryGetValue(symbol, out var signal))
            {
                currentPrice = signal.Price;
                return true;
            }

            currentPrice = 0;
            return false;
        }

        
        public async Task<IEnumerable<string>> GetAllSpotSymbolsAsync()
        {
            var url = "https://api.binance.com/api/v3/exchangeInfo";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync();
            var data = JObject.Parse(responseBody);
            var symbols = data["symbols"].Select(s => (string)s["symbol"]).ToList();

            return symbols;
        }

        public async Task<IEnumerable<string>> GetAllFuturesSymbolsAsync()
        {
            var url = "https://fapi.binance.com/fapi/v1/exchangeInfo";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync();
            var data = JObject.Parse(responseBody);
            var symbols = data["symbols"].Select(s => (string)s["symbol"]).ToList();

            return symbols;
        }


        public async Task FetchAndGenerateSignalsAsync(string symbol, int limit)
        {
            var signals = await _strategyContext.GenerateSignalsAsync(symbol, limit);
            foreach (var signal in signals)
            {
                HandleNewSignal(signal);
            }
        }

        public async Task<IEnumerable<TradingSignal>> GenerateSignalsForAllPairs(int limit = 500)
        {
            var intervals = new[] { "1m", "15m", "1h", "4h" };
            var symbols = new List<string> { "BTCUSDT", "ETHUSDT", "BNBUSDT", "SOLUSDT" };

            var tasks = symbols.Select(async symbol =>
            {
                var symbolSignals = new List<TradingSignal>();

                foreach (var interval in intervals)
                {
                    try
                    {
                        var signals = await _strategyContext.GenerateSignalsAsync(symbol, limit);
                        symbolSignals.AddRange(signals);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error generating signals for {symbol} at {interval} interval.");
                    }
                }

                return symbolSignals;
            });

            var results = await Task.WhenAll(tasks);
            return results.SelectMany(s => s);
        }

        private void HandleNewSignal(TradingSignal signal)
        {
            // Handle the new signal (e.g., log or process it)
            _logger.LogInformation($"New signal generated: {signal.Signal} for {signal.Symbol} at {signal.Price}");
        }

    }

    // Supporting classes
    public class CryptoPrice
    {
        public string Symbol { get; set; }
        public DateTime OpenTime { get; set; }
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public decimal Volume { get; set; }
        public DateTime CloseTime { get; set; }
    }


    /*
    public class TradingSignal
    {
        public string Symbol { get; set; }
        public decimal Price { get; set; }
        public string Signal { get; set; }
        public string Description { get; set; }
    } */

    public class OrderBlock
    {
        public string Symbol { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public decimal PriceLevel { get; set; }
        public string Description { get; set; }
    }

    public class TradeEntry
    {
        public string Symbol { get; set; }
        public decimal EntryPrice { get; set; }
        public DateTime EntryTime { get; set; }
        public string Description { get; set; }
    }

    public enum MarketPhase
    {
        Accumulation,
        Markup,
        Distribution,
        Markdown
    }
}