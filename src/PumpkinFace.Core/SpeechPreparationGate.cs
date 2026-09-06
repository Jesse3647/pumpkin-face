namespace PumpkinFace.Core;

/// <summary>Invalidates in-flight synthesis without disposing native inference under its worker.</summary>
public sealed class SpeechPreparationGate
{
    private long _generation;
    public long Begin() => ++_generation;
    public void Cancel() => ++_generation;
    public bool Accept(long generation) => generation == _generation;
}
