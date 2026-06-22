using HelpDeskHero.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace HelpDeskHero.Api.Infrastructure.Services;

public static class SeedData
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();

        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        string[] roles = ["Admin", "Agent", "User"];

        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        const string adminUserName = "admin";
        var admin = await userManager.FindByNameAsync(adminUserName);

        if (admin is null)
        {
            admin = new ApplicationUser
            {
                UserName = adminUserName,
                Email = "admin@helpdeskhero.local",
                DisplayName = "System Admin",
                IsActive = true,
                EmailConfirmed = true
            };

            var createResult = await userManager.CreateAsync(admin, "Admin1234");
            if (createResult.Succeeded)
            {
                await userManager.AddToRolesAsync(admin, ["Admin", "Agent"]);
            }
        }

        const string agentUserName = "agent";
        var agent = await userManager.FindByNameAsync(agentUserName);

        if (agent is null)
        {
            agent = new ApplicationUser
            {
                UserName = agentUserName,
                Email = "agent@helpdeskhero.local",
                DisplayName = "Support Agent",
                IsActive = true,
                EmailConfirmed = true
            };

            var createResult = await userManager.CreateAsync(agent, "Agent1234");
            if (createResult.Succeeded)
            {
                await userManager.AddToRoleAsync(agent, "Agent");
            }
        }
    }
}