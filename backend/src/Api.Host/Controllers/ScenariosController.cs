using CohereRndKnowledgeMining.Api.Host.Contracts;
using CohereRndKnowledgeMining.Api.Host.Services;
using Microsoft.AspNetCore.Mvc;

namespace CohereRndKnowledgeMining.Api.Host.Controllers;

[ApiController]
[Route("api/rd-knowledge/scenarios")]
public sealed class ScenariosController : ControllerBase
{
    private readonly PortfolioScenarioCatalogService _scenarioCatalogService;
    private readonly ILogger<ScenariosController> _logger;

    public ScenariosController(
        PortfolioScenarioCatalogService scenarioCatalogService,
        ILogger<ScenariosController> logger)
    {
        _scenarioCatalogService = scenarioCatalogService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PortfolioScenarioResponse>>> Get(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _scenarioCatalogService.LoadAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(new ProblemDetailsResponse
            {
                Title = "Scenario catalog not found.",
                Detail = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load portfolio scenarios.");
            return Problem(
                detail: ex.Message,
                title: "Failed to load scenarios.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
