using CohereRndKnowledgeMining.Api.Host.Contracts;
using CohereRndKnowledgeMining.Api.Host.Services;
using Microsoft.AspNetCore.Mvc;

namespace CohereRndKnowledgeMining.Api.Host.Controllers;

/// <summary>
/// Block 1 (Ingestion): start the Fabric -> agents -> Knowledge Curator gate workflow,
/// check status, and resume after the curator approves or denies the ingestion run.
/// </summary>
[ApiController]
[Route("api/rd-knowledge/ingestion")]
public sealed class IngestionController : ControllerBase
{
    private readonly IngestionWorkflowService _ingestionService;
    private readonly ILogger<IngestionController> _logger;

    public IngestionController(
        IngestionWorkflowService ingestionService,
        ILogger<IngestionController> logger)
    {
        _ingestionService = ingestionService;
        _logger = logger;
    }

    [HttpPost("start")]
    public async Task<ActionResult<IngestionWorkflowStatusResponse>> StartAsync(
        [FromBody] StartIngestionRequest request,
        CancellationToken cancellationToken)
    {
        string executionId = string.IsNullOrWhiteSpace(request.ExecutionId)
            ? Guid.NewGuid().ToString("N")
            : request.ExecutionId.Trim();

        try
        {
            _logger.LogInformation(
                "Starting ingestion workflow for source {SourceId} with execution {ExecutionId}.",
                request.SourceId,
                executionId);

            IngestionWorkflowStatusResponse response = await _ingestionService.StartIngestionAsync(
                request.SourceId,
                executionId,
                cancellationToken);

            return Ok(response);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetailsResponse
            {
                Title = "Raw knowledge source not found.",
                Detail = ex.Message
            });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ProblemDetailsResponse
            {
                Title = "Ingestion workflow cannot be started.",
                Detail = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to start ingestion workflow for source {SourceId} with execution {ExecutionId}.",
                request.SourceId,
                executionId);

            return Problem(
                detail: ex.Message,
                title: "Ingestion workflow failed to start.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    [HttpGet("executions/{executionId}/status")]
    public ActionResult<IngestionWorkflowStatusResponse> GetStatus(string executionId)
    {
        try
        {
            return Ok(_ingestionService.GetIngestionStatus(executionId));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetailsResponse
            {
                Title = "Ingestion execution not found.",
                Detail = ex.Message
            });
        }
    }

    [HttpPost("sources/{sourceId}/executions/{executionId}/resume")]
    public ActionResult<IngestionWorkflowStatusResponse> Resume(
        string sourceId,
        string executionId,
        [FromBody] WorkflowApprovalRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            IngestionWorkflowStatusResponse response = _ingestionService.ResumeIngestionAsync(
                sourceId,
                executionId,
                request.Approved,
                request.ReviewerComment,
                cancellationToken);

            return Ok(response);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetailsResponse
            {
                Title = "Ingestion execution not found.",
                Detail = ex.Message
            });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ProblemDetailsResponse
            {
                Title = "Ingestion workflow cannot be resumed.",
                Detail = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to resume ingestion workflow for source {SourceId} with execution {ExecutionId}.",
                sourceId,
                executionId);

            return Problem(
                detail: ex.Message,
                title: "Ingestion workflow failed to resume.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
