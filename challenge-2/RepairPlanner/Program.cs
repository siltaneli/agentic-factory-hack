using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RepairPlanner.Models;
using RepairPlanner.Services;

var services = new ServiceCollection();

// Configure logging
services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});

// Check if environment variables are set, otherwise use mock services
var cosmosEndpoint = Environment.GetEnvironmentVariable("COSMOS_ENDPOINT")?.Trim().Trim('"');
var cosmosKey = Environment.GetEnvironmentVariable("COSMOS_KEY")?.Trim().Trim('"');
var cosmosDbName = Environment.GetEnvironmentVariable("COSMOS_DATABASE_NAME")?.Trim().Trim('"');
var aiEndpoint = Environment.GetEnvironmentVariable("AZURE_AI_PROJECT_ENDPOINT")?.Trim().Trim('"');
var modelDeployment = Environment.GetEnvironmentVariable("MODEL_DEPLOYMENT_NAME")?.Trim().Trim('"') 
                      ?? Environment.GetEnvironmentVariable("AZURE_AI_MODEL_DEPLOYMENT_NAME")?.Trim().Trim('"');

Console.WriteLine($"DEBUG - COSMOS_KEY length: {cosmosKey?.Length ?? 0}");
Console.WriteLine($"DEBUG - COSMOS_KEY starts with: {cosmosKey?.Substring(0, Math.Min(10, cosmosKey?.Length ?? 0)) ?? "null"}");

var useRealServices = !string.IsNullOrEmpty(cosmosEndpoint) && !string.IsNullOrEmpty(cosmosKey) &&
                      !string.IsNullOrEmpty(cosmosDbName) && !string.IsNullOrEmpty(aiEndpoint) &&
                      !string.IsNullOrEmpty(modelDeployment);

Console.WriteLine("Environment variable check:");
Console.WriteLine($"COSMOS_ENDPOINT: {(string.IsNullOrEmpty(cosmosEndpoint) ? "NOT SET" : "SET")}");
Console.WriteLine($"COSMOS_KEY: {(string.IsNullOrEmpty(cosmosKey) ? "NOT SET" : "SET")}");
Console.WriteLine($"COSMOS_DATABASE_NAME: {(string.IsNullOrEmpty(cosmosDbName) ? "NOT SET" : "SET")}");
Console.WriteLine($"AZURE_AI_PROJECT_ENDPOINT: {(string.IsNullOrEmpty(aiEndpoint) ? "NOT SET" : "SET")}");
Console.WriteLine($"MODEL_DEPLOYMENT_NAME: {(string.IsNullOrEmpty(modelDeployment) ? "NOT SET" : "SET")}");
Console.WriteLine($"Using real services: {useRealServices}");
Console.WriteLine();

if (useRealServices)
{
    Console.WriteLine($"Creating CosmosDbOptions with key length: {cosmosKey!.Length}");
    var cosmosOptions = new CosmosDbOptions
    {
        Endpoint = cosmosEndpoint!,
        Key = cosmosKey!,
        DatabaseName = cosmosDbName!
    };

    // Register services
    services.AddSingleton(cosmosOptions);
    services.AddSingleton<CosmosDbService>();
    services.AddSingleton<IFaultMappingService, FaultMappingService>();

    // Configure AI Project Client
    services.AddSingleton(new AIProjectClient(new Uri(aiEndpoint!), new DefaultAzureCredential()));
    services.AddSingleton(modelDeployment!);

    // Register the agent
    services.AddSingleton<RepairPlannerAgent>();
}
else
{
    // Register mock services for demo
    services.AddSingleton<IFaultMappingService, FaultMappingService>();
    services.AddSingleton<MockCosmosDbService>();
    services.AddSingleton<MockRepairPlannerAgent>();
}

await using var provider = services.BuildServiceProvider();

var logger = provider.GetRequiredService<ILogger<Program>>();

