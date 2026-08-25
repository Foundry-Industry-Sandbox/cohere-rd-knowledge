using System.Text.RegularExpressions;

namespace RndKnowledgeMining.Mcp.Adapters;

internal static class NormalizedExecutionIdResolver
{
    private static readonly Regex Hex32Pattern = new("^[0-9a-f]{32}$", RegexOptions.Compiled);

    public static string Normalize(string executionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);
        return executionId.Trim().Replace("-", string.Empty).ToLowerInvariant();
    }

    public static bool IsValidFormat(string executionId)
    {
        if (string.IsNullOrWhiteSpace(executionId))
        {
            return false;
        }

        return Hex32Pattern.IsMatch(Normalize(executionId));
    }

    /// <summary>
    /// Finds an available execution folder when the agent passes a mistyped id (common LLM hex typo).
    /// </summary>
    public static string? FindClosestMatch(string requestedExecutionId, IReadOnlyList<string> availableExecutionIds)
    {
        if (availableExecutionIds.Count == 0)
        {
            return null;
        }

        string normalizedRequested = Normalize(requestedExecutionId);

        string? exact = availableExecutionIds
            .FirstOrDefault(candidate =>
                string.Equals(Normalize(candidate), normalizedRequested, StringComparison.Ordinal));

        if (exact is not null)
        {
            return exact;
        }

        return availableExecutionIds
            .Select(candidate => new { Candidate = candidate, Distance = LevenshteinDistance(normalizedRequested, Normalize(candidate)) })
            .Where(match => match.Distance is > 0 and <= 2)
            .OrderBy(match => match.Distance)
            .ThenByDescending(match => match.Candidate, StringComparer.Ordinal)
            .Select(match => match.Candidate)
            .FirstOrDefault();
    }

    public static bool TryParseExecutionIdFromBlobPath(
        string blobPath,
        string normalizedPrefix,
        string sourceId,
        out string executionId)
    {
        executionId = string.Empty;

        string expectedPrefix = $"{normalizedPrefix.Trim('/')}/{sourceId.Trim()}/";
        if (!blobPath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string remainder = blobPath[expectedPrefix.Length..];
        int slashIndex = remainder.IndexOf('/');
        if (slashIndex <= 0)
        {
            return false;
        }

        string candidate = remainder[..slashIndex];
        if (!IsValidFormat(candidate))
        {
            return false;
        }

        executionId = Normalize(candidate);
        return true;
    }

    private static int LevenshteinDistance(string left, string right)
    {
        if (left.Length == 0)
        {
            return right.Length;
        }

        if (right.Length == 0)
        {
            return left.Length;
        }

        int[] previous = new int[right.Length + 1];
        int[] current = new int[right.Length + 1];

        for (int index = 0; index <= right.Length; index++)
        {
            previous[index] = index;
        }

        for (int leftIndex = 1; leftIndex <= left.Length; leftIndex++)
        {
            current[0] = leftIndex;
            for (int rightIndex = 1; rightIndex <= right.Length; rightIndex++)
            {
                int substitutionCost = left[leftIndex - 1] == right[rightIndex - 1] ? 0 : 1;
                current[rightIndex] = Math.Min(
                    Math.Min(current[rightIndex - 1] + 1, previous[rightIndex] + 1),
                    previous[rightIndex - 1] + substitutionCost);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }
}
