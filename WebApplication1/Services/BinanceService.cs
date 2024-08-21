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

namespace WebApplication1.Services
{
    public class BinanceService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<BinanceService> _logger;
        private readonly ApplicationDbContext _context;

        // Dictionary to store the last known signals
        private readonly Dictionary<string, TradingSignal> _lastKnownSignals = new Dictionary<string, TradingSignal>();

        public BinanceService(ILogger<BinanceService> logger, ApplicationDbContext context)
        {
            _logger = logger;
            _context = context;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
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


        // Fetch historical prices for each symbol and apply ICT strategy
        public async Task<IEnumerable<TradingSignal>> GenerateICTSignalsForAllPairs(int limit = 500)
        {
            var intervals = new[] { "1m", "15m", "1h", "4h" };

            // Define the specific futures trading pairs you want to monitor
            var symbols = new List<string>
    {
        "BTCUSDT", "ETHUSDT", "BNBUSDT",
        "SOLUSDT"
    };

            var tasks = symbols.Select(async symbol =>
            {
                bool hasNewData = false;

                foreach (var interval in intervals)
                {
                    try
                    {
                        var prices = await GetHistoricalPricesAsync(symbol, interval, limit);

                        if (prices != null && prices.Any())
                        {
                            _logger.LogInformation($"Received {prices.Count()} data points for {symbol} at {interval} interval.");
                            hasNewData = true;
                            var signal = GenerateICTSignal(prices);

                            // Only update if the signal is valid
                            if (signal != null && !IsPlaceholderSignal(signal))
                            {
                                lock (_lastKnownSignals)
                                {
                                    _lastKnownSignals[symbol] = signal;
                                    _logger.LogInformation($"Updated signal for {symbol}: {signal.Signal} at {signal.Price}");
                                }

                                await HandleNewPriceAsync(symbol, signal.Price);
                            }
                        }
                        else
                        {
                            _logger.LogWarning($"No data received for {symbol} at {interval} interval.");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error fetching or processing data for {symbol} at {interval} interval.");
                    }
                }

                // If no new data was received and no signal has ever been set, set a default signal
                lock (_lastKnownSignals)
                {
                    if (!hasNewData && !_lastKnownSignals.ContainsKey(symbol))
                    {
                        _lastKnownSignals[symbol] = new TradingSignal
                        {
                            Symbol = symbol,
                            Price = 0, // Default value to indicate no price
                            Signal = "-", // Default signal to indicate no data
                            Description = "No data received"
                        };
                        _logger.LogInformation($"Set default signal for {symbol} due to missing data.");
                    }
                }
            });

            // Await all tasks to ensure all symbols are processed
            await Task.WhenAll(tasks);

            // Return the signals in the fixed order, using the last known signals
            return symbols.Select(symbol =>
            {
                if (_lastKnownSignals.TryGetValue(symbol, out var signal))
                {
                    return signal;
                }
                return new TradingSignal
                {
                    Symbol = symbol,
                    Price = 0,
                    Signal = "-",
                    Description = "No data received"
                };
            }).ToList();
        }

        // Helper method to determine if a signal is a placeholder
        private bool IsPlaceholderSignal(TradingSignal signal)
        {
            return signal.Signal == "-" || signal.Price == 0;
        }

        private bool IsStrongerSignal(TradingSignal newSignal, TradingSignal existingSignal)
        {
            // Example logic for prioritizing signals
            if (newSignal.Signal == "Strong Buy" || newSignal.Signal == "Strong Sell")
                return true;

            if ((newSignal.Signal == "Buy" || newSignal.Signal == "Sell") &&
                (existingSignal.Signal != "Strong Buy" && existingSignal.Signal != "Strong Sell"))
                return true;

            return false;
        }


        // Fetch historical prices for a symbol
        public async Task<IEnumerable<CryptoPrice>> GetHistoricalPricesAsync(string symbol, string interval, int limit = 500)
        {
            var url = $"https://fapi.binance.com/fapi/v1/klines?symbol={symbol}&interval={interval}&limit={limit}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync();
            var prices = JArray.Parse(responseBody).Select(k => new CryptoPrice
            {
                Symbol = symbol,
                OpenTime = DateTimeOffset.FromUnixTimeMilliseconds((long)k[0]).DateTime,
                Open = (decimal)k[1],
                High = (decimal)k[2],
                Low = (decimal)k[3],
                Close = (decimal)k[4],
                Volume = (decimal)k[5],
                CloseTime = DateTimeOffset.FromUnixTimeMilliseconds((long)k[6]).DateTime
            }).ToList();

            return prices;
        }

        // Generate trading signals based on ICT strategy
        public TradingSignal GenerateICTSignal(IEnumerable<CryptoPrice> prices)
        {
            TradingSignal signal = null;

            var marketPhase = IdentifyMarketPhase(prices);
            var orderBlocks = DetectOrderBlocks(prices);
            var tradeEntries = CalculateOTE(prices);

            var latestPrice = prices.Last();

            // Find the nearest order block to the current price
            var nearestOrderBlock = orderBlocks
                .OrderBy(ob => Math.Abs(ob.PriceLevel - latestPrice.Close))
                .FirstOrDefault();

            // Check if the latest price is within a certain threshold of the order block
            bool isPriceNearOrderBlock = nearestOrderBlock != null &&
                                         Math.Abs(nearestOrderBlock.PriceLevel - latestPrice.Close) <= (latestPrice.Close * 0.005m);

            // Check if there's an OTE entry near the latest price
            var matchingOTE = tradeEntries
                .FirstOrDefault(te => Math.Abs(te.EntryPrice - latestPrice.Close) <= (latestPrice.Close * 0.005m));

            if (marketPhase == MarketPhase.Markup && isPriceNearOrderBlock && matchingOTE != null)
            {
                return new TradingSignal
                {
                    Symbol = latestPrice.Symbol,
                    Price = latestPrice.Close,
                    Signal = "Strong Buy",
                    Description = $"Price near bullish order block with OTE during {marketPhase} phase"
                };
            }
            else if (marketPhase == MarketPhase.Markdown && isPriceNearOrderBlock && matchingOTE != null)
            {
                return new TradingSignal
                {
                    Symbol = latestPrice.Symbol,
                    Price = latestPrice.Close,
                    Signal = "Strong Sell",
                    Description = $"Price near bearish order block with OTE during {marketPhase} phase"
                };
            }
            else if (marketPhase == MarketPhase.Markup && isPriceNearOrderBlock)
            {
                return new TradingSignal
                {
                    Symbol = latestPrice.Symbol,
                    Price = latestPrice.Close,
                    Signal = "Buy",
                    Description = $"Price near bullish order block during {marketPhase} phase"
                };
            }
            else if (marketPhase == MarketPhase.Markdown && isPriceNearOrderBlock)
            {
                return new TradingSignal
                {
                    Symbol = latestPrice.Symbol,
                    Price = latestPrice.Close,
                    Signal = "Sell",
                    Description = $"Price near bearish order block during {marketPhase} phase"
                };
            }

            // No strong signal detected
            return null;
        }


        private MarketPhase IdentifyMarketPhase(IEnumerable<CryptoPrice> prices)
        {
            var closingPrices = prices.Select(p => p.Close).ToList();
            var shortTermMA = closingPrices.TakeLast(10).Average();
            var longTermMA = closingPrices.TakeLast(30).Average();

            if (shortTermMA > longTermMA)
                return MarketPhase.Markup;
            else if (shortTermMA < longTermMA)
                return MarketPhase.Markdown;
            else
                return MarketPhase.Accumulation;
        }


        private IEnumerable<OrderBlock> DetectOrderBlocks(IEnumerable<CryptoPrice> prices)
        {
            var orderBlocks = new List<OrderBlock>();
            var priceList = prices.ToList();

            for (int i = 1; i < priceList.Count; i++)
            {
                var currentPrice = priceList[i];
                var prevPrice = priceList[i - 1];

                if (prevPrice.Close > prevPrice.Open &&
                    currentPrice.Close > currentPrice.Open &&
                    currentPrice.Close > prevPrice.High)
                {
                    orderBlocks.Add(new OrderBlock
                    {
                        Symbol = currentPrice.Symbol,
                        StartTime = prevPrice.OpenTime,
                        EndTime = currentPrice.CloseTime,
                        PriceLevel = prevPrice.Low,
                        Description = "Bullish order block identified"
                    });
                }

                if (prevPrice.Close < prevPrice.Open &&
                    currentPrice.Close < currentPrice.Open &&
                    currentPrice.Close < prevPrice.Low)
                {
                    orderBlocks.Add(new OrderBlock
                    {
                        Symbol = currentPrice.Symbol,
                        StartTime = prevPrice.OpenTime,
                        EndTime = currentPrice.CloseTime,
                        PriceLevel = prevPrice.High,
                        Description = "Bearish order block identified"
                    });
                }
            }

            return orderBlocks;
        }


        // Example: Calculate Optimal Trade Entry (OTE)
        private IEnumerable<TradeEntry> CalculateOTE(IEnumerable<CryptoPrice> prices)
        {
            var tradeEntries = new List<TradeEntry>();

            var recentHigh = prices.Max(p => p.High);
            var recentLow = prices.Min(p => p.Low);

            var fibLevels = new[] { 0.618m, 0.786m };
            var retracementLevels = fibLevels.Select(f => recentLow + (recentHigh - recentLow) * f).ToArray();


            foreach (var level in retracementLevels)
            {
                var entry = prices.FirstOrDefault(p => p.Close >= level && p.Open <= level);
                if (entry != null)
                {
                    tradeEntries.Add(new TradeEntry
                    {
                        Symbol = entry.Symbol,
                        EntryPrice = level,
                        EntryTime = entry.CloseTime,
                        Description = "OTE entry at Fibonacci level"
                    });
                }
            }

            return tradeEntries;
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


    public class TradingSignal
    {
        public string Symbol { get; set; }
        public decimal Price { get; set; }
        public string Signal { get; set; }
        public string Description { get; set; }
    }

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