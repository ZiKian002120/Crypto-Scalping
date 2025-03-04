// PriceHub.cs
using Microsoft.AspNetCore.SignalR;
using WebApplication1.Services;
using WebApplication1.Models;

namespace WebApplication1.Hubs
{
    public class PriceHub : Hub
    {
        public async Task SendPriceUpdate(string symbol, decimal price)
        {
            await Clients.All.SendAsync("ReceivePriceUpdate", symbol, price);
        }

        public async Task SendTradingSignal(TradingSignal signal)
        {
            await Clients.All.SendAsync("ReceiveTradingSignal", signal);
        }
    }
}
