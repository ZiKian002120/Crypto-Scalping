using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;
using WebApplication1.Data;
using WebApplication1.Models;

namespace WebApplication1.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LoginController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public LoginController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpPost]
        public async Task<IActionResult> Login([FromBody] LoginRequest loginRequest)
        {
            if (loginRequest == null || string.IsNullOrWhiteSpace(loginRequest.Username) || string.IsNullOrWhiteSpace(loginRequest.Password))
            {
                return BadRequest("Invalid login request.");
            }

            
            var account = await _context.Accounts
                .FirstOrDefaultAsync(a => a.Username == loginRequest.Username && a.Password == loginRequest.Password);

            if (account == null)
            {
                
                return Unauthorized("Invalid username or password.");
            }

            // Store the AccountId in session
            HttpContext.Session.SetInt32("AccountId", account.Id);
            // HttpContext.Session.SetString("Username", account.Username);

            return Ok(new { Message = "Login successful." });
        }

        [HttpPost("logout")]
        public IActionResult Logout()
        {
            // Clear the session on logout
            HttpContext.Session.Clear();
            return Ok(new { Message = "Logout successful." });
        }

        [HttpGet("currentAccount")]
        public async Task<IActionResult> GetCurrentAccount()
        {
            // Retrieve the AccountId from session
            var accountId = HttpContext.Session.GetInt32("AccountId");

            if (accountId == null)
            {
                return Unauthorized("No user is logged in.");
            }

            var account = await _context.Accounts.FindAsync(accountId.Value);

            if (account == null)
            {
                return Unauthorized("Invalid account.");
            }

            return Ok(account);
        }
    }

    public class LoginRequest
    {
        public string Username { get; set; }
        public string Password { get; set; }
    }
}
