# Workflow Designer Entity Models

## Overview

These entities support the visual workflow designer, rules editor, and examples editor in the Admin Portal.

## Entity Relationship Diagram

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│                                                                                 │
│  Agent (existing)                                                               │
│    │                                                                            │
│    └──── WorkflowDefinitionId (FK, optional) ────▶ WorkflowDefinition           │
│                                                         │                       │
│                                                         ├─── ExampleSetId (FK)  │
│                                                         │         │             │
│                                                         │         ▼             │
│                                                         │    ExampleSet         │
│                                                         │         │             │
│                                                         │         └─── Example[]│
│                                                         │                       │
│                                                         ├─── WorkflowStep[]     │
│                                                         │         │             │
│                                                         │         ├─── StepTransition[]
│                                                         │         │             │
│                                                         │         └─── StepRulesetMapping[]
│                                                         │                   │   │
│                                                         │                   │   │
│                                                         └─── WorkflowRulesetMapping[]
│                                                                         │       │
│                                                                         ▼       │
│                                                                    Ruleset      │
│                                                                         │       │
│                                                                         └─── Rule[]
│                                                                                 │
└─────────────────────────────────────────────────────────────────────────────────┘
```

## Core Entities

### Ruleset

```csharp
// LucidAdmin.Core/Entities/Ruleset.cs

namespace LucidAdmin.Core.Entities;

/// <summary>
/// A collection of rules that define behavioral constraints for agent workflows.
/// Rulesets can be reused across multiple workflows and steps.
/// </summary>
public class Ruleset : BaseEntity
{
    /// <summary>
    /// Unique name for the ruleset (e.g., "security-rules", "escalation-rules").
    /// </summary>
    public required string Name { get; set; }
    
    /// <summary>
    /// Human-friendly display name.
    /// </summary>
    public string? DisplayName { get; set; }
    
    /// <summary>
    /// Description of what this ruleset does.
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// Category for organization (Security, Escalation, Validation, Communication).
    /// </summary>
    public required RulesetCategory Category { get; set; }
    
    /// <summary>
    /// Whether this ruleset ships with the product (vs user-created).
    /// Built-in rulesets cannot be deleted, only cloned.
    /// </summary>
    public bool IsBuiltIn { get; set; } = false;
    
    /// <summary>
    /// Whether this ruleset is active and can be used.
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    /// <summary>
    /// Order for applying this ruleset relative to others (lower = first).
    /// </summary>
    public int Priority { get; set; } = 100;
    
    // Navigation properties
    public ICollection<Rule> Rules { get; set; } = new List<Rule>();
    public ICollection<WorkflowRulesetMapping> WorkflowMappings { get; set; } = new List<WorkflowRulesetMapping>();
    public ICollection<StepRulesetMapping> StepMappings { get; set; } = new List<StepRulesetMapping>();
}

/// <summary>
/// Categories for organizing rulesets.
/// </summary>
public enum RulesetCategory
{
    /// <summary>Security rules (deny lists, privileged accounts, etc.)</summary>
    Security,
    
    /// <summary>Escalation rules (confidence thresholds, unknown types, etc.)</summary>
    Escalation,
    
    /// <summary>Validation rules (required fields, format checks, etc.)</summary>
    Validation,
    
    /// <summary>Communication rules (message templates, tone, etc.)</summary>
    Communication,
    
    /// <summary>General/other rules</summary>
    General
}
```

### Rule

```csharp
// LucidAdmin.Core/Entities/Rule.cs

namespace LucidAdmin.Core.Entities;

/// <summary>
/// An individual rule within a ruleset.
/// Maps to a Griptape Rule object.
/// </summary>
public class Rule : BaseEntity
{
    /// <summary>
    /// Reference to the parent ruleset.
    /// </summary>
    public Guid RulesetId { get; set; }
    
    /// <summary>
    /// Short name for the rule (e.g., "no-admin-password-reset").
    /// </summary>
    public required string Name { get; set; }
    
