using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RepairPlanner.Models;

namespace RepairPlanner.Services;

public sealed class CosmosDbService
{
    private readonly CosmosClient _cosmosClient;
    private readonly string _databaseName;
    private readonly ILogger<CosmosDbService> _logger;

    public CosmosDbService(CosmosDbOptions options, ILogger<CosmosDbService> logger)
    {
        _logger = logger;

        _logger.LogInformation("CosmosDbService initializing with endpoint: {Endpoint}, key length: {KeyLength}, database: {Database}",
            options.Endpoint, options.Key?.Length ?? 0, options.DatabaseName);

        _cosmosClient = new CosmosClient(options.Endpoint, options.Key);
        _databaseName = options.DatabaseName;

        _logger.LogInformation("CosmosDbService initialized successfully");
    }

    public async Task<IReadOnlyList<Technician>> GetAvailableTechniciansAsync(IReadOnlyList<string> requiredSkills, CancellationToken ct = default)
    {
        try
        {
            var container = _cosmosClient.GetContainer(_databaseName, "Technicians");
            var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.isAvailable = true AND EXISTS (SELECT VALUE s FROM s IN c.skills WHERE ARRAY_CONTAINS(@requiredSkills, s))")
                .WithParameter("@requiredSkills", requiredSkills);

            var iterator = container.GetItemQueryIterator<Technician>(query);
            var results = new List<Technician>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync(ct);
                results.AddRange(response);
            }

            _logger.LogInformation("Retrieved {Count} available technicians for skills: {Skills}", results.Count, string.Join(", ", requiredSkills));
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying available technicians for skills: {Skills}", string.Join(", ", requiredSkills));
            throw;
        }
    }

    public async Task<IReadOnlyList<Part>> GetPartsByNumbersAsync(IReadOnlyList<string> partNumbers, CancellationToken ct = default)
    {
        try
        {
            var container = _cosmosClient.GetContainer(_databaseName, "PartsInventory");
            var query = new QueryDefinition("SELECT * FROM c WHERE ARRAY_CONTAINS(@partNumbers, c.partNumber)")
                .WithParameter("@partNumbers", partNumbers);

            var iterator = container.GetItemQueryIterator<Part>(query);
            var results = new List<Part>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync(ct);
                results.AddRange(response);
            }

            _logger.LogInformation("Retrieved {Count} parts for numbers: {PartNumbers}", results.Count, string.Join(", ", partNumbers));
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying parts for numbers: {PartNumbers}", string.Join(", ", partNumbers));
            throw;
        }
    }

    public async Task CreateWorkOrderAsync(WorkOrder workOrder, CancellationToken ct = default)
    {
        try
        {
            var container = _cosmosClient.GetContainer(_databaseName, "WorkOrders");
            workOrder.Id = workOrder.WorkOrderNumber; // Set id to workOrderNumber
            workOrder.CreatedAt = DateTime.UtcNow;

            await container.CreateItemAsync(workOrder, new PartitionKey(workOrder.Status), cancellationToken: ct);
            _logger.LogInformation("Created work order {WorkOrderNumber}", workOrder.WorkOrderNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating work order {WorkOrderNumber}", workOrder.WorkOrderNumber);
            throw;
        }
    }
}