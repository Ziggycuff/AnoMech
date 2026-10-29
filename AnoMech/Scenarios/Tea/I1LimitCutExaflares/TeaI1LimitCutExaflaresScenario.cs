using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using AnoMech.Core;
using AnoMech.Core.EnemyActions;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios.Tea.I1LimitCut;
using AnoMech.Scenarios.Ucob;
using AnoMech.Scenarios.Ucob.P5Exaflares;

namespace AnoMech.Scenarios.Tea.I1LimitCutExaflares;

// Not a real mechanic: TEA's Limit Cut with UCoB's Exaflares rolling over it. The bots run 1256 on
// routes planned around the Exaflares at the start of each run.
//
// Extra hard chains each pair of numbers together. Only Super Blassty Charge's knockback breaks a chain:
// the even number has to take it, no immunity and their back to Cruise Chaser, and be flung across the
// quadrant. A chain still whole once the even number should have landed takes both of them with it.
public class TeaI1LimitCutExaflaresScenario : IScenario
{
    // A moment to settle before Limit Cut begins; everything after runs from there.
    private const float StartDelay = 5f;
    // From Limit Cut's start, so the lanes roll through the numbers resolving.
    internal const float ExaflaresStartAt = 4f;
    private const float DespawnAfterLastHit = 4f;
    private const float BgmFromTheTop = 0.01f;
    // Before the bots set out from the middle (Limit Cut AI's routes start at 6s).
    private const float RoutesNeededAt = 5.9f;
    private const float PartnerReadsWarpAfter = 0.05f;

    // FRU's Chains of Everlasting Light: the bright chain, and the status that glows on whoever wears it.
    private const ushort ChainTether = 110;
    private const ushort ChainStatus = 4157;
    // Each pair is chained this long before Cruise Chaser warps in for them, at Limit Cut's own warps.
    private const float ChainBeforeWarp = 3f;
    private static readonly float[] CruiseChaserWarpsAt = [11.55f, 16.16f, 20.80f, 25.50f];
    // Super Blassty Charge's knockback (20y) starts 0.53s after the hit and takes 0.5s.
    private const float ChainCheckAfterHit = 1.3f;
    // Further than anyone covers on foot in that time.
    private const float KnockedAway = 15f;
    private const int SnapFlashes = 1;
    private const float SnapFlashEvery = 0.25f;

    private readonly bool extraHard;

    private readonly TeaI1LimitCutScenario limitCut;
    private ExaflareDodgeRoutes? routes;

    public TeaI1LimitCutExaflaresScenario() : this(extraHard: false) { }

    protected TeaI1LimitCutExaflaresScenario(bool extraHard)
    {
        this.extraHard = extraHard;
        limitCut = new TeaI1LimitCutScenario
        {
            Ai = new TeaI1LimitCutAi((number, time) => PartnerSpot(number, time) ?? routes?.At(number, time), evensTakeTheCharge: extraHard),
        };
    }
    private IScenario Inner => limitCut;
    private readonly List<SimEnemy?> laneHelpers = [];
    private SimEnemy? bahamut;
    private SimEnemy? chainCaster;
    // A person's even partner, stood off where Cruise Chaser really warped in behind them.
    private (int Number, float From, float To, Vector2 Spot)? partner;
    private SimWorld world = null!;

    public string Name => extraHard ? "Limit Cut + Exaflares (Extra hard)" : "Limit Cut + Exaflares (for fun)";
    public IPhase Phase => limitCut.Phase;
    public bool SupportsSolo => Inner.SupportsSolo;
    public IReadOnlyList<IScenarioAi> AiStrats => limitCut.AiStrats;
    public float BgmSecondsAtStart => limitCut.BgmSecondsAtStart;
    public void DrawSettings() => limitCut.DrawSettings();
    public bool HasPerPlayerSettings => limitCut.HasPerPlayerSettings;
    public void DrawPerPlayerSettings() => limitCut.DrawPerPlayerSettings();
    public object SettingsOverrides => limitCut.SettingsOverrides;
    public IReadOnlyList<string> SettingsConflicts => limitCut.SettingsConflicts;
    // Beyond Redemption, Golden Bahamut's theme, until Limit Cut's own switch to BJ/CC's.
    public void RunInstanceEvents(SimWorld instanceWorld)
    {
        // Played, not switched to: a switch hands the track to the game's own music, which holds it for
        // seconds after a stop, so a quick restart picked it up mid-way. Any start past 0s plays it on
        // the plugin's output, which a stop ends at once.
        instanceWorld.Events.Add(0.1f, () =>
        {
            if (!Plugin.Config.SuppressBgm) instanceWorld.PlayBgm(UcobZone.P5.Bgm, BgmFromTheTop);
        });
        instanceWorld.Events.Add(StartDelay, () => limitCut.RunInstanceEvents(instanceWorld));
    }
    public void Tick(float delta, float elapsed) => Inner.Tick(delta, elapsed);
    public void OnActionResolved(ActionResolved resolved) => Inner.OnActionResolved(resolved);
    public void OnStatusAdded(StatusAdded added) => Inner.OnStatusAdded(added);
    public void OnStatusLost(StatusLost lost) => Inner.OnStatusLost(lost);
    public void OnTetherEnded(TetherEnded ended) => Inner.OnTetherEnded(ended);

