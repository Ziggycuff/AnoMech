using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using AnoMech.Core.EnemyActions;
using AnoMech.Scenarios.Tea.I1LimitCut;
using AnoMech.Scenarios.Ucob.P5Exaflares;
using static AnoMech.Scenarios.Tea.TeaConstants;

namespace AnoMech.Scenarios.Tea.I1LimitCutExaflares;

// Each number's way through 1256 with the Exaflares on top: as close to the real route as the
// blasts allow, standing still on its spot while the 1256 bots hold it for Cruise Chaser. A search
// over a grid of spots in time steps; anywhere a Hawk Blaster, an Exaflare or another number's Alpha
// Sword or Super Blassty Charge lands at that moment is off limits, with a margin for the bots'
// steering. Hit times, Hawk Blaster waves and Cruise Chaser's placement are Limit Cut's own.
internal sealed class ExaflareDodgeRoutes
{
    private const float Cell = 0.4f;
    private const float Step = 0.1f;
    // Below a run, and below Sprint where 1256 sprints.
    private const float RunSpeed = 6f;
    private const float SprintSpeed = 8f;
    private const float Arena = 18.5f;
    // Room for a bot's steering to be off the line. The Exaflares get more, room for a person to
    // be off too: 1256's own spots are only just clear of the Hawk Blasters and each other's hits.
    private const float Margin = 0.3f;
    private const float ExaflareMargin = 1f;
    // Cruise Chaser warps in off the odd number as they really stand, up to HoldTolerance from plan.
    private const float LimitCutMargin = 0.6f;
    private const float HoldTolerance = 1.2f;
    private const float EvenHoldTolerance = 0.6f;
    private const float HoldAfterHit = 0.2f;
    private const float EvenSettlesBeforeHit = 0.8f;
    // Extra hard: before they turn their back, which stops any walk still finishing.
    private const float ExtraHardEvenSettlesBeforeHit = 1.3f;
    // The odd number is there before turning clockwise, which a walk still finishing would undo.
    private const float OddSettlesBeforeFacing = 0.5f;
    private const float WarpBehind = 2f;
    // Further back than 1256's 3y: the charge goes where the even number stands, so the further they
    // are, the less their being off swings it across others.
    private const float EvenBehindCruiseChaser = 4f;
    // Extra hard's charge is aimed across the arena, so any error in where the even number stands swings
    // a 50y line over everyone. Further back would turn it less, but #2 then stands in the second centre
    // Hawk Blaster, 0.1s before their charge.
    private const float ExtraHardEvenBehindCruiseChaser = 4f;
    // Widening per yalm along the charge: a steering error of ~0.2y at 4y turns it ~3°.
    private const float ExtraHardChargeFlare = 0.06f;
    // The second pass stands each number on the very spot the first one found.
    private const float SameSpot = Cell / 2f;
    private const int ExtraHardPasses = 4;
    private const float ClearOfCruiseChaser = 3f;
    // Extra hard: bystanders keep out of the last pair's hits this long either side of them, for the
    // lag of bots already on their way to the middle for J Kick. Earlier pairs can't spare the room.
    private const float ExtraHardLastPairWindow = 0.3f;
    // Extra hard, the pair a person plays in: Cruise Chaser warps in off where they really stand and
    // face, so the bots give that pair's hits room for them not being on the exact spot.
    private const float PersonConeWiden = MathF.PI / 36f;
    private const float PersonChargeWiden = 0.5f;
    private const float PersonChargeFlare = 0.07f;
    private const float PersonClearOfCruiseChaser = 3.5f;
    // A knockback lands up to ~2y off the plan, and the bot is a moment getting back on it.
    private const float LandingSlack = 2f;
    private const float LandingSlackSeconds = 2f;
    // A blast centred on its caster reaches the caster's hitbox further: the helpers' is 0.5y.
    private const float HelperHitbox = 0.5f;
    private const float HawkBlasterRadius = 10f + HelperHitbox;
    private const float ExaflareRadius = 6f + HelperHitbox;

    // Extra hard: Super Blassty Charge's own knockback (Knockback row 3) carries the even number across
    // a quadrant, which is what snaps the chain to their odd number.
    private const float ChargeKnockback = 20f;
    private const float ChargeKnockbackSeconds = 0.5f;
    private const float ChargeKnockbackAfterHit = 0.53f;
    // Where the knockback should land them, short of the 20y edge.
    private const float LandingRadius = 18f;