try
{
    logger.LogInformation("Starting Repair Planner Agent demo");

    if (useRealServices)
    {
        var agent = provider.GetRequiredService<RepairPlannerAgent>();

        // Ensure agent is registered
        await agent.EnsureAgentVersionAsync();
        logger.LogInformation("Agent version ensured");

        // Create a sample diagnosed fault
        var sampleFault = new DiagnosedFault
        {
            Id = Guid.NewGuid().ToString(),
            FaultType = "curing_temperature_excessive",
            MachineId = "TIRE-CURING-001",
            Description = "Curing press temperature exceeded safe limits by 50°C, causing potential material degradation",
            Severity = "high",
            Timestamp = DateTime.UtcNow.AddMinutes(-30)
        };

        logger.LogInformation("Created sample fault: {FaultType} on machine {MachineId}", sampleFault.FaultType, sampleFault.MachineId);

        // Plan and create work order
        var workOrder = await agent.PlanAndCreateWorkOrderAsync(sampleFault);
        logger.LogInformation("Work order created successfully: {WorkOrderNumber}", workOrder.WorkOrderNumber);
        logger.LogInformation("Assigned to: {AssignedTo}, Priority: {Priority}, Estimated duration: {Duration} minutes",
            workOrder.AssignedTo ?? "Unassigned", workOrder.Priority, workOrder.EstimatedDuration);
        logger.LogInformation("Tasks: {TaskCount}, Parts used: {PartCount}",
            workOrder.Tasks.Count, workOrder.PartsUsed.Count);

        // Display work order details
        Console.WriteLine("\n=== WORK ORDER CREATED ===");
        Console.WriteLine($"Number: {workOrder.WorkOrderNumber}");
        Console.WriteLine($"Machine: {workOrder.MachineId}");
        Console.WriteLine($"Title: {workOrder.Title}");
        Console.WriteLine($"Type: {workOrder.Type}, Priority: {workOrder.Priority}");
        Console.WriteLine($"Status: {workOrder.Status}, Assigned To: {workOrder.AssignedTo ?? "Unassigned"}");
        Console.WriteLine($"Estimated Duration: {workOrder.EstimatedDuration} minutes");
        Console.WriteLine($"Description: {workOrder.Description}");

        if (workOrder.Tasks.Count > 0)
        {
            Console.WriteLine("\nTasks:");
            foreach (var task in workOrder.Tasks.OrderBy(t => t.Sequence))
            {
                Console.WriteLine($"  {task.Sequence}. {task.Title} ({task.EstimatedDurationMinutes} min)");
                Console.WriteLine($"     Skills: {string.Join(", ", task.RequiredSkills)}");
            }
        }

        if (workOrder.PartsUsed.Count > 0)
        {
            Console.WriteLine("\nParts Required:");
            foreach (var part in workOrder.PartsUsed)
            {
                Console.WriteLine($"  {part.PartNumber} (Qty: {part.Quantity})");
            }
        }

        Console.WriteLine("\n=== DEMO COMPLETED SUCCESSFULLY ===");
    }
    else
    {
        // Demo with mock services
        var mockAgent = provider.GetRequiredService<MockRepairPlannerAgent>();

        var sampleFault = new DiagnosedFault
        {
            Id = Guid.NewGuid().ToString(),
            FaultType = "curing_temperature_excessive",
            MachineId = "TIRE-CURING-001",
            Description = "Curing press temperature exceeded safe limits by 50°C, causing potential material degradation",
            Severity = "high",
            Timestamp = DateTime.UtcNow.AddMinutes(-30)
        };

        logger.LogInformation("Running demo with mock services (environment variables not set)");
        logger.LogInformation("Sample fault: {FaultType} on machine {MachineId}", sampleFault.FaultType, sampleFault.MachineId);

        var workOrder = await mockAgent.PlanAndCreateWorkOrderAsync(sampleFault);

        Console.WriteLine("\n=== MOCK WORK ORDER CREATED ===");
        Console.WriteLine($"Number: {workOrder.WorkOrderNumber}");
        Console.WriteLine($"Machine: {workOrder.MachineId}");
        Console.WriteLine($"Title: {workOrder.Title}");
        Console.WriteLine($"Type: {workOrder.Type}, Priority: {workOrder.Priority}");
        Console.WriteLine($"Status: {workOrder.Status}, Assigned To: {workOrder.AssignedTo ?? "Unassigned"}");
        Console.WriteLine($"Estimated Duration: {workOrder.EstimatedDuration} minutes");

        if (workOrder.Tasks.Count > 0)
        {
            Console.WriteLine("\nTasks:");
            foreach (var task in workOrder.Tasks.OrderBy(t => t.Sequence))
            {
                Console.WriteLine($"  {task.Sequence}. {task.Title} ({task.EstimatedDurationMinutes} min)");
                Console.WriteLine($"     Skills: {string.Join(", ", task.RequiredSkills)}");
            }
        }

        if (workOrder.PartsUsed.Count > 0)
        {
            Console.WriteLine("\nParts Required:");
            foreach (var part in workOrder.PartsUsed)
            {
                Console.WriteLine($"  {part.PartNumber} (Qty: {part.Quantity})");
            }
        }

        Console.WriteLine("\n=== MOCK DEMO COMPLETED ===");
        Console.WriteLine("To run with real Azure services, set these environment variables:");
        Console.WriteLine("- AZURE_AI_PROJECT_ENDPOINT");
        Console.WriteLine("- MODEL_DEPLOYMENT_NAME");
        Console.WriteLine("- COSMOS_ENDPOINT");
        Console.WriteLine("- COSMOS_KEY");
        Console.WriteLine("- COSMOS_DATABASE_NAME");
    }
}
catch (Exception ex)
{
    logger.LogError(ex, "Error during repair planning workflow");
    Console.WriteLine($"Error: {ex.Message}");
    return 1;
}

return 0;
