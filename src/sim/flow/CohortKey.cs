using System;
using AirportSim.Sim.Core;

namespace AirportSim.Sim.Flow
{
    /// <summary>
    /// The discriminator two cohorts must share, in every field, to merge.
    /// Spec: 09-interfaces-flow.md §9.2, §9.3.
    /// </summary>
    public readonly struct CohortKey : IEquatable<CohortKey>
    {
        /// <summary>The flight the cohort belongs to.</summary>
        public FlightId Flight { get; }

        /// <summary>The cohort's journey direction.</summary>
        public FlowDirection Direction { get; }

        /// <summary>The passenger profile, data-driven.</summary>
        public ContentId PaxProfile { get; }

        /// <summary>Whether the cohort carries hold baggage.</summary>
        public bool HasHoldBaggage { get; }

        /// <summary>Whether the cohort requires assistance.</summary>
        public bool RequiresAssistance { get; }

        /// <summary>Constructs the key from its five fields.</summary>
        public CohortKey(FlightId flight, FlowDirection direction, ContentId paxProfile, bool hasHoldBaggage, bool requiresAssistance)
        {
            Flight = flight;
            Direction = direction;
            PaxProfile = paxProfile;
            HasHoldBaggage = hasHoldBaggage;
            RequiresAssistance = requiresAssistance;
        }

        /// <summary>Equality by every field.</summary>
        public bool Equals(CohortKey other)
        {
            return Flight.Equals(other.Flight)
                && Direction == other.Direction
                && PaxProfile.Equals(other.PaxProfile)
                && HasHoldBaggage == other.HasHoldBaggage
                && RequiresAssistance == other.RequiresAssistance;
        }

        /// <summary>Equality by every field.</summary>
        public override bool Equals(object? obj) => obj is CohortKey other && Equals(other);

        /// <summary>Hash combining every field.</summary>
        public override int GetHashCode()
        {
            unchecked
            {
                int h = Flight.GetHashCode();
                h = (h * 397) ^ (int)Direction;
                h = (h * 397) ^ PaxProfile.GetHashCode();
                h = (h * 397) ^ HasHoldBaggage.GetHashCode();
                h = (h * 397) ^ RequiresAssistance.GetHashCode();
                return h;
            }
        }

        /// <summary>Equality by every field.</summary>
        public static bool operator ==(CohortKey left, CohortKey right) => left.Equals(right);

        /// <summary>Inequality by every field.</summary>
        public static bool operator !=(CohortKey left, CohortKey right) => !left.Equals(right);
    }
}
