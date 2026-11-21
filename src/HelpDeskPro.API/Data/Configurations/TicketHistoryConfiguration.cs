using HelpDeskPro.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HelpDeskPro.API.Data.Configurations;

public class TicketHistoryConfiguration : IEntityTypeConfiguration<TicketHistory>
{
    public void Configure(EntityTypeBuilder<TicketHistory> builder)
    {
        builder.ToTable("TicketHistories");

        builder.HasKey(history => history.Id);

        builder.Property(history => history.OldStatus)
            .HasConversion<int>();

        builder.Property(history => history.NewStatus)
            .HasConversion<int>();

        builder.Property(history => history.Action)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(history => history.ChangedAtUtc)
            .IsRequired()
            .HasColumnType("datetime2")
            .HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasOne(history => history.Ticket)
            .WithMany(ticket => ticket.HistoryRecords)
            .HasForeignKey(history => history.TicketId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(history => history.ChangedByUser)
            .WithMany(user => user.ChangedTicketHistories)
            .HasForeignKey(history => history.ChangedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(history => new { history.TicketId, history.ChangedAtUtc });
        builder.HasIndex(history => history.ChangedByUserId);
    }
}
