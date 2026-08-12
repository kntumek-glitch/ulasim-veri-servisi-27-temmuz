using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using ulasim_veri_servisi.Models.Routing;
using ulasim_veri_servisi.Services;
using Xunit;

namespace TransportDataService.Tests.UnitTests;

public class RaptorBinarySearchTests
{
    private readonly RaptorRoutingEngine _engine;
    private readonly MethodInfo _findEarliestMethod;
    private readonly MethodInfo _findLatestMethod;

    public RaptorBinarySearchTests()
    {
        // Use FormatterServices to bypass DI constructor since we only test isolated private logic
        _engine = (RaptorRoutingEngine)FormatterServices.GetUninitializedObject(typeof(RaptorRoutingEngine));

        _findEarliestMethod = typeof(RaptorRoutingEngine).GetMethod("FindEarliestTripIndex", BindingFlags.NonPublic | BindingFlags.Instance)!;
        _findLatestMethod = typeof(RaptorRoutingEngine).GetMethod("FindLatestTripIndex", BindingFlags.NonPublic | BindingFlags.Instance)!;
    }

    private RoutingSnapshot CreateTestSnapshot()
    {
        var snapshot = new RoutingSnapshot();
        string pId = "p1";
        string s1 = "s1";
        string s2 = "s2";
        string srv1 = "srv1";

        snapshot.PatternToStops[pId] = new List<string> { s1, s2 };
        snapshot.PatternToTrips[pId] = new List<string> { "t1", "t2", "t3", "t4" };
        
        snapshot.TripToServiceId["t1"] = srv1;
        snapshot.TripToServiceId["t2"] = srv1;
        snapshot.TripToServiceId["t3"] = srv1;
        snapshot.TripToServiceId["t4"] = srv1;

        // Departures at s1: 1000, 2000, 3000, 4000
        snapshot.TripTimetables["t1"] = new List<SnapshotStopTime> { new() { StopId = s1, DepartureSeconds = 1000 }, new() { StopId = s2, ArrivalSeconds = 1500 } };
        snapshot.TripTimetables["t2"] = new List<SnapshotStopTime> { new() { StopId = s1, DepartureSeconds = 2000 }, new() { StopId = s2, ArrivalSeconds = 2500 } };
        snapshot.TripTimetables["t3"] = new List<SnapshotStopTime> { new() { StopId = s1, DepartureSeconds = 3000 }, new() { StopId = s2, ArrivalSeconds = 3500 } };
        snapshot.TripTimetables["t4"] = new List<SnapshotStopTime> { new() { StopId = s1, DepartureSeconds = 4000 }, new() { StopId = s2, ArrivalSeconds = 4500 } };

        snapshot.PatternStopDepartureIndices[$"{s1}_{pId}"] = new int[] { 0, 1, 2, 3 };
        snapshot.PatternStopArrivalIndices[$"{s2}_{pId}"] = new int[] { 0, 1, 2, 3 };

        return snapshot;
    }

    [Theory]
    [InlineData(500, 0)]  // EBT before first trip -> finds first trip at index 0 (dept 1000)
    [InlineData(1000, 0)] // EBT exactly first trip -> finds first trip
    [InlineData(1500, 1)] // EBT between first and second -> finds second trip at index 1 (dept 2000)
    [InlineData(3000, 2)] // EBT exactly third -> finds third trip
    [InlineData(4000, 3)] // EBT exactly fourth -> finds fourth trip
    public void FindEarliestTripIndex_ValidTargets_ReturnsCorrectIndex(int targetTime, int expectedIndex)
    {
        var snapshot = CreateTestSnapshot();
        var activeToday = new HashSet<string> { "srv1" };
        var activeYesterday = new HashSet<string> { "srv1" };

        object[] args = { snapshot, "p1", "s1", targetTime, activeToday, activeYesterday, 0 };
        var result = (int)_findEarliestMethod.Invoke(_engine, args)!;

        Assert.Equal(expectedIndex, result);
    }

    [Fact]
    public void FindEarliestTripIndex_AfterLastTrip_ReturnsMinusOne()
    {
        var snapshot = CreateTestSnapshot();
        var activeToday = new HashSet<string> { "srv1" };
        var activeYesterday = new HashSet<string> { "srv1" };

        object[] args = { snapshot, "p1", "s1", 4500, activeToday, activeYesterday, 0 };
        var result = (int)_findEarliestMethod.Invoke(_engine, args)!;

        // No trip leaves after 4500 (max is 4000)
        Assert.Equal(-1, result);
    }

    [Theory]
    [InlineData(5000, 3)] // LDT after last trip -> finds last trip (arr 4500)
    [InlineData(4500, 3)] // LDT exactly last trip -> finds last trip
    [InlineData(4000, 2)] // LDT between third and fourth -> finds third trip (arr 3500)
    [InlineData(2500, 1)] // LDT exactly second -> finds second trip
    [InlineData(1500, 0)] // LDT exactly first -> finds first trip
    public void FindLatestTripIndex_ValidTargets_ReturnsCorrectIndex(int targetArrival, int expectedIndex)
    {
        var snapshot = CreateTestSnapshot();
        var activeToday = new HashSet<string> { "srv1" };
        var activeYesterday = new HashSet<string> { "srv1" };

        object[] args = { snapshot, "p1", "s2", targetArrival, activeToday, activeYesterday, 0 };
        var result = (int)_findLatestMethod.Invoke(_engine, args)!;

        Assert.Equal(expectedIndex, result);
    }

    [Fact]
    public void FindLatestTripIndex_BeforeFirstTrip_ReturnsMinusOne()
    {
        var snapshot = CreateTestSnapshot();
        var activeToday = new HashSet<string> { "srv1" };
        var activeYesterday = new HashSet<string>();

        object[] args = { snapshot, "p1", "s2", 1000, activeToday, activeYesterday, 0 };
        var result = (int)_findLatestMethod.Invoke(_engine, args)!;

        // No trip arrives before or at 1000 (min is 1500)
        Assert.Equal(-1, result);
    }
}
