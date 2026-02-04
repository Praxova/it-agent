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
        await SeedHelpdeskPasswordResetWorkflow();
        await FixTransitionOutputIndexes();
        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// Fixes OutputIndex on branching transitions that should use output_2 (index 1).
    /// This is a one-time migration for existing data.
    /// </summary>
    private async Task FixTransitionOutputIndexes()
    {
        // Fix branching transitions that should use output_2 (OutputIndex = 1)
        var branchingTransitions = await _context.StepTransitions
            .Include(t => t.FromStep)
            .Where(t => t.OutputIndex == 0 && t.FromStep != null && (
                // Classify → Escalate (low confidence)
                (t.FromStep.StepType == StepType.Classify && t.Condition != null && t.Condition.Contains("< 0.8")) ||
                // Validate → Escalate (invalid)
                (t.FromStep.StepType == StepType.Validate && t.Condition != null && t.Condition.Contains("false")) ||
                // Execute → Escalate (failure)
                (t.FromStep.StepType == StepType.Execute && t.Condition != null && t.Condition.Contains("false"))
            ))
            .ToListAsync();

        if (branchingTransitions.Any())
        {
            foreach (var t in branchingTransitions)
            {
                t.OutputIndex = 1;
                _logger.LogInformation("Fixed OutputIndex for transition: {From} -> (condition: {Cond})",
                    t.FromStep?.Name, t.Condition);
            }
            await _context.SaveChangesAsync();
        }
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

    private async Task SeedHelpdeskPasswordResetWorkflow()
    {
        const string workflowName = "helpdesk-password-reset-workflow";

        var existing = await _context.WorkflowDefinitions
            .Include(w => w.Steps)
            .ThenInclude(s => s.OutgoingTransitions)
            .FirstOrDefaultAsync(w => w.Name == workflowName);

        if (existing != null)
        {
            _logger.LogDebug("Built-in workflow already exists: {WorkflowName}", workflowName);
            return;
        }

        _logger.LogInformation("Seeding workflow: {WorkflowName}", workflowName);

        // Ensure test-agent exists
        var agent = await EnsureTestAgentExistsAsync();

        // Get required rulesets
        var securityDefaults = await _context.Rulesets.FirstOrDefaultAsync(r => r.Name == "security-defaults");
        var classificationRules = await _context.Rulesets.FirstOrDefaultAsync(r => r.Name == "classification-rules");
        var securityRules = await _context.Rulesets.FirstOrDefaultAsync(r => r.Name == "security-rules");
        var communicationRules = await _context.Rulesets.FirstOrDefaultAsync(r => r.Name == "communication-rules");
        var auditRules = await _context.Rulesets.FirstOrDefaultAsync(r => r.Name == "audit-rules");

        if (securityDefaults == null || classificationRules == null || securityRules == null ||
            communicationRules == null || auditRules == null)
        {
            _logger.LogWarning("Required rulesets not found. Skipping workflow seed.");
            return;
        }

        // Get example set
        var passwordResetExamples = await _context.ExampleSets
            .FirstOrDefaultAsync(e => e.Name == "password-reset-examples");

        // Create workflow
        var workflow = new WorkflowDefinition
        {
            Name = workflowName,
            DisplayName = "Helpdesk Password Reset Workflow",
            Description = "Automated workflow for handling password reset requests from ServiceNow",
            Version = "1.0.0",
            TriggerType = "ServiceNow",
            TriggerConfigJson = """{"pollIntervalSeconds": 30, "assignmentGroup": "Helpdesk"}""",
            ExampleSetId = passwordResetExamples?.Id,
            IsActive = true,
            IsBuiltIn = true
        };

        _context.WorkflowDefinitions.Add(workflow);
        await _context.SaveChangesAsync();

        // Create steps
        var triggerStep = new WorkflowStep
        {
            Name = "trigger-start",
            StepType = StepType.Trigger,
            ConfigurationJson = """{"source": "servicenow"}""",
            PositionX = 100,
            PositionY = 300,
            SortOrder = 1,
            WorkflowDefinitionId = workflow.Id
        };

        var classifyStep = new WorkflowStep
        {
            Name = "classify-ticket",
            StepType = StepType.Classify,
            ConfigurationJson = """{"useExampleSet": "password-reset-examples"}""",
            PositionX = 300,
            PositionY = 300,
            SortOrder = 2,
            WorkflowDefinitionId = workflow.Id
        };

        var validateStep = new WorkflowStep
        {
            Name = "validate-request",
            StepType = StepType.Validate,
            ConfigurationJson = """{"checks": ["user_exists", "not_admin", "requester_authorized"]}""",
            PositionX = 500,
            PositionY = 200,
            SortOrder = 3,
            WorkflowDefinitionId = workflow.Id
        };

        var executeStep = new WorkflowStep
        {
            Name = "execute-reset",
            StepType = StepType.Execute,
            ConfigurationJson = """{"capability": "ad-password-reset", "generateTempPassword": true}""",
            PositionX = 700,
            PositionY = 200,
            SortOrder = 4,
            WorkflowDefinitionId = workflow.Id
        };

        var notifyStep = new WorkflowStep
        {
            Name = "notify-user",
            StepType = StepType.Notify,
            ConfigurationJson = """{"template": "password-reset-success", "channel": "ticket-comment", "includeTempPassword": true}""",
            PositionX = 900,
            PositionY = 200,
            SortOrder = 5,
            WorkflowDefinitionId = workflow.Id
        };

        var escalateStep = new WorkflowStep
        {
            Name = "escalate-to-human",
            StepType = StepType.Escalate,
            ConfigurationJson = """{"targetGroup": "Level 2 Support", "preserveWorkNotes": true}""",
            PositionX = 500,
            PositionY = 450,
            SortOrder = 6,
            WorkflowDefinitionId = workflow.Id
        };

        var closeStep = new WorkflowStep
        {
            Name = "close-ticket",
            StepType = StepType.UpdateTicket,
            ConfigurationJson = """{"state": "resolved", "closeCode": "automated", "addResolutionNotes": true}""",
            PositionX = 900,
            PositionY = 400,
            SortOrder = 7,
            WorkflowDefinitionId = workflow.Id
        };

        _context.WorkflowSteps.AddRange(new[] { triggerStep, classifyStep, validateStep, executeStep, notifyStep, escalateStep, closeStep });
        await _context.SaveChangesAsync();

        // Create transitions
        var transitions = new List<StepTransition>
        {
            // trigger-start → classify-ticket
            new() { FromStepId = triggerStep.Id, ToStepId = classifyStep.Id, Label = "start", OutputIndex = 0, SortOrder = 1 },

            // classify-ticket → validate-request (high confidence) - output_1
            new() { FromStepId = classifyStep.Id, ToStepId = validateStep.Id, Condition = "confidence >= 0.8", Label = "high-confidence", OutputIndex = 0, SortOrder = 1 },

            // classify-ticket → escalate-to-human (low confidence) - output_2
            new() { FromStepId = classifyStep.Id, ToStepId = escalateStep.Id, Condition = "confidence < 0.8", Label = "low-confidence", OutputIndex = 1, SortOrder = 2 },

            // validate-request → execute-reset (valid) - output_1
            new() { FromStepId = validateStep.Id, ToStepId = executeStep.Id, Condition = "valid == true", Label = "valid", OutputIndex = 0, SortOrder = 1 },

            // validate-request → escalate-to-human (invalid) - output_2
            new() { FromStepId = validateStep.Id, ToStepId = escalateStep.Id, Condition = "valid == false", Label = "invalid", OutputIndex = 1, SortOrder = 2 },

            // execute-reset → notify-user (success) - output_1
            new() { FromStepId = executeStep.Id, ToStepId = notifyStep.Id, Condition = "success == true", Label = "success", OutputIndex = 0, SortOrder = 1 },

            // execute-reset → escalate-to-human (failure) - output_2
            new() { FromStepId = executeStep.Id, ToStepId = escalateStep.Id, Condition = "success == false", Label = "failure", OutputIndex = 1, SortOrder = 2 },

            // notify-user → close-ticket
            new() { FromStepId = notifyStep.Id, ToStepId = closeStep.Id, Label = "done", OutputIndex = 0, SortOrder = 1 },

            // escalate-to-human → close-ticket
            new() { FromStepId = escalateStep.Id, ToStepId = closeStep.Id, Label = "escalated", OutputIndex = 0, SortOrder = 1 }
        };

        _context.StepTransitions.AddRange(transitions);
        await _context.SaveChangesAsync();

        // Create workflow-level ruleset mappings
        var workflowRulesetMappings = new List<WorkflowRulesetMapping>
        {
            new() { WorkflowDefinitionId = workflow.Id, RulesetId = securityDefaults.Id, Priority = 100, IsEnabled = true },
            new() { WorkflowDefinitionId = workflow.Id, RulesetId = auditRules.Id, Priority = 200, IsEnabled = true }
        };
        _context.WorkflowRulesetMappings.AddRange(workflowRulesetMappings);

        // Create step-level ruleset mappings
        var stepRulesetMappings = new List<StepRulesetMapping>
        {
            new() { WorkflowStepId = classifyStep.Id, RulesetId = classificationRules.Id, Priority = 100, IsEnabled = true },
            new() { WorkflowStepId = validateStep.Id, RulesetId = securityRules.Id, Priority = 100, IsEnabled = true },
            new() { WorkflowStepId = executeStep.Id, RulesetId = securityRules.Id, Priority = 100, IsEnabled = true },
            new() { WorkflowStepId = notifyStep.Id, RulesetId = communicationRules.Id, Priority = 100, IsEnabled = true }
        };
        _context.StepRulesetMappings.AddRange(stepRulesetMappings);
        await _context.SaveChangesAsync();

        // Link workflow to test-agent
        agent.WorkflowDefinitionId = workflow.Id;
        agent.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Successfully seeded workflow '{WorkflowName}' with {StepCount} steps and linked to agent '{AgentName}'",
            workflowName, 7, agent.Name);
    }

    private async Task<Agent> EnsureTestAgentExistsAsync()
    {
        var agent = await _context.Agents.FirstOrDefaultAsync(a => a.Name == "test-agent");
        if (agent != null) return agent;

        // Get service accounts for LLM and ServiceNow (if they exist)
        var llmAccount = await _context.ServiceAccounts
            .FirstOrDefaultAsync(s => s.Provider.StartsWith("llm-"));
        var snowAccount = await _context.ServiceAccounts
            .FirstOrDefaultAsync(s => s.Provider == "servicenow");

        agent = new Agent
        {
            Name = "test-agent",
            DisplayName = "Test Helpdesk Agent",
            Description = "Test agent for development and validation",
            IsEnabled = true,
            LlmServiceAccountId = llmAccount?.Id,
            ServiceNowAccountId = snowAccount?.Id,
            AssignmentGroup = "Helpdesk",
            Status = AgentStatus.Stopped
        };

        _context.Agents.Add(agent);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created test agent: {AgentName}", agent.Name);
        return agent;
    }
}
