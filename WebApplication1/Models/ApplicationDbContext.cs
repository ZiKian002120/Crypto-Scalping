using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Reflection.Emit;
using WebApplication1.Models; 

namespace WebApplication1.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        
        public DbSet<Order> Orders { get; set; } 
        public DbSet<Account> Accounts { get; set; }
        public DbSet<Transaction> Transactions { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

           
            modelBuilder.Entity<Order>()
                .Property(o => o.Symbol)
                .IsRequired()
                .HasMaxLength(12);

            modelBuilder.Entity<Order>()
                .Property(o => o.Action)
                .IsRequired()
                .HasMaxLength(4);

            modelBuilder.Entity<Order>()
                .Property(o => o.EntryPrice)
                .HasColumnType("decimal(18,5)");

            modelBuilder.Entity<Order>()
                .Property(o => o.Roi)
                .HasColumnType("decimal(5,2)");

            modelBuilder.Entity<Order>()
                .Property(o => o.Timestamp)
                .HasDefaultValueSql("GETDATE()");

            modelBuilder.Entity<Order>()
                .Property(o => o.AccountId)
                .IsRequired();

            modelBuilder.Entity<Order>()
                .HasOne(o => o.Account)
                .WithMany(a => a.Orders)
                .HasForeignKey(o => o.AccountId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Order>()
                .Property(o => o.OrderPrice)
                .HasColumnType("decimal(18,2)") 
                .IsRequired(); 

            modelBuilder.Entity<Order>()
                .Property(o => o.ProfitAndLoss)
                .HasColumnType("decimal(18,2)") 
                .IsRequired();

            modelBuilder.Entity<Order>()
                .Property(o => o.Status)
                .IsRequired()
                .HasDefaultValue(OrderStatus.Active);

            modelBuilder.Entity<Account>()
               .Property(a => a.Role)
               .IsRequired()
               .HasMaxLength(10);

            modelBuilder.Entity<Account>()
                .Property(a => a.Username)
                .IsRequired()
                .HasMaxLength(25);

            modelBuilder.Entity<Account>()
               .Property(a => a.Password)
               .IsRequired()
               .HasMaxLength(20);

            modelBuilder.Entity<Account>()
               .Property(a => a.Email)
               .IsRequired()
               .HasMaxLength(50);

            modelBuilder.Entity<Account>()
                .Property(a => a.Balance)
                .HasColumnType("decimal(18,2)");

            modelBuilder.Entity<Account>()
                .HasIndex(a => a.Username)
                .IsUnique();

        }
    }
}
