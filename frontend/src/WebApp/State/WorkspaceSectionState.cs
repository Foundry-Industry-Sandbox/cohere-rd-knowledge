namespace Cohere.AgenticRDKnowledge.WebApp.State;

public sealed class WorkspaceSectionState
{
    private readonly HashSet<string> _expanded = new(StringComparer.OrdinalIgnoreCase);
    private string? _currentResourceId;

    public bool IsExpanded(string sectionId) => _expanded.Contains(sectionId);

    public void Toggle(string sectionId)
    {
        if (_expanded.Contains(sectionId))
        {
            _expanded.Remove(sectionId);
        }
        else
        {
            _expanded.Add(sectionId);
        }
    }

    public void SetExpanded(string sectionId, bool expanded)
    {
        if (expanded)
        {
            _expanded.Add(sectionId);
        }
        else
        {
            _expanded.Remove(sectionId);
        }
    }

    public void ResetForResource(string resourceId, params ReadOnlySpan<string> defaultExpanded)
    {
        if (_currentResourceId != resourceId)
        {
            _expanded.Clear();
            _currentResourceId = resourceId;
            foreach (var sectionId in defaultExpanded)
            {
                _expanded.Add(sectionId);
            }
        }
    }
}

public static class WorkspaceSections
{
    public const string Overview = "overview";
    public const string Workflow = "workflow";
    public const string StageIngestionTranslation = "stage-ingestion-translation";
    public const string StageMetadataLinking = "stage-metadata-linking";
    public const string StageIngestionHitl = "stage-ingestion-hitl";
    public const string StageSearchChat = "stage-search-chat";
    public const string StageCurationCompliance = "stage-curation-compliance";
    public const string StageQueryHitl = "stage-query-hitl";
    public const string SidebarStudySummary = "sidebar-study-summary";
    public const string SidebarFabricSources = "sidebar-fabric-sources";
    public const string SidebarWorkflowProgress = "sidebar-workflow-progress";
}
