using HelpDeskHero.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskHero.Api.Infrastructure.Persistence;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        await db.Database.MigrateAsync(ct);

        if (!await db.Users.AnyAsync(ct))
        {
            db.Users.AddRange(
                new AppUser
                {
                    UserName = "admin",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin123!"),
                    Role = "Admin"
                },
                new AppUser
                {
                    UserName = "agent",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Agent123!"),
                    Role = "Agent"
                },
                new AppUser
                {
                    UserName = "user",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("User123!"),
                    Role = "User"
                });

            await db.SaveChangesAsync(ct);
        }
    }
}