    private static readonly float[] HawkBlasterAt = [5.27f, 7.49f, 9.69f, 11.90f, 14.05f, 16.25f, 18.49f, 20.68f, 22.83f, 25.07f];

    private readonly Vector2[]?[] routes = new Vector2[8][];
    private readonly float start;
    private readonly List<string> warnings = [];

    private ExaflareDodgeRoutes(float start) => this.start = start;

    // Extra hard: a number that couldn't keep to the spot the hits were aimed off.
    private readonly bool[] loose = new bool[8];
    private bool settled = true;

    // The number a person plays, whom no route steers.
    private int person;

    public bool EveryNumberHasARoute => settled && Enumerable.Range(1, 8).All(n => n == person || routes[n - 1] is not null);

    // Collected off the framework thread, to log from it.
    public IReadOnlyList<string> Warnings => warnings;

    public Vector2? At(int number, float time)
    {
        if (routes[number - 1] is not { } route) return null;
        var f = (time - start) / Step;
        if (f <= 0f || f >= route.Length - 1) return null;
        var i = (int)f;
        return Vector2.Lerp(route[i], route[i + 1], f - i);
    }

    // Twice: Cruise Chaser warps in off where each odd number really stands, which the first pass
    // decides, so the second plans around those Alpha Swords and charges.
    public static ExaflareDodgeRoutes Plan(int firstBlasterAxis, UcobP5ExaflaresState exaflares, float exaflaresStartAt,
        bool evensRideTheCharge = false, int person = 0)
    {
        var (from, to) = (float.MaxValue, 0f);
        for (var n = 1; n <= 8; n++)
        {
            var span = TeaI1LimitCutAi.PathSpan(n);
            from = MathF.Min(from, span.From);
            to = MathF.Max(to, span.To);
        }
        var grid = new Grid(from, to);

        var shared = new List<(float Time, Func<Vector2, bool> Hits)>();
        for (var wave = 0; wave < HawkBlasterAt.Length; wave++)
            foreach (var spot in HawkBlasterSpots(firstBlasterAxis, wave))
                shared.Add((HawkBlasterAt[wave], p => Vector2.Distance(p, spot) <= HawkBlasterRadius + Margin));
        foreach (var line in exaflares.Lines)
            for (var i = 0; i < line.Hits.Count; i++)
            {
                var hit = line.Hits[i];
                var spot = new Vector2(hit.Position.X, hit.Position.Z);
                Func<Vector2, bool> hits = p => Vector2.Distance(p, spot) <= ExaflareRadius + ExaflareMargin;
                // The first eruption is a cast's release: it snapshots at the bar's end, before the time
                // the state gives it, which is the ActionEffect's.
                var snapshot = i == 0 ? hit.Time - CastSpec.ActionEffectOffset : hit.Time;
                shared.Add((exaflaresStartAt + snapshot, hits));
            }

        var oddSpots = new Vector2[4];
        for (var pair = 0; pair < 4; pair++)
            oddSpots[pair] = TeaI1LimitCutAi.PlannedAt(firstBlasterAxis, 2 * pair + 1, TeaI1LimitCutAi.HitAt[2 * pair]);
        var plan = PlanEveryNumber(grid, from, shared, LimitCutPairs(oddSpots, evensRideTheCharge, null, person), firstBlasterAxis, evensRideTheCharge, exact: false, person);
        // Extra hard aims the hits off where each number really stands, and pins them there; a number that
        // can't keep to its spot moves it, and the hits are aimed again, until everyone keeps theirs.
        var passes = evensRideTheCharge ? ExtraHardPasses : 1;
        for (var pass = 0; pass < passes; pass++)
        {
            var evenSpots = new Vector2?[4];
            for (var pair = 0; pair < 4; pair++)
            {
                oddSpots[pair] = plan.At(2 * pair + 1, TeaI1LimitCutAi.HitAt[2 * pair]) ?? oddSpots[pair];
                if (evensRideTheCharge) evenSpots[pair] = plan.At(2 * pair + 2, TeaI1LimitCutAi.HitAt[2 * pair + 1]);
            }
            plan = PlanEveryNumber(grid, from, shared, LimitCutPairs(oddSpots, evensRideTheCharge, evenSpots, person), firstBlasterAxis,
                evensRideTheCharge, exact: evensRideTheCharge, person);
            if (Array.TrueForAll(plan.loose, l => !l)) return plan;
        }
        if (evensRideTheCharge)
        {
            plan.settled = false;
            plan.warnings.Add("The Limit Cut hits never settled on where everyone stands.");
        }
        return plan;
    }

