namespace CopilotAgent.Panel.Domain.ValueObjects;

/// <summary>
/// Type-safe turn counter with overflow protection.
/// </summary>
public readonly record struct TurnNumber(int Value)
{
    public TurnNumber Increment() => new(Value + 1);
    public bool Exceeds(int max) => Value >= max;
    public override string ToString() => Value.ToString();
}