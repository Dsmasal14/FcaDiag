namespace FcaDiag.Protocols.Transport;

/// <summary>
/// Common FCA routine IDs
/// </summary>
public static class FcaRoutines
{
    // Standard programming routines
    public const ushort EraseMemory = 0xFF00;
    public const ushort CheckProgrammingDependencies = 0xFF01;
    public const ushort CheckProgrammingPreconditions = 0x0203;
    public const ushort PrepareForProgramming = 0xF000;

    // Module-specific routines
    public const ushort ResetAdaptiveValues = 0x0100;
    public const ushort CalibrateSensor = 0x0101;
    public const ushort TestActuator = 0x0102;
    public const ushort LearnProcedure = 0x0103;

    /// <summary>
    /// Routine control sub-functions
    /// </summary>
    public static class SubFunction
    {
        public const byte StartRoutine = 0x01;
        public const byte StopRoutine = 0x02;
        public const byte RequestRoutineResults = 0x03;
    }
}
