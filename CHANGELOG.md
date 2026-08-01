# Changelog

All notable changes to MCPify are documented here.
This project follows [Semantic Versioning](https://semver.org/) with a `*-preview` suffix while on the preview line.

## [0.0.15-preview] — 2026-08-01

### Breaking Changes (since 0.0.14-preview)

- **MCP SDK upgraded to 2.0.0** (from 0.6.0-preview.1). Streamable HTTP is now the only HTTP transport; SSE transport is removed.
- **Project SDK changed** from `Microsoft.NET.Sdk.Web` to `Microsoft.NET.Sdk` + `FrameworkReference Include="Microsoft.AspNetCore.App"`. Library no longer forces a web project.
- **`ITokenProvider` interface** changed from returning a token string to `ApplyAsync(HttpRequestMessage) -> Task<bool>`. Custom `ITokenProvider` implementations must be updated.
- **MCP route `RequireAuthorization`** no longer pins `AuthenticationSchemes = McpAuth`. The host's `DefaultAuthenticateScheme` (e.g. JwtBearer, OpenIddict) now validates tokens. If you relied on MCP being the sole auth scheme, set it as `DefaultAuthenticateScheme` explicitly.
- **`stateSecret` is now required** for OAuth flows. Passing `null` throws `ArgumentNullException`. Set via configuration or constructor.
- **`TokenValidationOptions`** removed from docs/examples. JWT validation is the host's responsibility via standard `AddJwtBearer`.
- **Store rekey on `id_token` `sub` removed.** Tokens are always stored under the session handle. `UpgradeSession` and `ExtractPrincipalFromIdToken` deleted.
- **OpenAPI readers pinned** to `1.6.29` (was `Version="*"`).
- **`TokenExchangeAuthentication`** now throws on non-success responses instead of silently returning null.
- **`EncryptedFileTokenStore`** no longer falls back to an ephemeral key. Throws if the key source is unavailable.

### Added

- **Tool curation:** global `MaxTools` cap (default 100) and per-API `MaxTools` on `ExternalApiOptions` / `LocalEndpointsOptions`.
- **`ToolFilter`:** declarative allow/deny lists by path prefix, HTTP method, tag, operationId, and `ExcludeDeprecated`.
- **`AddAuthorizationFilters()`** on `IMcpServerBuilder` — required by SDK 2.0 for tools with `ScopeRequirement` metadata.
- **SSRF guard** for OpenAPI URL fetching (`SsrfGuard` class with `AllowPrivateAddresses`, `BlockedHosts`).
- **`IHttpClientFactory`** used for all outbound HTTP (OpenAPI fetch, TokenExchange, Sample OAuth).
- **Input schema quality:** `additionalProperties: false`, nullable → type array, default values, `oneOf`/`anyOf`/`allOf`, `minItems`/`maxItems`, `x-parameter-location`, body content-type.
- **Agent-first tool descriptions:** prefers `description` > `summary` > structured fallback with method + tags.
- **Playwright smoke tests:** OAuth login + MCP tool call, protected resource metadata, unauthorized 401.
- **`AddDemoDatabaseAndAuth`** now accepts optional `dbName` parameter for test isolation.

### Fixed

- No host default authentication scheme hijack — `AddMcpifyAuthentication` no longer sets `DefaultScheme`, `DefaultAuthenticateScheme`, or `DefaultChallengeScheme`.
- PKCE defaults to `true` with S256.
- Log leak: OAuth state/signature no longer included in exception messages.
- Duplicate `/.well-known/oauth-protected-resource` endpoint removed.
- Single registration path: `MapMcpifyEndpoint` now registers both local and external tools.
- Scope enforcement is fail-closed when `IAuthorizationService` is unavailable.
- No-op auth provider detection in `AuthenticationFactoryTokenProvider` (snapshots headers/URI).

### Migration from 0.0.14-preview

1. Update `ITokenProvider` implementations to use `ApplyAsync(HttpRequestMessage)` instead of returning a token string.
2. Add `stateSecret` (≥32 chars) to your OAuth configuration.
3. If using JWT validation, ensure `AddJwtBearer` is configured in your host. Remove any `TokenValidationOptions` references.
4. If you relied on MCP auth scheme being the default, set `DefaultAuthenticateScheme = McpAuth` explicitly or configure your own.
5. Pin OpenAPI readers to `1.6.29` if you were using `Version="*"`.