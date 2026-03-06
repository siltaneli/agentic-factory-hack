using Microsoft.Extensions.Logging;
using RepairPlanner.Models;

namespace RepairPlanner.Services;

public sealed class MockCosmosDbService(ILogger<MockCosmosDbService> logger)
{
    // Mock data
    private static readonly IReadOnlyList<Technician> MockTechnicians = new[]
    {
        new Technician
        {
            Id = "TECH-001",
            Name = "John Smith",
            Department = "Maintenance",
            Skills = new[] { "tire_curing_press", "temperature_control", "electrical_systems", "plc_troubleshooting" },
            IsAvailable = true
        },
        new Technician
        {
            Id = "TECH-002",
            Name = "Jane Doe",
            Department = "Maintenance",
            Skills = new[] { "tire_building_machine", "vibration_analysis", "bearing_replacement" },
            IsAvailable = true
        }
    };

    private static readonly IReadOnlyList<Part> MockParts = new[]
    {
        new Part
        {
            Id = "PART-001",
            PartNumber = "TCP-HTR-4KW",
            Name = "4KW Heating Element",
            Category = "Electrical",
            QuantityInStock = 5,
            UnitCost = 250.00m
        },
        new Part
        {
            Id = "PART-002",
            PartNumber = "GEN-TS-K400",
            Name = "K-Type Thermocouple",
            Category = "Sensors",
            QuantityInStock = 10,
            UnitCost = 45.00m
        }
    };

    public async Task<IReadOnlyList<Technician>> GetAvailableTechniciansAsync(IReadOnlyList<string> requiredSkills, CancellationToken ct = default)
    {
        await Task.Delay(100, ct); // Simulate async operation
        var matchingTechnicians = MockTechnicians
            .Where(t => t.IsAvailable && t.Skills.Any(skill => requiredSkills.Contains(skill)))
            .ToList();

        logger.LogInformation("Mock: Retrieved {Count} available technicians for skills: {Skills}",
            matchingTechnicians.Count, string.Join(", ", requiredSkills));
        return matchingTechnicians;
    }

    public async Task<IReadOnlyList<Part>> GetPartsByNumbersAsync(IReadOnlyList<string> partNumbers, CancellationToken ct = default)
    {
        await Task.Delay(100, ct); // Simulate async operation
        var matchingParts = MockParts
            .Where(p => partNumbers.Contains(p.PartNumber))
            .ToList();

        logger.LogInformation("Mock: Retrieved {Count} parts for numbers: {PartNumbers}",
            matchingParts.Count, string.Join(", ", partNumbers));
        return matchingParts;
    }

    public async Task CreateWorkOrderAsync(WorkOrder workOrder, CancellationToken ct = default)
    {
        await Task.Delay(100, ct); // Simulate async operation
        logger.LogInformation("Mock: Created work order {WorkOrderNumber}", workOrder.WorkOrderNumber);
    }
}