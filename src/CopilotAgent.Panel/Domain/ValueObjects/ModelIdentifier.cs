namespace CopilotAgent.Panel.Domain.ValueObjects;

/// <summary>
/// Identifies a specific AI model by provider and name.
/// Immutable — safe to pass by value across threads.
/// </summary>
public readonly record struct ModelIdentifier(string Provider, string ModelName)
{
    public override string ToString() => $"{Provider}/{ModelName}";
}