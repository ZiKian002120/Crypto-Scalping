using Microsoft.AspNetCore.Mvc;
using WebApplication1.Data;
using WebApplication1.Models;
using WebApplication1.Services;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using Microsoft.EntityFrameworkCore;

namespace WebApplication1.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class OrdersController : Controller
    {
        private readonly BinanceService _binanceService;
        private readonly ApplicationDbContext _context;
        private readonly ILogger<CryptoController> _logger;

        public OrdersController(BinanceService binanceService, ApplicationDbContext context, ILogger<CryptoController> logger)
        {
            _binanceService = binanceService;
            _context = context;
            _logger = logger;
        }

        [HttpPost] 
        public async Task<IActionResult> PlaceOrder([FromBody] Order order)
        {
            try
            {
                var accountId = HttpContext.Session.GetInt32("AccountId");

                if (accountId == null)
                {
                    return Unauthorized("User not logged in.");
                }

                var account = await _context.Accounts.FindAsync(accountId.Value);
                if (account == null)
                {
                    return NotFound("Account not found.");
                }

                // Check if the account has sufficient balance
                if (account.Balance < order.OrderPrice)
                {
                    return BadRequest("Insufficient balance.");
                }

                // Deduct the order price from the account balance
                account.Balance -= order.OrderPrice;

                order.AccountId = accountId.Value;
                order.Account = account;

                if (order == null)
                {
                    return BadRequest("Order is null.");
                }

                order.Roi = 0;
                order.Timestamp = DateTime.Now;
                order.Status = OrderStatus.Active;

                _context.Orders.Add(order);

                
                _context.Accounts.Update(account);

                // Add a transaction entry
                var transaction = new Transaction
                {
                    AccountId = accountId.Value,
                    TransactionDate = DateTime.Now,
                    Amount = -order.OrderPrice,
                    Description = $"Order placed for {order.Symbol}",
                    BalanceAtTransaction = account.Balance
                };
                _context.Transactions.Add(transaction);

                await _context.SaveChangesAsync();

                // Calculate ROI after saving the order
                if (_binanceService.TryGetLastKnownPrice(order.Symbol, out var currentPrice))
                {
                    order.Roi = ((currentPrice - order.EntryPrice) / order.EntryPrice) * 100;
                    await UpdateProfitAndLossAsync(order, currentPrice);
                    _context.Orders.Update(order);
                    await _context.SaveChangesAsync();
                }

                return Ok(order);
            }
            catch (Exception ex)
            {
                // Log detailed error information
                var errorMessage = $"Error in PlaceOrder: {ex.Message} \n StackTrace: {ex.StackTrace}";

                // Log inner exception details, if present
                if (ex.InnerException != null)
                {
                    errorMessage += $"\n Inner Exception: {ex.InnerException.Message} \n Inner StackTrace: {ex.InnerException.StackTrace}";
                }

                // Print the detailed error message to the console or log it using your logging framework
                Console.WriteLine(errorMessage);

                // Return the error details to the client
                return StatusCode(500, new { error = "An internal server error occurred.", details = errorMessage });
            }
        }



        // Method to update ROI for all orders with the given symbol and new price
        public async Task UpdateOrdersRoiAsync(string symbol, decimal newPrice)
        {
            if (string.IsNullOrEmpty(symbol))
            {
                throw new ArgumentNullException(nameof(symbol), "Symbol cannot be null or empty.");
            }

            var orders = _context.Orders.Where(o => o.Symbol == symbol).ToList();

            foreach (var order in orders)
            {
                // Calculate ROI based on the new price
                order.Roi = ((newPrice - order.EntryPrice) / order.EntryPrice) * 100;

                // Save changes
                _context.Orders.Update(order);
            }

            await _context.SaveChangesAsync();
        }

        // Method to update Profit and Loss
        private async Task UpdateProfitAndLossAsync(Order order, decimal currentPrice)
        {
            order.ProfitAndLoss = (currentPrice - order.EntryPrice) * order.OrderPrice;
            _context.Orders.Update(order);
            await _context.SaveChangesAsync();
        }

        [HttpGet]
        public async Task<IActionResult> GetActiveOrders(int page = 1, int pageSize = 10)
        {
            try
            {
                var accountId = HttpContext.Session.GetInt32("AccountId");

                if (accountId == null)
                {
                    return Unauthorized(); // No AccountId in session
                }

                var totalOrdersCount = await _context.Orders
                .Where(o => o.AccountId == accountId.Value && o.Status == OrderStatus.Active)
                .CountAsync();

                var orders = await _binanceService.GetOrdersAsync(accountId.Value, page, pageSize);
                
                var totalPages = (int)Math.Ceiling(totalOrdersCount / (double)pageSize);



                var response = new
                {
                    Orders = orders,
                    TotalPages = totalPages,
                    CurrentPage = page
                };

                _logger.LogInformation($"Total Pages: {totalPages}");
                return Ok(response);
            }
            catch (Exception ex)
            {
                
                _logger.LogError(ex, "An error occurred while fetching orders.");

                return StatusCode(StatusCodes.Status500InternalServerError, "An error occurred while processing your request.");
            }
        }

        [HttpPut("{id}/close")]
        public async Task<IActionResult> CloseOrder(int id)
        {
            var accountId = HttpContext.Session.GetInt32("AccountId");

            if (accountId == null)
            {
                return Unauthorized("User not logged in.");
            }

            var account = await _context.Accounts.FindAsync(accountId.Value);
            if (account == null)
            {
                return NotFound("Account not found.");
            }

            var order = await _context.Orders.FindAsync(id);
            if (order == null)
            {
                return NotFound("Order not found.");
            }

            if (order.Status != OrderStatus.Active)
            {
                return BadRequest("Order is not active.");
            }

            //calculate balance after closed order
            account.Balance = account.Balance + order.OrderPrice + order.ProfitAndLoss;

            order.Status = OrderStatus.Closed;
            order.ClosedTime = DateTime.Now;

            _context.Orders.Update(order);
            _context.Accounts.Update(account);

            // Add a transaction entry for closing the order
            var transaction = new Transaction
            {
                AccountId = accountId.Value,
                TransactionDate = DateTime.Now,
                Amount = order.OrderPrice + order.ProfitAndLoss,
                Description = $"Order closed for {order.Symbol}",
                BalanceAtTransaction = account.Balance
            };
            _context.Transactions.Add(transaction);

            await _context.SaveChangesAsync();

            return Ok(order);
        }
    }
}
