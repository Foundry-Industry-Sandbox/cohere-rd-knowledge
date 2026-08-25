using CohereRndKnowledgeMining.Api.Host.Contracts;
using CohereRndKnowledgeMining.Api.Host.Services;
using Microsoft.AspNetCore.Mvc;

namespace CohereRndKnowledgeMining.Api.Host.Controllers;

/// <summary>
/// Block 2 (Query). Process 1: interactive Search &amp; Chat (no gate). Process 2: on-demand Curate
/// that runs the curation-compliance workflow over accumulated chat responses and pauses at the
/// Compliance Reviewer gate.
/// </summary>
[ApiController]
[Route("api/rd-knowledge/query")]
public sealed class QueryController : ControllerBase
{
    private readonly QueryWorkflowService _queryService;
    private readonly ILogger<QueryController> _logger;

    public QueryController(
        QueryWorkflowService queryService,
        ILogger<QueryController> logger)
    {
        _queryService = queryService;
        _logger = logger;
    }

    [HttpPost("ask")]
    public async Task<ActionResult<ChatAnswerResponse>> AskAsync(
        [FromBody] AskRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            ChatAnswerResponse response = await _queryService.AskAsync(
                request.SessionId,
                request.Question,
                cancellationToken);

            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetailsResponse
            {
                Title = "Search & Chat request is invalid.",
                Detail = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Search & Chat failed for session {SessionId}.", request.SessionId);

            return Problem(
                detail: ex.Message,
                title: "Search & Chat failed.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    [HttpPost("curate/start")]
    public ActionResult<CurateWorkflowStatusResponse> StartCurate([FromBody] StartCurateRequest request)
    {
        string executionId = string.IsNullOrWhiteSpace(request.ExecutionId)
            ? Guid.NewGuid().ToString("N")
            : request.ExecutionId.Trim();

        try
        {
            CurateWorkflowStatusResponse response = _queryService.StartCurate(request.SessionId, executionId);
            return Ok(response);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetailsResponse
            {
                Title = "Query session not found.",
                Detail = ex.Message
            });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ProblemDetailsResponse
            {
                Title = "Curate workflow cannot be started.",
                Detail = ex.Message
            });
        }
    }

    [HttpGet("curate/executions/{executionId}/status")]
    public ActionResult<CurateWorkflowStatusResponse> GetCurateStatus(string executionId)
    {
        try
        {
            return Ok(_queryService.GetCurateStatus(executionId));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetailsResponse
            {
                Title = "Curate execution not found.",
                Detail = ex.Message
            });
        }
    }

    [HttpPost("sessions/reset")]
    public ActionResult<QuerySessionsResetResponse> ResetSessions()
    {
        _logger.LogInformation("Resetting all query sessions and curate executions.");
        return Ok(_queryService.ResetSessions());
    }

    [HttpPost("curate/sessions/{sessionId}/executions/{executionId}/resume")]
    public ActionResult<CurateWorkflowStatusResponse> ResumeCurate(
        string sessionId,
        string executionId,
        [FromBody] WorkflowApprovalRequest request)
    {
        try
        {
            CurateWorkflowStatusResponse response = _queryService.ResumeCurate(
                sessionId,
                executionId,
                request.Approved,
                request.ReviewerComment);

            return Ok(response);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetailsResponse
            {
                Title = "Curate execution not found.",
                Detail = ex.Message
            });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ProblemDetailsResponse
            {
                Title = "Curate workflow cannot be resumed.",
                Detail = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to resume curate workflow for session {SessionId} with execution {ExecutionId}.",
                sessionId,
                executionId);

            return Problem(
                detail: ex.Message,
                title: "Curate workflow failed to resume.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
