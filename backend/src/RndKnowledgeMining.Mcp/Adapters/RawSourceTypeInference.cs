namespace RndKnowledgeMining.Mcp.Adapters;

internal static class RawSourceTypeInference
{
    public static string InferSourceType(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "article";
        }

        var name = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName).ToLowerInvariant();

        if (name.StartsWith("PMC", StringComparison.OrdinalIgnoreCase) || ext is ".xml")
        {
            return "article";
        }

        if (name.StartsWith("eln_", StringComparison.OrdinalIgnoreCase))
        {
            return "eln_lims";
        }

        if (name.StartsWith("lims_", StringComparison.OrdinalIgnoreCase))
        {
            return ext is ".csv" ? "dataset" : "protocol";
        }

        if (name.StartsWith("CUR-EXCLUDE-", StringComparison.OrdinalIgnoreCase))
        {
            return "submission";
        }

        return ext switch
        {
            ".pdf" or ".html" or ".htm" => "article",
            ".csv" => "dataset",
            ".txt" => "protocol",
            _ => "article"
        };
    }
}
