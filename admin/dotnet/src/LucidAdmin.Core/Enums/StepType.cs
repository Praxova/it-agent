namespace LucidAdmin.Core.Enums;

/// <summary>
/// Types of steps available in workflow designer.
/// </summary>
public enum StepType
{
    /// <summary>Entry point - triggered when ticket matches criteria.</summary>
    Trigger,

    /// <summary>Use LLM to classify ticket type and extract data.</summary>
    Classify,

    /// <summary>Query external systems for context (AD user info, etc.).</summary>
    Query,

    /// <summary>Validate data against rules before proceeding.</summary>
    Validate,

    /// <summary>Execute an action via tool server (reset password, etc.).</summary>
    Execute,

    /// <summary>Update the ticket in ServiceNow.</summary>
    UpdateTicket,

    /// <summary>Send notification (email, Teams, etc.).</summary>
    Notify,

    /// <summary>Escalate to human operator.</summary>
    Escalate,

    /// <summary>Conditional branch based on data.</summary>
    Condition,

    /// <summary>End of workflow path.</summary>
    End,

    /// <summary>Execute another workflow as a sub-step.</summary>
    SubWorkflow,

    /// <summary>Human-in-the-loop approval checkpoint.</summary>
    Approval
}