    /// <summary>
    /// The actual rule text that will be passed to the LLM.
    /// Example: "Never reset passwords for accounts ending in '-admin' or '-svc'."
    /// </summary>
    public required string RuleText { get; set; }
    
    /// <summary>
    /// Optional description explaining why this rule exists.
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// Order within the ruleset (lower = first).
    /// </summary>
    public int SortOrder { get; set; } = 0;
    
    /// <summary>
    /// Whether this rule is active.
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    /// <summary>
    /// Which step types this rule applies to (null = all steps).
    /// Comma-separated list: "classify,validate" or null for all.
    /// </summary>
    public string? AppliesTo { get; set; }
    
    // Navigation property
    public Ruleset? Ruleset { get; set; }
}
```

### ExampleSet

```csharp
// LucidAdmin.Core/Entities/ExampleSet.cs

namespace LucidAdmin.Core.Entities;

/// <summary>
/// A collection of few-shot examples for training the LLM classifier.
/// </summary>
public class ExampleSet : BaseEntity
{
    /// <summary>
    /// Unique name for the example set.
    /// </summary>
    public required string Name { get; set; }
    
    /// <summary>
    /// Human-friendly display name.
    /// </summary>
    public string? DisplayName { get; set; }
    
    /// <summary>
    /// Description of what ticket types this example set covers.
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// Whether this example set ships with the product.
    /// </summary>
    public bool IsBuiltIn { get; set; } = false;
    
    /// <summary>
    /// Whether this example set is active.
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    /// <summary>
    /// Comma-separated list of ticket types this set covers.
    /// Example: "password_reset,group_access_add,group_access_remove"
    /// </summary>
    public string? CoveredTicketTypes { get; set; }
    
    // Navigation properties
    public ICollection<Example> Examples { get; set; } = new List<Example>();
    public ICollection<WorkflowDefinition> Workflows { get; set; } = new List<WorkflowDefinition>();
}
```

### Example

```csharp
// LucidAdmin.Core/Entities/Example.cs

namespace LucidAdmin.Core.Entities;

/// <summary>
/// A single few-shot example for classifier training.
/// </summary>
public class Example : BaseEntity
{
    /// <summary>
    /// Reference to the parent example set.
    /// </summary>
    public Guid ExampleSetId { get; set; }
    
    /// <summary>
    /// Short name/identifier for this example.
    /// </summary>
    public required string Name { get; set; }
    
    /// <summary>
    /// The ticket short description (input).
    /// </summary>
    public required string ShortDescription { get; set; }
    
    /// <summary>
    /// The ticket full description (input).
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// The expected classification output as JSON.
    /// Contains: ticket_type, confidence, affected_user, target_group, etc.
    /// </summary>
    public required string ExpectedOutputJson { get; set; }
    
    /// <summary>
    /// Notes explaining why this example is useful for training.
    /// </summary>
    public string? Notes { get; set; }
    
    /// <summary>
    /// Order within the example set.
    /// </summary>
    public int SortOrder { get; set; } = 0;
    
    /// <summary>
    /// Whether this example is active.
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    // Navigation property
    public ExampleSet? ExampleSet { get; set; }
}
```

### WorkflowDefinition

```csharp
// LucidAdmin.Core/Entities/WorkflowDefinition.cs

namespace LucidAdmin.Core.Entities;

/// <summary>
/// A visual workflow definition that can be edited in the designer.
/// </summary>
public class WorkflowDefinition : BaseEntity
{
    /// <summary>
    /// Unique name for the workflow.
    /// </summary>
    public required string Name { get; set; }
    
    /// <summary>
    /// Human-friendly display name.
    /// </summary>
    public string? DisplayName { get; set; }
    
    /// <summary>
    /// Description of what this workflow does.
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// Version string for tracking changes.
    /// </summary>
    public string Version { get; set; } = "1.0.0";
    
