using Microsoft.AspNetCore.Mvc;
using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Web.Api.Models.Requests;
using LucidAdmin.Web.Api.Models.Responses;
using LucidAdmin.Infrastructure.Data;

namespace LucidAdmin.Web.Endpoints;

public static class WorkflowEndpoints
{
    public static void MapWorkflowEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/workflows")
            .WithTags("Workflows");

        // Get step types with metadata
        group.MapGet("/step-types", () =>
        {
            var stepTypes = new List<StepTypeInfo>
            {
                new(StepType.Trigger, "Trigger", "Entry point when ticket matches criteria", "🎯", "#4CAF50", 0, 1),
                new(StepType.Classify, "Classify", "Use LLM to classify ticket and extract data", "🏷️", "#2196F3", 1, 2),
                new(StepType.Query, "Query", "Query external systems for context", "🔍", "#9C27B0", 1, 1),
                new(StepType.Validate, "Validate", "Validate data against rules", "✓", "#FF9800", 1, 2),
                new(StepType.Execute, "Execute", "Execute action via tool server", "⚡", "#F44336", 1, 2),
                new(StepType.UpdateTicket, "Update Ticket", "Update the ServiceNow ticket", "📝", "#00BCD4", 1, 1),
                new(StepType.Notify, "Notify", "Send notification", "📧", "#E91E63", 1, 1),
                new(StepType.Escalate, "Escalate", "Escalate to human operator", "🚨", "#FF5722", 1, 1),
                new(StepType.Condition, "Condition", "Branch based on condition", "❓", "#795548", 1, 2),
                new(StepType.End, "End", "End of workflow path", "🏁", "#607D8B", 1, 0)
            };
            return Results.Ok(stepTypes);
        });

        // List all workflows
        group.MapGet("/", async (IWorkflowDefinitionRepository repo) =>
        {
            var workflows = await repo.GetAllAsync();
            var response = workflows.Select(w => new WorkflowListResponse(
                w.Id, w.Name, w.DisplayName, w.Description, w.Version,
                w.IsBuiltIn, w.IsActive, w.Steps.Count,
                w.ExampleSet?.DisplayName ?? w.ExampleSet?.Name,
                w.CreatedAt, w.UpdatedAt
            ));
            return Results.Ok(response);
        });

        // Get single workflow
        group.MapGet("/{id:guid}", async (Guid id, IWorkflowDefinitionRepository repo) =>
        {
            var workflow = await repo.GetFullWorkflowAsync(id);
            if (workflow is null)
                return Results.NotFound(new { error = "WorkflowNotFound" });

            var response = MapToDetailResponse(workflow);
            return Results.Ok(response);
        });

        // Create workflow
        group.MapPost("/", async (CreateWorkflowRequest request, IWorkflowDefinitionRepository repo) =>
        {
            if (await repo.ExistsAsync(request.Name))
                return Results.Conflict(new { error = "WorkflowExists", message = $"Workflow '{request.Name}' already exists" });

            var workflow = new WorkflowDefinition
            {
                Name = request.Name,
                DisplayName = request.DisplayName,
                Description = request.Description,
                ExampleSetId = request.ExampleSetId,
                IsActive = request.IsActive,
                IsBuiltIn = false,
                Version = "1.0.0"
            };

            await repo.AddAsync(workflow);

            return Results.Created($"/api/v1/workflows/{workflow.Id}", new WorkflowListResponse(
                workflow.Id, workflow.Name, workflow.DisplayName, workflow.Description, workflow.Version,
                workflow.IsBuiltIn, workflow.IsActive, 0, null,
                workflow.CreatedAt, workflow.UpdatedAt
            ));
        });

        // Update workflow
        group.MapPut("/{id:guid}", async (Guid id, UpdateWorkflowRequest request, IWorkflowDefinitionRepository repo) =>
        {
            var workflow = await repo.GetByIdAsync(id);
            if (workflow is null)
                return Results.NotFound(new { error = "WorkflowNotFound" });

            if (workflow.IsBuiltIn)
                return Results.BadRequest(new { error = "CannotModifyBuiltIn", message = "Built-in workflows cannot be modified. Copy it to create a custom version." });

            if (request.DisplayName is not null) workflow.DisplayName = request.DisplayName;
            if (request.Description is not null) workflow.Description = request.Description;
            if (request.Version is not null) workflow.Version = request.Version;
            if (request.ExampleSetId.HasValue) workflow.ExampleSetId = request.ExampleSetId;
            if (request.IsActive.HasValue) workflow.IsActive = request.IsActive.Value;

            await repo.UpdateAsync(workflow);

            return Results.Ok(new WorkflowListResponse(
                workflow.Id, workflow.Name, workflow.DisplayName, workflow.Description, workflow.Version,
                workflow.IsBuiltIn, workflow.IsActive, workflow.Steps.Count, null,
                workflow.CreatedAt, workflow.UpdatedAt
            ));
        });

