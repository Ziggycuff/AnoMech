using AnoMech.Scenarios;
using AnoMech.Scenarios.Tea.I1LimitCutExaflares;
using AnoMech.Scenarios.Ucob.P5Exaflares;

namespace AnoMech.Tests;

public class ExaflareDodgeRoutesTests
{
    private static readonly float[] Lanes = [-20f, -12f, -4f, 4f, 12f, 20f];

    // Every firing order (pairs in order, unordered within a pair).
    private static IEnumerable<float[]> LaneOrders()
    {
        var seen = new HashSet<string>();
        foreach (var p in Permutations(Lanes))
        {
            var key = string.Join("|", Enumerable.Range(0, 3).Select(i => $"{Math.Min(p[2 * i], p[2 * i + 1])},{Math.Max(p[2 * i], p[2 * i + 1])}"));
            if (seen.Add(key)) yield return p;
        }
    }

    private static IEnumerable<float[]> Permutations(float[] items)
    {
        if (items.Length <= 1)
        {
            yield return items;
            yield break;
        }
        for (var i = 0; i < items.Length; i++)
            foreach (var rest in Permutations(items.Where((_, j) => j != i).ToArray()))
                yield return [items[i], .. rest];
    }

    private static bool Solvable(float[] order, bool evensRideTheCharge = false, int person = 0, float heading = 0f)
    {
        for (var axis = 0; axis < 4; axis++)
        {
            var exaflares = new UcobP5ExaflaresState(Rng.Detached, new UcobP5ExaflaresStateOverrides
            {
                Direction = TeaI1LimitCutExaflaresScenario.LaneDirection(axis, heading),
                LaneOrder = order,
            });
            if (!ExaflareDodgeRoutes.Plan(axis, exaflares, TeaI1LimitCutExaflaresScenario.ExaflaresStartAt, evensRideTheCharge, person).EveryNumberHasARoute) return false;
        }
        return true;
    }

    // Whichever number a person plays. Minutes long: run it when the planner or the lists change.
    [Explicit]
    [TestCase(false)]
    [TestCase(true)]
    public void EveryRolledLaneOrderHasARouteForEveryNumber(bool extraHard)
    {
        // Fun mode's planning is the same whichever number a person plays.
        var people = extraHard ? Enumerable.Range(1, 8).ToArray() : [0];
        var stuck = TeaI1LimitCutExaflaresScenario.LaneHeadings(extraHard)
            .SelectMany(h => TeaI1LimitCutExaflaresScenario.LaneOrders(extraHard, h).Select(o => (Heading: h, Order: o)))
            .Where(c => !people.All(person => Solvable(c.Order, extraHard, person, c.Heading)))
            .Select(c => c.Order)
            .ToList();
        Assert.That(stuck, Is.Empty, () => "No route: " + string.Join(" ", stuck.Select(o => $"[{string.Join(", ", o)}]")));
    }

    // Why a plan fails: its warnings per axis, with the Exaflares pushed past the end of the fight.
    [Explicit]
    [TestCase(false)]
    [TestCase(true)]
    public void ExplainWithoutExaflares(bool evensRideTheCharge)
    {
        for (var axis = 0; axis < 4; axis++)
        {
            var exaflares = new UcobP5ExaflaresState(Rng.Detached, new UcobP5ExaflaresStateOverrides
            {
                Direction = TeaI1LimitCutExaflaresScenario.TowardTrackA(axis),
                LaneOrder = TeaI1LimitCutExaflaresScenario.SolvableLaneOrders[0],
            });
            var plan = ExaflareDodgeRoutes.Plan(axis, exaflares, 1000f, evensRideTheCharge);
            TestContext.Out.WriteLine($"axis {axis}: {(plan.EveryNumberHasARoute ? "ok" : string.Join(" | ", plan.Warnings))}");
        }
    }

    // Why the extra hard lane orders fail with a person in a given number.
    [Explicit]
    [Test]
    public void ExplainExtraHardForEachPerson()
    {
        var order = TeaI1LimitCutExaflaresScenario.ExtraHardLaneOrders[0];
        for (var person = 0; person <= 8; person++)
        {
            var exaflares = new UcobP5ExaflaresState(Rng.Detached, new UcobP5ExaflaresStateOverrides
            {
                Direction = TeaI1LimitCutExaflaresScenario.TowardTrackA(1),
                LaneOrder = order,
            });
            var plan = ExaflareDodgeRoutes.Plan(1, exaflares, TeaI1LimitCutExaflaresScenario.ExaflaresStartAt, true, person);
            TestContext.Out.WriteLine($"person {person}: {(plan.EveryNumberHasARoute ? "ok" : string.Join(" | ", plan.Warnings))}");
        }
    }

    // Regenerates SolvableLaneOrders (or the extra hard list); paste its output there.
    // Extra hard checks every number a person might play only for orders that pass with none.
    [Explicit]
    [TestCase(false, 0f)]
    [TestCase(true, 0f)]
    [TestCase(true, 270f)]
    public void ListSolvableLaneOrders(bool evensRideTheCharge, float heading)
    {
        var solvable = LaneOrders()
            .Where(o => Solvable(o, evensRideTheCharge, 0, heading))
            .Where(o => !evensRideTheCharge || Enumerable.Range(1, 8).All(person => Solvable(o, true, person, heading)))
            .ToList();
        TestContext.Out.WriteLine($"{solvable.Count} solvable:");
        foreach (var o in solvable) TestContext.Out.WriteLine($"[{string.Join(", ", o.Select(v => v.ToString("0")))}],");
    }
}
