namespace RndKnowledgeMining.Governance;

public static class FoundryAgentGovernancePolicyPaths
{
    public const string PoliciesRootDirectoryName = "policies";
    public const string GovernanceFileName = "governance.yaml";
    public const string RogueFileName = "rogue.yaml";

    public static string ResolvePoliciesRoot(string? baseDirectory = null)
    {
        string root = string.IsNullOrWhiteSpace(baseDirectory)
            ? AppContext.BaseDirectory
            : baseDirectory;

        return Path.Combine(root, PoliciesRootDirectoryName);
    }

    public static string ResolveGovernancePolicyPath(AgentRole role, string? baseDirectory = null) =>
        Path.Combine(
            ResolvePoliciesRoot(baseDirectory),
            AgentCatalog.ToFolderName(role),
            GovernanceFileName);

    public static string ResolveRoguePolicyPath(AgentRole role, string? baseDirectory = null) =>
        Path.Combine(
            ResolvePoliciesRoot(baseDirectory),
            AgentCatalog.ToFolderName(role),
            RogueFileName);

    public static void EnsurePolicyFilesExist(AgentRole role, string? baseDirectory = null)
    {
        string governancePath = ResolveGovernancePolicyPath(role, baseDirectory);
        if (!File.Exists(governancePath))
        {
            throw new FileNotFoundException(
                $"Governance policy file was not found for role '{role}' at '{governancePath}'.",
                governancePath);
        }

        string roguePath = ResolveRoguePolicyPath(role, baseDirectory);
        if (!File.Exists(roguePath))
        {
            throw new FileNotFoundException(
                $"Rogue policy file was not found for role '{role}' at '{roguePath}'.",
                roguePath);
        }
    }
}