    internal static float KnockbackStarts(int even) => TeaI1LimitCutAi.HitAt[even - 1] + ChargeKnockbackAfterHit;

    private static ExaflareDodgeRoutes PlanEveryNumber(Grid grid, float from, List<(float Time, Func<Vector2, bool> Hits)> shared,
        LimitCutPair[] pairs, int firstBlasterAxis, bool evensRideTheCharge, bool exact, int person)
    {
        var plan = new ExaflareDodgeRoutes(from) { person = person };
        var stuck = new float[8];
        Parallel.For(1, 9, n =>
        {
            var hazards = new List<(float, Func<Vector2, bool>)>(shared);
            // Extra hard's hits reach across the arena, where a route only skimming one between steps is
            // no margin at all: they block the steps either side too.
            foreach (var pair in pairs)
                foreach (var shift in !evensRideTheCharge ? [0f]
                    : pair.Odd == 7 ? Enumerable.Range(-3, 7).Select(k => k * ExtraHardLastPairWindow / 3f).ToArray()
                    : new[] { -Step, 0f, Step })
                {
                    if (pair.Odd != n) hazards.Add((TeaI1LimitCutAi.HitAt[pair.Odd - 1] + shift, p => InCone(p, pair.CruiseChaser, pair.Clockwise, pair.ConeHalf)));
                    if (pair.Odd + 1 != n) hazards.Add((TeaI1LimitCutAi.HitAt[pair.Odd] + shift, p => InCharge(p, pair.CruiseChaser, pair.Charge, pair.Flare, pair.Widen)));
                    // Right beside Cruise Chaser the smallest error in where he warps in decides who is hit.
                    if (pair.Odd != n && pair.Odd + 1 != n && pair.Clear > 0f)
                        foreach (var hit in new[] { TeaI1LimitCutAi.HitAt[pair.Odd - 1], TeaI1LimitCutAi.HitAt[pair.Odd] })
                            hazards.Add((hit + shift, p => Vector2.Distance(p, pair.CruiseChaser) <= pair.Clear));
                }
            var mine = pairs[(n - 1) / 2];
            var hold = n % 2 == 1
                ? (TeaI1LimitCutAi.FaceAt[n - 1] - OddSettlesBeforeFacing, TeaI1LimitCutAi.HitAt[n - 1] + HoldAfterHit, mine.OddSpot, exact ? SameSpot : HoldTolerance)
                : (TeaI1LimitCutAi.HitAt[n - 1] - (evensRideTheCharge ? ExtraHardEvenSettlesBeforeHit : EvenSettlesBeforeHit),
                    evensRideTheCharge ? KnockbackStarts(n) : TeaI1LimitCutAi.HitAt[n - 1] + HoldAfterHit, mine.EvenSpot, exact ? SameSpot : EvenHoldTolerance);
            Knockback? knockback = evensRideTheCharge && n % 2 == 0
                ? new Knockback(KnockbackStarts(n), ChargeKnockbackSeconds, ChargeKnockback, mine.CruiseChaser)
                : null;
            var sprint = TeaI1LimitCutAi.SprintWindows[n - 1];
            Func<float, Vector2> planned = t => TeaI1LimitCutAi.PlannedAt(firstBlasterAxis, n, t);
            Func<float, float> speed = t => t >= sprint.From && t < sprint.To ? SprintSpeed : RunSpeed;
            plan.routes[n - 1] = grid.Route(hazards, planned, speed, hold, knockback, out stuck[n - 1]);
            if (plan.routes[n - 1] is null && exact)
            {
                var looser = (hold.Item1, hold.Item2, hold.Item3, n % 2 == 1 ? HoldTolerance : EvenHoldTolerance);
                plan.routes[n - 1] = grid.Route(hazards, planned, speed, looser, knockback, out stuck[n - 1]);
                plan.loose[n - 1] = true;
            }
        });
        for (var n = 1; n <= 8; n++)
            if (plan.routes[n - 1] is null && n != person)
                plan.warnings.Add($"No way through for number {n} past t={stuck[n - 1]:F1}; it keeps to the plain 1256 route.");
        return plan;
    }

