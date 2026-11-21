using HelpDeskPro.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HelpDeskPro.API.Data.Configurations;

public class TicketCommentConfiguration : IEntityTypeConfiguration<TicketComment>
{
    public void Configure(EntityTypeBuilder<TicketComment> builder)
    {
        builder.ToTable("TicketComments");

        builder.HasKey(comment => comment.Id);

        builder.Property(comment => comment.CommentText)
            .IsRequired()
            .HasMaxLength(4000);

        builder.Property(comment => comment.CreatedAtUtc)
            .IsRequired();

        builder.HasOne(comment => comment.Ticket)
            .WithMany(ticket => ticket.Comments)
            .HasForeignKey(comment => comment.TicketId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(comment => comment.User)
            .WithMany(user => user.Comments)
            .HasForeignKey(comment => comment.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(comment => new { comment.TicketId, comment.CreatedAtUtc });
        builder.HasIndex(comment => comment.UserId);
    }
}
