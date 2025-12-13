using System.Security.Claims;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;

namespace MCPify.Sample.Controllers;

public class AuthorizationController : Controller
{
    [HttpGet("~/connect/authorize")]
    [HttpPost("~/connect/authorize")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Authorize()
    {
        var request = HttpContext.GetOpenIddictServerRequest() ??
            throw new InvalidOperationException("The OpenIddict request cannot be retrieved.");

        // Retrieve the user principal stored in the authentication cookie.
        // If the user is not authenticated, challenge the Cookie authentication scheme.
        // Since this is a demo, we'll auto-login a fake user if not authenticated, 
        // or just bypass the UI and simulate a "Click Accept" for the CLI demo flow.
        
        // Ideally: return Challenge(authenticationSchemes: CookieAuthenticationDefaults.AuthenticationScheme);
        
        // For this DEMO: We will create a dummy principal immediately to simulate a logged-in user approving the app.
        // In a real app, you'd show a login screen here.
        var identity = new ClaimsIdentity(
            authenticationType: "Identity.Application",
            nameType: ClaimsIdentity.DefaultNameClaimType,
            roleType: ClaimsIdentity.DefaultRoleClaimType);

        identity.AddClaim(new Claim(ClaimsIdentity.DefaultNameClaimType, "demo_user"));
        identity.AddClaim(new Claim("sub", "12345"));
        identity.AddClaim(new Claim("role", "Admin"));

        // Add the requested scopes
        var scopes = request.GetScopes();
        foreach (var scope in scopes)
        {
            identity.AddClaim(new Claim(OpenIddictConstants.Claims.Private.Scope, scope));
        }

        var principal = new ClaimsPrincipal(identity);
        
        // Set the scopes on the principal explicitly
        principal.SetScopes(request.GetScopes());

        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    [HttpPost("~/connect/token")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Exchange()
    {
        var request = HttpContext.GetOpenIddictServerRequest() ??
            throw new InvalidOperationException("The OpenIddict request cannot be retrieved.");

        if (request.IsAuthorizationCodeGrantType() || request.IsRefreshTokenGrantType() || request.IsDeviceCodeGrantType())
        {
            // Retrieve the claims principal stored in the authorization code/refresh token
            var result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            var principal = result.Principal;

            return SignIn(principal!, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }
        else if (request.IsClientCredentialsGrantType())
        {
            var identity = new ClaimsIdentity(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            identity.AddClaim(OpenIddictConstants.Claims.Subject, request.ClientId ?? "client");
            
            var principal = new ClaimsPrincipal(identity);
            principal.SetScopes(request.GetScopes());

            return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        throw new NotImplementedException("The specified grant type is not implemented.");
    }
}
