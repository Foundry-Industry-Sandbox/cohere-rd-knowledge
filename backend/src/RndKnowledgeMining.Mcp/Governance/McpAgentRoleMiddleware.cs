using System.Text.Json;
using Microsoft.Extensions.Options;
using RndKnowledgeMining.Governance;

namespace RndKnowledgeMining.Mcp.Governance;

public static class McpGovernanceHttpContextKeys
{
    public const string AgentRole = "McpGovernance.AgentRole";
}

public sealed class McpAgentRoleMiddleware
{
    private readonly RequestDelegate _next;

    public McpAgentRoleMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        GovernanceSettings settings = context.RequestServices
            .GetRequiredService<IOptions<GovernanceSettings>>()
            .Value;

        if (!settings.EnableMcpToolGovernance || !AgentCatalog.IsMcpRequest(context.Request.Path.Value))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        string? headerValue = context.Request.Headers[AgentCatalog.AgentRoleHeaderName].FirstOrDefault();
        if (!AgentCatalog.TryResolveRoleFromHeader(headerValue, out AgentRole role))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                    JsonSerializer.Serialize(new { error = $"Missing or unknown {AgentCatalog.AgentRoleHeaderName} header." }),
                    context.RequestAborted)
                .ConfigureAwait(false);
            return;
        }

        if (!AgentCatalog.IsRoleAllowedOnPath(role, context.Request.Path.Value))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                    JsonSerializer.Serialize(new
                    {
                        error = $"Agent '{AgentCatalog.ToFolderName(role)}' is not allowed on MCP route '{context.Request.Path.Value}'."
                    }),
                    context.RequestAborted)
                .ConfigureAwait(false);
            return;
        }

        context.Items[McpGovernanceHttpContextKeys.AgentRole] = role;

        await _next(context).ConfigureAwait(false);
    }
}
