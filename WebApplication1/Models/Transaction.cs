namespace WebApplication1.Models
{
    public class Transaction
    {
        public int Id { get; set; }
        public int AccountId { get; set; }
        public Account Account { get; set; }
        public DateTime TransactionDate { get; set; }
        public decimal Amount { get; set; } 
        public string Description { get; set; }

        public decimal BalanceAtTransaction { get; set; }
    }
}
