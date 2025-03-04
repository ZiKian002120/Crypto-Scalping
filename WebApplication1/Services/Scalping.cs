using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WebApplication1.Models;
using WebApplication1.Services.Interfaces;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using Newtonsoft.Json.Linq;

namespace WebApplication1.Services
{
    public class Scalping : ITradingStrategy
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<Scalping> _logger;

        public Scalping(HttpClient httpClient, ILogger<Scalping> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<IEnumerable<TradingSignal>> GenerateSignalsAsync(string symbol, int limit)
        {
            var intervals = new[] { "1m", "5m" }; // Use 1-minute or 5-minute chart for high-frequency trades
            var tasks = intervals.Select(async interval =>
            {
                var prices = await GetHistoricalPricesAsync(symbol, interval, limit);
                if (prices != null && prices.Any())
                {
                    _logger.LogInformation($"Received {prices.Count()} data points for {symbol} at {interval} interval.");
                    return GenerateScalpingSignals(prices);
                }
                else
                {
                    _logger.LogWarning($"No data received for {symbol} at {interval} interval.");
                    return null;
                }
            });

            var signals = (await Task.WhenAll(tasks)).Where(s => s != null).SelectMany(s => s).ToList();
            return signals;
        }

        public async Task<IEnumerable<TradingSignal>> GenerateSignalsForAllPairsAsync(int limit = 500)
        {
            var intervals = new[] { "1m", "5m" };
            var symbols = new List<string> { "BTCUSDT", "ETHUSDT", "BNBUSDT", "SOLUSDT" };

            var tasks = symbols.SelectMany(symbol =>
            {
                return intervals.Select(async interval =>
                {
                    try
                    {
                        var prices = await GetHistoricalPricesAsync(symbol, interval, limit);

                        if (prices != null && prices.Any())
                        {
                            _logger.LogInformation($"Received {prices.Count()} data points for {symbol} at {interval} interval.");
                            var signals = GenerateScalpingSignals(prices);

                            return signals;
                        }
                        else
                        {
                            _logger.LogWarning($"No data received for {symbol} at {interval} interval.");
                            return new List<TradingSignal>();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error fetching or processing data for {symbol} at {interval} interval.");
                        return new List<TradingSignal>();
                    }
                });
            });

            var signals = await Task.WhenAll(tasks);
            return signals.SelectMany(s => s).ToList();
        }
/*
        public async Task HandleNewPriceAsync(string symbol, decimal newPrice)
        {
            await _binanceService.HandleNewPriceAsync(symbol, newPrice);
        }
*/
        public async Task<IEnumerable<CryptoPrice>> GetHistoricalPricesAsync(string symbol, string interval, int limit)
        {
            var url = $"https://api.binance.com/api/v3/klines?symbol={symbol}&interval={interval}&limit={limit}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync();
            var data = JArray.Parse(responseBody);

            var prices = new List<CryptoPrice>();
            foreach (var item in data)
            {
                prices.Add(new CryptoPrice
                {
                    OpenTime = DateTimeOffset.FromUnixTimeMilliseconds((long)item[0]).UtcDateTime,
                    Open = (decimal)item[1],
                    High = (decimal)item[2],
                    Low = (decimal)item[3],
                    Close = (decimal)item[4],
                    Volume = (decimal)item[5],
                    CloseTime = DateTimeOffset.FromUnixTimeMilliseconds((long)item[6]).UtcDateTime
                });
            }

            return prices;
        }

        public IEnumerable<TradingSignal> GenerateScalpingSignals(IEnumerable<CryptoPrice> prices)
        {
            var signals = new List<TradingSignal>();
            var closingPrices = prices.Select(p => p.Close).ToList();

            var ema9 = CalculateEMA(closingPrices, 9);
            var ema21 = CalculateEMA(closingPrices, 21);
            var (upperBand, lowerBand) = CalculateBollingerBands(closingPrices, 20, 2);
            var rsi = CalculateRSI(closingPrices, 7);

            for (int i = 1; i < closingPrices.Count; i++)
            {
                // Long Position
                if (closingPrices[i] < lowerBand[i] && ema9[i] > ema21[i] && rsi[i] >= 30 && rsi[i] <= 50)
                {
                    signals.Add(new TradingSignal
                    {
                        Symbol = prices.ElementAt(i).Symbol,
                        Price = closingPrices[i],
                        Signal = "Buy",
                        Description = "Long position signal generated"
                    });
                }

                // Short Position
                if (closingPrices[i] > upperBand[i] && ema9[i] < ema21[i] && rsi[i] >= 50 && rsi[i] <= 70)
                {
                    signals.Add(new TradingSignal
                    {
                        Symbol = prices.ElementAt(i).Symbol,
                        Price = closingPrices[i],
                        Signal = "Sell",
                        Description = "Short position signal generated"
                    });
                }
            }

            return signals;
        }

        private List<decimal> CalculateEMA(IEnumerable<decimal> prices, int period)
        {
            var ema = new List<decimal>();
            var multiplier = 2m / (period + 1);
            decimal previousEma = prices.Take(period).Average();

            ema.Add(previousEma);
            foreach (var price in prices.Skip(period))
            {
                var currentEma = ((price - previousEma) * multiplier) + previousEma;
                ema.Add(currentEma);
                previousEma = currentEma;
            }

            return ema;
        }

        private (List<decimal> UpperBand, List<decimal> LowerBand) CalculateBollingerBands(IEnumerable<decimal> prices, int period, decimal standardDeviation)
        {
            var upperBand = new List<decimal>();
            var lowerBand = new List<decimal>();
            var movingAverage = prices.Take(period).Average();

            for (int i = 0; i <= prices.Count() - period; i++)
            {
                var segment = prices.Skip(i).Take(period).ToList();
                var mean = segment.Average();
                var stdDev = (decimal)Math.Sqrt(segment.Average(v => Math.Pow((double)v - (double)mean, 2)));

                upperBand.Add(mean + (stdDev * standardDeviation));
                lowerBand.Add(mean - (stdDev * standardDeviation));
            }

            return (upperBand, lowerBand);
        }

        private List<decimal> CalculateRSI(IEnumerable<decimal> prices, int period)
        {
            var rsi = new List<decimal>();
            var gains = new List<decimal>();
            var losses = new List<decimal>();

            for (int i = 1; i < prices.Count(); i++)
            {
                var change = prices.ElementAt(i) - prices.ElementAt(i - 1);
                if (change > 0)
                {
                    gains.Add(change);
                    losses.Add(0);
                }
                else
                {
                    gains.Add(0);
                    losses.Add(-change);
                }
            }

            var averageGain = gains.Take(period).Average();
            var averageLoss = losses.Take(period).Average();
            rsi.Add(100 - (100 / (1 + (averageGain / averageLoss))));

            for (int i = period; i < gains.Count; i++)
            {
                averageGain = ((averageGain * (period - 1)) + gains[i]) / period;
                averageLoss = ((averageLoss * (period - 1)) + losses[i]) / period;
                rsi.Add(100 - (100 / (1 + (averageGain / averageLoss))));
            }

            return rsi;
        }
    }
}