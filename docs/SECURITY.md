# Security Model

MCPify is designed for safe operation in both local (Stdio) and hosted (HTTP) environments. This document covers the security threat model and configuration guidance for each layer.

## Authentication Layers

### 1. MCP Challenge & Protected Resource Metadata

Handled by the official `McpAuthenticationHandler` from the MCP SDK. When OAuth configurations are present, the MCP route is protected with `RequireAuthorization`. Unauthenticated requests receive a `401` with `WWW-Authenticate` pointing to `/.well-known/oauth-protected-resource`.

The MCP route does **not** pin `AuthenticationSchemes`. The host's `DefaultAuthenticateScheme` (e.g. `AddJwtBearer`, OpenIddict) validates inbound tokens. The MCP handler serves as `DefaultChallengeScheme` for protocol-level challenges.

### 2. Inbound JWT Validation

**The host owns JWT validation.** MCPify does not ship a JWT validator. Use standard ASP.NET Core:

```csharp
builder.Services.AddAuthentication()
    .AddJwtBearer("MyJwt", options =>
    {
        options.Authority = "https://auth.example.com";
        options.Audience = "my-api";
        options.TokenValidationParameters.ValidateIssuer = true;
        options.TokenValidationParameters.ValidateLifetime = true;
    });

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = "MyJwt";
    options.DefaultChallengeScheme = McpAuthenticationDefaults.AuthenticationScheme;
});
```

### 3. Per-Tool Scope Enforcement

`SessionAwareToolDecorator` evaluates `ScopeRequirement` metadata on each tool via `IAuthorizationService`. Enforcement is **fail-closed** — if `IAuthorizationService` is unavailable, the tool call is denied.

For HTTP transport, scopes are read from `HttpContext.User.Claims`. For Stdio, scopes are extracted from the JWT access token in the MCP context.

```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("read_secrets", policy => policy.RequireClaim("scope", "read_secrets"));
});
```

## Outbound Authentication (UpstreamAuth)

### Pass-Through

Forwards the MCP client's bearer token to upstream APIs. **Requires explicit opt-in** for HTTP transport:

```csharp
options.AllowClientTokenPassthrough = true;
```

Without this flag, MCPify fails fast at startup on HTTP. This prevents accidental token leakage in multi-user hosted environments.

| Deployment | Transport | Pass-Through | Risk |
|------------|-----------|-------------|------|
| Local / single-user | Stdio | Safe | Token stays on machine |
| Hosted / multi-user | HTTP | Explicit opt-in only | Client token may not have upstream audience |

### Token Exchange (RFC 8693)

Exchanges the client's token for an upstream-scoped token. Throws on non-success responses — no silent failures.

### Server-Managed OAuth

MCPify runs its own OAuth flow (authorization code + PKCE) to obtain tokens for upstream APIs. Tokens are stored encrypted on disk via `EncryptedFileTokenStore`.

**`stateSecret` is required** (minimum 32 characters). Passing `null` throws. Set via configuration, environment variable, or constructor — never hardcode in source.

**PKCE is enabled by default** with S256. There is no option to disable it.

## SSRF Protection

OpenAPI spec URLs are validated before fetching:

- **Blocked by default**: loopback, link-local (169.254/16), RFC1918 private ranges (10/8, 172.16/12, 192.168/16), cloud metadata endpoints
- **Allow private addresses**: `options.SsrfGuard.AllowPrivateAddresses = true` (for internal APIs behind a proxy)
- **Disable all checks**: `options.SsrfGuard.DisableSsrfChecks = true` (trusted environments only)
- **Custom blocked hosts**: `options.SsrfGuard.BlockedHosts.Add("internal.corp.local")`

## Token Storage

`EncryptedFileTokenStore` uses DPAPI (Windows) or AES-GCM with a machine key (Linux/macOS). The key is derived from the machine identity. If the key source is unavailable, the store **throws** — it does not fall back to an ephemeral key.

For production:
- **Use `EncryptedFileTokenStore.FromEnvironmentVariable(path)`** — the key is sourced from `MCPIFY_TOKENSTORE_KEY` and never written to disk next to the tokens
- Ensure the `AuthTokens` directory is protected (restrict filesystem permissions to the service account only)
- Consider a custom `ISecureTokenStore` backed by a secrets manager (Azure Key Vault, AWS Secrets Manager, HashiCorp Vault)
- Tokens are never logged
- Session directories are hashed (SHA-256 of session ID) to prevent path traversal

