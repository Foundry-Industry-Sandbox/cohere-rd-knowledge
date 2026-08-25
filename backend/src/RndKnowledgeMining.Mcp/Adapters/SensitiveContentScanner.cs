using System.Text.RegularExpressions;
using RndKnowledgeMining.Mcp.Models;

namespace RndKnowledgeMining.Mcp.Adapters;

public sealed partial class SensitiveContentScanner
{
    private static readonly (string Flag, Regex Pattern)[] Rules =
    [
        ("phi_ssn_pattern", SsnRegex()),
        ("phi_email_pattern", EmailRegex()),
        ("phi_phone_pattern", PhoneRegex()),
        ("phi_mrn_pattern", MrnRegex()),
        ("pii_patient_name_pattern", PatientNameRegex()),
        ("confidential_partner_material", ConfidentialPartnerRegex()),
        ("restricted_regulatory_draft", RestrictedRegulatoryRegex())
    ];

    public FlagSensitiveContentResponse Scan(string sessionId, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var flags = new List<string>();
        var matchedPatterns = new List<string>();

        foreach ((string flag, Regex pattern) in Rules)
        {
            if (!pattern.IsMatch(text))
            {
                continue;
            }

            flags.Add(flag);
            matchedPatterns.Add(pattern.ToString());
        }

        return new FlagSensitiveContentResponse
        {
            SessionId = sessionId.Trim(),
            SensitiveContentFound = flags.Count > 0,
            Flags = flags,
            MatchedPatterns = matchedPatterns
        };
    }

    [GeneratedRegex(@"\b\d{3}-\d{2}-\d{4}\b")]
    private static partial Regex SsnRegex();

    [GeneratedRegex(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"\b(?:\+?1[-.\s]?)?\(?\d{3}\)?[-.\s]?\d{3}[-.\s]?\d{4}\b")]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"\bMRN[:\s#-]*\d{6,12}\b", RegexOptions.IgnoreCase)]
    private static partial Regex MrnRegex();

    [GeneratedRegex(@"\bpatient name[:\s]+[A-Z][a-z]+(?:\s+[A-Z][a-z]+)+\b", RegexOptions.IgnoreCase)]
    private static partial Regex PatientNameRegex();

    [GeneratedRegex(@"\bconfidential partner (?:repo|repository|data|material)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ConfidentialPartnerRegex();

    [GeneratedRegex(@"\b(?:draft|embargoed|unreleased)\s+(?:label|regulatory)\s+(?:text|submission)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RestrictedRegulatoryRegex();
}
