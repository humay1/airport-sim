using System;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Schedule.Tests
{
    /// <summary>
    /// 11 §11.9a: aircraft_type and pax_profile resolve through
    /// services.Content at construction, and a miss is a load failure
    /// (§11.4): FormatException naming the field and the offending id
    /// (07 "Error handling", Q-030).
    /// </summary>
    public sealed class ScheduleFactoryTests
    {
        private static FormatException AssertConstructionFails(byte[] csv, string field, string id)
        {
            ISimHostBuilder b = SimHostFactory.CreateBuilder(new SimHostConfig(1UL, ScheduleContent.Index(), new RecordingCheckpointSink(), new NullLog()));
            ScheduleTable table = Load.Table(csv, "factory.csv");
            FormatException ex = Assert.Throws<FormatException>(() => ScheduleFactory.CreateSystem(b.Services, table, null));
            Assert.Contains(field, ex.Message, StringComparison.Ordinal);
            Assert.Contains(id, ex.Message, StringComparison.Ordinal);
            return ex;
        }

        [Fact]
        public void test_schedule_factory_creates_loader_and_system_without_flow()
        {
            Assert.NotNull(ScheduleFactory.CreateLoader());
            ISimHostBuilder b = SimHostFactory.CreateBuilder(new SimHostConfig(1UL, ScheduleContent.Index(), new RecordingCheckpointSink(), new NullLog()));
            ScheduleTable table = Load.Table(Fixture.Bytes(), Fixture.SourceName);
            Assert.Equal(200, table.Rows.Count);
            IScheduleSystem s = ScheduleFactory.CreateSystem(b.Services, table, null);
            b.Register(s);
            ISimHost host = b.Build();
            host.Step(1);
            Assert.Equal(200, s.PublishedFlights().Count);
        }

        [Fact]
        public void test_schedule_factory_rejects_unknown_aircraft_type()
        {
            AssertConstructionFails(Csv.Of(Csv.Row("X1", "D", "12:00", aircraft: "b999")), "aircraft_type", "b999");
        }

        [Fact]
        public void test_schedule_factory_rejects_unknown_pax_profile()
        {
            AssertConstructionFails(Csv.Of(Csv.Row("X1", "D", "12:00", profile: "vip")), "pax_profile", "vip");
        }

        [Fact]
        public void test_schedule_factory_rejects_unknown_pax_profile_on_arrival()
        {
            AssertConstructionFails(Csv.Of(Csv.Row("X1", "A", "12:00", profile: "vip")), "pax_profile", "vip");
        }

        [Fact]
        public void test_schedule_factory_rejects_pax_profile_naming_an_aircraft()
        {
            AssertConstructionFails(Csv.Of(Csv.Row("X1", "D", "12:00", profile: "a320")), "pax_profile", "a320");
        }

        [Fact]
        public void test_schedule_factory_rejects_aircraft_type_naming_a_pax_profile()
        {
            AssertConstructionFails(Csv.Of(Csv.Row("X1", "D", "12:00", aircraft: "business")), "aircraft_type", "business");
        }
    }
}
