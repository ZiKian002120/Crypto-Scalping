using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WebApplication1.Services.Interfaces;
using WebApplication1.Models;
using Newtonsoft.Json.Linq;
using System.Net.Http;

namespace WebApplication1.Services
{
    public class ICTStrategy : ITradingStrategy
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<ICTStrategy> _logger;
        private readonly BinanceService _binanceService;
        private readonly WebSocketService _webSocketService;
        private readonly Dictionary<string, TradingSignal> _lastKnownSignals = new Dictionary<string, TradingSignal>();

        public ICTStrategy(HttpClient httpClient, ILogger<ICTStrategy> logger, BinanceService binanceService, WebSocketService webSocketService)
        {
            _logger = logger;
            _httpClient = httpClient;
            _binanceService = binanceService;
            _webSocketService = webSocketService;
        }

        public void ConnectToWebSocket(string symbol)
        {
            var url = $"wss://stream.binance.com:9443/ws/{symbol.ToLower()}@kline_1m";
            _webSocketService.Connect(url);
            _webSocketService.OnMessage += (sender, e) => HandleWebSocketMessage(symbol, e.Data);
        }

        private async void HandleWebSocketMessage(string symbol, string message)
        {
            var json = JObject.Parse(message);
            var kline = json["k"];

            if (kline != null && kline["x"].Value<bool>())
            {
                var price = new CryptoPrice
                {
                    Symbol = symbol,
                    OpenTime = DateTimeOffset.FromUnixTimeMilliseconds(kline["t"].Value<long>()).UtcDateTime,
                    Open = kline["o"].Value<decimal>(),
                    High = kline["h"].Value<decimal>(),
                    Low = kline["l"].Value<decimal>(),
                    Close = kline["c"].Value<decimal>(),
                    Volume = kline["v"].Value<decimal>(),
                    CloseTime = DateTimeOffset.FromUnixTimeMilliseconds(kline["T"].Value<long>()).UtcDateTime
                };

                var prices = new List<CryptoPrice> { price };
                var signal = GenerateICTSignal(prices);

                if (signal != null)
                {
                    lock (_lastKnownSignals)
                    {
                        _lastKnownSignals[symbol] = signal;
                        _logger.LogInformation($"Updated signal for {symbol}: {signal.Signal} at {signal.Price}");
                    }

                    await _binanceService.HandleNewPriceAsync(symbol, signal.Price);
                }
            }
        }

        public async Task<IEnumerable<TradingSignal>> GenerateSignalsAsync(string symbol, int limit)
        {
            var intervals = new[] { "1m", "15m", "1h", "4h" };
            var tasks = intervals.Select(async interval =>
            {
                var prices = await GetHistoricalPricesAsync(symbol, interval, limit);
                if (prices != null && prices.Any())
                {
                    _logger.LogInformation($"Received {prices.Count()} data points for {symbol} at {interval} interval.");
                    return GenerateICTSignal(prices);
                }
                else
                {
                    _logger.LogWarning($"No data received for {symbol} at {interval} interval.");
                    return null;
                }
            });

            var signals = (await Task.WhenAll(tasks)).Where(s => s != null).ToList();
            return signals;
        }

