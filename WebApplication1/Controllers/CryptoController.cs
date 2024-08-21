using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using WebApplication1.Services;
using WebApplication1.Hubs;

namespace WebApplication1.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CryptoController : ControllerBase
    {
        private readonly BinanceService _binanceService;
        private readonly IHubContext<PriceHub> _priceHubContext;
        private readonly ILogger<CryptoController> _logger;

        public CryptoController(BinanceService binanceService, IHubContext<PriceHub> priceHubContext, ILogger<CryptoController> logger)
        {
            _binanceService = binanceService;
            _priceHubContext = priceHubContext;
            _logger = logger;
        }

        
        [HttpGet("ict-signals")]
        public async Task<ActionResult<IEnumerable<TradingSignal>>> GetICTSignals(int limit = 500)
        {
            try
            {
                // Generate signals for all trading pairs across multiple intervals
                var signals = await _binanceService.GenerateICTSignalsForAllPairs(limit);
                return Ok(signals);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating ICT signals.");
                return StatusCode(500, "Internal server error");
            }
        }


    }
}
