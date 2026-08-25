using CohereRndKnowledgeMining.Api.Host.Contracts;
using Microsoft.AspNetCore.Mvc;
using RndKnowledgeMining.Mcp.Adapters;

namespace CohereRndKnowledgeMining.Api.Host.Controllers;

[ApiController]
[Route("api/rd-knowledge")]
public sealed class VectorDbController : ControllerBase
{
    private readonly KnowledgeIndexAdapter _indexAdapter;
    private readonly ILogger<VectorDbController> _logger;

    public VectorDbController(
        KnowledgeIndexAdapter indexAdapter,
        ILogger<VectorDbController> logger)
    {
        _indexAdapter = indexAdapter;
        _logger = logger;
    }

    [HttpGet("vector-db/summary")]
    public async Task<ActionResult<VectorDbStoreSummaryResponse>> GetSummary(CancellationToken cancellationToken)
    {
        var summary = await _indexAdapter.GetSummaryAsync(cancellationToken);

        return Ok(new VectorDbStoreSummaryResponse
        {
            TotalStudies = (int)summary.TotalStudies,
            TotalDocuments = (int)summary.TotalDocuments,
            TotalEntities = (int)summary.TotalEntities,
            TotalLinks = (int)summary.TotalLinks,
            LastIngestionAt = summary.LastIngestionAt,
            LastIngestedStudyId = summary.LastIngestedStudyId
        });
    }

    [HttpPost("vector-db/clear-data")]
    public async Task<ActionResult<VectorDbClearDataResponse>> ClearData(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Clearing all documents from the rd-knowledge-evidence index.");

            long deletedCount = await _indexAdapter
                .ClearAllAsync(cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Cleared {DeletedCount} documents from the rd-knowledge-evidence index.",
                deletedCount);

            return Ok(new VectorDbClearDataResponse
            {
                DeletedCount = deletedCount
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear the rd-knowledge-evidence index.");

            return Problem(
                detail: ex.Message,
                title: "Failed to clear Vector DB data.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
