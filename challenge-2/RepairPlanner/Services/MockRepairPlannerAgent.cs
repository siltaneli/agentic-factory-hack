using Microsoft.Extensions.Logging;
using RepairPlanner.Models;

namespace RepairPlanner.Services;

public sealed class MockRepairPlannerAgent(
    MockCosmosDbService cosmosDb,
    IFaultMappingService faultMapping,
    ILogger<MockRepairPlannerAgent> logger)
{
    public async Task EnsureAgentVersionAsync(CancellationToken ct = default)
    {
        await Task.Delay(50, ct); // Simulate async operation
        logger.LogInformation("Mock: Agent version ensured");
    }

    public async Task<WorkOrder> PlanAndCreateWorkOrderAsync(DiagnosedFault fault, CancellationToken ct = default)
    {
        try
        {
            // 1. Get required skills and parts from mapping
            var requiredSkills = faultMapping.GetRequiredSkills(fault.FaultType);
            var requiredPartNumbers = faultMapping.GetRequiredParts(fault.FaultType);
            logger.LogInformation("Mock: Fault {FaultType}: required skills={Skills}, parts={Parts}",
                fault.FaultType, string.Join(",", requiredSkills), string.Join(",", requiredPartNumbers));

            // 2. Query technicians and parts from mock Cosmos DB
            var availableTechnicians = await cosmosDb.GetAvailableTechniciansAsync(requiredSkills, ct);
            var availableParts = new List<Part>();
            if (requiredPartNumbers.Count > 0)
            {
                availableParts.AddRange(await cosmosDb.GetPartsByNumbersAsync(requiredPartNumbers, ct));
            }
            logger.LogInformation("Mock: Found {TechnicianCount} technicians and {PartCount} parts", availableTechnicians.Count, availableParts.Count);

            // 3. Create mock work order (simulate AI agent response)
            var workOrder = CreateMockWorkOrder(fault, availableTechnicians, availableParts);
            logger.LogInformation("Mock: Work order parsed: {WorkOrderNumber}", workOrder.WorkOrderNumber);

            // 4. Save to mock Cosmos DB
            await cosmosDb.CreateWorkOrderAsync(workOrder, ct);
            logger.LogInformation("Mock: Work order {WorkOrderNumber} created and saved", workOrder.WorkOrderNumber);

            return workOrder;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Mock: Error planning repair and creating work order for fault {FaultType}", fault.FaultType);
            throw;
        }
    }

    private WorkOrder CreateMockWorkOrder(DiagnosedFault fault, IReadOnlyList<Technician> technicians, IReadOnlyList<Part> parts)
    {
        var workOrderNumber = $"WO-{DateTime.UtcNow:yyyy-MM-dd}-{Guid.NewGuid().ToString().Substring(0, 5).ToUpper()}";

        var assignedTechnician = technicians.FirstOrDefault();
        var assignedTo = assignedTechnician?.Id;

        var partsUsed = parts.Select(p => new WorkOrderPartUsage
        {
            PartId = p.Id,
            PartNumber = p.PartNumber,
            Quantity = 1
        }).ToList();

        var tasks = new List<RepairTask>
        {
            new()
            {
                Sequence = 1,
                Title = "Inspect Equipment",
                Description = $"Perform visual inspection of {fault.MachineId} for signs of {fault.FaultType}",
                EstimatedDurationMinutes = 30,
                RequiredSkills = new[] { "general_maintenance" },
                SafetyNotes = "Wear appropriate PPE and follow lockout/tagout procedures"
            },
            new()
            {
                Sequence = 2,
                Title = "Diagnose Fault",
                Description = $"Use diagnostic tools to confirm {fault.FaultType} and identify root cause",
                EstimatedDurationMinutes = 45,
                RequiredSkills = faultMapping.GetRequiredSkills(fault.FaultType).Take(2).ToArray(),
                SafetyNotes = "Ensure equipment is safely isolated before diagnostics"
            },
            new()
            {
                Sequence = 3,
                Title = "Repair Equipment",
                Description = $"Perform necessary repairs to resolve {fault.FaultType}",
                EstimatedDurationMinutes = 90,
                RequiredSkills = faultMapping.GetRequiredSkills(fault.FaultType).ToArray(),
                SafetyNotes = "Follow manufacturer repair procedures and safety guidelines"
            },
            new()
            {
                Sequence = 4,
                Title = "Test and Verify",
                Description = "Test equipment functionality and verify fault is resolved",
                EstimatedDurationMinutes = 30,
                RequiredSkills = new[] { "general_maintenance" },
                SafetyNotes = "Perform all tests with equipment properly guarded"
            }
        };

        return new WorkOrder
        {
            Id = workOrderNumber,
            WorkOrderNumber = workOrderNumber,
            MachineId = fault.MachineId,
            Title = $"Repair: {fault.FaultType.Replace('_', ' ')}",
            Description = fault.Description,
            Type = "corrective",
            Priority = fault.Severity == "high" ? "high" : "medium",
            Status = "pending",
            AssignedTo = assignedTo,
            Notes = $"Mock work order generated for {fault.FaultType}",
            EstimatedDuration = tasks.Sum(t => t.EstimatedDurationMinutes),
            PartsUsed = partsUsed,
            Tasks = tasks,
            CreatedAt = DateTime.UtcNow
        };
    }
}