        // Delete workflow
        group.MapDelete("/{id:guid}", async (Guid id, IWorkflowDefinitionRepository repo) =>
        {
            var workflow = await repo.GetByIdAsync(id);
            if (workflow is null)
                return Results.NotFound(new { error = "WorkflowNotFound" });

            if (workflow.IsBuiltIn)
                return Results.BadRequest(new { error = "CannotDeleteBuiltIn", message = "Built-in workflows cannot be deleted." });

            await repo.DeleteAsync(id);
            return Results.NoContent();
        });

        // Copy workflow
        group.MapPost("/{id:guid}/copy", async (
            Guid id,
            [FromQuery] string newName,
            IWorkflowDefinitionRepository repo,
            LucidDbContext db) =>
        {
            var source = await repo.GetFullWorkflowAsync(id);
            if (source is null)
                return Results.NotFound(new { error = "WorkflowNotFound" });

            if (await repo.ExistsAsync(newName))
                return Results.Conflict(new { error = "WorkflowExists", message = $"Workflow '{newName}' already exists" });

            var copy = new WorkflowDefinition
            {
                Name = newName,
                DisplayName = $"{source.DisplayName} (Copy)",
                Description = source.Description,
                Version = "1.0.0",
                ExampleSetId = source.ExampleSetId,
                LayoutJson = source.LayoutJson,
                IsActive = true,
                IsBuiltIn = false
            };

            // Copy steps
            var oldToNewStep = new Dictionary<Guid, WorkflowStep>();
            foreach (var srcStep in source.Steps)
            {
                var newStep = new WorkflowStep
                {
                    Name = srcStep.Name,
                    DisplayName = srcStep.DisplayName,
                    StepType = srcStep.StepType,
                    ConfigurationJson = srcStep.ConfigurationJson,
                    PositionX = srcStep.PositionX,
                    PositionY = srcStep.PositionY,
                    DrawflowNodeId = srcStep.DrawflowNodeId,
                    SortOrder = srcStep.SortOrder
                };
                copy.Steps.Add(newStep);
                oldToNewStep[srcStep.Id] = newStep;
            }

            await repo.AddAsync(copy);

            // Copy transitions (after steps have IDs)
            foreach (var srcStep in source.Steps)
            {
                foreach (var trans in srcStep.OutgoingTransitions)
                {
                    if (oldToNewStep.TryGetValue(trans.FromStepId, out var newFrom) &&
                        oldToNewStep.TryGetValue(trans.ToStepId, out var newTo))
                    {
                        db.StepTransitions.Add(new StepTransition
                        {
                            FromStepId = newFrom.Id,
                            ToStepId = newTo.Id,
                            Label = trans.Label,
                            Condition = trans.Condition,
                            OutputIndex = trans.OutputIndex,
                            InputIndex = trans.InputIndex
                        });
                    }
                }
            }

            // Copy workflow ruleset mappings
            foreach (var mapping in source.RulesetMappings)
            {
                db.WorkflowRulesetMappings.Add(new WorkflowRulesetMapping
                {
                    WorkflowDefinitionId = copy.Id,
                    RulesetId = mapping.RulesetId,
                    Priority = mapping.Priority,
                    IsEnabled = mapping.IsEnabled
                });
            }

            await db.SaveChangesAsync();

            return Results.Created($"/api/v1/workflows/{copy.Id}", new WorkflowListResponse(
                copy.Id, copy.Name, copy.DisplayName, copy.Description, copy.Version,
                copy.IsBuiltIn, copy.IsActive, copy.Steps.Count, null,
                copy.CreatedAt, copy.UpdatedAt
            ));
        });