    private static IEnumerable<Vector2> HawkBlasterSpots(int axis, int wave)
    {
        if (wave is 4 or 9)
        {
            yield return Vector2.Zero;
            yield break;
        }
        var heading = (axis + wave % 5) * 45f;
        yield return Flat(Geometry.OnCompass(heading, Geometry.BlasterRadius));
        yield return Flat(Geometry.OnCompass(heading + 180f, Geometry.BlasterRadius));
    }

    // Away from Source, Distance over Seconds, from At: nothing steers it.
    private sealed record Knockback(float At, float Seconds, float Distance, Vector2 Source);

    private sealed record LimitCutPair(int Odd, Vector2 OddSpot, Vector2 Clockwise, Vector2 CruiseChaser, Vector2 EvenSpot, float Flare,
        float ConeHalf, float Widen, float Clear)
    {
        public Vector2 Charge => Vector2.Normalize(EvenSpot - CruiseChaser);
    }

    // Cruise Chaser warps in behind the odd number as they face clockwise, cleaves forward, and
    // charges back at the even number standing behind it.
    private static LimitCutPair[] LimitCutPairs(Vector2[] oddSpots, bool evensRideTheCharge, Vector2?[]? evenSpots, int person)
    {
        var pairs = new LimitCutPair[4];
        for (var pair = 0; pair < 4; pair++)
        {
            var odd = 2 * pair + 1;
            var oddSpot = oddSpots[pair];
            var clockwise = Vector2.Normalize(new Vector2(-oddSpot.Y, oddSpot.X));
            var cruiseChaser = oddSpot - clockwise * WarpBehind;
            var evenSpot = evenSpots?[pair] ?? EvenSpotBehind(cruiseChaser, oddSpot, evensRideTheCharge);
            var flare = evensRideTheCharge ? ExtraHardChargeFlare : 0f;
            // Extra hard only: room around Cruise Chaser, and more for the pair a person plays in.
            pairs[pair] = !evensRideTheCharge
                ? new LimitCutPair(odd, oddSpot, clockwise, cruiseChaser, evenSpot, flare, MathF.PI / 4f, 0f, 0f)
                : person == odd || person == odd + 1
                    ? new LimitCutPair(odd, oddSpot, clockwise, cruiseChaser, evenSpot, MathF.Max(flare, PersonChargeFlare),
                        MathF.PI / 4f + PersonConeWiden, PersonChargeWiden, PersonClearOfCruiseChaser)
                    : new LimitCutPair(odd, oddSpot, clockwise, cruiseChaser, evenSpot, flare, MathF.PI / 4f, 0f, ClearOfCruiseChaser);
        }
        return pairs;
    }

    // Where the even number stands for Cruise Chaser warped in at `cruiseChaser`, behind an odd number
    // standing at `oddSpot` and facing clockwise.
    internal static Vector2 EvenSpotBehind(Vector2 cruiseChaser, Vector2 oddSpot, bool evensRideTheCharge)
    {
        var clockwise = Vector2.Normalize(new Vector2(-oddSpot.Y, oddSpot.X));
        var back = evensRideTheCharge ? IntoTheQuadrant(cruiseChaser, -clockwise) : -clockwise;
        return cruiseChaser + back * (evensRideTheCharge ? ExtraHardEvenBehindCruiseChaser : EvenBehindCruiseChaser);
    }

    // How long the even number stands there.
    internal static float EvenLeavesAt(int even, bool evensRideTheCharge)
        => evensRideTheCharge ? KnockbackStarts(even) : TeaI1LimitCutAi.HitAt[even - 1] + HoldAfterHit;

    // Back from Cruise Chaser and turned in toward the middle, so the knockback runs along a chord across
    // the quadrant and lands LandingRadius out rather than in the wall.
    private static Vector2 IntoTheQuadrant(Vector2 cruiseChaser, Vector2 back)
    {
        var r = cruiseChaser.Length();
        var reach = ExtraHardEvenBehindCruiseChaser + ChargeKnockback;
        var sin = Math.Clamp((r * r + reach * reach - LandingRadius * LandingRadius) / (2f * r * reach), 0f, 1f);
        var inward = -Vector2.Normalize(cruiseChaser);
        return Vector2.Normalize(back * MathF.Sqrt(1f - sin * sin) + inward * sin);
    }

