using CohereRndKnowledgeMining.Api.Host.Contracts;
using CohereRndKnowledgeMining.Api.Host.Services;
using Microsoft.AspNetCore.Mvc;

namespace CohereRndKnowledgeMining.Api.Host.Controllers;

[ApiController]
[Route("api/rd-knowledge/cases")]
public sealed class CasesController : ControllerBase
{
    private readonly IngestionSourceDocumentLoader _documentLoader;
    private readonly ILogger<CasesController> _logger;

    public CasesController(
        IngestionSourceDocumentLoader documentLoader,
        ILogger<CasesController> logger)
    {
        _documentLoader = documentLoader;
        _logger = logger;
    }

    [HttpGet("{caseId}/documents")]
    public async Task<ActionResult<StudyDocumentsResponse>> GetStudyDocuments(
        string caseId,
        CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<RawKnowledgeItem> items =
                await _documentLoader.LoadAsync(caseId.Trim(), cancellationToken).ConfigureAwait(false);

            var sources = items.Select(item => new KnowledgeSourceResponse
            {
                SourceId = item.ItemId,
                Title = item.Title,
                SourceType = item.SourceType,
                Format = InferFormat(item),
                Summary = item.Content.Length > 200
                    ? item.Content[..200] + "..."
                    : item.Content
            }).ToList();

            return Ok(new StudyDocumentsResponse
            {
                StudyId = caseId,
                Sources = sources
            });
        }
        catch (DirectoryNotFoundException ex)
        {
            return NotFound(new ProblemDetailsResponse
            {
                Title = "Study source not found.",
                Detail = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load documents for case {CaseId}.", caseId);

            return Problem(
                detail: ex.Message,
                title: "Failed to load study documents.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static string InferFormat(RawKnowledgeItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.SourcePath))
        {
            string ext = Path.GetExtension(item.SourcePath).ToLowerInvariant();
            return ext switch
            {
                ".xml" or ".jats" => "JATS XML",
                ".json" => "JSON",
                ".csv" => "CSV",
                ".tsv" => "TSV",
                ".txt" or ".md" => "Plain text",
                ".pdf" => "PDF",
                ".xlsx" or ".xls" => "Excel",
                ".docx" or ".doc" => "Word",
                _ => item.SourceType
            };
        }

        return item.SourceType;
    }
}
