using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using RndKnowledgeMining.Mcp.Models;

namespace RndKnowledgeMining.Mcp.Adapters;

public sealed partial class PolicyParser
{
    public IReadOnlyList<PolicyEntry> Parse(string policyText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyText);

        var normalizedText = policyText.Replace("\r\n", "\n", StringComparison.Ordinal);
        var blocks = normalizedText
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(block => block.StartsWith("Policy Ref:", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var entries = new List<PolicyEntry>(blocks.Length);

        foreach (var block in blocks)
        {
            var lines = block.Split('\n', StringSplitOptions.TrimEntries);
            var policyRef = lines[0].Replace("Policy Ref:", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();

            entries.Add(new PolicyEntry
            {
                PolicyRef = policyRef,
                Rule = ExtractValue(lines, "Rule:"),
                Threshold = ExtractValue(lines, "Threshold:"),
                Action = ExtractValue(lines, "Action:"),
                Exception = ExtractValue(lines, "Exception:"),
                FullText = block.Trim()
            });
        }

        return entries;
    }

    public static string ComputeContentHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes);
    }

    private static string ExtractValue(IEnumerable<string> lines, string prefix)
    {
        var line = lines.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return line is null ? string.Empty : line[prefix.Length..].Trim();
    }

    [GeneratedRegex(@"Policy Ref:\s*([A-Z0-9\-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex PolicyRefPattern();
}
