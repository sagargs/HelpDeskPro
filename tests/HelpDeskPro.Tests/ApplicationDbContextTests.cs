using HelpDeskPro.API.Data;
using HelpDeskPro.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace HelpDeskPro.Tests;

public class ApplicationDbContextTests
{
    [Fact]
    public void ModelContainsAllDatabaseEntities()
    {
        using var context = CreateContext();

        Assert.NotNull(context.Model.FindEntityType(typeof(User)));
        Assert.NotNull(context.Model.FindEntityType(typeof(Category)));
        Assert.NotNull(context.Model.FindEntityType(typeof(Ticket)));
        Assert.NotNull(context.Model.FindEntityType(typeof(TicketHistory)));
        Assert.NotNull(context.Model.FindEntityType(typeof(TicketComment)));
    }

    [Fact]
    public void ModelContainsFiveStableDefaultCategorySeeds()
    {
        using var context = CreateContext();

        var designTimeModel = context.GetService<IDesignTimeModel>().Model;
        var categoryEntity = designTimeModel.FindEntityType(typeof(Category))!;
        var seedData = categoryEntity.GetSeedData();
        var expectedCategories = new[]
        {
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
            }
        };

        Assert.Equal(expectedCategories.Length, seedData.Count());

        foreach (var expected in expectedCategories)
        {
            var seed = seedData.Single(row => (int)row[nameof(Category.Id)]! == expected.Id);
            Assert.Equal(expected.Name, seed[nameof(Category.Name)]);
            Assert.Equal(expected.Description, seed[nameof(Category.Description)]);
            Assert.Equal(expected.IsActive, seed[nameof(Category.IsActive)]);
        }
    }

    [Fact]
    public void RelationshipsRestrictDeletesAndAllowUnassignedTickets()
    {
        using var context = CreateContext();

        var foreignKeys = context.Model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetForeignKeys())
            .ToArray();

        Assert.Equal(7, foreignKeys.Length);
        Assert.All(foreignKeys, foreignKey => Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior));

        var assignedToForeignKey = foreignKeys.Single(foreignKey =>
            foreignKey.Properties.Single().Name == nameof(Ticket.AssignedToUserId));

        Assert.True(assignedToForeignKey.Properties.Single().IsNullable);
    }

    [Fact]
    public void EnumPropertiesUseIntegerStorage()
    {
        using var context = CreateContext();
        var enumProperties = new[]
        {
            GetProperty(context, typeof(User), nameof(User.Role)),
            GetProperty(context, typeof(Ticket), nameof(Ticket.Priority)),
            GetProperty(context, typeof(Ticket), nameof(Ticket.Status)),
            GetProperty(context, typeof(TicketHistory), nameof(TicketHistory.OldStatus)),
            GetProperty(context, typeof(TicketHistory), nameof(TicketHistory.NewStatus))
        };

        Assert.All(enumProperties, property =>
            Assert.Equal(typeof(int), property.GetTypeMapping().Converter?.ProviderClrType));
    }

    [Fact]
    public void GeneratedTimestampsUseSqlServerUtcDefaults()
    {
        using var context = CreateContext();
        var timestampProperties = new[]
        {
            GetProperty(context, typeof(User), nameof(User.CreatedAtUtc)),
            GetProperty(context, typeof(Ticket), nameof(Ticket.CreatedAtUtc)),
            GetProperty(context, typeof(Ticket), nameof(Ticket.UpdatedAtUtc)),
            GetProperty(context, typeof(TicketHistory), nameof(TicketHistory.ChangedAtUtc)),
            GetProperty(context, typeof(TicketComment), nameof(TicketComment.CreatedAtUtc))
        };

        Assert.All(timestampProperties, property =>
        {
            Assert.Equal("datetime2", property.GetColumnType());
            Assert.Equal("SYSUTCDATETIME()", property.GetDefaultValueSql());
        });

        Assert.Equal("datetime2", GetProperty(context, typeof(Ticket), nameof(Ticket.DueAtUtc)).GetColumnType());
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=localhost;Database=HelpDeskProModelTests;Trusted_Connection=True;TrustServerCertificate=True;")
            .Options;

        return new ApplicationDbContext(options);
    }

    private static IProperty GetProperty(
        ApplicationDbContext context,
        Type entityType,
        string propertyName)
    {
        return context.Model.FindEntityType(entityType)!.FindProperty(propertyName)!;
    }
}