    /// <summary>
    /// Whether this workflow ships with the product.
    /// </summary>
    public bool IsBuiltIn { get; set; } = false;
    
    /// <summary>
    /// Whether this workflow is active and can be assigned to agents.
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    // Trigger configuration
    /// <summary>
    /// Type of connector that triggers this workflow (ServiceNow, Jira, Email).
    /// </summary>
    public required string TriggerType { get; set; }
    
    /// <summary>
    /// Trigger-specific configuration as JSON.
    /// </summary>
    public string? TriggerConfigJson { get; set; }
    
    // Example set reference
    /// <summary>
    /// Which example set to use for classification.
    /// </summary>
    public Guid? ExampleSetId { get; set; }
    
    /// <summary>
    /// The full workflow layout as JSON (Drawflow export).
    /// Used to restore the visual designer state.
    /// </summary>
    public string? LayoutJson { get; set; }
    
    // Navigation properties
    public ExampleSet? ExampleSet { get; set; }
    public ICollection<WorkflowStep> Steps { get; set; } = new List<WorkflowStep>();
    public ICollection<WorkflowRulesetMapping> RulesetMappings { get; set; } = new List<WorkflowRulesetMapping>();
    public ICollection<Agent> Agents { get; set; } = new List<Agent>();
}
```

### WorkflowStep

```csharp
// LucidAdmin.Core/Entities/WorkflowStep.cs

namespace LucidAdmin.Core.Entities;

/// <summary>
/// A single step/node in a workflow.
/// </summary>
public class WorkflowStep : BaseEntity
{
    /// <summary>
    /// Reference to the parent workflow.
    /// </summary>
    public Guid WorkflowDefinitionId { get; set; }
    
    /// <summary>
    /// Name of this step (unique within workflow).
    /// </summary>
    public required string Name { get; set; }
    
    /// <summary>
    /// Type of step.
    /// </summary>
    public required WorkflowStepType StepType { get; set; }
    
    /// <summary>
    /// Step-specific configuration as JSON.
    /// </summary>
    public string? ConfigurationJson { get; set; }
    
    /// <summary>
    /// X position in the visual designer.
    /// </summary>
    public int PositionX { get; set; } = 100;
    
    /// <summary>
    /// Y position in the visual designer.
    /// </summary>
    public int PositionY { get; set; } = 100;
    
    /// <summary>
    /// Order of execution (for non-branching sequences).
    /// </summary>
    public int SortOrder { get; set; } = 0;
    
    // Navigation properties
    public WorkflowDefinition? WorkflowDefinition { get; set; }
    public ICollection<StepTransition> OutgoingTransitions { get; set; } = new List<StepTransition>();
    public ICollection<StepTransition> IncomingTransitions { get; set; } = new List<StepTransition>();
    public ICollection<StepRulesetMapping> RulesetMappings { get; set; } = new List<StepRulesetMapping>();
}

/// <summary>
/// Types of workflow steps.
/// </summary>
public enum WorkflowStepType
{
    /// <summary>Entry point - receives ticket from connector</summary>
    Trigger,
    
    /// <summary>LLM classification of ticket type</summary>
    Classify,
    
    /// <summary>Query tool server for information (ADR-009)</summary>
    Query,
    
    /// <summary>Validate inputs before execution</summary>
    Validate,
    
    /// <summary>Execute action via tool server</summary>
    Execute,
    
    /// <summary>Update ticket in source system</summary>
    UpdateTicket,
    
    /// <summary>Send notification (email, Teams, etc.)</summary>
    Notify,
    
    /// <summary>Escalate to human queue</summary>
    Escalate,
    
    /// <summary>Conditional branch based on expression</summary>
    Condition,
    
    /// <summary>End of workflow</summary>
    End
}
```

### StepTransition

```csharp
// LucidAdmin.Core/Entities/StepTransition.cs

namespace LucidAdmin.Core.Entities;

