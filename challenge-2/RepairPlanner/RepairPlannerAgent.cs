using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.AI.Projects;
using Azure.AI.Projects.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using RepairPlanner.Models;

namespace RepairPlanner.Services;

public sealed class RepairPlannerAgent(
    AIProjectClient projectClient,
    CosmosDbService cosmosDb,
    IFaultMappingService faultMapping,
    string modelDeploymentName,
    ILogger<RepairPlannerAgent> logger)
{
    private const string AgentName = "RepairPlannerAgent";
    private const string AgentInstructions = """
        You are a Repair Planner Agent for tire manufacturing equipment maintenance.
        Your job is to generate a comprehensive repair plan based on the diagnosed fault.
        
        Output MUST be valid JSON matching the WorkOrder schema with these fields:
        - workOrderNumber: string (format: "WO-YYYY-MM-DD-XXXXX")
        - machineId: string
        - title: string (short repair title)
        - description: string (detailed description)
        - type: "corrective" | "preventive" | "emergency"
        - priority: "critical" | "high" | "medium" | "low"
        - status: "pending" | "scheduled" | "in_progress" | "completed"
        - assignedTo: string or null (technician id, or null if not assigned)
        - notes: string (additional notes)
        - estimatedDuration: integer (minutes only, e.g. 90 not "90 minutes")
        - partsUsed: array of {partId, partNumber, quantity} (integers for quantity)
        - tasks: array of {sequence, title, description, estimatedDurationMinutes (integer only), requiredSkills (string array), safetyNotes}
        
        CRITICAL RULES:
        1. All duration fields MUST be integers representing minutes (e.g. 90), NOT strings
        2. Quantity fields MUST be integers, NOT strings
        3. Sequence MUST be integer, NOT string
        4. Assign the most qualified available technician from provided list
        5. Include only relevant parts from provided inventory
        6. Tasks must be ordered logically and actionable
        7. Return ONLY valid JSON, no markdown or extra text
        """;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public async Task EnsureAgentVersionAsync(CancellationToken ct = default)
    {
        try
        {
            var definition = new PromptAgentDefinition(model: modelDeploymentName)
            {
                Instructions = AgentInstructions
            };
            await projectClient.Agents.CreateAgentVersionAsync(AgentName, new AgentVersionCreationOptions(definition), ct);
            logger.LogInformation("Agent {AgentName} version ensured", AgentName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error ensuring agent {AgentName} version", AgentName);
            throw;
        }
    }

    public async Task<WorkOrder> PlanAndCreateWorkOrderAsync(DiagnosedFault fault, CancellationToken ct = default)
    {
        try
        {
            // 1. Get required skills and parts from mapping
            var requiredSkills = faultMapping.GetRequiredSkills(fault.FaultType);
            var requiredPartNumbers = faultMapping.GetRequiredParts(fault.FaultType);
            logger.LogInformation("Fault {FaultType}: required skills={Skills}, parts={Parts}",
                fault.FaultType, string.Join(",", requiredSkills), string.Join(",", requiredPartNumbers));

            // 2. Query technicians and parts from Cosmos DB
            var availableTechnicians = await cosmosDb.GetAvailableTechniciansAsync(requiredSkills, ct);
            var availableParts = new List<Part>();
            if (requiredPartNumbers.Count > 0)
            {
                availableParts.AddRange(await cosmosDb.GetPartsByNumbersAsync(requiredPartNumbers, ct));
            }
            logger.LogInformation("Found {TechnicianCount} technicians and {PartCount} parts", availableTechnicians.Count, availableParts.Count);

            // 3. Build prompt and invoke agent
            var prompt = BuildPrompt(fault, availableTechnicians, availableParts);
            logger.LogDebug("Invoking agent with prompt: {Prompt}", prompt);

            var agent = projectClient.GetAIAgent(name: AgentName);
            var response = await agent.RunAsync(prompt, thread: null, options: null, cancellationToken: ct);
            var agentResponse = response.Text ?? "";

            logger.LogInformation("Agent response received, length={Length}", agentResponse.Length);

            // 4. Parse response and create work order
            var workOrder = ParseAgentResponse(agentResponse, fault);
            logger.LogInformation("Work order parsed: {WorkOrderNumber}", workOrder.WorkOrderNumber);

            // 5. Save to Cosmos DB
            await cosmosDb.CreateWorkOrderAsync(workOrder, ct);
            logger.LogInformation("Work order {WorkOrderNumber} created and saved", workOrder.WorkOrderNumber);

            return workOrder;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error planning repair and creating work order for fault {FaultType}", fault.FaultType);
            throw;
        }
    }

    private string BuildPrompt(DiagnosedFault fault, IReadOnlyList<Technician> technicians, IReadOnlyList<Part> parts)
    {
        var technicianList = string.Join("\n", technicians.Select(t =>
            $"  - ID: {t.Id}, Name: {t.Name}, Skills: {string.Join(", ", t.Skills)}"));

        var partsList = string.Join("\n", parts.Select(p =>
            $"  - PartNumber: {p.PartNumber}, PartId: {p.Id}, Stock: {p.QuantityInStock}, Cost: {p.UnitCost}"));

        return $"""
            Diagnosed Fault:
            - Type: {fault.FaultType}
            - Machine: {fault.MachineId}
            - Description: {fault.Description}
            - Severity: {fault.Severity}
            - Timestamp: {fault.Timestamp:O}
            
            Available Technicians:
            {(technicians.Count > 0 ? technicianList : "  (None available)")}
            
            Available Parts:
            {(parts.Count > 0 ? partsList : "  (None needed or available)")}
            
            Generate a complete repair work order JSON for this fault. Select the best qualified technician from the list. Include only parts from the available parts list if repair requires them.
            """;
    }

    private WorkOrder ParseAgentResponse(string response, DiagnosedFault fault)
    {
        try
        {
            // Try to extract JSON from response (in case there's extra text)
            var jsonStart = response.IndexOf('{');
            var jsonEnd = response.LastIndexOf('}');

            if (jsonStart < 0 || jsonEnd < 0 || jsonEnd <= jsonStart)
            {
                logger.LogWarning("Could not find JSON in agent response, creating minimal work order");
                return CreateMinimalWorkOrder(fault);
            }

            var jsonStr = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
            var workOrder = JsonSerializer.Deserialize<WorkOrder>(jsonStr, JsonOptions)
                ?? throw new InvalidOperationException("Failed to deserialize work order");

            // Apply defaults and ensure required fields
            workOrder.Id = workOrder.WorkOrderNumber;
            workOrder.Status ??= "pending";
            workOrder.Priority ??= "medium";
            workOrder.Type ??= "corrective";
            workOrder.CreatedAt = DateTime.UtcNow;

            // Ensure collections are mutable lists
            workOrder.PartsUsed = (workOrder.PartsUsed ?? Array.Empty<WorkOrderPartUsage>()).ToList();
            workOrder.Tasks = (workOrder.Tasks ?? Array.Empty<RepairTask>()).ToList();

            logger.LogInformation("Successfully parsed work order {WorkOrderNumber}", workOrder.WorkOrderNumber);
            return workOrder;
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to parse agent response as JSON, creating minimal work order. Response: {Response}", response);
            return CreateMinimalWorkOrder(fault);
        }
    }

    private WorkOrder CreateMinimalWorkOrder(DiagnosedFault fault)
    {
        return new WorkOrder
        {
            WorkOrderNumber = $"WO-{DateTime.UtcNow:yyyy-MM-dd}-{Guid.NewGuid().ToString().Substring(0, 5).ToUpper()}",
            MachineId = fault.MachineId,
            Title = $"Repair: {fault.FaultType}",
            Description = fault.Description,
            Type = "corrective",
            Priority = "medium",
            Status = "pending",
            AssignedTo = null,
            Notes = $"Auto-generated from fault: {fault.FaultType}",
            EstimatedDuration = 120,
            PartsUsed = new List<WorkOrderPartUsage>(),
            Tasks = new List<RepairTask>
            {
                new()
                {
                    Sequence = 1,
                    Title = "Initial Inspection",
                    Description = $"Inspect equipment for {fault.FaultType}",
                    EstimatedDurationMinutes = 30,
                    RequiredSkills = new[] { "general_maintenance" },
                    SafetyNotes = "Follow standard safety procedures"
                }
            },
            Id = string.Empty,
            CreatedAt = DateTime.UtcNow
        };
    }
}