    public void Run(SimWorld worldParam, int? selectedAi)
    {
        world = worldParam;
        laneHelpers.Clear();
        routes = null;
        partner = null;

        // Rolled now, so the rolls are the same whichever number a person turns out to have.
        var overrides = (TeaI1LimitCutStateOverrides)limitCut.SettingsOverrides;
        var rng = world.Rng.Fork("bahamut-exaflares");
        var axis = overrides.FirstBlasterAxis ?? rng.NextInt(4);
        var heading = rng.NextObj(LaneHeadings(extraHard));
        var exaflares = new UcobP5ExaflaresState(rng, new UcobP5ExaflaresStateOverrides
        {
            Direction = LaneDirection(axis, heading),
            LaneOrder = rng.NextObj(LaneOrders(extraHard, heading)),
        });

        // Times scheduled from inside an event count from that event.
        world.Events.Add(StartDelay, () => RunAfterDelay(selectedAi, axis, exaflares));
    }

    private void RunAfterDelay(int? selectedAi, int axis, UcobP5ExaflaresState exaflares)
    {
        var overrides = (TeaI1LimitCutStateOverrides)limitCut.SettingsOverrides;
        var chosen = overrides.FirstBlasterAxis;
        overrides.FirstBlasterAxis = axis;
        try
        {
            limitCut.Run(world, selectedAi);
        }
        finally
        {
            overrides.FirstBlasterAxis = chosen;
        }
        // Planned off the framework thread once the numbers say which one a person plays, giving that
        // pair's hits room; the bots take their routes as they start out.
        var playing = extraHard && world.Party.Get(world.Party.PlayerRole) is { } seat && !world.Party.IsBotDriven(seat);
        var person = playing ? limitCut.LastState?.NumberOf(world.Party.PlayerRole) ?? 0 : 0;
        if (selectedAi is not null)
        {
            var planning = Task.Run(() => ExaflareDodgeRoutes.Plan(axis, exaflares, ExaflaresStartAt, extraHard, person));
            world.Events.Add(RoutesNeededAt, () =>
            {
                routes = planning.Result;
                foreach (var warning in routes.Warnings) DiagnosticLog.Warn($"[TeaLimitCutExaflares] {warning}");
            });
        }
        if (extraHard && person % 2 == 1)
        {
            var pair = (person - 1) / 2;
            world.Events.Add(CruiseChaserWarpsAt[pair] + PartnerReadsWarpAfter, () => StandPartnerOffWarp(pair));
        }

        if (extraHard)
        {
            world.Events.Add(0.1f, SpawnChainCaster);
            for (var pair = 0; pair < 4; pair++)
            {
                var chained = pair;
                world.Events.Add(CruiseChaserWarpsAt[pair] - ChainBeforeWarp, () => ChainPair(chained));
            }
        }

        var first = UcobActions.ExaflareFirst;
        var rest = UcobActions.ExaflareRest;

        world.Events.Add(ExaflaresStartAt, () => SpawnExaflareActors(exaflares.Lines.Count));
        world.Events.Add(ExaflaresStartAt + 0.5f, () => bahamut?.AddStatusParam(UcobConstants.StatusId.LohsDaih, UcobConstants.LohsDaihTransformation));
        world.Events.Add(ExaflaresStartAt + UcobP5ExaflaresState.BossCastAt, () => bahamut?.Cast(UcobActions.Exaflare));
        for (var lane = 0; lane < exaflares.Lines.Count; lane++)
        {
            var line = exaflares.Lines[lane];
            var helper = lane;
            world.Events.Add(ExaflaresStartAt + line.TelegraphAt, () =>
            {
                laneHelpers[helper]?.SetPosition(new Placement(line.Start, line.Rotation));
                laneHelpers[helper]?.Cast(first);
            });
            foreach (var hit in line.Hits.Skip(1))
                world.Events.Add(ExaflaresStartAt + hit.Time, () =>
                {
                    laneHelpers[helper]?.SetPosition(new Placement(hit.Position, line.Rotation));
                    laneHelpers[helper]?.Cast(rest);
                });
        }
        world.Events.Add(ExaflaresStartAt + exaflares.LastHitAt + DespawnAfterLastHit, DespawnExaflareActors);
    }