/// <summary>
/// A connection between two workflow steps.
/// </summary>
public class StepTransition : BaseEntity
{
    /// <summary>
    /// Source step ID.
    /// </summary>
    public Guid FromStepId { get; set; }
    
    /// <summary>
    /// Target step ID.
    /// </summary>
    public Guid ToStepId { get; set; }
    
    /// <summary>
    /// Label for this transition (e.g., "success", "failure", "escalate").
    /// </summary>
    public string? Label { get; set; }
    
    /// <summary>
    /// Condition expression for conditional transitions.
    /// Example: "confidence >= 0.8", "ticket_type == 'password_reset'"
    /// </summary>
    public string? Condition { get; set; }
    
    /// <summary>
    /// Order when multiple transitions leave same step.
    /// </summary>
    public int SortOrder { get; set; } = 0;
    
    // Navigation properties
    public WorkflowStep? FromStep { get; set; }
    public WorkflowStep? ToStep { get; set; }
}
```

### Mapping Tables

```csharp
// LucidAdmin.Core/Entities/WorkflowRulesetMapping.cs

namespace LucidAdmin.Core.Entities;

/// <summary>
/// Links a ruleset to a workflow (applies to all steps).
/// </summary>
public class WorkflowRulesetMapping : BaseEntity
{
    public Guid WorkflowDefinitionId { get; set; }
    public Guid RulesetId { get; set; }
    
    /// <summary>
    /// Order of ruleset application.
    /// </summary>
    public int Priority { get; set; } = 100;
    
    // Navigation properties
    public WorkflowDefinition? WorkflowDefinition { get; set; }
    public Ruleset? Ruleset { get; set; }
}

// LucidAdmin.Core/Entities/StepRulesetMapping.cs

namespace LucidAdmin.Core.Entities;

/// <summary>
/// Links a ruleset to a specific workflow step.
/// </summary>
public class StepRulesetMapping : BaseEntity
{
    public Guid WorkflowStepId { get; set; }
    public Guid RulesetId { get; set; }
    
    /// <summary>
    /// Order of ruleset application.
    /// </summary>
    public int Priority { get; set; } = 100;
    
    // Navigation properties
    public WorkflowStep? WorkflowStep { get; set; }
    public Ruleset? Ruleset { get; set; }
}
```

## Agent Entity Update

```csharp
// Add to existing Agent entity:

/// <summary>
/// Optional workflow definition for this agent.
/// If null, uses default hardcoded pipeline.
/// </summary>
public Guid? WorkflowDefinitionId { get; set; }

// Navigation property
public WorkflowDefinition? WorkflowDefinition { get; set; }
```

## Built-In Seed Data

### Security Ruleset
```
Name: security-rules
Category: Security
Rules:
  - "Never reset passwords for accounts ending in '-admin', '-svc', or 'service'."
  - "Never modify group membership for Domain Admins, Enterprise Admins, or Schema Admins."
  - "Always verify the requesting user has a manager listed in Active Directory."
  - "Flag any request involving accounts in the 'Executives' OU for manual review."
```

### Escalation Ruleset
```
Name: escalation-rules
Category: Escalation
Rules:
  - "Escalate to human queue if classification confidence is below 0.6."
  - "Escalate if ticket type is 'unknown' or cannot be determined."
  - "Escalate if the affected user cannot be found in Active Directory."
  - "Escalate if the requested resource does not exist."
```

### Password Reset Example Set
```
Name: password-reset-examples
Examples:
  - Input: "I forgot my password"
    Output: { ticket_type: "password_reset", confidence: 0.95 }
  - Input: "My account is locked out after too many failed attempts"
    Output: { ticket_type: "password_reset", confidence: 0.90 }
  - Input: "Han Solo's cube mate says he forgot his password and needs a temp one"
    Output: { ticket_type: "password_reset", confidence: 0.85, affected_user: "Han Solo" }
```
