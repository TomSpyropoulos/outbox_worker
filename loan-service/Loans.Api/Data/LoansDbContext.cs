using Loans.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Loans.Api.Data;

public class LoansDbContext : DbContext
{
    public LoansDbContext(DbContextOptions<LoansDbContext> options)
        : base(options)
    {
    }

    public DbSet<LoanApplication> LoanApplications { get; set; }

    public DbSet<OutboxMessage> OutboxMessages { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var loan = modelBuilder.Entity<LoanApplication>();
        loan.ToTable("LoanApplications");
        loan.HasKey(l => l.Id);
        loan.Property(l => l.Id).ValueGeneratedNever();
        loan.Property(l => l.BorrowerName).HasMaxLength(LoanApplication.BorrowerNameMaxLength);
        loan.Property(l => l.Amount).HasPrecision(18, 2);
        loan.Property(l => l.Currency).HasMaxLength(3).IsFixedLength().IsUnicode(false);
        // Stored as text ("Submitted") so the table is readable without knowing the enum's numbers.
        loan.Property(l => l.Status).HasConversion<string>().HasMaxLength(16);

        var outbox = modelBuilder.Entity<OutboxMessage>();
        outbox.ToTable("OutboxMessages");
        outbox.HasKey(m => m.Id);
        outbox.Property(m => m.Id).ValueGeneratedNever();
        outbox.Property(m => m.Type).HasMaxLength(200);
        outbox.Property(m => m.Error).HasMaxLength(OutboxMessage.ErrorMaxLength);

        // Only pending rows are in this index, so it stays small however large the table grows.
        outbox.HasIndex(m => m.OccurredOnUtc)
            .HasDatabaseName("IX_OutboxMessages_Pending")
            .HasFilter("[ProcessedAtUtc] IS NULL")
            .IncludeProperties(m => m.AttemptCount);
    }
}