    // Alpha Sword: 90° wide (halfAngle π/4), 25y.
    private static bool InCone(Vector2 p, Vector2 apex, Vector2 forward, float halfAngle)
    {
        var d = p - apex;
        var length = d.Length();
        if (length > 25f + LimitCutMargin) return false;
        if (length < LimitCutMargin) return true;
        var angle = MathF.Acos(Math.Clamp(Vector2.Dot(d / length, forward), -1f, 1f));
        return angle <= halfAngle || length * MathF.Sin(angle - halfAngle) <= LimitCutMargin;
    }

    // Super Blassty Charge: 50y long, 10y wide.
    private static bool InCharge(Vector2 p, Vector2 origin, Vector2 forward, float flare, float widen)
    {
        var d = p - origin;
        var along = Vector2.Dot(d, forward);
        var across = MathF.Abs(d.X * forward.Y - d.Y * forward.X);
        return along >= -LimitCutMargin && along <= 50f + LimitCutMargin && across <= 5f + LimitCutMargin + widen + MathF.Max(0f, along) * flare;
    }

    private static Vector2 Flat(Vector3 v) => new(v.X, v.Z);

    // Cheapest path through the spots, cost being the distance from the planned route.
    private sealed class Grid
    {
        private readonly int size;
        private readonly int steps;
        private readonly float start;
        private readonly Vector2[] positions;
        private readonly bool[] inside;
        private readonly (int Offset, int Dx, int Dz)[] moves;

        public Grid(float from, float to)
        {
            start = from;
            steps = (int)MathF.Ceiling((to - from) / Step) + 1;
            size = 2 * (int)MathF.Ceiling(Arena / Cell) + 1;
            positions = new Vector2[size * size];
            inside = new bool[size * size];
            var half = size / 2;
            for (var z = 0; z < size; z++)
                for (var x = 0; x < size; x++)
                {
                    var p = new Vector2((x - half) * Cell, (z - half) * Cell);
                    positions[z * size + x] = p;
                    inside[z * size + x] = p.Length() <= Arena;
                }
            var reach = SprintSpeed * Step / Cell;
            var list = new List<(int, int, int)>();
            var r = (int)MathF.Ceiling(reach);
            for (var dz = -r; dz <= r; dz++)
                for (var dx = -r; dx <= r; dx++)
                    if (MathF.Sqrt(dx * dx + dz * dz) <= reach) list.Add((dz * size + dx, dx, dz));
            moves = list.ToArray();
        }

