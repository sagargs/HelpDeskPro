using HelpDeskPro.API.Data;
using HelpDeskPro.API.Models;
using Microsoft.EntityFrameworkCore;
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
