using System;

namespace AirportSim.Sim.Core
{
    /// <summary>
    /// The one <see cref="ICommandHandlerRegistry"/> implementation. Spec:
    /// 08-interfaces-core.md §8.7 ("Queue semantics", Q-020; "Issuer, kinds and payloads"
    /// and "Dispatch", Q-010). Each kind's owner is fixed by the payload table: only two
    /// slots exist at Phase 1, so they are explicit fields rather than a dictionary keyed
    /// by <see cref="CommandKind"/> — there is no unordered collection to iterate.
    /// </summary>
    internal sealed class CommandHandlerRegistry : ICommandHandlerRegistry
    {
        // Registry positions from the payload table (08 §8.7): SetServersOpen -> sim.flow,
        // ReassignStand -> sim.airside.
        internal const ushort FlowOwner = 4;
        internal const ushort AirsideOwner = 3;

        private ICommandHandler? _setServersOpenHandler;
        private ICommandHandler? _reassignStandHandler;
        private bool _built;

        public void Register(SystemId owner, ICommandHandler handler)
        {
            if (handler is null)
            {
                throw new ArgumentNullException(nameof(handler));
            }
            if (_built)
            {
                throw new InvalidOperationException("Register cannot be called after Build");
            }

            switch (handler.Kind)
            {
                case CommandKind.NoOp:
                    throw new ArgumentException(
                        "sim.core handles NoOp itself; no handler may be registered for it", nameof(handler));

                case CommandKind.SetServersOpen:
                    if (owner.Value != FlowOwner)
                    {
                        throw new ArgumentException(
                            "SetServersOpen's owner is registry position " + FlowOwner.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            nameof(owner));
                    }
                    if (_setServersOpenHandler is not null)
                    {
                        throw new ArgumentException("a handler for SetServersOpen is already registered", nameof(handler));
                    }
                    _setServersOpenHandler = handler;
                    break;

                case CommandKind.ReassignStand:
                    if (owner.Value != AirsideOwner)
                    {
                        throw new ArgumentException(
                            "ReassignStand's owner is registry position " + AirsideOwner.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            nameof(owner));
                    }
                    if (_reassignStandHandler is not null)
                    {
                        throw new ArgumentException("a handler for ReassignStand is already registered", nameof(handler));
                    }
                    _reassignStandHandler = handler;
                    break;

                default:
                    throw new ArgumentException("unknown command kind", nameof(handler));
            }
        }

        /// <summary>Looks up the registered handler and its fixed owner for a non-NoOp kind.</summary>
        internal bool TryGetHandler(CommandKind kind, out ICommandHandler? handler, out SystemId owner)
        {
            switch (kind)
            {
                case CommandKind.SetServersOpen:
                    handler = _setServersOpenHandler;
                    owner = new SystemId(FlowOwner);
                    return handler is not null;

                case CommandKind.ReassignStand:
                    handler = _reassignStandHandler;
                    owner = new SystemId(AirsideOwner);
                    return handler is not null;

                default:
                    handler = null;
                    owner = default;
                    return false;
            }
        }

        /// <summary>Spec: "If an owner is not registered as a system by Build, Build throws".</summary>
        internal void EnsureOwnersRegistered(Func<ushort, bool> isSystemRegistered)
        {
            if (_setServersOpenHandler is not null && !isSystemRegistered(FlowOwner))
            {
                throw new InvalidOperationException(
                    "SetServersOpen's owner (registry position " + FlowOwner.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    ") has no registered system");
            }
            if (_reassignStandHandler is not null && !isSystemRegistered(AirsideOwner))
            {
                throw new InvalidOperationException(
                    "ReassignStand's owner (registry position " + AirsideOwner.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    ") has no registered system");
            }
        }

        internal void MarkBuilt()
        {
            _built = true;
        }
    }
}
