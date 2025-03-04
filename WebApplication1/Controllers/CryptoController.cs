using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WebApplication1.Services;
using WebApplication1.Hubs;
using WebApplication1.Models;
using WebApplication1.Services.Interfaces;

namespace WebApplication1.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CryptoController : ControllerBase
    {
        private readonly BinanceService _binanceService;
        private readonly IHubContext<PriceHub> _priceHubContext;
        private readonly ILogger<CryptoController> _logger;
        private readonly TradingStrategyContext _strategyContext;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<ICTStrategy> _ictStrategyLogger;
        private readonly ILogger<Scalping> _scalpingLogger;
        private readonly WebSocketService _webSocketService;

        public CryptoController(BinanceService binanceService, IHubContext<PriceHub> priceHubContext,
                                ILogger<CryptoController> logger, TradingStrategyContext strategyContext,
                                IHttpClientFactory httpClientFactory, ILogger<ICTStrategy> ictStrategyLogger, ILogger<Scalping> scalpingLogger, WebSocketService webSocketService)
        {
            _binanceService = binanceService;
            _priceHubContext = priceHubContext;
            _logger = logger;
            _strategyContext = strategyContext;
            _httpClientFactory = httpClientFactory;
            _ictStrategyLogger = ictStrategyLogger;
            _scalpingLogger = scalpingLogger;
            _webSocketService = webSocketService;
        }

        
        [HttpGet("ict-signals")]
        public async Task<ActionResult<IEnumerable<TradingSignal>>> GetICTSignals(int limit = 500)
        {
            try
            {
                // Set the ICT strategy as the current strategy
                var httpClient = _httpClientFactory.CreateClient();
                var ictStrategy = new ICTStrategy(httpClient, _ictStrategyLogger, _binanceService, _webSocketService);
                _strategyContext.SetStrategy(ictStrategy);


                // Generate signals for all trading pairs across multiple intervals
                var signals = await _strategyContext.GenerateSignalsForAllPairsAsync(limit);

                _logger.LogInformation("Signals generated successfully.");
                return Ok(signals);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating ICT signals.");
                return StatusCode(500, "Internal server error" + ex.Message);
            }
        }

        [HttpGet("scalping-signals")]
        public async Task<ActionResult<IEnumerable<TradingSignal>>> GetScalpingSignals(int limit = 500)
        {
            try
            {
                // Set the Scalping strategy as the current strategy
                var httpClient = _httpClientFactory.CreateClient();
                var scalpingStrategy = new Scalping(httpClient, _scalpingLogger);
                _strategyContext.SetStrategy(scalpingStrategy);

                // Generate signals for all trading pairs across multiple intervals
                var signals = await _strategyContext.GenerateSignalsForAllPairsAsync(limit);

                _logger.LogInformation("Signals generated successfully.");
                return Ok(signals);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating Scalping signals.");
                return StatusCode(500, "Internal server error" + ex.Message);
            }
        }

        [HttpGet("{symbol}/data")]
        public async Task<ActionResult<TradingSignal>> GetData(string symbol)
        {
            try
            {
                // Set the Scalping strategy as the current strategy
                var httpClient = _httpClientFactory.CreateClient();
                var scalpingStrategy = new Scalping(httpClient, _scalpingLogger);
                _strategyContext.SetStrategy(scalpingStrategy);

                // Generate signals for the specified trading pair
                var prices = await scalpingStrategy.GetHistoricalPricesAsync(symbol, "1m", 100);
                var signals = scalpingStrategy.GenerateScalpingSignals(prices);
                var latestSignal = signals.LastOrDefault();

                if (latestSignal == null)
                {
                    return NotFound();
                }

                _logger.LogInformation($"Data fetched for {symbol}.");
                return Ok(latestSignal);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching data for {symbol}.");
                return StatusCode(500, "Internal server error" + ex.Message);
            }
        }

    }
}
