# MCPify Sample Application

A reference implementation showing correct MCPify integration: local endpoints, external OpenAPI import, OAuth login, and both HTTP/Stdio transports.

## Architecture

The Sample demonstrates the three-concern auth composition:

1. **MCP Challenge & PRM** — `McpAuthenticationHandler` (from SDK) issues `WWW-Authenticate` challenges pointing to `/.well-known/oauth-protected-resource`. Registered as `DefaultChallengeScheme` via `PostConfigure`.

2. **Inbound token validation** — OpenIddict validates access tokens. Registered as `DefaultAuthenticateScheme` so the MCP route's `RequireAuthorization()` validates tokens through OpenIddict, not the MCP handler.

3. **Per-tool scope enforcement** — `ScopeRequirement` metadata on protected tools, enforced by `SessionAwareToolDecorator` via `IAuthorizationService` (fail-closed).

The `PostConfigure<AuthenticationOptions>` in `DemoServiceExtensions.cs` sets this up:
- `DefaultAuthenticateScheme = OpenIddict` — validates inbound tokens
- `DefaultChallengeScheme = McpAuth` — issues MCP-protocol challenges

This is the recommended pattern for production hosts with a real IdP.

## What You Get

- Local tools from app endpoints (prefixed with `api_`)
- External tools from Petstore OpenAPI (`petstore_`)
- External tools from local OpenAPI file (`localfile_`)
- OAuth metadata endpoint for MCP auth discovery
- OpenID discovery + dynamic client registration for MCP clients that require DCR
- HTTP and Stdio transport support

## Prerequisites

- .NET 8, 9, or 10 SDK

## Out-of-Box Run (HTTP)

The Development profile runs on `https://localhost:5001` and uses HTTP transport.

```bash
dotnet run --project Sample/MCPify.Sample.csproj --framework net10.0
```

Key endpoints:

- MCP endpoint: `https://localhost:5001/mcp`
- OAuth metadata: `https://localhost:5001/.well-known/oauth-protected-resource`
- OpenID discovery: `https://localhost:5001/.well-known/openid-configuration`
- Health endpoint: `https://localhost:5001/status`

## Tools You Should See

Local tools include:

- `api_get_api_users_id` (requires `id` argument)
- `api_get_api_secrets` (requires auth)
- `api_get_status`
- `api_get_weather`

Example call payload for user lookup:

```json
{
  "id": 1
}
```

## Login Flow

For protected tools like `api_get_api_secrets`:

1. On HTTP transport, MCPify uses server-managed upstream auth by default.
2. If auth is missing, use the login tool or follow the challenge flow.
3. Open the provided authorization URL in browser.
4. Complete consent.
5. MCPify callback stores token server-side.
6. Retry the protected tool call.

Note: the sample enables session fallback for server-managed auth so clients that rotate MCP session IDs between calls still work after login.

## Pass-Through Opt-In (HTTP)

Client token pass-through on HTTP is disabled by default.
On Stdio transport, MCPify tries pass-through first and falls back to server-managed auth for local workflows.
In this sample code, `UpstreamAuth.Fallback(UpstreamAuth.PassThrough(), serverManagedAuth)` is used for Stdio by default, and only used for HTTP when the allow-flag is enabled.

- Enable only when you explicitly want it:
  - `Mcpify:AllowClientTokenPassthrough = true`
- This mode is intended for local/dev scenarios and is unsafe for hosted multi-user deployments.

## Dynamic Client Registration  (DCR - Optional)

- If your MCP client supports manual OAuth client configuration, you can use a pre-registered client and skip DCR.
- If your MCP client requires DCR (for example ChatGPT over HTTP), keep DCR enabled.

The sample includes DCR support out of the box:

- Discovery document with `registration_endpoint`
- Registration endpoint at `POST https://localhost:5001/connect/register`
- Confidential client registration (`client_secret_basic`) with PKCE

For manual registration, use:

- Authorization endpoint: `https://localhost:5001/connect/authorize`
- Token endpoint: `https://localhost:5001/connect/token`
- Redirect URI: `https://localhost:5001/auth/callback` (or your configured callback)

## Claude Desktop (Stdio)

The Sample supports Stdio transport for local desktop integration. In Stdio mode, logs are suppressed (stdout is reserved for MCP JSON-RPC).

### Option A: Published executable (recommended)

```bash
dotnet publish Sample/MCPify.Sample.csproj -c Release -f net10.0
```

Claude config (`~/Library/Application Support/Claude/claude_desktop_config.json` on macOS):

```json
{
  "mcpServers": {
    "mcpify-sample": {
      "command": "<abs-path>/Sample/bin/Release/net10.0/publish/MCPify.Sample.dll"
    }
  }
}
```

### Option B: dotnet run (debugging only)

```json
{
  "mcpServers": {
    "mcpify-sample": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "<abs-path>/Sample/MCPify.Sample.csproj",
        "--",
        "--Mcpify:Transport=Stdio"
      ]
    }
  }
}
```

> **Warning:** `dotnet run` prints build logs to stdout which corrupts the MCP protocol. Use `--debug` flag or publish the app instead.

### Stdio auth flow

In Stdio mode, the Sample uses `UpstreamAuth.Fallback(PassThrough(), ServerManaged())`:
1. If the MCP client provides a bearer token, it's forwarded to local endpoints.
2. If not, the `login_auth_code_pkce` tool initiates a browser-based OAuth flow.
3. The login tool opens the system browser to `https://localhost:5001/connect/authorize`.
4. After login, the callback stores the token and the tool call succeeds on retry.

## Troubleshooting

- If MCP initialization hangs in Stdio mode, do not use `dotnet run` in the MCP client command.
- For `api_get_api_users_id`, always send `id`.
- If auth is required, complete login and retry.
