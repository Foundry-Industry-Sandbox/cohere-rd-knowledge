namespace CohereRndKnowledgeMining.Api.Host.Options;

public enum DataSourceMode
{
    Local = 0,
    Fabric = 1
}

public sealed class DataSourceOptions
{
    public const string SectionName = "DataSource";

    public DataSourceMode Mode { get; set; } = DataSourceMode.Local;
    public FabricLakehouseOptions? FabricLakehouse { get; set; }
}
