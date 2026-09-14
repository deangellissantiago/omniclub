using Checkin.Application.Ports.Repositories;
using Checkin.Application.Ports.Security;
using Checkin.Domain.Entities;
using Checkin.Domain.Enums;

namespace Checkin.Api.Seed;

/// <summary>
/// Popula o banco, na primeira execução, com o tenant "Escola de Tênis", um admin padrão
/// e os 4 pontos de check-in Wellhub já informados pelo cliente.
/// </summary>
public static class DataSeeder
{
    // Usados quando Seed__AdminEmail/Seed__AdminPassword não estão configurados (dev local).
    // Em produção, defina essas duas variáveis no .env da VPS com credenciais de verdade —
    // caso contrário o admin nasce com esta senha placeholder, conhecida por qualquer um com
    // acesso ao código-fonte.
    public const string DefaultAdminEmail = "admin@escoladetenis.com";
    public const string DefaultAdminPassword = "Trocar@123";

    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var tenantRepo = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
        var adminRepo = scope.ServiceProvider.GetRequiredService<IAdminUserRepository>();
        var pointRepo = scope.ServiceProvider.GetRequiredService<ICheckinPointRepository>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        if (await tenantRepo.AnyAsync()) return;

        var adminEmail = config["Seed:AdminEmail"] ?? DefaultAdminEmail;
        var adminPassword = config["Seed:AdminPassword"] ?? DefaultAdminPassword;

        // Explícito (não só o default da classe): este é o tenant "fundador", sem passar pelo
        // checkout do Stripe — ver Tenant.SubscriptionStatus.
        var tenant = await tenantRepo.CreateAsync(new Tenant { Name = "Escola de Tênis", SubscriptionStatus = SubscriptionStatus.Active });

        await adminRepo.CreateAsync(new AdminUser
        {
            TenantId = tenant.Id,
            Name = "Administrador",
            Email = adminEmail,
            PasswordHash = hasher.Hash(adminPassword)
        });

        var seedPoints = new (string ExternalId, string Name)[]
        {
            ("500977", "Dynamis Santa Lúcia"),
            ("804587", "Dynamis Pilar"),
            ("500205", "Dynamis Buritis"),
            ("547050", "Dynamis Vila da Serra"),
        };

        foreach (var (externalId, name) in seedPoints)
        {
            await pointRepo.CreateAsync(new CheckinPoint
            {
                TenantId = tenant.Id,
                App = IntegrationApp.Wellhub,
                ExternalId = externalId,
                Name = name
            });
        }
    }
}
