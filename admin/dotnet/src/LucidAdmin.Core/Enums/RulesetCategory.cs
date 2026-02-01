namespace LucidAdmin.Core.Enums;

/// <summary>
/// Categories for organizing rulesets.
/// </summary>
public static class RulesetCategory
{
    public const string Security = "Security";
    public const string Validation = "Validation";
    public const string Communication = "Communication";
    public const string Escalation = "Escalation";
    public const string Custom = "Custom";

    public static readonly string[] All = { Security, Validation, Communication, Escalation, Custom };
}