### Key Management by Platform

| Platform | Default Key Source | Production Recommendation |
|----------|-------------------|--------------------------|
| Windows | DPAPI (CurrentUser scope) | Service account isolation; DPAPI is sufficient |
| Linux | Auto-generated key file in token dir | Use `MCPIFY_TOKENSTORE_KEY` env var |
| macOS | Auto-generated key file in token dir | Use `MCPIFY_TOKENSTORE_KEY` env var |
| Container | Auto-generated key file (ephemeral) | Use `MCPIFY_TOKENSTORE_KEY` from secrets manager |

## Multi-IdP Token Isolation

The token store key is `(sessionId, providerName)`.

### Auto-namespaced defaults

When `providerName` is **omitted**, MCPify derives a store name from the OAuth **client id**:

| Type | Default when `providerName` is null |
|------|-------------------------------------|
| Authorization code | `OAuth:{clientId}` (e.g. `OAuth:demo-client-id`) |
| Client credentials | `ClientCredentials:{clientId}` |
| Device code | `DeviceCode:{clientId}` |

Single-IdP hosts with one client id need no change. Multi-IdP hosts that use **different client ids** get automatic isolation. If two IdPs share the same client id string, set **explicit** `providerName` values.

```csharp
// Automatic isolation (different client ids → different store slots)
new OAuthAuthorizationCodeAuthentication(clientId: "entra-client", /* ... */);  // OAuth:entra-client
new OAuthAuthorizationCodeAuthentication(clientId: "github-client", /* ... */); // OAuth:github-client

// Explicit override when you need a stable or shared name
new OAuthAuthorizationCodeAuthentication(
    clientId: "shared-client",
    /* ... */,
    providerName: "OAuth:api-a");
```

**Breaking note (0.0.15-preview):** older defaults used the bare names `"OAuth"` / `"ClientCredentials"` / `"DeviceCode"`. Existing encrypted token files under those names will not be picked up after upgrade unless you set `providerName` back to the old value or re-login.

### Pass-Through Cross-Audience Risk

`UpstreamAuth.PassThrough()` sends the inbound MCP client token to the upstream API. If multiple tools with different `ApiBaseUrl` values use pass-through, the same token goes to all of them. This is intentional but risky:

- Only use pass-through when all upstream APIs accept the same token audience
- Prefer `UpstreamAuth.ServerManaged(...)` with distinct OAuth clients / `providerName`s per API
- On multi-user HTTP hosts, never use pass-through without explicit opt-in
- At startup, MCPify **logs a warning** when two or more `ExternalApiOptions` with **different hosts** use PassThrough (including inside `Fallback`)

### `allowDefaultSessionFallback`

When enabled, the OAuth provider dual-writes tokens to both the real session and `Constants.DefaultSessionId`. The dual-write uses the same `providerName`, so cross-provider isolation is preserved — but **cross-session** isolation is weakened. Only use on single-user Stdio hosts.

### Checklist for Multi-IdP Hosts

- [ ] Different IdPs use different `clientId`s **or** explicit unique `providerName`s
- [ ] Each `ExternalApiOptions.UpstreamAuth` points to the correct provider factory
- [ ] `allowDefaultSessionFallback` is `false` on multi-user HTTP hosts
- [ ] `PassThrough` is not used for APIs with different audiences (watch startup multi-host warning)
- [ ] Each `TokenExchange` config has a distinct `ProviderName`

- OAuth state tokens are truncated in log messages
- Authorization headers are never logged
- Token values are never logged
- Error messages do not include signatures or state secrets

## HttpClient Lifetime

All outbound HTTP uses `IHttpClientFactory` — no `new HttpClient()` instances. This avoids socket exhaustion and enables centralized handler configuration (timeouts, retry policies, TLS).

## Checklist for Hosted Deployments

- [ ] `AllowClientTokenPassthrough` is explicitly set (not left as default `false` unless intended)
- [ ] `stateSecret` is at least 32 characters, sourced from environment or secrets manager
- [ ] JWT validation is configured with `ValidateIssuer`, `ValidateLifetime`, `ValidateAudience`
- [ ] `SsrfGuard` blocks internal addresses (default) unless explicitly overridden
- [ ] `MaxTools` is set to a reasonable limit for your API surface
- [ ] Token store directory permissions are restricted
- [ ] HTTPS is enforced for the MCP endpoint and upstream API calls