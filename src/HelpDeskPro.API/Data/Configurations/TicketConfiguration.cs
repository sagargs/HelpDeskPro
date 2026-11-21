using HelpDeskPro.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HelpDeskPro.API.Data.Configurations;

public class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.ToTable("Tickets");

        builder.HasKey(ticket => ticket.Id);

        builder.Property(ticket => ticket.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(ticket => ticket.Description)
            .IsRequired()
            .HasMaxLength(4000);

        builder.Property(ticket => ticket.Priority)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(ticket => ticket.Status)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(ticket => ticket.CreatedAtUtc)
            .IsRequired()
            .HasColumnType("datetime2")
            .HasDefaultValueSql("SYSUTCDATETIME()");

        builder.Property(ticket => ticket.UpdatedAtUtc)
            .IsRequired()
            .HasColumnType("datetime2")
            .HasDefaultValueSql("SYSUTCDATETIME()");

        builder.Property(ticket => ticket.DueAtUtc)
            .HasColumnType("datetime2");

        builder.HasOne(ticket => ticket.Category)
            .WithMany(category => category.Tickets)
            .HasForeignKey(ticket => ticket.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(ticket => ticket.CreatedByUser)
            .WithMany(user => user.CreatedTickets)
            .HasForeignKey(ticket => ticket.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(ticket => ticket.AssignedToUser)
            .WithMany(user => user.AssignedTickets)
            .HasForeignKey(ticket => ticket.AssignedToUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(ticket => ticket.CategoryId);
        builder.HasIndex(ticket => ticket.CreatedByUserId);
        builder.HasIndex(ticket => ticket.AssignedToUserId);
        builder.HasIndex(ticket => new { ticket.Status, ticket.Priority });
        builder.HasIndex(ticket => ticket.CreatedAtUtc);
    }
}
