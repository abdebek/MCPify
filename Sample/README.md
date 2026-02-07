# MCPify Sample Application

This sample exposes local ASP.NET Core endpoints and OpenAPI specs as MCP tools, with OAuth login handled by MCPify.

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

- Enable only when you explicitly want it:
  - `Mcpify:AllowClientTokenPassthrough = true`
- This mode is intended for local/dev scenarios and is unsafe for hosted multi-user deployments.

## Dynamic Client Registration

Some MCP clients (including ChatGPT) require OAuth dynamic client registration.
The sample provides:

- Discovery document with `registration_endpoint`
- Registration endpoint at `POST https://localhost:5001/connect/register`
- Confidential client registration (`client_secret_basic`) with PKCE

## Claude Desktop (Stdio)

For local desktop use, publish and run with Stdio:

```bash
dotnet publish Sample/MCPify.Sample.csproj -c Release -f net10.0
```

Example Claude config:

```json
{
  "mcpServers": {
    "mcpify-sample": {
      "command": "dotnet",
      "args": [
        "<abs-path>/Sample/bin/Release/net10.0/publish/MCPify.Sample.dll"
      ]
    }
  }
}
```

## Troubleshooting

- If MCP initialization hangs in Stdio mode, do not use `dotnet run` in the MCP client command.
- For `api_get_api_users_id`, always send `id`.
- If auth is required, complete login and retry.