        public async Task<IEnumerable<TradingSignal>> GenerateSignalsForAllPairsAsync(int limit = 500)
        {
            var intervals = new[] { "1m", "15m", "1h", "4h" };
            var symbols = new List<string>
            {
                "BTCUSDT", "ETHUSDT", "BNBUSDT", "SOLUSDT", "ARBUSDT", "OPUSDT", "ASTRUSDT", "IOUSDT", "CELOUSDT", "XRPUSDT"
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

                            if (signal != null && !IsPlaceholderSignal(signal))
                            {
                                lock (_lastKnownSignals)
                                {
                                    _lastKnownSignals[symbol] = signal;
                                    _logger.LogInformation($"Updated signal for {symbol}: {signal.Signal} at {signal.Price}");
                                }

                                await _binanceService.HandleNewPriceAsync(symbol, signal.Price);
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

                lock (_lastKnownSignals)
                {
                    if (!hasNewData && !_lastKnownSignals.ContainsKey(symbol))
                    {
                        _lastKnownSignals[symbol] = new TradingSignal
                        {
                            Symbol = symbol,
                            Price = 0,
                            Signal = "-",
                            Description = "No data received"
                        };
                        _logger.LogInformation($"Set default signal for {symbol} due to missing data.");
                    }
                }
            });

            await Task.WhenAll(tasks);

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

        private async Task<IEnumerable<CryptoPrice>> GetHistoricalPricesAsync(string symbol, string interval, int limit)
        {
            var url = $"https://api.binance.com/api/v3/klines?symbol={symbol}&interval={interval}&limit={limit}";

            try
            {
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var responseBody = await response.Content.ReadAsStringAsync();
                var data = JArray.Parse(responseBody);

                var prices = data.Select(item => new CryptoPrice
                {
                    Symbol = symbol,
                    OpenTime = DateTimeOffset.FromUnixTimeMilliseconds((long)item[0]).UtcDateTime,
                    Open = (decimal)item[1],
                    High = (decimal)item[2],
                    Low = (decimal)item[3],
                    Close = (decimal)item[4],
                    Volume = (decimal)item[5],
                    CloseTime = DateTimeOffset.FromUnixTimeMilliseconds((long)item[6]).UtcDateTime
                }).ToList();

                return prices;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching historical prices for {symbol} at {interval} interval.");
                return new List<CryptoPrice>();
            }
        }

        private TradingSignal GenerateICTSignal(IEnumerable<CryptoPrice> prices)
        {
            TradingSignal signal = null;
            var marketPhase = IdentifyMarketPhase(prices);
            var orderBlocks = DetectOrderBlocks(prices);
            var tradeEntries = CalculateOTE(prices);
            var latestPrice = prices.Last();

            var nearestOrderBlock = orderBlocks.OrderBy(ob => Math.Abs(ob.PriceLevel - latestPrice.Close)).FirstOrDefault();
            bool isPriceNearOrderBlock = nearestOrderBlock != null &&
                                         Math.Abs(nearestOrderBlock.PriceLevel - latestPrice.Close) <= (latestPrice.Close * 0.005m);

            var matchingOTE = tradeEntries.FirstOrDefault(te => Math.Abs(te.EntryPrice - latestPrice.Close) <= (latestPrice.Close * 0.005m));

            if (marketPhase == MarketPhase.Markup && isPriceNearOrderBlock && matchingOTE != null)
            {
                signal = new TradingSignal
                {
                    Symbol = latestPrice.Symbol,
                    Price = latestPrice.Close,
                    Signal = "Strong Buy",
                    Description = $"Price near bullish order block with OTE during {marketPhase} phase"
                };
            }
            else if (marketPhase == MarketPhase.Markdown && isPriceNearOrderBlock && matchingOTE != null)
            {
                signal = new TradingSignal
                {
                    Symbol = latestPrice.Symbol,
                    Price = latestPrice.Close,
                    Signal = "Strong Sell",
                    Description = $"Price near bearish order block with OTE during {marketPhase} phase"
                };
            }
            else if (marketPhase == MarketPhase.Markup && isPriceNearOrderBlock)
            {
                signal = new TradingSignal
                {
                    Symbol = latestPrice.Symbol,
                    Price = latestPrice.Close,
                    Signal = "Buy",
                    Description = $"Price near bullish order block during {marketPhase} phase"
                };
            }
            else if (marketPhase == MarketPhase.Markdown && isPriceNearOrderBlock)
            {
                signal = new TradingSignal
                {
                    Symbol = latestPrice.Symbol,
                    Price = latestPrice.Close,
                    Signal = "Sell",
                    Description = $"Price near bearish order block during {marketPhase} phase"
                };
            }

            return signal;
        }

        private bool IsPlaceholderSignal(TradingSignal signal)
        {
            return signal.Signal == "-" || signal.Price == 0;
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
}