using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WebApplication1.Services.Interfaces;
using WebApplication1.Models;

namespace WebApplication1.Services
{
    public class TradingStrategyContext
    {
        private ITradingStrategy _strategy;

        public void SetStrategy(ITradingStrategy strategy)
        {
            _strategy = strategy;
        }

        public async Task<IEnumerable<TradingSignal>> GenerateSignalsAsync(string symbol, int limit)
        {
            if (_strategy == null)
            {
                throw new InvalidOperationException("Strategy not set.");
            }

            return await _strategy.GenerateSignalsAsync(symbol, limit);
        }

        public async Task<IEnumerable<TradingSignal>> GenerateSignalsForAllPairsAsync(int limit)
        {
            if (_strategy == null)
            {
                throw new InvalidOperationException("Strategy not set.");
            }

            return await _strategy.GenerateSignalsForAllPairsAsync(limit);
        }
    }
}