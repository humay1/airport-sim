namespace AirportSim.Sim.Core
{
    /// <summary>
    /// Spec: 08-interfaces-core.md §8.7 ("Issuer, kinds and payloads", Q-010). Values are
    /// saved in command logs: never renumbered. A new kind is appended with the next value,
    /// by amendment, together with its row in the payload table.
    /// </summary>
    public enum CommandKind : ushort
    {
        /// <summary>Applies no state change. Always admitted; owned by sim.core itself.</summary>
        NoOp = 0,

        /// <summary>Owner: sim.flow (registry position 4). Payload: NodeId.Value (uint32), count (int32).</summary>
        SetServersOpen = 1,

        /// <summary>Owner: sim.airside (registry position 3). Payload: FlightId.Value (uint64), StandId.Value (uint16).</summary>
        ReassignStand = 2
    }
}