        public Vector2[]? Route(List<(float Time, Func<Vector2, bool> Hits)> hazards, Func<float, Vector2> planned,
            Func<float, float> speed, (float From, float To, Vector2 Spot, float Tolerance) hold, Knockback? knockback, out float stuckAt)
        {
            stuckAt = 0f;
            var cells = size * size;
            var blocked = new List<Func<Vector2, bool>>[steps];
            foreach (var (time, hits) in hazards)
            {
                var s = (int)MathF.Round((time - start) / Step);
                if (s >= 0 && s < steps) (blocked[s] ??= []).Add(hits);
            }

            var cost = new float[cells];
            var next = new float[cells];
            var parent = new int[steps, cells];
            Array.Fill(cost, float.MaxValue);
            cost[Nearest(planned(start))] = 0f;

            var takeoff = knockback is null ? -1 : (int)MathF.Round((knockback.At - start) / Step);
            var flight = knockback is null ? 0 : Math.Max(1, (int)MathF.Round(knockback.Seconds / Step));
            for (var s = 1; s < steps; s++)
            {
                var t = start + s * Step;
                if (knockback is not null && s > takeoff && s < takeoff + flight) continue;
                if (knockback is not null && s == takeoff + flight)
                {
                    Land(knockback, takeoff, flight, blocked, cost, next, parent, s);
                    (cost, next) = (next, cost);
                    if (Array.TrueForAll(cost, v => v == float.MaxValue))
                    {
                        stuckAt = t;
                        return null;
                    }
                    continue;
                }
                var target = planned(t);
                var holding = t >= hold.From && t <= hold.To;
                var justLanded = knockback is not null && s > takeoff + flight && t <= knockback.At + knockback.Seconds + LandingSlackSeconds;
                var standing = holding && t - Step >= hold.From;
                var reach = speed(t) * Step / Cell;
                Array.Fill(next, float.MaxValue);
                for (var c = 0; c < cells; c++)
                {
                    if (!inside[c]) continue;
                    var p = positions[c];
                    if (holding && Vector2.Distance(p, hold.Spot) > hold.Tolerance) continue;
                    if (blocked[s] is { } here && (justLanded ? here.Exists(h => Around(p).Any(q => h(q))) : here.Exists(h => h(p)))) continue;
                    var (x, z) = (c % size, c / size);
                    var best = float.MaxValue;
                    var from = -1;
                    foreach (var (offset, dx, dz) in moves)
                    {
                        if (standing && offset != 0) continue;
                        if (dx * dx + dz * dz > reach * reach) continue;
                        var (px, pz) = (x - dx, z - dz);
                        if (px < 0 || pz < 0 || px >= size || pz >= size) continue;
                        var prev = c - offset;
                        if (cost[prev] < best)
                        {
                            best = cost[prev];
                            from = prev;
                        }
                    }
                    if (from < 0) continue;
                    next[c] = best + Vector2.DistanceSquared(p, target);
                    parent[s, c] = from;
                }
                (cost, next) = (next, cost);
                if (Array.TrueForAll(cost, v => v == float.MaxValue))
                {
                    stuckAt = t;
                    return null;
                }
            }

            var end = -1;
            for (var c = 0; c < cells; c++)
                if (cost[c] < float.MaxValue && (end < 0 || cost[c] < cost[end])) end = c;
            if (end < 0) return null;
            var route = new Vector2[steps];
            for (var s = steps - 1; s >= 0; s--)
            {
                route[s] = positions[end];
                if (s == 0) break;
                var from = parent[s, end];
                if (knockback is not null && s == takeoff + flight)
                {
                    var launch = positions[from];
                    var away = Away(launch, knockback.Source);
                    for (var k = 1; k < flight; k++)
                        route[takeoff + k] = launch + away * (knockback.Distance * k / flight);
                    s = takeoff + 1;
                }
                end = from;
            }
            return route;
        }

        // Every spot standing at takeoff flies to its own landing; the flight must miss what lands on
        // the way and stay inside the arena.
        private void Land(Knockback knockback, int takeoff, int flight, List<Func<Vector2, bool>>[] blocked,
            float[] cost, float[] next, int[,] parent, int landingStep)
        {
            Array.Fill(next, float.MaxValue);
            for (var c = 0; c < cost.Length; c++)
            {
                if (cost[c] == float.MaxValue) continue;
                var launch = positions[c];
                var away = Away(launch, knockback.Source);
                var clear = true;
                for (var k = 1; k <= flight && clear; k++)
                {
                    var p = launch + away * (knockback.Distance * k / flight);
                    if (p.Length() > Arena || blocked[takeoff + k] is { } here && here.Exists(h => h(p))) clear = false;
                }
                if (!clear) continue;
                var landing = Nearest(launch + away * knockback.Distance);
                if (!inside[landing] || cost[c] >= next[landing]) continue;
                next[landing] = cost[c];
                parent[landingStep, landing] = c;
            }
        }

        private static IEnumerable<Vector2> Around(Vector2 p)
        {
            yield return p;
            for (var i = 0; i < 8; i++)
            {
                var angle = i * MathF.PI / 4f;
                yield return p + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * LandingSlack;
            }
        }

        private static Vector2 Away(Vector2 from, Vector2 source)
        {
            var d = from - source;
            return d.LengthSquared() > 1e-6f ? Vector2.Normalize(d) : Vector2.UnitX;
        }

        private int Nearest(Vector2 p)
        {
            var half = size / 2;
            var x = Math.Clamp((int)MathF.Round(p.X / Cell) + half, 0, size - 1);
            var z = Math.Clamp((int)MathF.Round(p.Y / Cell) + half, 0, size - 1);
            return z * size + x;
        }
    }
}
