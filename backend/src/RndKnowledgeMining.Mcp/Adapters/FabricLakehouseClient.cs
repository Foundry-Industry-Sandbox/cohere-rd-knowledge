using Azure;
using Azure.Identity;
using Azure.Storage.Files.DataLake;
using Microsoft.Extensions.Logging;
using RndKnowledgeMining.Mcp.Options;

namespace RndKnowledgeMining.Mcp.Adapters;

/// <summary>
/// OneLake access: account = onelake, filesystem = workspace,
/// paths = {lakehouse}.lakehouse/Files/...
/// </summary>
public sealed class FabricLakehouseClient : IDisposable
{
    private const string FabricOneLakeEndpoint = "https://onelake.dfs.fabric.microsoft.com";
    private const string LakehouseSuffix = ".lakehouse";

    private readonly DataLakeFileSystemClient _workspaceClient;
    private readonly string _lakehouseItemName;
    private readonly int _timeoutSeconds;
    private readonly ILogger<FabricLakehouseClient> _logger;

    private FabricLakehouseClient(
        DataLakeFileSystemClient workspaceClient,
        string lakehouseItemName,
        int timeoutSeconds,
        ILogger<FabricLakehouseClient> logger)
    {
        _workspaceClient = workspaceClient;
        _lakehouseItemName = lakehouseItemName;
        _timeoutSeconds = timeoutSeconds;
        _logger = logger;
    }

    public static FabricLakehouseClient Create(FabricLakehouseOptions options, ILogger<FabricLakehouseClient> logger)
    {
        if (string.IsNullOrWhiteSpace(options.WorkspaceName) || string.IsNullOrWhiteSpace(options.LakehouseName))
        {
            throw new InvalidOperationException(
                "FabricLakehouse:WorkspaceName and FabricLakehouse:LakehouseName are required.");
        }

        var credential = new DefaultAzureCredential();
        var serviceClient = new DataLakeServiceClient(new Uri(FabricOneLakeEndpoint), credential);
        var workspaceClient = serviceClient.GetFileSystemClient(options.WorkspaceName.Trim());
        var lakehouseName = options.LakehouseName.Trim().TrimEnd('/');
        var lakehouseItemName = lakehouseName.EndsWith(LakehouseSuffix, StringComparison.OrdinalIgnoreCase)
            ? lakehouseName
            : lakehouseName + LakehouseSuffix;

        logger.LogInformation(
            "FabricLakehouseClient initialized against workspace {WorkspaceName} item {LakehouseItem}.",
            options.WorkspaceName,
            lakehouseItemName);

        return new FabricLakehouseClient(workspaceClient, lakehouseItemName, options.TimeoutSeconds, logger);
    }

    public async Task<bool> FileExistsAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_timeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var (directory, fileName) = SplitPath(ToLakehousePath(relativePath));
        var fileClient = _workspaceClient.GetDirectoryClient(directory).GetFileClient(fileName);
        try
        {
            var response = await fileClient.ExistsAsync(linkedCts.Token).ConfigureAwait(false);
            return response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
    }

    public async Task<string> ReadFileAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_timeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var (directory, fileName) = SplitPath(ToLakehousePath(relativePath));
        var fileClient = _workspaceClient.GetDirectoryClient(directory).GetFileClient(fileName);

        try
        {
            var download = await fileClient.ReadAsync(linkedCts.Token).ConfigureAwait(false);
            using var reader = new StreamReader(download.Value.Content);
            return await reader.ReadToEndAsync(linkedCts.Token).ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new FileNotFoundException(
                $"Fabric file '{relativePath}' was not found in lakehouse '{_lakehouseItemName}'.",
                ex);
        }
    }

    public async Task<IReadOnlyList<string>> ListFilesAsync(string relativeDirectory, CancellationToken cancellationToken = default)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_timeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var lakehouseDirectory = ToLakehousePath(relativeDirectory);
        var results = new List<string>();

        // Non-recursive under the target folder — return file names only.
        await foreach (var pathItem in _workspaceClient
            .GetPathsAsync(lakehouseDirectory, recursive: false, userPrincipalName: false, linkedCts.Token)
            .WithCancellation(linkedCts.Token)
            .ConfigureAwait(false))
        {
            if (pathItem.IsDirectory == true)
            {
                continue;
            }

            var fileName = Path.GetFileName(pathItem.Name.TrimEnd('/').Replace('\\', '/'));
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                results.Add(fileName);
            }
        }

        _logger.LogInformation(
            "Listed {FileCount} file(s) under {LakehouseDirectory}.",
            results.Count,
            lakehouseDirectory);

        return results;
    }

    public async Task UploadFileAsync(string relativePath, string content, CancellationToken cancellationToken = default)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        await UploadFileAsync(relativePath, bytes, cancellationToken).ConfigureAwait(false);
    }

    public async Task UploadFileAsync(string relativePath, byte[] bytes, CancellationToken cancellationToken = default)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_timeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var (directory, fileName) = SplitPath(ToLakehousePath(relativePath));
        var fileClient = _workspaceClient.GetDirectoryClient(directory).GetFileClient(fileName);

        await using var stream = new MemoryStream(bytes);
        await fileClient.UploadAsync(stream, overwrite: true, cancellationToken: linkedCts.Token).ConfigureAwait(false);
    }

    private string ToLakehousePath(string evidenceRelativePath) =>
        $"{_lakehouseItemName}/{evidenceRelativePath.Trim().Trim('/')}";

    private static (string Directory, string FileName) SplitPath(string relativePath)
    {
        var trimmed = relativePath.Trim('/');
        var lastSlash = trimmed.LastIndexOf('/');
        if (lastSlash <= 0)
        {
            throw new ArgumentException(
                $"Path '{relativePath}' must include a directory and a file name.",
                nameof(relativePath));
        }

        return (trimmed[..lastSlash], trimmed[(lastSlash + 1)..]);
    }

    public void Dispose()
    {
    }
}
