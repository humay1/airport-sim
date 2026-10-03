using System.Globalization;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Airside.Tests
{
    /// <summary>
    /// 12 §12.12: the hold state is hashed through AircraftTrack's
    /// PassengerHoldSince and DueAt; AirsideRules is load-time data and not
    /// hashed; tracked aircraft are hashed field by field.
    /// </summary>
    public sealed class AirsideHashTests
    {
        [Fact]
        public void test_airside_hash_includes_boarding_hold_deadline()
        {
            // Same run, held for good, hold max 10 vs 20 minutes: identical
            // until the doors-close point, where only DueAt differs.
            HostRig ten = DepartureTests.Rig(ScriptedFlow.For(DepartureTests.Rd, ulong.MaxValue, 3, 77U), holdMinutes: 10U);
            HostRig twenty = DepartureTests.Rig(ScriptedFlow.For(DepartureTests.Rd, ulong.MaxValue, 3, 77U), holdMinutes: 20U);
            ulong firstDiff = ulong.MaxValue;
            while (ten.Host.CurrentTick < AirConst.TicksPerDay && firstDiff == ulong.MaxValue)
            {
                ulong t = ten.Host.CurrentTick;
                ten.Host.Step(1);
                twenty.Host.Step(1);
                if (ten.Airside.ComputeStateHash() != twenty.Airside.ComputeStateHash())
                {
                    firstDiff = t;
                }
            }

            ulong h = DepartureTests.DoorsClosePoint(ten);
            Assert.True(firstDiff == h, "hashes first differ at " + firstDiff.ToString(CultureInfo.InvariantCulture) + ", doors-close point " + h.ToString(CultureInfo.InvariantCulture));
            Assert.Equal(h + 100UL, ten.Track(DepartureTests.Rd).DueAt);
            Assert.Equal(h + 200UL, twenty.Track(DepartureTests.Rd).DueAt);
        }

        [Fact]
        public void test_airside_hash_excludes_rules_when_no_hold_occurs()
        {
            // No sim.flow, so the rules change no behaviour and must change no hash.
            HostRig ten = DepartureTests.Rig(null, holdMinutes: 10U);
            HostRig twenty = DepartureTests.Rig(null, holdMinutes: 20U);
            while (ten.Host.CurrentTick < AirConst.TicksPerDay)
            {
                ulong t = ten.Host.CurrentTick;
                ten.Host.Step(1);
                twenty.Host.Step(1);
                Assert.True(ten.Airside.ComputeStateHash() == twenty.Airside.ComputeStateHash(), "hash differs at t=" + t.ToString(CultureInfo.InvariantCulture));
            }

            Assert.True(ten.Rec.Has(DepartureTests.Rd, FlightMilestone.Airborne));
        }

        [Fact]
        public void test_airside_hash_changes_as_aircraft_move()
        {
            var rig = new HostRig(Csv.Of(Csv.Row("A1", "A", "06:00")));
            ulong empty = rig.Airside.ComputeStateHash();
            rig.RunTo(2401UL);
            ulong tracked = rig.Airside.ComputeStateHash();
            rig.RunTo(3601UL);
            ulong landed = rig.Airside.ComputeStateHash();
            rig.RunTo(3620UL);
            ulong taxiing = rig.Airside.ComputeStateHash();
            rig.RunTo(3700UL);
            ulong onStand = rig.Airside.ComputeStateHash();

            Assert.NotEqual(empty, tracked);
            Assert.NotEqual(tracked, landed);
            Assert.NotEqual(landed, taxiing);
            Assert.NotEqual(taxiing, onStand);

            var again = new HostRig(Csv.Of(Csv.Row("A1", "A", "06:00")));
            Assert.Equal(empty, again.Airside.ComputeStateHash());
            again.RunTo(3700UL);
            Assert.Equal(onStand, again.Airside.ComputeStateHash());
        }
    }
}