    // The lanes roll toward the first Hawk Blaster on the 1/2/5/6 side, the direction that blocks
    // the fewest Limit Cut spots.
    internal static Direction TowardTrackA(int firstBlasterAxis) => LaneDirection(firstBlasterAxis, 0f);

    // The ways the lanes may roll, in degrees clockwise from the first Hawk Blaster on the 1/2/5/6 side:
    // straight at it, the direction that blocks the fewest Limit Cut spots, and in extra hard also
    // across, 90° short of it, the next best.
    internal static float[] LaneHeadings(bool extraHard) => extraHard ? [0f, 270f] : [0f];

    internal static Direction LaneDirection(int firstBlasterAxis, float heading)
    {
        var trackA = firstBlasterAxis switch { 0 => 0f, 1 => 45f, 2 => 270f, _ => 315f };
        return Direction.All[(int)MathF.Round((trackA + heading) % 360f / 45f) % 8];
    }

    internal static float[][] LaneOrders(bool extraHard, float heading)
        => !extraHard ? SolvableLaneOrders : heading == 0f ? ExtraHardLaneOrders : ExtraHardLaneOrdersAcross;

    // The firing orders (offsets across the direction of travel, two per pair) that leave every
    // number a route through 1256 on every Hawk Blaster axis with room to spare (ExaflareDodgeRoutes; regenerate with
    // ExaflareDodgeRoutesTests.ListSolvableLaneOrders). Any other order pins one number's spot under
    // a lane while Cruise Chaser is lining up on them.
    internal static readonly float[][] SolvableLaneOrders =
    [
        [-20, -4, -12, 4, 12, 20], [-20, -4, -12, 12, 4, 20], [-20, -4, -12, 20, 4, 12],
        [-4, 4, -20, -12, 12, 20], [-4, 4, -12, 12, -20, 20], [-4, 4, -12, 20, -20, 12],
        [-4, 12, -20, -12, 4, 20], [-4, 12, -12, 4, -20, 20], [-4, 12, -12, 20, -20, 4],
        [-4, 20, -20, -12, 4, 12], [-4, 20, -12, 4, -20, 12], [-4, 20, -12, 12, -20, 4],
    ];

    // Where Cruise Chaser really warped in behind the person: their even partner stands off that.
    private void StandPartnerOffWarp(int pair)
    {
        if (limitCut.LastState is not { } state || state.Objects.CruiseChaser is not { } cruiseChaser) return;
        if (world.Party.Get(state.Numbers[2 * pair]) is not { } odd) return;
        var even = 2 * pair + 2;
        var spot = ExaflareDodgeRoutes.EvenSpotBehind(new Vector2(cruiseChaser.Position.X, cruiseChaser.Position.Z),
            new Vector2(odd.Position.X, odd.Position.Z), extraHard);
        partner = (even, CruiseChaserWarpsAt[pair] + PartnerReadsWarpAfter, ExaflareDodgeRoutes.EvenLeavesAt(even, extraHard), spot);
    }

    private Vector2? PartnerSpot(int number, float time)
        => partner is { } p && p.Number == number && time >= p.From && time <= p.To ? p.Spot : null;

    // The snap: Photon's hit on both, pulsed.
    private void FlashOnBoth(SimCharacter odd, SimCharacter even)
    {
        for (var flash = 0; flash < SnapFlashes; flash++)
            world.Events.Add(flash * SnapFlashEvery, () =>
            {
                foreach (var member in new[] { odd, even })
                    if (member.IsAlive()) chainCaster?.Cast(TeaConstants.ActionId.PhotonHit, member, animationLock: 0.1f);
            });
    }

    // Shines Photon's hit on a snapped chain's pair; a cast with no effects, only its look.
    private void SpawnChainCaster()
    {
        chainCaster = world.SpawnEnemy(new EnemySpawnConfig(
            BNpcBaseId: TeaConstants.BNpcBaseId.Helper, NameId: TeaConstants.BNpcNameId.CruiseChaser, Level: TeaConstants.Level,
            Targetable: false, EnemyList: EnemyListMode.Never, Visibility: SpawnVisibility.InvisibleHelper,
            Placement: new Placement(Vector3.Zero, 0f)));
    }

