using System.Diagnostics;
using FluentAssertions;
using ParcelTracking.Domain.Enums;
using ParcelTracking.Domain.Models;
using ParcelTracking.Domain.StateMachine;
using ParcelTracking.Rules.Rules;

namespace ParcelTracking.LoadTests;

/// <summary>
/// In-process throughput benchmarks for the rules engine and state machine.
///
/// These tests verify the non-functional requirement:
///   "Sustain 5,000 scan events/second ingestion" (NFR — Throughput)
///
/// All tests are pure CPU work with no I/O or external dependencies.
/// A CI-grade machine with a single core easily surpasses the 5 k/s threshold.
///
/// Real-world throughput across the full pipeline (Service Bus → DB → Redis) is
/// governed by the infrastructure tier; these tests isolate the business-logic
/// layer and confirm it is never the bottleneck.
/// </summary>
public sealed class ThroughputBenchmarkTests
{
    private const int TotalEvents           = 50_000;
    private const int MinEventsPerSecond    = 5_000;
    private const int Concurrency           = 32;   // parallel workers

    // ─────────────────────────────────────────────────────────────────────────
    //  Test 1 — Full rules-engine pipeline (Rule A + Rule B + state machine)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RulesEngine_FullPipeline_AtLeast5000EventsPerSecond()
    {
        var rule   = new CollectionValidationRule();
        var events = BuildValidCollectionEvents(TotalEvents);

        var sw = Stopwatch.StartNew();

        await Parallel.ForEachAsync(
            events,
            new ParallelOptions { MaxDegreeOfParallelism = Concurrency },
            async (scanEvent, ct) =>
            {
                // Rule A — collection validation
                var result = await rule.EvaluateAsync(scanEvent, ct);
                if (!result.IsSuccess) return;

                // Rule B — sizing
                var parcel = BuildParcelFromEvent(scanEvent);
                SizingRule.Apply(parcel);

                // Rule C — state machine transition check
                ParcelStateMachine.IsValidTransition(null, scanEvent.EventType);
            });

        sw.Stop();

        var throughput = TotalEvents / sw.Elapsed.TotalSeconds;
        Console.WriteLine(
            $"[Rules Engine] {TotalEvents:N0} events in {sw.Elapsed.TotalSeconds:F3}s " +
            $"→ {throughput:N0} events/sec");

        throughput.Should().BeGreaterThanOrEqualTo(MinEventsPerSecond,
            because: $"the rules engine must sustain ≥{MinEventsPerSecond:N0} events/sec " +
                     $"(NFR: 5,000 events/sec ingestion target)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Test 2 — State machine only (pure lookup-table performance)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void StateMachine_TransitionLookup_AtLeast5000PerSecond()
    {
        // Full happy-path sequence per parcel; repeat across many parcels
        var transitions = new (ParcelStatus? From, ParcelStatus To)[]
        {
            (null,                          ParcelStatus.COLLECTED),
            (ParcelStatus.COLLECTED,        ParcelStatus.SOURCE_SORT),
            (ParcelStatus.SOURCE_SORT,      ParcelStatus.DESTINATION_SORT),
            (ParcelStatus.DESTINATION_SORT, ParcelStatus.DELIVERY_CENTRE),
            (ParcelStatus.DELIVERY_CENTRE,  ParcelStatus.READY_FOR_DELIVERY),
            (ParcelStatus.READY_FOR_DELIVERY, ParcelStatus.DELIVERED),
        };

        var totalLookups = TotalEvents * transitions.Length;   // 300 k lookups

        var sw = Stopwatch.StartNew();

        for (var i = 0; i < TotalEvents; i++)
            foreach (var (from, to) in transitions)
                ParcelStateMachine.IsValidTransition(from, to);

        sw.Stop();

        var throughput = totalLookups / sw.Elapsed.TotalSeconds;
        Console.WriteLine(
            $"[State Machine] {totalLookups:N0} lookups in {sw.Elapsed.TotalSeconds:F3}s " +
            $"→ {throughput:N0} lookups/sec");

        // State-machine lookups should be orders of magnitude faster than 5 k/s
        throughput.Should().BeGreaterThanOrEqualTo(MinEventsPerSecond,
            because: "state-machine dictionary lookups must not be the bottleneck");
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Test 3 — Rule A alone (pure validation throughput)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RuleA_ValidationAlone_AtLeast5000EventsPerSecond()
    {
        var rule   = new CollectionValidationRule();
        var events = BuildValidCollectionEvents(TotalEvents);

        var sw = Stopwatch.StartNew();

        await Parallel.ForEachAsync(
            events,
            new ParallelOptions { MaxDegreeOfParallelism = Concurrency },
            async (e, ct) => await rule.EvaluateAsync(e, ct));

        sw.Stop();

        var throughput = TotalEvents / sw.Elapsed.TotalSeconds;
        Console.WriteLine(
            $"[Rule A] {TotalEvents:N0} validations in {sw.Elapsed.TotalSeconds:F3}s " +
            $"→ {throughput:N0} events/sec");

        throughput.Should().BeGreaterThanOrEqualTo(MinEventsPerSecond,
            because: "Rule A validation alone must not be the bottleneck");
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Builders
    // ─────────────────────────────────────────────────────────────────────────

    private static ScanEvent[] BuildValidCollectionEvents(int count)
    {
        var from    = new Address("1 Sender St",   "London",     "E1 1AA", "GB");
        var to      = new Address("2 Receiver Rd", "Manchester", "M1 1AA", "GB");
        var sender  = new Contact("Alice Smith", "07700000001", "alice@example.com");
        var receiver = new Contact("Bob Jones",  "07700000002", "bob@example.com",  true);
        var dims    = new Dimensions(30m, 20m, 15m, 2.5m);

        return Enumerable.Range(0, count)
            .Select(i => new ScanEvent
            {
                EventId      = $"evt-load-{i:D9}",
                TrackingId   = $"PKG-LOAD{i:D8}",
                EventType    = ParcelStatus.COLLECTED,
                EventTimeUtc = DateTime.UtcNow,
                LocationId   = "HUB-LONDON",
                ActorId      = "LOAD-TESTER",
                Dimensions   = dims,
                FromAddress  = from,
                ToAddress    = to,
                Sender       = sender,
                Receiver     = receiver,
                BaseCharge   = 8.50m,
            })
            .ToArray();
    }

    private static Parcel BuildParcelFromEvent(ScanEvent e) => new()
    {
        TrackingId  = e.TrackingId,
        Status      = e.EventType,
        Dimensions  = e.Dimensions!,
        FromAddress = e.FromAddress!,
        ToAddress   = e.ToAddress!,
        Sender      = e.Sender!,
        Receiver    = e.Receiver!,
        BaseCharge  = e.BaseCharge ?? 0m,
    };
}
