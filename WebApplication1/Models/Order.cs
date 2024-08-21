using System.Text.Json.Serialization;

namespace WebApplication1.Models
{
    public class Order
    {
        public int Id { get; set; }
        public string Symbol { get; set; }
        public string Action { get; set; }
        public decimal EntryPrice { get; set; }
        public decimal? Roi { get; set; } = 0;
        public DateTime Timestamp { get; set; }
        public decimal OrderPrice { get; set; }
        public decimal ProfitAndLoss {get; set;}
        public OrderStatus Status { get; set; }
        public DateTime? ClosedTime { get; set; }
        public int AccountId { get; set; }

        [JsonIgnore]
        public Account? Account { get; set; }
    }

    public enum OrderStatus
    {
        Active,
        Closed
    }
}
