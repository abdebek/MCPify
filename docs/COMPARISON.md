# MCPify vs Alternatives — Honest Comparison

## vs Raw MCP SDK (ModelContextProtocol.AspNetCore)

| Aspect | Raw SDK | MCPify |
|--------|---------|--------|
| OpenAPI → tools | Manual: write each `McpServerTool` by hand | Automatic: pass a URL or file path |
| Auth composition | Build your own challenge/PRM/JWT/scope wiring | Three-concern composition pre-wired |
| Outbound OAuth | Implement token store, PKCE, refresh, exchange yourself | Built-in with `UpstreamAuth` |
| Tool curation | Manual filtering, no caps, no allow/deny | `ToolFilter`, `MaxTools`, per-API limits |
| Local endpoints | Write proxy tools for each endpoint | `LocalEndpoints.Enabled = true` |
| Custom tools | Full control, full boilerplate | `AddMcpifyTool(name, desc, handler)` |
| Pipeline hooks | Build from scratch | `IToolInvocationPolicy` (rate limit, audit, allowlist) |
| Metrics | None | `mcpify.tool_calls` + `mcpify.tool_duration_ms` |

**When to use raw SDK:** You want full control over the protocol surface, custom resource templates, or non-OpenAPI tool definitions.

**When to use MCPify:** You have existing OpenAPI/ASP.NET Core APIs and want to expose them as MCP tools with auth, curation, and observability without writing boilerplate.

## vs CLI converters (openapi-mcp-generator, etc.)

| Aspect | CLI converters | MCPify |
|--------|---------------|--------|
| Output | Static generated code at build time | Runtime: fetch + parse + register |
| Updates | Re-run the converter | Re-fetch at startup |
| Auth | Usually none or manual | Full OAuth/PKCE/exchange/passthrough |
| Local endpoints | Not supported | Yes, self-loopback proxy |
| Curation | Limited to template config | `ToolFilter`, `MaxTools`, predicates |
| Custom tools | Mix generated + manual | Single `AddMcpifyTool` |

**When to use CLI converters:** You want static, auditable generated code with no runtime OpenAPI fetching.

**When to use MCPify:** You want runtime flexibility (spec changes reflected on restart), auth integration, and a mix of OpenAPI + local + custom tools.

## vs FastMCP (Python ecosystem)

| Aspect | FastMCP (Python) | MCPify (.NET) |
|--------|------------------|---------------|
| Language | Python | C# / .NET |
| OpenAPI import | Limited | First-class with 3.1 support |
| Auth | Basic | OAuth + PKCE + exchange + passthrough |
| Hosting | Python ASGI | ASP.NET Core (HTTP + Stdio) |
| Enterprise | Limited | ASP.NET Core ecosystem (Entra, OpenIddict, IdSrv) |

**When to use FastMCP:** Your stack is Python.

**When to use MCPify:** Your stack is .NET and you want to leverage ASP.NET Core auth, middleware, and the NuGet ecosystem.

## What MCPify Does Not Do

- **No JWT validation** — the host owns this via `AddJwtBearer`
- **No resource templates** — all operations are tools (documented rationale)
- **No protocol reimplementation** — rides the official MCP SDK
- **No generated static code** — tools are built at runtime from OpenAPI specs