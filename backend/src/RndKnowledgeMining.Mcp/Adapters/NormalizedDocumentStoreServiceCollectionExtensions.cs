using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RndKnowledgeMining.Mcp.Options;

namespace RndKnowledgeMining.Mcp.Adapters;

public static class NormalizedDocumentStoreServiceCollectionExtensions
{
    public static IServiceCollection AddNormalizedDocumentStore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<DatasetOptions>(configuration.GetSection(DatasetOptions.SectionName));
        services.Configure<AzureBlobStorageOptions>(configuration.GetSection(AzureBlobStorageOptions.SectionName));
        services.PostConfigure<AzureBlobStorageOptions>(options =>
        {
            string? blobServiceUri = configuration["AZURE_STORAGE_BLOB_SERVICE_URI"];
            if (!string.IsNullOrWhiteSpace(blobServiceUri))
            {
                options.BlobServiceUri = blobServiceUri;
            }

            string? connectionString = configuration["AZURE_STORAGE_CONNECTION_STRING"];
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                options.ConnectionString = connectionString;
            }
        });

        if (IsBlobStorageConfigured(configuration))
        {
            services.AddSingleton<INormalizedDocumentStore, BlobNormalizedDocumentStore>();
            return services;
        }

        var datasetOptions = configuration.GetSection(DatasetOptions.SectionName).Get<DatasetOptions>()
            ?? new DatasetOptions();

        if (string.IsNullOrWhiteSpace(datasetOptions.RootPath))
        {
            throw new InvalidOperationException(
                "Normalized documents require Azure Blob storage (AZURE_STORAGE_BLOB_SERVICE_URI / AzureStorage) or Dataset:RootPath.");
        }

        services.AddSingleton<INormalizedDocumentStore>(sp =>
            new LocalNormalizedDocumentStore(
                datasetOptions.RootPath,
                sp.GetRequiredService<ILogger<LocalNormalizedDocumentStore>>()));

        return services;
    }

    private static bool IsBlobStorageConfigured(IConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(configuration["AZURE_STORAGE_CONNECTION_STRING"]))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(configuration["AZURE_STORAGE_BLOB_SERVICE_URI"]))
        {
            return true;
        }

        AzureBlobStorageOptions? options = configuration
            .GetSection(AzureBlobStorageOptions.SectionName)
            .Get<AzureBlobStorageOptions>();

        return !string.IsNullOrWhiteSpace(options?.ConnectionString)
            || !string.IsNullOrWhiteSpace(options?.BlobServiceUri);
    }
}
