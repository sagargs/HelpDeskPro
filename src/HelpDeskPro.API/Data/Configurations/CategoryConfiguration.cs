using HelpDeskPro.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HelpDeskPro.API.Data.Configurations;

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Categories");

        builder.HasKey(category => category.Id);

        builder.Property(category => category.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(category => category.Description)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(category => category.IsActive)
            .IsRequired();

        builder.HasIndex(category => category.Name)
            .IsUnique();

        builder.HasData(
            new Category
            {
                Id = 2,
                Name = "Hardware",
                Description = "Computer, laptop, printer and other physical equipment issues.",
                IsActive = true
            },
            new Category
            {
                Id = 3,
                Name = "Software",
                Description = "Software installation, application errors and licensing issues.",
                IsActive = true
            },
            new Category
            {
                Id = 4,
                Name = "Network",
                Description = "Internet, VPN, Wi-Fi and network connectivity issues.",
                IsActive = true
            },
            new Category
            {
                Id = 5,
                Name = "Access Management",
                Description = "User accounts, permissions, password and access-related issues.",
                IsActive = true
            },
            new Category
            {
                Id = 6,
                Name = "Other",
                Description = "IT support issues that do not fit other categories.",
                IsActive = true
            });
    }
}
