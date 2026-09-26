using System;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Schedule.Tests
{
    /// <summary>
    /// 11 §11.9a: aircraft_type and pax_profile resolve through
    /// services.Content at construction, and a miss is a load failure: a
    /// FormatException whose message starts "sim.schedule: " and contains the
    /// row's flight_ref, the column name and the unresolved id (07 "Error
    /// handling", Q-030, Q-031).
    /// </summary>
    public sealed class ScheduleFactoryTests
    {
        private const string BadRef = "ZQ901";

        private static void AssertConstructionFails(string badRow, string column, string id)
        {
            ISimHostBuilder b = SimHostFactory.CreateBuilder(new SimHostConfig(1UL, ScheduleContent.Index(), new RecordingCheckpointSink(), new NullLog()));
            ScheduleTable table = Load.Table(Csv.Of(Csv.Row("AA100", "D", "09:00"), badRow, Csv.Row("ZZ100", "D", "15:00")), "factory.csv");
            FormatException ex = Assert.Throws<FormatException>(() => ScheduleFactory.CreateSystem(b.Services, table, null));
            Assert.StartsWith("sim.schedule: ", ex.Message, StringComparison.Ordinal);
            Assert.Contains(BadRef, ex.Message, StringComparison.Ordinal);
            Assert.Contains(column, ex.Message, StringComparison.Ordinal);
            Assert.Contains(id, ex.Message, StringComparison.Ordinal);
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
            AssertConstructionFails(Csv.Row(BadRef, "D", "12:00", aircraft: "b999"), "aircraft_type", "b999");
        }

        [Fact]
        public void test_schedule_factory_rejects_unknown_pax_profile()
        {
            AssertConstructionFails(Csv.Row(BadRef, "D", "12:00", profile: "vip"), "pax_profile", "vip");
        }

        [Fact]
        public void test_schedule_factory_rejects_unknown_pax_profile_on_arrival()
        {
            AssertConstructionFails(Csv.Row(BadRef, "A", "12:00", profile: "vip"), "pax_profile", "vip");
        }

        [Fact]
        public void test_schedule_factory_rejects_pax_profile_naming_an_aircraft()
        {
            AssertConstructionFails(Csv.Row(BadRef, "D", "12:00", profile: "a320"), "pax_profile", "a320");
        }

        [Fact]
        public void test_schedule_factory_rejects_aircraft_type_naming_a_pax_profile()
        {
            AssertConstructionFails(Csv.Row(BadRef, "D", "12:00", aircraft: "business"), "aircraft_type", "business");
        }
    }
}
