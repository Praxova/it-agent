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

        // Sub-workflows (no Trigger/Classify — used by dispatcher)
        var pwResetSub = await SeedPasswordResetSubWorkflow();
        var groupMembershipSub = await SeedGroupMembershipSubWorkflow();
        var filePermissionsSub = await SeedFilePermissionsSubWorkflow();

        // Dispatcher workflow (routes to sub-workflows)
        if (pwResetSub != null && groupMembershipSub != null && filePermissionsSub != null)
        {
            await SeedDispatcherWorkflow(pwResetSub, groupMembershipSub, filePermissionsSub);
        }

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
                ConfigurationJson = """{"use_example_set": "password-reset-examples", "model": "claude-sonnet-4-5", "temperature": 0.0, "extract_fields": ["affected_user", "caller_name", "confidence", "should_escalate", "escalation_reason"]}""",
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
            ConfigurationJson = """{"use_example_set": "password-reset-examples"}""",
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

    private async Task SeedDispatcherWorkflow(
        WorkflowDefinition pwResetSub,
        WorkflowDefinition groupMembershipSub,
        WorkflowDefinition filePermissionsSub)
    {
        const string name = "it-dispatch";

        var existing = await _context.WorkflowDefinitions
            .FirstOrDefaultAsync(w => w.Name == name);
        if (existing != null) return;

        var classificationRules = await _context.Rulesets
            .FirstOrDefaultAsync(r => r.Name == "classification-rules");
        var securityDefaults = await _context.Rulesets
            .FirstOrDefaultAsync(r => r.Name == "security-defaults");

        // Get example set for classification
        var exampleSet = await _context.ExampleSets
            .FirstOrDefaultAsync(e => e.Name == "it-dispatch-classification");

        var workflow = new WorkflowDefinition
        {
            Name = name,
            DisplayName = "IT Helpdesk Dispatcher",
            Description = "Central dispatcher that classifies incoming tickets and routes to specialized sub-workflows. Single classification pass, dynamic routing.",
            Version = "1.0.0",
            TriggerType = "ServiceNow",
            TriggerConfigJson = """{"pollIntervalSeconds": 30}""",
            ExampleSetId = exampleSet?.Id,
            IsActive = true,
            IsBuiltIn = true
        };

        _context.WorkflowDefinitions.Add(workflow);
        await _context.SaveChangesAsync();

        // === Steps ===

        var trigger = new WorkflowStep
        {
            Name = "trigger",
            DisplayName = "Ticket Received",
            StepType = StepType.Trigger,
            ConfigurationJson = """{"source": "servicenow"}""",
            PositionX = 100, PositionY = 300, SortOrder = 1,
            WorkflowDefinitionId = workflow.Id
        };

        var classify = new WorkflowStep
        {
            Name = "classify",
            DisplayName = "Classify Ticket",
            StepType = StepType.Classify,
            ConfigurationJson = """{"use_example_set": "it-dispatch-classification", "extract_fields": ["ticket_type", "affected_user", "caller_name", "confidence", "should_escalate", "escalation_reason"]}""",
            PositionX = 350, PositionY = 300, SortOrder = 2,
            WorkflowDefinitionId = workflow.Id
        };

        var approveClassification = new WorkflowStep
        {
            Name = "approve-classification",
            DisplayName = "Approve Classification",
            StepType = StepType.Approval,
            ConfigurationJson = """{"description_template": "Classified as {{ticket_type}} (confidence: {{confidence}}). Affected user: {{affected_user}}. Proposed action: route to {{ticket_type}} sub-workflow.", "auto_approve_threshold": 0.99, "timeout_minutes": 60, "timeout_action": "escalate", "context_fields_to_display": ["ticket_type", "confidence", "affected_user", "reasoning"]}""",
            PositionX = 550, PositionY = 300, SortOrder = 3,
            WorkflowDefinitionId = workflow.Id
        };

        // SubWorkflow steps — each references a sub-workflow by name and ID
        var subPwReset = new WorkflowStep
        {
            Name = "sub-password-reset",
            DisplayName = "Password Reset",
            StepType = StepType.SubWorkflow,
            ConfigurationJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                workflow_id = pwResetSub.Id.ToString(),
                workflow_name = pwResetSub.Name
            }),
            PositionX = 800, PositionY = 150, SortOrder = 4,
            WorkflowDefinitionId = workflow.Id
        };

        var subGroupMembership = new WorkflowStep
        {
            Name = "sub-group-membership",
            DisplayName = "Group Membership",
            StepType = StepType.SubWorkflow,
            ConfigurationJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                workflow_id = groupMembershipSub.Id.ToString(),
                workflow_name = groupMembershipSub.Name
            }),
            PositionX = 800, PositionY = 300, SortOrder = 5,
            WorkflowDefinitionId = workflow.Id
        };

        var subFilePermissions = new WorkflowStep
        {
            Name = "sub-file-permissions",
            DisplayName = "File Permissions",
            StepType = StepType.SubWorkflow,
            ConfigurationJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                workflow_id = filePermissionsSub.Id.ToString(),
                workflow_name = filePermissionsSub.Name
            }),
            PositionX = 800, PositionY = 450, SortOrder = 6,
            WorkflowDefinitionId = workflow.Id
        };

        var escalate = new WorkflowStep
        {
            Name = "escalate-unknown",
            DisplayName = "Escalate Unknown",
            StepType = StepType.Escalate,
            ConfigurationJson = """{"targetGroup": "Level 2 Support", "reason": "Unrecognized ticket type"}""",
            PositionX = 800, PositionY = 600, SortOrder = 7,
            WorkflowDefinitionId = workflow.Id
        };

        var endSuccess = new WorkflowStep
        {
            Name = "end-success",
            DisplayName = "Resolved",
            StepType = StepType.End,
            ConfigurationJson = """{"status": "resolved"}""",
            PositionX = 1100, PositionY = 300, SortOrder = 8,
            WorkflowDefinitionId = workflow.Id
        };

        var endEscalated = new WorkflowStep
        {
            Name = "end-escalated",
            DisplayName = "Escalated",
            StepType = StepType.End,
            ConfigurationJson = """{"status": "pending"}""",
            PositionX = 1100, PositionY = 600, SortOrder = 9,
            WorkflowDefinitionId = workflow.Id
        };

        _context.WorkflowSteps.AddRange(new[]
        {
            trigger, classify, approveClassification,
            subPwReset, subGroupMembership, subFilePermissions,
            escalate, endSuccess, endEscalated
        });
        await _context.SaveChangesAsync();

        // === Transitions ===
        var transitions = new List<StepTransition>
        {
            // Trigger → Classify
            new() { FromStepId = trigger.Id, ToStepId = classify.Id,
                    Label = "start", OutputIndex = 0 },

            // Classify → Approval (high confidence)
            new() { FromStepId = classify.Id, ToStepId = approveClassification.Id,
                    Condition = "confidence >= 0.7",
                    Label = "high-confidence", OutputIndex = 0 },

            // Classify → Escalate (unknown type or low confidence)
            new() { FromStepId = classify.Id, ToStepId = escalate.Id,
                    Condition = "ticket_type == 'unknown' or confidence < 0.7",
                    Label = "unknown", OutputIndex = 1 },

            // Approval → SubWorkflows (approved, routed by ticket_type)
            new() { FromStepId = approveClassification.Id, ToStepId = subPwReset.Id,
                    Condition = "ticket_type == 'password-reset'",
                    Label = "password-reset", OutputIndex = 0 },

            new() { FromStepId = approveClassification.Id, ToStepId = subGroupMembership.Id,
                    Condition = "ticket_type == 'group-membership'",
                    Label = "group-membership", OutputIndex = 0 },

            new() { FromStepId = approveClassification.Id, ToStepId = subFilePermissions.Id,
                    Condition = "ticket_type == 'file-permissions'",
                    Label = "file-permissions", OutputIndex = 0 },

            // Approval → Escalate (rejected)
            new() { FromStepId = approveClassification.Id, ToStepId = escalate.Id,
                    Label = "rejected", OutputIndex = 1 },

            // SubWorkflow completed → End-Success
            new() { FromStepId = subPwReset.Id, ToStepId = endSuccess.Id,
                    Condition = "outcome == 'completed'",
                    Label = "completed", OutputIndex = 0 },
            new() { FromStepId = subGroupMembership.Id, ToStepId = endSuccess.Id,
                    Condition = "outcome == 'completed'",
                    Label = "completed", OutputIndex = 0 },
            new() { FromStepId = subFilePermissions.Id, ToStepId = endSuccess.Id,
                    Condition = "outcome == 'completed'",
                    Label = "completed", OutputIndex = 0 },

            // SubWorkflow escalated → End-Escalated
            new() { FromStepId = subPwReset.Id, ToStepId = endEscalated.Id,
                    Condition = "outcome == 'escalated'",
                    Label = "escalated", OutputIndex = 1 },
            new() { FromStepId = subGroupMembership.Id, ToStepId = endEscalated.Id,
                    Condition = "outcome == 'escalated'",
                    Label = "escalated", OutputIndex = 1 },
            new() { FromStepId = subFilePermissions.Id, ToStepId = endEscalated.Id,
                    Condition = "outcome == 'escalated'",
                    Label = "escalated", OutputIndex = 1 },

            // Escalate → End-Escalated
            new() { FromStepId = escalate.Id, ToStepId = endEscalated.Id,
                    Label = "escalated", OutputIndex = 0 },
        };

        _context.StepTransitions.AddRange(transitions);

        // Workflow-level rulesets
        if (securityDefaults != null)
        {
            _context.WorkflowRulesetMappings.Add(new WorkflowRulesetMapping
            {
                WorkflowDefinitionId = workflow.Id,
                RulesetId = securityDefaults.Id,
                Priority = 100, IsEnabled = true
            });
        }
        // Step-level rulesets
        if (classificationRules != null)
        {
            _context.StepRulesetMappings.Add(new StepRulesetMapping
            {
                WorkflowStepId = classify.Id,
                RulesetId = classificationRules.Id,
                Priority = 100, IsEnabled = true
            });
        }

        await _context.SaveChangesAsync();

        // Link dispatcher to test-agent
        var agent = await _context.Agents.FirstOrDefaultAsync(a => a.Name == "test-agent");
        if (agent != null)
        {
            agent.WorkflowDefinitionId = workflow.Id;
            agent.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            _logger.LogInformation("Linked dispatcher workflow to test-agent");
        }

        _logger.LogInformation("Seeded dispatcher workflow: {Name} with 9 steps, 14 transitions", name);
    }

    private async Task<WorkflowDefinition?> SeedPasswordResetSubWorkflow()
    {
        const string name = "pw-reset-sub";

        var existing = await _context.WorkflowDefinitions
            .FirstOrDefaultAsync(w => w.Name == name);
        if (existing != null) return existing;

        var securityRules = await _context.Rulesets
            .FirstOrDefaultAsync(r => r.Name == "security-rules");
        var communicationRules = await _context.Rulesets
            .FirstOrDefaultAsync(r => r.Name == "communication-rules");

        var workflow = new WorkflowDefinition
        {
            Name = name,
            DisplayName = "Password Reset (Sub-Workflow)",
            Description = "Specialized password reset logic. Used as sub-workflow in dispatcher — no Trigger or Classify steps.",
            Version = "1.0.0",
            IsActive = true,
            IsBuiltIn = true
        };

        _context.WorkflowDefinitions.Add(workflow);
        await _context.SaveChangesAsync();

        // Steps: Validate → [Approval] → Execute → Notify → End-Success
        //                                        ↘ Escalate → End-Escalated
        var validate = new WorkflowStep
        {
            Name = "validate-request",
            DisplayName = "Validate Password Reset",
            StepType = StepType.Validate,
            ConfigurationJson = """{"checks": ["user_exists", "not_admin", "requester_authorized"]}""",
            PositionX = 100, PositionY = 200, SortOrder = 1,
            WorkflowDefinitionId = workflow.Id
        };

        var approveReset = new WorkflowStep
        {
            Name = "approve-reset",
            DisplayName = "Approve Password Reset",
            StepType = StepType.Approval,
            ConfigurationJson = """{"description_template": "Reset password for {{affected_user}}. Tool: ad-password-reset.", "auto_approve_threshold": 0.99, "timeout_minutes": 30, "timeout_action": "escalate", "context_fields_to_display": ["affected_user", "ticket_type", "confidence"]}""",
            PositionX = 250, PositionY = 200, SortOrder = 2,
            WorkflowDefinitionId = workflow.Id
        };

        var execute = new WorkflowStep
        {
            Name = "execute-reset",
            DisplayName = "Reset Password",
            StepType = StepType.Execute,
            ConfigurationJson = """{"capability": "ad-password-reset", "generateTempPassword": true}""",
            PositionX = 450, PositionY = 200, SortOrder = 3,
            WorkflowDefinitionId = workflow.Id
        };

        var notify = new WorkflowStep
        {
            Name = "notify-user",
            DisplayName = "Notify User",
            StepType = StepType.Notify,
            ConfigurationJson = """{"template": "password-reset-success", "channel": "ticket-comment", "includeTempPassword": true}""",
            PositionX = 700, PositionY = 200, SortOrder = 4,
            WorkflowDefinitionId = workflow.Id
        };

        var escalate = new WorkflowStep
        {
            Name = "escalate",
            DisplayName = "Escalate",
            StepType = StepType.Escalate,
            ConfigurationJson = """{"targetGroup": "Level 2 Support", "preserveWorkNotes": true}""",
            PositionX = 450, PositionY = 400, SortOrder = 5,
            WorkflowDefinitionId = workflow.Id
        };

        var endSuccess = new WorkflowStep
        {
            Name = "end-success",
            DisplayName = "Completed",
            StepType = StepType.End,
            ConfigurationJson = """{"status": "resolved", "resolution_code": "Automated - Password Reset"}""",
            PositionX = 950, PositionY = 200, SortOrder = 6,
            WorkflowDefinitionId = workflow.Id
        };

        var endEscalated = new WorkflowStep
        {
            Name = "end-escalated",
            DisplayName = "Escalated",
            StepType = StepType.End,
            ConfigurationJson = """{"status": "pending", "resolution_code": "Escalated"}""",
            PositionX = 700, PositionY = 400, SortOrder = 7,
            WorkflowDefinitionId = workflow.Id
        };

        _context.WorkflowSteps.AddRange(new[] { validate, approveReset, execute, notify, escalate, endSuccess, endEscalated });
        await _context.SaveChangesAsync();

        var transitions = new List<StepTransition>
        {
            new() { FromStepId = validate.Id, ToStepId = approveReset.Id, Condition = "valid == true", Label = "valid", OutputIndex = 0 },
            new() { FromStepId = validate.Id, ToStepId = escalate.Id, Condition = "valid == false", Label = "invalid", OutputIndex = 1 },
            new() { FromStepId = approveReset.Id, ToStepId = execute.Id, Label = "approved", OutputIndex = 0 },
            new() { FromStepId = approveReset.Id, ToStepId = escalate.Id, Label = "rejected", OutputIndex = 1 },
            new() { FromStepId = execute.Id, ToStepId = notify.Id, Condition = "success == true", Label = "success", OutputIndex = 0 },
            new() { FromStepId = execute.Id, ToStepId = escalate.Id, Condition = "success == false", Label = "failed", OutputIndex = 1 },
            new() { FromStepId = notify.Id, ToStepId = endSuccess.Id, Label = "done", OutputIndex = 0 },
            new() { FromStepId = escalate.Id, ToStepId = endEscalated.Id, Label = "escalated", OutputIndex = 0 },
        };

        _context.StepTransitions.AddRange(transitions);

        // Step-level ruleset mappings
        if (securityRules != null)
        {
            _context.StepRulesetMappings.AddRange(new[]
            {
                new StepRulesetMapping { WorkflowStepId = validate.Id, RulesetId = securityRules.Id, Priority = 100, IsEnabled = true },
                new StepRulesetMapping { WorkflowStepId = execute.Id, RulesetId = securityRules.Id, Priority = 100, IsEnabled = true },
            });
        }
        if (communicationRules != null)
        {
            _context.StepRulesetMappings.Add(
                new StepRulesetMapping { WorkflowStepId = notify.Id, RulesetId = communicationRules.Id, Priority = 100, IsEnabled = true }
            );
        }

        await _context.SaveChangesAsync();
        _logger.LogInformation("Seeded sub-workflow: {Name} with 7 steps", name);
        return workflow;
    }

    private async Task<WorkflowDefinition?> SeedGroupMembershipSubWorkflow()
    {
        const string name = "group-membership-sub";

        var existing = await _context.WorkflowDefinitions
            .FirstOrDefaultAsync(w => w.Name == name);
        if (existing != null) return existing;

        var securityRules = await _context.Rulesets
            .FirstOrDefaultAsync(r => r.Name == "security-rules");
        var communicationRules = await _context.Rulesets
            .FirstOrDefaultAsync(r => r.Name == "communication-rules");

        var workflow = new WorkflowDefinition
        {
            Name = name,
            DisplayName = "Group Membership (Sub-Workflow)",
            Description = "Specialized group membership logic. Used as sub-workflow in dispatcher — no Trigger or Classify steps.",
            Version = "1.0.0",
            IsActive = true,
            IsBuiltIn = true
        };

        _context.WorkflowDefinitions.Add(workflow);
        await _context.SaveChangesAsync();

        var validate = new WorkflowStep
        {
            Name = "validate-request",
            DisplayName = "Validate Group Request",
            StepType = StepType.Validate,
            ConfigurationJson = """{"checks": ["user_exists", "group_exists", "requester_authorized"]}""",
            PositionX = 100, PositionY = 200, SortOrder = 1,
            WorkflowDefinitionId = workflow.Id
        };

        var execute = new WorkflowStep
        {
            Name = "execute-group-change",
            DisplayName = "Modify Group Membership",
            StepType = StepType.Execute,
            ConfigurationJson = """{"capability": "ad-group-add", "action": "add"}""",
            PositionX = 350, PositionY = 200, SortOrder = 2,
            WorkflowDefinitionId = workflow.Id
        };

        var notify = new WorkflowStep
        {
            Name = "notify-user",
            DisplayName = "Notify User",
            StepType = StepType.Notify,
            ConfigurationJson = """{"template": "group-membership-success", "channel": "ticket-comment"}""",
            PositionX = 600, PositionY = 200, SortOrder = 3,
            WorkflowDefinitionId = workflow.Id
        };

        var escalate = new WorkflowStep
        {
            Name = "escalate",
            DisplayName = "Escalate",
            StepType = StepType.Escalate,
            ConfigurationJson = """{"targetGroup": "Level 2 Support", "preserveWorkNotes": true}""",
            PositionX = 350, PositionY = 400, SortOrder = 4,
            WorkflowDefinitionId = workflow.Id
        };

        var endSuccess = new WorkflowStep
        {
            Name = "end-success",
            DisplayName = "Completed",
            StepType = StepType.End,
            ConfigurationJson = """{"status": "resolved", "resolution_code": "Automated - Group Membership Change"}""",
            PositionX = 850, PositionY = 200, SortOrder = 5,
            WorkflowDefinitionId = workflow.Id
        };

        var endEscalated = new WorkflowStep
        {
            Name = "end-escalated",
            DisplayName = "Escalated",
            StepType = StepType.End,
            ConfigurationJson = """{"status": "pending", "resolution_code": "Escalated"}""",
            PositionX = 600, PositionY = 400, SortOrder = 6,
            WorkflowDefinitionId = workflow.Id
        };

        _context.WorkflowSteps.AddRange(new[] { validate, execute, notify, escalate, endSuccess, endEscalated });
        await _context.SaveChangesAsync();

        var transitions = new List<StepTransition>
        {
            new() { FromStepId = validate.Id, ToStepId = execute.Id, Condition = "valid == true", Label = "valid", OutputIndex = 0 },
            new() { FromStepId = validate.Id, ToStepId = escalate.Id, Condition = "valid == false", Label = "invalid", OutputIndex = 1 },
            new() { FromStepId = execute.Id, ToStepId = notify.Id, Condition = "success == true", Label = "success", OutputIndex = 0 },
            new() { FromStepId = execute.Id, ToStepId = escalate.Id, Condition = "success == false", Label = "failed", OutputIndex = 1 },
            new() { FromStepId = notify.Id, ToStepId = endSuccess.Id, Label = "done", OutputIndex = 0 },
            new() { FromStepId = escalate.Id, ToStepId = endEscalated.Id, Label = "escalated", OutputIndex = 0 },
        };

        _context.StepTransitions.AddRange(transitions);

        if (securityRules != null)
        {
            _context.StepRulesetMappings.AddRange(new[]
            {
                new StepRulesetMapping { WorkflowStepId = validate.Id, RulesetId = securityRules.Id, Priority = 100, IsEnabled = true },
                new StepRulesetMapping { WorkflowStepId = execute.Id, RulesetId = securityRules.Id, Priority = 100, IsEnabled = true },
            });
        }
        if (communicationRules != null)
        {
            _context.StepRulesetMappings.Add(
                new StepRulesetMapping { WorkflowStepId = notify.Id, RulesetId = communicationRules.Id, Priority = 100, IsEnabled = true }
            );
        }

        await _context.SaveChangesAsync();
        _logger.LogInformation("Seeded sub-workflow: {Name} with 6 steps", name);
        return workflow;
    }

    private async Task<WorkflowDefinition?> SeedFilePermissionsSubWorkflow()
    {
        const string name = "file-permissions-sub";

        var existing = await _context.WorkflowDefinitions
            .FirstOrDefaultAsync(w => w.Name == name);
        if (existing != null) return existing;

        var securityRules = await _context.Rulesets
            .FirstOrDefaultAsync(r => r.Name == "security-rules");
        var communicationRules = await _context.Rulesets
            .FirstOrDefaultAsync(r => r.Name == "communication-rules");

        var workflow = new WorkflowDefinition
        {
            Name = name,
            DisplayName = "File Permissions (Sub-Workflow)",
            Description = "Specialized file permissions logic. Used as sub-workflow in dispatcher — no Trigger or Classify steps.",
            Version = "1.0.0",
            IsActive = true,
            IsBuiltIn = true
        };

        _context.WorkflowDefinitions.Add(workflow);
        await _context.SaveChangesAsync();

        var validate = new WorkflowStep
        {
            Name = "validate-request",
            DisplayName = "Validate Permissions Request",
            StepType = StepType.Validate,
            ConfigurationJson = """{"checks": ["user_exists", "path_exists", "requester_authorized"]}""",
            PositionX = 100, PositionY = 200, SortOrder = 1,
            WorkflowDefinitionId = workflow.Id
        };

        var execute = new WorkflowStep
        {
            Name = "execute-permissions",
            DisplayName = "Set File Permissions",
            StepType = StepType.Execute,
            ConfigurationJson = """{"capability": "ntfs-permission-grant", "permission": "read"}""",
            PositionX = 350, PositionY = 200, SortOrder = 2,
            WorkflowDefinitionId = workflow.Id
        };

        var notify = new WorkflowStep
        {
            Name = "notify-user",
            DisplayName = "Notify User",
            StepType = StepType.Notify,
            ConfigurationJson = """{"template": "file-permissions-success", "channel": "ticket-comment"}""",
            PositionX = 600, PositionY = 200, SortOrder = 3,
            WorkflowDefinitionId = workflow.Id
        };

        var escalate = new WorkflowStep
        {
            Name = "escalate",
            DisplayName = "Escalate",
            StepType = StepType.Escalate,
            ConfigurationJson = """{"targetGroup": "Level 2 Support", "preserveWorkNotes": true}""",
            PositionX = 350, PositionY = 400, SortOrder = 4,
            WorkflowDefinitionId = workflow.Id
        };

        var endSuccess = new WorkflowStep
        {
            Name = "end-success",
            DisplayName = "Completed",
            StepType = StepType.End,
            ConfigurationJson = """{"status": "resolved", "resolution_code": "Automated - File Permissions Change"}""",
            PositionX = 850, PositionY = 200, SortOrder = 5,
            WorkflowDefinitionId = workflow.Id
        };

        var endEscalated = new WorkflowStep
        {
            Name = "end-escalated",
            DisplayName = "Escalated",
            StepType = StepType.End,
            ConfigurationJson = """{"status": "pending", "resolution_code": "Escalated"}""",
            PositionX = 600, PositionY = 400, SortOrder = 6,
            WorkflowDefinitionId = workflow.Id
        };

        _context.WorkflowSteps.AddRange(new[] { validate, execute, notify, escalate, endSuccess, endEscalated });
        await _context.SaveChangesAsync();

        var transitions = new List<StepTransition>
        {
            new() { FromStepId = validate.Id, ToStepId = execute.Id, Condition = "valid == true", Label = "valid", OutputIndex = 0 },
            new() { FromStepId = validate.Id, ToStepId = escalate.Id, Condition = "valid == false", Label = "invalid", OutputIndex = 1 },
            new() { FromStepId = execute.Id, ToStepId = notify.Id, Condition = "success == true", Label = "success", OutputIndex = 0 },
            new() { FromStepId = execute.Id, ToStepId = escalate.Id, Condition = "success == false", Label = "failed", OutputIndex = 1 },
            new() { FromStepId = notify.Id, ToStepId = endSuccess.Id, Label = "done", OutputIndex = 0 },
            new() { FromStepId = escalate.Id, ToStepId = endEscalated.Id, Label = "escalated", OutputIndex = 0 },
        };

        _context.StepTransitions.AddRange(transitions);

        if (securityRules != null)
        {
            _context.StepRulesetMappings.AddRange(new[]
            {
                new StepRulesetMapping { WorkflowStepId = validate.Id, RulesetId = securityRules.Id, Priority = 100, IsEnabled = true },
                new StepRulesetMapping { WorkflowStepId = execute.Id, RulesetId = securityRules.Id, Priority = 100, IsEnabled = true },
            });
        }
        if (communicationRules != null)
        {
            _context.StepRulesetMappings.Add(
                new StepRulesetMapping { WorkflowStepId = notify.Id, RulesetId = communicationRules.Id, Priority = 100, IsEnabled = true }
            );
        }

        await _context.SaveChangesAsync();
        _logger.LogInformation("Seeded sub-workflow: {Name} with 6 steps", name);
        return workflow;
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
