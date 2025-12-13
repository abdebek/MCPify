using MCPify.Sample.Data;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;

namespace MCPify.Sample;

public class Worker : IHostedService
{
    private readonly IServiceProvider _serviceProvider;

    public Worker(IServiceProvider serviceProvider)
        => _serviceProvider = serviceProvider;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureCreatedAsync(cancellationToken);

        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        var client = await manager.FindByClientIdAsync("demo-client-id", cancellationToken);
        if (client != null)
        {
            await manager.DeleteAsync(client, cancellationToken);
        }

        await manager.CreateAsync(new OpenIddictApplicationDescriptor
        {
            ClientId = "demo-client-id",
            ClientSecret = "demo-client-secret",
            DisplayName = "MCPify Demo Client",
            Permissions =
            {
                OpenIddictConstants.Permissions.Endpoints.Authorization,
                OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
                OpenIddictConstants.Permissions.GrantTypes.ClientCredentials,
                OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
                OpenIddictConstants.Permissions.ResponseTypes.Code,
                OpenIddictConstants.Permissions.Scopes.Email,
                OpenIddictConstants.Permissions.Scopes.Profile,
                OpenIddictConstants.Permissions.Scopes.Roles,
                OpenIddictConstants.Permissions.Prefixes.Scope + "read_secrets",
                OpenIddictConstants.Permissions.Prefixes.Scope + "openid"
            },
            RedirectUris =
            {
                new Uri("http://localhost:5000/auth/callback"),
                new Uri("https://localhost:5001/auth/callback"),
                new Uri("http://localhost:5005/auth/callback"),
            }
        }, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}