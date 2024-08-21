using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WebApplication1.Data;
using WebApplication1.Models;
using BCrypt.Net;

namespace WebApplication1.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AccountsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public AccountsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: api/accounts
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Account>>> GetAccounts()
        {
            return await _context.Accounts.ToListAsync();
        }

        // GET: api/accounts/5
        [HttpGet("{id}")]
        public async Task<ActionResult<Account>> GetAccount(int id)
        {
            var account = await _context.Accounts.FindAsync(id);

            if (account == null)
            {
                return NotFound();
            }

            return account;
        }

        [HttpGet("balance")]
        public async Task<IActionResult> GetBalance()
        {
            try
            {
                // Retrieve the AccountId from the session
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

                return Ok(new { balance = account.Balance });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        // POST: api/accounts
        [HttpPost]
        public async Task<ActionResult<Account>> CreateAccount(Account account)
        {
            if (account == null)
            {
                return BadRequest("Account is null.");
            }

            _context.Accounts.Add(account);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetAccount), new { id = account.Id }, account);
        }

        // PUT: api/accounts/5
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateAccount(int id, [FromBody] UpdateAccountDto updateAccountDto)
        {
            if (id != updateAccountDto.Id)
            {
                return BadRequest("Account ID mismatch.");
            }

            var existingAccount = await _context.Accounts.FindAsync(id);
            if (existingAccount == null)
            {
                return NotFound();
            }

            // Update basic information
            existingAccount.Username = updateAccountDto.Username;
            existingAccount.Email = updateAccountDto.Email;

            // Update password if provided
            if (!string.IsNullOrEmpty(updateAccountDto.CurrentPassword) && !string.IsNullOrEmpty(updateAccountDto.NewPassword))
            {
                // Validate current password
                if (!BCrypt.Net.BCrypt.Verify(updateAccountDto.CurrentPassword, existingAccount.Password))
                {
                    return BadRequest("Current password is incorrect.");
                }

                // Update to the new password
                existingAccount.Password = BCrypt.Net.BCrypt.HashPassword(updateAccountDto.NewPassword);
            }

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!AccountExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return Ok(existingAccount);
        }

        // DELETE: api/accounts/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteAccount(int id)
        {
            var account = await _context.Accounts.FindAsync(id);
            if (account == null)
            {
                return NotFound();
            }

            _context.Accounts.Remove(account);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        [HttpPost("{accountId}/deposit")]
        public async Task<IActionResult> Deposit(int accountId, [FromBody] TransactionDto transactionDto)
        {
            try
            {
                var account = await _context.Accounts.FindAsync(accountId);

                if (account == null)
                {
                    return NotFound(new { message = "Account not found" });
                }

                // Add logging here to debug the values
                Console.WriteLine($"Account ID: {accountId}");
                Console.WriteLine($"Deposit Amount: {transactionDto.Amount}");

                account.Balance += transactionDto.Amount;
                var transaction = new Transaction
                {
                    AccountId = accountId,
                    Amount = transactionDto.Amount,
                    TransactionDate = DateTime.Now,
                    Description = "Deposit",
                    BalanceAtTransaction = account.Balance
                };

                _context.Transactions.Add(transaction);
                await _context.SaveChangesAsync();

                return Ok(new AccountDto
                {
                    Id = account.Id,
                    Balance = account.Balance
                });
            }
            catch (Exception ex)
            {
                // Log the error
                Console.WriteLine($"Error during deposit: {ex.Message}");
                Console.WriteLine($"Stack Trace: {ex.StackTrace}");
                return StatusCode(500, "An internal server error occurred.");
            }
        }


        [HttpPost("{accountId}/withdraw")]
        public async Task<IActionResult> Withdraw(int accountId, [FromBody] TransactionDto transactionDto)
        {
            try
            {
                var account = await _context.Accounts.FindAsync(accountId);
                if (account == null)
                {
                    return NotFound(new { message = "Account not found" });
                }

                if (account.Balance < transactionDto.Amount)
                {
                    return BadRequest(new { message = "Insufficient funds" });
                }

                account.Balance -= transactionDto.Amount;
                var transaction = new Transaction
                {
                    AccountId = accountId,
                    Amount = -transactionDto.Amount, // Negative for withdrawal
                    TransactionDate = DateTime.Now,
                    Description = "Withdrawal",
                    BalanceAtTransaction = account.Balance
                };
                _context.Transactions.Add(transaction);
                await _context.SaveChangesAsync();

                return Ok(new AccountDto
                {
                    Id = account.Id,
                    Balance = account.Balance
                }); ;
            }
            catch (Exception ex)
            {
                // Log the error
                Console.WriteLine($"Error during withdrawal: {ex.Message}");
                Console.WriteLine($"Stack Trace: {ex.StackTrace}");
                return StatusCode(500, new { message = "An internal server error occurred." });
            }
        }


        // GET: api/accounts/user-details
        [HttpGet("user-details")]
        public async Task<IActionResult> GetUserDetails()
        {
            var accountId = HttpContext.Session.GetInt32("AccountId");
            if (accountId == null)
            {
                return Unauthorized("User not logged in.");
            }

            var account = await _context.Accounts.FindAsync(accountId);
            if (account == null)
            {
                return NotFound("Account not found.");
            }

            return Ok(account);
        }

        [HttpPost("{id}/validate-password")]
        public async Task<IActionResult> ValidatePassword(int id, [FromBody] PasswordValidationModel model)
        {
            try
            {
                var account = await _context.Accounts.FindAsync(id);

                if (account == null)
                {
                    return Unauthorized("Account not found.");
                }

                if (string.IsNullOrEmpty(model.CurrentPassword) || string.IsNullOrEmpty(account.Password))
                {
                    return BadRequest("Password cannot be empty.");
                }

                // Log the password check
                bool isPasswordCorrect = BCrypt.Net.BCrypt.Verify(model.CurrentPassword, account.Password);

                if (!isPasswordCorrect)
                {
                    return Unauthorized("Current password is incorrect.");
                }
                return Ok();
            }
            catch(Exception ex)
            {
                Console.WriteLine($"Error during password validation: {ex.Message}");
                Console.WriteLine($"Stack Trace: {ex.StackTrace}");
                return StatusCode(500, $"An internal server error occurred: {ex.Message}");
            }
        }

        public class PasswordValidationModel
        {
            public string? CurrentPassword { get; set; }
        }

        private bool AccountExists(int id)
        {
            return _context.Accounts.Any(e => e.Id == id);
        }

        public class TransactionDto
        {
            public decimal Amount { get; set; }
        }
    }

    public class UpdateAccountDto
    {
        public int Id { get; set; }
        public string Username { get; set; }
        public string Email { get; set; }
        public string? CurrentPassword { get; set; }  // Only used for validation, not stored in DB
        public string? NewPassword { get; set; }      // New password to update, if provided
    }

    public class AccountDto
    {
        public int Id { get; set; }
        public decimal Balance { get; set; }
    }

}
