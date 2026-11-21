using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace HelpDeskPro.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeedDefaultCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                SET IDENTITY_INSERT [dbo].[Categories] ON;

                IF NOT EXISTS (SELECT 1 FROM [dbo].[Categories] WHERE [Id] = 2 OR [Name] = N'Hardware')
                    INSERT INTO [dbo].[Categories] ([Id], [Name], [Description], [IsActive])
                    VALUES (2, N'Hardware', N'Computer, laptop, printer and other physical equipment issues.', 1);

                IF NOT EXISTS (SELECT 1 FROM [dbo].[Categories] WHERE [Id] = 3 OR [Name] = N'Software')
                    INSERT INTO [dbo].[Categories] ([Id], [Name], [Description], [IsActive])
                    VALUES (3, N'Software', N'Software installation, application errors and licensing issues.', 1);

                IF NOT EXISTS (SELECT 1 FROM [dbo].[Categories] WHERE [Id] = 4 OR [Name] = N'Network')
                    INSERT INTO [dbo].[Categories] ([Id], [Name], [Description], [IsActive])
                    VALUES (4, N'Network', N'Internet, VPN, Wi-Fi and network connectivity issues.', 1);

                IF NOT EXISTS (SELECT 1 FROM [dbo].[Categories] WHERE [Id] = 5 OR [Name] = N'Access Management')
                    INSERT INTO [dbo].[Categories] ([Id], [Name], [Description], [IsActive])
                    VALUES (5, N'Access Management', N'User accounts, permissions, password and access-related issues.', 1);

                IF NOT EXISTS (SELECT 1 FROM [dbo].[Categories] WHERE [Id] = 6 OR [Name] = N'Other')
                    INSERT INTO [dbo].[Categories] ([Id], [Name], [Description], [IsActive])
                    VALUES (6, N'Other', N'IT support issues that do not fit other categories.', 1);

                SET IDENTITY_INSERT [dbo].[Categories] OFF;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // These categories may predate this migration and must not be deleted on rollback.
        }
    }
}
