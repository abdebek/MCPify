# Changelog

All notable changes to MCPify are documented here.
This project follows [Semantic Versioning](https://semver.org/) with a `*-preview` suffix while on the preview line.

## [0.0.16-preview] — 2026-08-08

### Changed

- **MCP SDK upgraded to 2.1.0** (from 2.0.0). Includes HTTP transport reliability fixes and `subscriptions/listen` support from the official C# SDK.
- **HTTP transport is explicitly configured** via `WithHttpTransport`. SDK 2.x defaults to **stateless** Streamable HTTP (`HttpServerTransportOptions.Stateless = true`), which is the forward-compatible path for the [2026-07-28 MCP revision](https://modelcontextprotocol.io/specification/2026-07-28).

### Added

- **`McpifyOptions.HttpStateless`** — optional override for SDK stateless mode. Set `false` only when you need transport-level sessions (`McpServer.SessionId`, legacy session affinity).
- **`McpifyOptions.ConfigureHttpTransport`** — hook for idle timeout, event stores, and other `HttpServerTransportOptions`.
- **`SessionIdResolver` is now wired** in `SessionAwareToolDecorator` (was documented but unused). Under stateless HTTP, resolve an app-level session handle from `HttpContext` (or `HttpContext.Items["McpSessionId"]`) so server-managed token storage still works without `Mcp-Session-Id`.

### Migration notes

1. No action required for most hosts: existing `WithHttpTransport()` behavior already followed the SDK default (stateless).
2. If your host depended on a non-null `McpServer.SessionId` for multi-turn ServerManaged OAuth, either set `HttpStateless = false` or supply a stable handle via `SessionIdResolver` / tool argument `sessionId`.
3. Prefer `UpstreamAuth.PassThrough()` on multi-user HTTP hosts; keep ServerManaged + encrypted token store for Stdio / single-user.

## [0.0.15-preview] — 2026-08-01

### Breaking Changes (since 0.0.14-preview)

- **Default token-store `providerName` is namespaced by client id** (`OAuth:{clientId}`, `ClientCredentials:{clientId}`, `DeviceCode:{clientId}`) when `providerName` is omitted. Explicit `providerName` is unchanged. Existing tokens stored under bare `"OAuth"` / `"ClientCredentials"` / `"DeviceCode"` will not match until re-login or setting the old name explicitly.

### GitHub issues addressed

- **#14** — `WWW-Authenticate` on 401 now includes `scope="..."` from configured OAuth scopes (RFC 6750 / MCP Authorization SHOULD).
- **#9** — `LoginBrowserBehavior` (Auto/Always/Never) plus `LoginFlow` (`AuthorizationCode` | `DeviceCode`) for remote/headless strategy selection.
- **#2** — `OAuthAuthorizationCodeAuthenticator` / `AddOAuthAuthorizationCodeAuthenticator` builds auth-code clients from options or OpenAPI OAuth2 security schemes.
- **#20** — Outbound auth uses `UpstreamAuth` + `ITokenProvider` (PassThrough / ServerManaged / None); no per-tool authentication factory required for client-managed tokens.


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