        // Save workflow layout (steps and transitions)
        group.MapPut("/{id:guid}/layout", async (
            Guid id,
            SaveWorkflowLayoutRequest request,
            IWorkflowDefinitionRepository repo,
            LucidDbContext db) =>
        {
            var workflow = await repo.GetFullWorkflowAsync(id);
            if (workflow is null)
                return Results.NotFound(new { error = "WorkflowNotFound" });

            if (workflow.IsBuiltIn)
                return Results.BadRequest(new { error = "CannotModifyBuiltIn", message = "Built-in workflows cannot be modified." });

            // Save Drawflow JSON
            workflow.LayoutJson = request.LayoutJson;

            // Clear existing steps and transitions
            var existingSteps = workflow.Steps.ToList();
            db.StepTransitions.RemoveRange(existingSteps.SelectMany(s => s.OutgoingTransitions));
            db.WorkflowSteps.RemoveRange(existingSteps);

            // Create new steps
            var stepNameToEntity = new Dictionary<string, WorkflowStep>();
            foreach (var stepDto in request.Steps)
            {
                var step = new WorkflowStep
                {
                    WorkflowDefinitionId = workflow.Id,
                    Name = stepDto.Name,
                    DisplayName = stepDto.DisplayName,
                    StepType = stepDto.StepType,
                    ConfigurationJson = stepDto.ConfigurationJson,
                    PositionX = stepDto.PositionX,
                    PositionY = stepDto.PositionY,
                    DrawflowNodeId = stepDto.DrawflowNodeId,
                    SortOrder = stepDto.SortOrder
                };
                db.WorkflowSteps.Add(step);
                stepNameToEntity[step.Name] = step;
            }

            // Create transitions (steps will get IDs when we save at the end)
            foreach (var transDto in request.Transitions)
            {
                if (stepNameToEntity.TryGetValue(transDto.FromStepName, out var fromStep) &&
                    stepNameToEntity.TryGetValue(transDto.ToStepName, out var toStep))
                {
                    var transition = new StepTransition
                    {
                        FromStepId = fromStep.Id,
                        ToStepId = toStep.Id,
                        Label = transDto.Label,
                        Condition = transDto.Condition,
                        OutputIndex = transDto.OutputIndex,
                        InputIndex = transDto.InputIndex
                    };
                    db.StepTransitions.Add(transition);
                }
            }

            await db.SaveChangesAsync();

            // Reload and return
            workflow = await repo.GetFullWorkflowAsync(id);
            return Results.Ok(MapToDetailResponse(workflow!));
        });

        // === Workflow ruleset mappings ===

        group.MapPost("/{id:guid}/rulesets", async (
            Guid id,
            AddWorkflowRulesetRequest request,
            IWorkflowDefinitionRepository repo,
            LucidDbContext db) =>
        {
            var workflow = await repo.GetByIdAsync(id);
            if (workflow is null)
                return Results.NotFound(new { error = "WorkflowNotFound" });

            if (workflow.IsBuiltIn)
                return Results.BadRequest(new { error = "CannotModifyBuiltIn" });

            var mapping = new WorkflowRulesetMapping
            {
                WorkflowDefinitionId = id,
                RulesetId = request.RulesetId,
                Priority = request.Priority,
                IsEnabled = request.IsEnabled
            };

            db.WorkflowRulesetMappings.Add(mapping);
            await db.SaveChangesAsync();

            return Results.Created($"/api/v1/workflows/{id}/rulesets/{mapping.Id}", mapping);
        });

        group.MapDelete("/{id:guid}/rulesets/{mappingId:guid}", async (
            Guid id,
            Guid mappingId,
            IWorkflowDefinitionRepository repo,
            LucidDbContext db) =>
        {
            var workflow = await repo.GetByIdAsync(id);
            if (workflow is null)
                return Results.NotFound(new { error = "WorkflowNotFound" });

            if (workflow.IsBuiltIn)
                return Results.BadRequest(new { error = "CannotModifyBuiltIn" });

            var mapping = await db.WorkflowRulesetMappings.FindAsync(mappingId);
            if (mapping is null || mapping.WorkflowDefinitionId != id)
                return Results.NotFound(new { error = "MappingNotFound" });

            db.WorkflowRulesetMappings.Remove(mapping);
            await db.SaveChangesAsync();

            return Results.NoContent();
        });
    }

    private static WorkflowDetailResponse MapToDetailResponse(WorkflowDefinition w)
    {
        var allTransitions = w.Steps.SelectMany(s => s.OutgoingTransitions).ToList();

        return new WorkflowDetailResponse(
            w.Id, w.Name, w.DisplayName, w.Description, w.Version,
            w.IsBuiltIn, w.IsActive, w.LayoutJson,
            w.ExampleSetId, w.ExampleSet?.DisplayName ?? w.ExampleSet?.Name,
            w.Steps.Select(s => new WorkflowStepResponse(
                s.Id, s.Name, s.DisplayName, s.StepType,
                s.ConfigurationJson, s.PositionX, s.PositionY,
                s.DrawflowNodeId, s.SortOrder,
                s.RulesetMappings.Select(m => new StepRulesetMappingResponse(
                    m.Id, m.RulesetId,
                    m.Ruleset?.Name ?? "",
                    m.Ruleset?.DisplayName,
                    m.Priority, m.IsEnabled
                )).ToList()
            )).ToList(),
            allTransitions.Select(t => new StepTransitionResponse(
                t.Id, t.FromStepId, t.FromStep?.Name ?? "",
                t.ToStepId, t.ToStep?.Name ?? "",
                t.Label, t.Condition, t.OutputIndex, t.InputIndex
            )).ToList(),
            w.RulesetMappings.Select(m => new WorkflowRulesetMappingResponse(
                m.Id, m.RulesetId,
                m.Ruleset?.Name ?? "",
                m.Ruleset?.DisplayName,
                m.Priority, m.IsEnabled
            )).ToList(),
            w.CreatedAt, w.UpdatedAt
        );
    }
}