    private void ChainPair(int pair)
    {
        if (limitCut.LastState is not { } state) return;
        var even = 2 * pair + 2;
        if (world.Party.Get(state.Numbers[even - 2]) is not { } oddMember || !oddMember.IsAlive()) return;
        if (world.Party.Get(state.Numbers[even - 1]) is not { } evenMember || !evenMember.IsAlive()) return;
        var chargedIn = TeaI1LimitCutAi.HitAt[even - 1] - (CruiseChaserWarpsAt[pair] - ChainBeforeWarp);
        var chain = world.Tether(oddMember, evenMember, ChainTether);
        foreach (var member in new[] { oddMember, evenMember })
            member.AddStatus(ChainStatus, chargedIn + ChainCheckAfterHit);
        world.Events.Add(chargedIn, () =>
        {
            var standing = evenMember.Position;
            world.Events.Add(ChainCheckAfterHit, () => SnapOrBurn(chain, oddMember, evenMember, standing));
        });
    }

    private void SnapOrBurn(SimTether chain, SimCharacter odd, SimCharacter even, Vector3 standing)
    {
        if (!chain.IsActive) return;
        chain.Despawn();
        odd.RemoveStatus(ChainStatus);
        even.RemoveStatus(ChainStatus);
        if (Vector3.Distance(even.Position, standing) >= KnockedAway)
        {
            FlashOnBoth(odd, even);
            return;
        }
        foreach (var member in new[] { odd, even })
            if (member.IsAlive()) member.Die(SimCharacterDeathExtensions.Environment, "the chain wasn't broken by Super Blassty Charge's knockback");
    }

    // As SolvableLaneOrders, with the even numbers knocked across their quadrant (and ChainEveryPair).
    // As ExtraHardLaneOrders, for the lanes rolling across, 90° short of track A.
    internal static readonly float[][] ExtraHardLaneOrdersAcross =
    [
        [-20, -12, -4, 4, 12, 20], [-20, -12, -4, 12, 4, 20], [-20, -12, -4, 20, 4, 12],
        [-20, -12, 4, 12, -4, 20], [-20, -12, 4, 20, -4, 12], [-20, -12, 12, 20, -4, 4],
        [-12, -4, 4, 12, -20, 20], [-12, -4, 4, 20, -20, 12], [-12, -4, 12, 20, -20, 4],
        [-12, 20, -4, 4, -20, 12], [-12, 20, -4, 12, -20, 4], [-12, 20, 4, 12, -20, -4],
    ];

    internal static readonly float[][] ExtraHardLaneOrders =
    [
        [-20, 4, -12, -4, 12, 20], [-20, 12, -12, -4, 4, 20], [4, 12, -12, -4, -20, 20],
        [4, 20, -12, -4, -20, 12], [12, 20, -12, -4, -20, 4],
    ];

    // Bahamut hovers over the middle and casts; the lanes fire from their own helpers.
    private void SpawnExaflareActors(int lanes)
    {
        bahamut = world.SpawnEnemy(new EnemySpawnConfig(
            BNpcBaseId: UcobConstants.BNpcBaseId.BahamutPrime, NameId: UcobConstants.BNpcNameId.BahamutPrime, Level: TeaConstants.Level,
            Targetable: false, EnemyList: EnemyListMode.Never, Visibility: SpawnVisibility.Visible,
            Placement: new Placement(new Vector3(0f, 5f, 0f), 0f)));
        for (var i = 0; i < lanes; i++)
            laneHelpers.Add(world.SpawnEnemy(new EnemySpawnConfig(
                BNpcBaseId: TeaConstants.BNpcBaseId.Helper, NameId: UcobConstants.BNpcNameId.BahamutPrime, Level: TeaConstants.Level,
                Targetable: false, EnemyList: EnemyListMode.Never, Visibility: SpawnVisibility.InvisibleHelper,
                Placement: new Placement(Vector3.Zero, 0f))));
    }

    private void DespawnExaflareActors()
    {
        bahamut?.Despawn();
        chainCaster?.Despawn();
        foreach (var helper in laneHelpers) helper?.Despawn();
        laneHelpers.Clear();
    }
}

public sealed class TeaI1LimitCutExaflaresExtraHardScenario() : TeaI1LimitCutExaflaresScenario(extraHard: true);
