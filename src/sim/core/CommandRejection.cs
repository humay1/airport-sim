namespace AirportSim.Sim.Core
{
    /// <summary>Spec: 08-interfaces-core.md §8.7.</summary>
    public enum CommandRejection
    {
        /// <summary>Not rejected; admitted.</summary>
        None,

        /// <summary>The command's tick has already passed.</summary>
        TooLate,

        /// <summary>No handler is registered for the command's kind.</summary>
        UnknownKind,

        /// <summary>The payload failed the handler's <c>Validate</c>.</summary>
        MalformedPayload,

        /// <summary>The issuer is not permitted to submit this command.</summary>
        NotPermitted
    }
}
