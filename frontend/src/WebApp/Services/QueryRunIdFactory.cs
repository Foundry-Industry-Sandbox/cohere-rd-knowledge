namespace Cohere.AgenticRDKnowledge.WebApp.Services;

public static class QueryRunIdFactory
{
    public static string Create() => Guid.NewGuid().ToString("N");
}
