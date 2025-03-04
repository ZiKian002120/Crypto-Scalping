using System.Collections.Generic;
using System.Threading.Tasks;
using WebApplication1.Models;

namespace WebApplication1.Services.Interfaces
{
    public interface ITradingStrategy
    {
        Task<IEnumerable<TradingSignal>> GenerateSignalsAsync(string symbol, int limit);
        Task<IEnumerable<TradingSignal>> GenerateSignalsForAllPairsAsync(int limit);
    }
}