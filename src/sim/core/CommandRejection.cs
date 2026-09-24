namespace AirportSim.Sim.Core
{
    /// <summary>Spec: 08-interfaces-core.md §8.7.</summary>
    public enum CommandRejection
    {
        None,
        TooLate,
        UnknownKind,
        MalformedPayload,
        NotPermitted
    }
}
