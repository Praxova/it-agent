using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LucidAdmin.Infrastructure.Data.Seeding;

/// <summary>
/// Seeds built-in workflow definitions.
/// </summary>
public class WorkflowSeeder
{
    private readonly LucidDbContext _context;
    private readonly ILogger<WorkflowSeeder> _logger;

    public WorkflowSeeder(LucidDbContext context, ILogger<WorkflowSeeder> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task SeedAsync()
    {
        await SeedPasswordResetWorkflow();
        await _context.SaveChangesAsync();
    }

    private async Task SeedPasswordResetWorkflow()
    {
        const string workflowName = "password-reset-standard";

        var existing = await _context.WorkflowDefinitions
            .Include(w => w.Steps)
            .ThenInclude(s => s.OutgoingTransitions)
            .FirstOrDefaultAsync(w => w.Name == workflowName);

        if (existing == null)
        {
            // Get the password-reset-examples example set to link to this workflow
            var exampleSet = await _context.ExampleSets
                .FirstOrDefaultAsync(e => e.Name == "password-reset-examples");

            var workflow = new WorkflowDefinition
            {
                Name = workflowName,
                DisplayName = "Password Reset - Standard Flow",
                Description = "Automated password reset workflow with LLM classification and validation",
                Version = "1.0.0",
                ExampleSetId = exampleSet?.Id,
                IsBuiltIn = true,
                IsActive = true
            };

            // Create steps with visual layout positions
            var triggerStep = new WorkflowStep
            {
                Name = "trigger",
                DisplayName = "Ticket Received",
                StepType = StepType.Trigger,
                ConfigurationJson = """{"criteria": {"category": "Account Access", "subcategory": "Password Reset"}}""",
                PositionX = 100,
                PositionY = 200,
                DrawflowNodeId = 1,
                SortOrder = 0,
                WorkflowDefinition = workflow
            };

            var classifyStep = new WorkflowStep
            {
                Name = "classify",
                DisplayName = "Classify & Extract",
                StepType = StepType.Classify,
                ConfigurationJson = """{"model": "claude-sonnet-4-5", "temperature": 0.0, "extract_fields": ["affected_user", "caller_name", "confidence", "should_escalate", "escalation_reason"]}""",
                PositionX = 300,
                PositionY = 200,
                DrawflowNodeId = 2,
                SortOrder = 1,
                WorkflowDefinition = workflow
            };

            var validateStep = new WorkflowStep
            {
                Name = "validate",
                DisplayName = "Validate Confidence",
                StepType = StepType.Validate,
                ConfigurationJson = """{"rules": [{"field": "confidence", "operator": ">=", "value": 0.75}]}""",
                PositionX = 500,
                PositionY = 200,
                DrawflowNodeId = 3,
                SortOrder = 2,
                WorkflowDefinition = workflow
            };

            var escalateStep = new WorkflowStep
            {
                Name = "escalate",
                DisplayName = "Escalate to Human",
                StepType = StepType.Escalate,
                ConfigurationJson = """{"assignment_group": "IT-Tier2", "reason_field": "escalation_reason"}""",
                PositionX = 500,
                PositionY = 350,
                DrawflowNodeId = 4,
                SortOrder = 3,
                WorkflowDefinition = workflow
            };

            var executeStep = new WorkflowStep
            {
                Name = "execute",
                DisplayName = "Reset Password",
                StepType = StepType.Execute,
                ConfigurationJson = """{"tool": "ad_reset_password", "parameters": {"username": "{{affected_user}}", "notify": true}}""",
                PositionX = 700,
                PositionY = 200,
                DrawflowNodeId = 5,
                SortOrder = 4,
                WorkflowDefinition = workflow
            };

            var notifyStep = new WorkflowStep
            {
                Name = "notify",
                DisplayName = "Send Notification",
                StepType = StepType.Notify,
                ConfigurationJson = """{"template": "password_reset_success", "recipient_field": "caller_name", "include_temp_password": true}""",
                PositionX = 900,
                PositionY = 200,
                DrawflowNodeId = 6,
                SortOrder = 5,
                WorkflowDefinition = workflow
            };

            var endSuccessStep = new WorkflowStep
            {
                Name = "end-success",
                DisplayName = "Completed Successfully",
                StepType = StepType.End,
                ConfigurationJson = """{"status": "resolved", "resolution_code": "Automated - Password Reset"}""",
                PositionX = 1100,
                PositionY = 200,
                DrawflowNodeId = 7,
                SortOrder = 6,
                WorkflowDefinition = workflow
            };

            var endEscalatedStep = new WorkflowStep
            {
                Name = "end-escalated",
                DisplayName = "Escalated for Review",
                StepType = StepType.End,
                ConfigurationJson = """{"status": "pending", "resolution_code": "Escalated - Requires Review"}""",
                PositionX = 700,
                PositionY = 350,
                DrawflowNodeId = 8,
                SortOrder = 7,
                WorkflowDefinition = workflow
            };

            workflow.Steps.Add(triggerStep);
            workflow.Steps.Add(classifyStep);
            workflow.Steps.Add(validateStep);
            workflow.Steps.Add(escalateStep);
            workflow.Steps.Add(executeStep);
            workflow.Steps.Add(notifyStep);
            workflow.Steps.Add(endSuccessStep);
            workflow.Steps.Add(endEscalatedStep);

            _context.WorkflowDefinitions.Add(workflow);
            await _context.SaveChangesAsync();

            // Create transitions after steps have IDs
            var transitions = new List<StepTransition>
            {
                // Main flow
                new StepTransition
                {
                    FromStepId = triggerStep.Id,
                    ToStepId = classifyStep.Id,
                    Label = "Ticket Received",
                    OutputIndex = 0,
                    InputIndex = 0
                },
                new StepTransition
                {
                    FromStepId = classifyStep.Id,
                    ToStepId = validateStep.Id,
                    Label = "Classified",
                    OutputIndex = 0,
                    InputIndex = 0
                },
                // High confidence path
                new StepTransition
                {
                    FromStepId = validateStep.Id,
                    ToStepId = executeStep.Id,
                    Label = "Valid (≥75%)",
                    Condition = "confidence >= 0.75",
                    OutputIndex = 0,
                    InputIndex = 0
                },
                new StepTransition
                {
                    FromStepId = executeStep.Id,
                    ToStepId = notifyStep.Id,
                    Label = "Success",
                    OutputIndex = 0,
                    InputIndex = 0
                },
                new StepTransition
                {
                    FromStepId = notifyStep.Id,
                    ToStepId = endSuccessStep.Id,
                    Label = "Notified",
                    OutputIndex = 0,
                    InputIndex = 0
                },
                // Low confidence path (escalation)
                new StepTransition
                {
                    FromStepId = validateStep.Id,
                    ToStepId = escalateStep.Id,
                    Label = "Invalid (<75%)",
                    Condition = "confidence < 0.75",
                    OutputIndex = 1,
                    InputIndex = 0
                },
                new StepTransition
                {
                    FromStepId = escalateStep.Id,
                    ToStepId = endEscalatedStep.Id,
                    Label = "Escalated",
                    OutputIndex = 0,
                    InputIndex = 0
                },
                // Error handling - execute failure also escalates
                new StepTransition
                {
                    FromStepId = executeStep.Id,
                    ToStepId = escalateStep.Id,
                    Label = "Failed",
                    Condition = "execution_status == 'failed'",
                    OutputIndex = 1,
                    InputIndex = 0
                },
                new StepTransition
                {
                    FromStepId = classifyStep.Id,
                    ToStepId = escalateStep.Id,
                    Label = "Should Escalate",
                    Condition = "should_escalate == true",
                    OutputIndex = 1,
                    InputIndex = 0
                }
            };

            _context.StepTransitions.AddRange(transitions);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Seeded built-in workflow: {WorkflowName} with {StepCount} steps and {TransitionCount} transitions",
                workflowName, workflow.Steps.Count, transitions.Count);
        }
        else
        {
            _logger.LogDebug("Built-in workflow already exists: {WorkflowName}", workflowName);
        }
    }
}
