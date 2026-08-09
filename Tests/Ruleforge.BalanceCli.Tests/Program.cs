using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using RuleforgeTD.BalanceCli.Balance;
using RuleforgeTD.BalanceCli.Content;
using RuleforgeTD.BalanceCli.Evaluation;
using RuleforgeTD.BalanceCli.Infrastructure;
using RuleforgeTD.BalanceCli.Llm;
using RuleforgeTD.BalanceCli.Policies;
using RuleforgeTD.BalanceCli.Simulation;
using RuleforgeTD.BalanceCli.Terminal;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;
using RuleforgeTD.GameLogic.Simulation;

namespace RuleforgeTD.BalanceCli.Tests;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            RepositoryPaths paths = RepositoryPaths.Discover(
                args.Length > 0 ? args[0] : null);
            var suite = new VerificationSuite(paths);
            IReadOnlyList<TestCase> cases = suite.Cases();
            int failures = 0;
            var total = Stopwatch.StartNew();

            Console.WriteLine(
                "Ruleforge Balance CLI verification (repository: " +
                paths.Root + ")");
            foreach (TestCase test in cases)
            {
                var elapsed = Stopwatch.StartNew();
                try
                {
                    test.Body();
                    Console.WriteLine(
                        "[PASS] " + test.Name + " (" +
                        elapsed.ElapsedMilliseconds + " ms)");
                }
                catch (Exception exception)
                {
                    failures++;
                    Console.Error.WriteLine(
                        "[FAIL] " + test.Name + " (" +
                        elapsed.ElapsedMilliseconds + " ms)");
                    Console.Error.WriteLine(exception.ToString());
                }
            }

            Console.WriteLine(
                "Completed " + cases.Count + " checks in " +
                total.ElapsedMilliseconds + " ms; failures=" + failures + ".");
            return failures == 0 ? 0 : 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.ToString());
            return 2;
        }
    }
}

internal sealed record TestCase(string Name, Action Body);

internal sealed class VerificationSuite
{
    private const ulong IntegrationGameSeed = 910001;
    private const ulong IntegrationPolicySeed = 920001;

    private readonly RepositoryPaths paths;
    private readonly HeadlessContentLoader loader;
    private SimulationRunOutput? firstRun;
    private SimulationRunOutput? secondRun;

    public VerificationSuite(RepositoryPaths paths)
    {
        this.paths = paths;
        loader = new HeadlessContentLoader(paths);
    }

    public IReadOnlyList<TestCase> Cases() => new[]
    {
        new TestCase(
            "difficulty profiles reject schema and unknown fields",
            DifficultyProfilesAreStrict),
        new TestCase(
            "difficulty override values reject invalid ranges",
            DifficultyOverrideRangesAreStrict),
        new TestCase(
            "current difficulty profile is an identity overlay",
            CurrentProfileIsIdentityOverlay),
        new TestCase(
            "automatic balance patches reject forbidden fields",
            ForbiddenPatchFieldIsRejected),
        new TestCase(
            "seed sets are disjoint and reject overlap",
            SeedSetsAreDisjoint),
        new TestCase(
            "all active dual-interpretation cards compile",
            AllActiveCardsLoad),
        new TestCase(
            "fixed card scenarios disable every reward-card source",
            FixedCardScenariosDisableEveryRewardSource),
        new TestCase(
            "legal actions are authoritative and accepted",
            LegalActionsAreAuthoritative),
        new TestCase(
            "combat construction is legal while upgrades are rejected",
            CombatConstructionAndUpgradeLockAreAuthoritative),
        new TestCase(
            "policy decisions are deterministic for a fixed policy seed",
            PolicySeedIsDeterministic),
        new TestCase(
            "stable snapshot projection hashes identically",
            StableSnapshotHashIsStable),
        new TestCase(
            "telemetry observation does not change simulation state",
            TelemetryDoesNotChangeState),
        new TestCase(
            "live progress observation preserves authoritative state",
            LiveProgressPreservesAuthoritativeState),
        new TestCase(
            "live terminal frame exposes current combat metrics",
            LiveTerminalFrameContainsCombatMetrics),
        new TestCase(
            "invalid policy actions are rejected by the driver",
            InvalidPolicyActionIsRejected),
        new TestCase(
            "LLM selections cannot invent an action id",
            LlmCannotInventActionId),
        new TestCase(
            "card fixtures support legal levels, order and mixed subjects",
            CardFixtureSupportsLevelOrderAndMixedSubjects),
        new TestCase(
            "coverage novice fixtures use accepted commands for exact slots and subjects",
            CoverageNoviceFixtureUsesAuthoritativeCommands),
        new TestCase(
            "card-pack loadouts preserve explicit global single-card limits",
            CardPackLoadoutsPreserveGlobalSingleCardLimit),
        new TestCase(
            "synergy lookup requires exact difficulty tower level slots and subjects",
            SynergyLookupRequiresExactContext),
        new TestCase(
            "triple discovery enumerates only three-slot pair contexts",
            TripleDiscoveryEnumeratesThreeSlotContexts),
        new TestCase(
            "card runtime failures remain matched losses with diagnostics",
            CardRuntimeFailuresRemainMatchedLosses),
        new TestCase(
            "batch safety and rejected-command runs are effective losses",
            BatchRuntimeFailuresAreLosses),
        new TestCase(
            "frozen policy files match their recorded hashes",
            FrozenPolicyHashesMatch),
        new TestCase(
            "authoritative policy run reaches a terminal phase",
            AuthoritativeRunReachesTerminalPhase),
        new TestCase(
            "same seeds reproduce the same terminal state",
            SameSeedsAreDeterministic),
        new TestCase(
            "recorded replay matches commands and final state",
            ReplayMatches),
        new TestCase(
            "timeout replay outcome is derived from limits",
            TimeoutReplayOutcomeIsDerived),
        new TestCase(
            "unreplayable policy or host errors cannot tautologically match",
            UnreplayablePolicyOrHostErrorDoesNotMatch)
    };

    private void DifficultyProfilesAreStrict()
    {
        string? baseHash = null;
        foreach (string difficulty in new[]
                 {
                     "current", "easy", "medium", "hard"
                 })
        {
            DifficultyProfile profile = JsonSupport.ReadStrict<DifficultyProfile>(
                paths.Profile(difficulty));
            DifficultyProfileValidator.Validate(profile, difficulty);
            LoadedSimulationContent loaded = loader.Load(
                difficulty,
                SimulationScenario.Standard());
            baseHash ??= loaded.BaseContentHash;
            Verify.Equal(baseHash, loaded.BaseContentHash,
                "All overlays must target the same base catalog.");
        }

        DifficultyProfile invalidSchema =
            JsonSupport.ReadStrict<DifficultyProfile>(paths.Profile("current"));
        invalidSchema.SchemaVersion = 999;
        Verify.Throws<InvalidOperationException>(() =>
            DifficultyProfileValidator.Validate(invalidSchema, "current"));

        JsonNode root = JsonNode.Parse(
            File.ReadAllText(paths.Profile("current"))) ??
            throw new InvalidOperationException("Profile JSON parsed to null.");
        root["unexpectedField"] = true;
        Verify.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<DifficultyProfile>(
                root.ToJsonString(),
                JsonSupport.StrictOptions));
    }

    private void ForbiddenPatchFieldIsRejected()
    {
        DifficultyProfile source =
            JsonSupport.ReadStrict<DifficultyProfile>(paths.Profile("current"));
        var patch = new BalancePatch
        {
            ProposalId = "forbidden-field-fixture",
            Difficulty = source.DifficultyId,
            SourceProfileHash = BalanceProfileHasher.Compute(source),
            Diagnosis = new List<BalanceDiagnosis>
            {
                new()
                {
                    Metric = "baseHealth",
                    Actual = 20,
                    Target = "unchanged",
                    Evidence = "schema guard fixture"
                }
            },
            Changes = new List<BalanceChange>
            {
                new()
                {
                    JsonPointer = "/run/baseHealth",
                    OldValue = 20,
                    NewValue = 19,
                    ReasonCode = "FORBIDDEN_FIXTURE"
                }
            }
        };

        BalancePatchValidationResult result =
            new BalanceProposalValidator().Validate(source, patch);
        Verify.False(result.IsValid, "A structural field was accepted.");
        Verify.Contains(
            result.Errors,
            value => value.Contains(
                "not approved",
                StringComparison.OrdinalIgnoreCase),
            "The validator did not identify the forbidden field.");
    }

    private void DifficultyOverrideRangesAreStrict()
    {
        DifficultyProfile invalid =
            JsonSupport.ReadStrict<DifficultyProfile>(paths.Profile("easy"));
        invalid.EnemyOverrides.Add(new EnemyBalanceOverride
        {
            EnemyId = "raider",
            MaxHealthMilli = 0,
            FireResistanceBps = 10001
        });
        invalid.WaveOverrides.Add(new WaveBalanceOverride
        {
            WaveId = "wave_1",
            Spawns = new List<WaveSpawnBalanceOverride>
            {
                new()
                {
                    EnemyId = "raider",
                    Occurrence = 0,
                    Count = -1,
                    IntervalTicks = 0
                }
            }
        });
        invalid.BossOverrides.Add(new BossBalanceOverride
        {
            EnemyId = "boss_guardian",
            AbilityIntervalTicks = 0,
            ShieldBps = -1
        });

        InvalidOperationException exception =
            Verify.Throws<InvalidOperationException>(() =>
                DifficultyProfileValidator.Validate(invalid, "easy"));
        Verify.True(
            exception.Message.Contains("must be positive", StringComparison.Ordinal) &&
            exception.Message.Contains("between 0 and 10000", StringComparison.Ordinal),
            "Invalid override values were not described: " + exception.Message);
    }

    private void CurrentProfileIsIdentityOverlay()
    {
        LoadedSimulationContent loaded = loader.Load(
            "current",
            SimulationScenario.Standard());
        Verify.Equal(
            loader.ComputeBaseContentHash(),
            loaded.BaseContentHash,
            "The loader reported a different base catalog hash.");
        Verify.Equal(
            loaded.BaseContentHash,
            loaded.CompiledContentHash,
            "current.profile.json changed the compiled authoritative content.");
        Verify.Equal(
            0,
            loaded.Profile.EnemyOverrides.Count +
            loaded.Profile.WaveOverrides.Count +
            loaded.Profile.BossOverrides.Count,
            "The current profile contains non-identity overrides.");
    }

    private void SeedSetsAreDisjoint()
    {
        var counts = new Dictionary<SeedSetKind, int>
        {
            [SeedSetKind.Train] = 64,
            [SeedSetKind.Validation] = 64,
            [SeedSetKind.Holdout] = 128
        };
        SeedSetDocument production = SeedSetLoader.Load(paths.SeedSets, counts);
        Verify.Equal(64, production.Train.Count);
        Verify.Equal(64, production.Validation.Count);
        Verify.Equal(128, production.Holdout.Count);

        var overlapping = new SeedSetDocument
        {
            Train = new List<SeedPair> { new(1, 11) },
            Validation = new List<SeedPair> { new(1, 12) },
            Holdout = new List<SeedPair> { new(3, 13) }
        };
        SeedSetValidationException exception =
            Verify.Throws<SeedSetValidationException>(() =>
                SeedSetLoader.Validate(overlapping));
        Verify.Contains(
            exception.Errors,
            value => value.Contains("overlaps", StringComparison.Ordinal),
            "Overlapping game seeds were not reported.");
    }

    private void AllActiveCardsLoad()
    {
        LoadedSimulationContent loaded = loader.Load(
            "current",
            SimulationScenario.Standard());
        int sourceCardCount = CountSourceCards();
        CompiledCardDefinition[] cards = loaded.Content.Cards;
        Verify.Equal(sourceCardCount, cards.Length,
            "Not every base/module card reached the compiled catalog.");
        Verify.True(cards.Length > 0, "The active card catalog is empty.");
        Verify.Equal(
            cards.Length,
            cards.Select(card => card.StableId)
                .Distinct(StringComparer.Ordinal)
                .Count(),
            "Compiled card IDs are not unique.");
        foreach (CompiledCardDefinition card in cards)
        {
            Verify.True(
                !string.IsNullOrWhiteSpace(card.StableId),
                "A compiled card has no stable ID.");
            Verify.True(
                card.ProjectileEffects.Length > 0,
                card.StableId + " has no projectile interpretation.");
            Verify.True(
                card.EnemyEffects.Length > 0,
                card.StableId + " has no enemy interpretation.");
        }
    }

    private void LegalActionsAreAuthoritative()
    {
        LoadedSimulationContent loaded = loader.Load(
            "current",
            SimulationScenario.Standard());
        var simulation = new GameSimulation();
        simulation.Initialize(loaded.Content, 930001);
        var generator = new LegalActionGenerator();

        SimulationSnapshot awaiting = simulation.GetSnapshot();
        IReadOnlyList<LegalAction> starting = generator.Generate(
            simulation,
            awaiting);
        Verify.True(starting.Count > 0, "No starting tower action exists.");
        Verify.True(
            starting.All(action =>
                action.Kind == LegalActionKind.ChooseStartingTower &&
                action.HasCommand),
            "Starting actions contain a non-authoritative action.");
        SubmitAccepted(simulation, starting[0]);

        SimulationSnapshot planning = simulation.GetSnapshot();
        IReadOnlyList<LegalAction> planningActions = generator.Generate(
            simulation,
            planning);
        LegalAction build = planningActions.First(action =>
            action.Kind == LegalActionKind.PlaceTower && action.Cost == 0);
        SubmitAccepted(simulation, build);

        planning = simulation.GetSnapshot();
        Verify.Equal(1, planning.Towers.Length,
            "The accepted placement did not create the tower.");
        planningActions = generator.Generate(simulation, planning);
        LegalAction startWave = planningActions.Single(action =>
            action.Kind == LegalActionKind.StartWave);
        SubmitAccepted(simulation, startWave);

        SimulationSnapshot combat = simulation.GetSnapshot();
        Verify.Equal(RunPhase.Combat, combat.Phase);
        IReadOnlyList<LegalAction> combatActions = generator.Generate(
            simulation,
            combat);
        Verify.Contains(
            combatActions,
            action => action.Kind == LegalActionKind.NoOp && !action.HasCommand,
            "Combat has no logical-tick action.");
        Verify.False(
            combatActions.Any(action =>
                action.Command?.Type == GameCommandType.GrantDebugGold),
            "Legal actions exposed the debug-gold command.");
    }

    private void StableSnapshotHashIsStable()
    {
        LoadedSimulationContent loaded = loader.Load(
            "current",
            SimulationScenario.Standard());
        var simulation = new GameSimulation();
        simulation.Initialize(loaded.Content, 940001);
        SimulationSnapshot snapshot = simulation.GetSnapshot();

        string firstJson = JsonSupport.SerializeStable(
            StableSnapshotProjection.From(snapshot, loaded.Content));
        string secondJson = JsonSupport.SerializeStable(
            StableSnapshotProjection.From(snapshot, loaded.Content));
        string firstHash = StableSnapshotProjection.ComputeHash(
            snapshot,
            loaded.Content);
        string secondHash = StableSnapshotProjection.ComputeHash(
            snapshot,
            loaded.Content);
        Verify.Equal(firstJson, secondJson);
        Verify.Equal(firstHash, secondHash);
        Verify.Equal(JsonSupport.Sha256Text(firstJson), firstHash);
    }

    private void CombatConstructionAndUpgradeLockAreAuthoritative()
    {
        var scenario = SimulationScenario.Standard();
        scenario.StartingGoldOverride = 1000;
        LoadedSimulationContent loaded = loader.Load("current", scenario);
        var simulation = new GameSimulation();
        simulation.Initialize(loaded.Content, 932001);
        string startingTower = loaded.Content.GetTower(
            loaded.Content.Run.StartingTowerChoices[0]).StableId;
        Verify.True(simulation.Submit(
            GameCommand.ChooseStartingTower(startingTower)).Accepted);
        Verify.True(simulation.Submit(
            GameCommand.PlaceTower(startingTower, 0)).Accepted);
        int towerInstanceId = simulation.GetSnapshot().Towers.Single().Id;
        Verify.True(simulation.Submit(GameCommand.StartWave()).Accepted);

        SimulationSnapshot combat = simulation.GetSnapshot();
        Verify.Equal(RunPhase.Combat, combat.Phase);
        string additionalTower = combat.UnlockedTowerIds
            .OrderBy(value => value, StringComparer.Ordinal)
            .First();
        CommandResult build = simulation.Submit(
            GameCommand.PlaceTower(additionalTower, 1));
        Verify.True(
            build.Accepted,
            "Combat construction was rejected: " + build.Error + " " +
            build.Message);

        ulong beforeRejectedUpgrade = simulation.ComputeStateHash();
        CommandResult upgrade = simulation.Submit(
            GameCommand.UpgradeTower(towerInstanceId));
        Verify.False(upgrade.Accepted, "Combat upgrade unexpectedly succeeded.");
        Verify.Equal(CommandError.CombatLoadoutLocked, upgrade.Error);
        Verify.Equal(
            beforeRejectedUpgrade,
            simulation.ComputeStateHash(),
            "A rejected combat upgrade changed authoritative state.");
    }

    private void PolicySeedIsDeterministic()
    {
        LoadedSimulationContent loaded = loader.Load(
            "current",
            SimulationScenario.Standard());
        var simulation = new GameSimulation();
        simulation.Initialize(loaded.Content, 935001);
        SimulationSnapshot snapshot = simulation.GetSnapshot();
        IReadOnlyList<LegalAction> actions = new LegalActionGenerator()
            .Generate(simulation, snapshot);
        PublicGameKnowledge knowledge =
            PublicGameKnowledge.FromContent(loaded.Content);

        PolicyDecision first = PolicyRegistry.Create("novice-random-spender")
            .Decide(
                snapshot,
                CreatePolicyContext(
                    actions,
                    knowledge,
                    SimulationScenario.Standard(),
                    935002));
        PolicyDecision second = PolicyRegistry.Create("novice-random-spender")
            .Decide(
                snapshot,
                CreatePolicyContext(
                    actions,
                    knowledge,
                    SimulationScenario.Standard(),
                    935002));

        Verify.Equal(first.ActionId, second.ActionId);
        Verify.Equal(first.ReasonCode, second.ReasonCode);
        Verify.Contains(
            actions,
            action => string.Equals(
                action.ActionId,
                first.ActionId,
                StringComparison.Ordinal),
            "The deterministic policy selected a non-legal action.");
    }

    private void TelemetryDoesNotChangeState()
    {
        LoadedSimulationContent loaded = loader.Load(
            "current",
            SimulationScenario.Standard());
        var observed = new GameSimulation();
        var control = new GameSimulation();
        observed.Initialize(loaded.Content, 950001);
        control.Initialize(loaded.Content, 950001);
        var telemetry = new SimulationTelemetry();
        telemetry.ObserveInitial(observed.GetSnapshot());
        telemetry.ObserveCommandEvents(
            observed.ReadPresentationEvents(),
            observed.GetSnapshot());

        string towerId = loaded.Content.GetTower(
            loaded.Content.Run.StartingTowerChoices[0]).StableId;
        int sequence = 0;
        SubmitBoth(
            observed,
            control,
            GameCommand.ChooseStartingTower(towerId),
            telemetry,
            loaded.Content,
            sequence++);
        int buildPoint = observed.GetSnapshot().BuildSpots
            .First(spot => spot.Unlocked).Index;
        SubmitBoth(
            observed,
            control,
            GameCommand.PlaceTower(towerId, buildPoint),
            telemetry,
            loaded.Content,
            sequence++);
        SubmitBoth(
            observed,
            control,
            GameCommand.StartWave(),
            telemetry,
            loaded.Content,
            sequence);

        for (int tick = 0; tick < 240; tick++)
        {
            SimulationSnapshot beforeObserved = observed.GetSnapshot();
            SimulationSnapshot beforeControl = control.GetSnapshot();
            observed.Step();
            control.Step();
            SimulationSnapshot afterObserved = observed.GetSnapshot();
            SimulationSnapshot afterControl = control.GetSnapshot();
            telemetry.ObserveStep(
                beforeObserved,
                afterObserved,
                observed.ReadPresentationEvents(),
                loaded.Content);
            Verify.Equal(
                control.ComputeStateHash(),
                observed.ComputeStateHash(),
                "Telemetry observation changed authoritative state at tick " +
                tick + ".");
            Verify.Equal(beforeControl.Phase, beforeObserved.Phase);
            Verify.Equal(afterControl.Phase, afterObserved.Phase);
            if (afterControl.Phase != RunPhase.Combat)
            {
                break;
            }
        }

        Verify.Equal(
            StableSnapshotProjection.ComputeHash(
                control.GetSnapshot(), loaded.Content),
            StableSnapshotProjection.ComputeHash(
                observed.GetSnapshot(), loaded.Content));
    }

    private void InvalidPolicyActionIsRejected()
    {
        var driver = new HeadlessRunDriver(loader);
        SimulationRunOutput output = driver.Execute(
            new SimulationRunRequest
            {
                DifficultyId = "current",
                GameSeed = 960001,
                PolicySeed = 960002,
                Scenario = new SimulationScenario
                {
                    ScenarioId = "invalid-policy-action",
                    MaximumLogicalTicks = 10,
                    MaximumDecisions = 10,
                    CaptureReplay = false,
                    CaptureTelemetry = true
                }
            },
            new InvalidActionPolicy());
        Verify.Equal(SimulationOutcome.Error, output.Result.Result);
        Verify.True(
            output.Result.Error?.Contains(
                "non-legal action",
                StringComparison.OrdinalIgnoreCase) == true,
            "The driver did not report the invalid action: " +
            output.Result.Error);
    }

    private void LiveProgressPreservesAuthoritativeState()
    {
        SimulationRunOutput control = FirstRun();
        var observer = new RecordingProgressObserver();
        SimulationRunOutput observed = RunDeterministicIntegration(observer);

        Verify.True(observer.UpdateCount > 1,
            "The live observer did not receive simulation progress.");
        Verify.True(observer.Latest?.Outcome.HasValue == true,
            "The live observer did not receive a terminal update.");
        Verify.Equal(
            control.Result.FinalStateHash,
            observed.Result.FinalStateHash,
            "A read-only live observer changed authoritative state.");
        Verify.Equal(
            control.Result.FinalSnapshotHash,
            observed.Result.FinalSnapshotHash,
            "A read-only live observer changed the final snapshot.");
    }

    private static void LiveTerminalFrameContainsCombatMetrics()
    {
        string frame = LiveTerminalDashboard.BuildFrame(
            new SimulationProgressUpdate
            {
                DifficultyId = "current",
                PolicyId = "test-policy",
                GameSeed = 1001,
                PolicySeed = 2001,
                Tick = 75,
                TickRate = 30,
                Phase = RunPhase.Combat,
                WaveNumber = 2,
                TotalWaves = 5,
                BaseHealth = 18,
                StartingBaseHealth = 20,
                Gold = 42,
                GoldEarned = 70,
                GoldSpent = 28,
                EnemiesAlive = 5,
                EnemiesKilled = 7,
                EnemiesLeaked = 1,
                ActiveProjectiles = 9,
                ActiveStatuses = 4,
                TowerCount = 3,
                TotalDamageMilli = 123400,
                Decisions = 90,
                LastAction = "combat:noop"
            },
            120);

        Verify.True(frame.Contains("Gold        42", StringComparison.Ordinal));
        Verify.True(frame.Contains("Killed  7", StringComparison.Ordinal));
        Verify.True(frame.Contains("5 alive", StringComparison.Ordinal));
        Verify.True(frame.Contains("Wave  2 / 5", StringComparison.Ordinal));
        Verify.True(frame.Contains("123.4", StringComparison.Ordinal));
    }

    private void LlmCannotInventActionId()
    {
        var legal = new[]
        {
            new LegalAction
            {
                ActionId = "no-op",
                Kind = LegalActionKind.NoOp,
                Summary = "Advance one logical tick"
            }
        };
        InvalidDataException exception = Verify.Throws<InvalidDataException>(() =>
            LlmPlayerAdapter.ParseAndValidateResponse(
                "{\"selectedActionId\":\"invented\",\"reasonCode\":\"NO_OP\"}",
                legal));
        Verify.True(
            exception.Message.Contains("illegal actionId", StringComparison.Ordinal),
            "The LLM adapter did not identify the invented action: " +
            exception.Message);
    }

    private void CardFixtureSupportsLevelOrderAndMixedSubjects()
    {
        CardExperimentEnumeration enumeration =
            new CompiledCardExperimentEnumerator(loader).Enumerate(
                "easy",
                new CardExperimentEnumerationOptions
                {
                    IncludePairExperiments = true,
                    MaximumPairExperiments = 50000
                });
        CardSynergyPairExperiment pair = enumeration.PairExperiments
            .Where(value =>
                value.First.SubjectType != value.Second.SubjectType &&
                string.Equals(
                    value.First.CardId,
                    "accelerate",
                    StringComparison.Ordinal) &&
                string.Equals(
                    value.Second.CardId,
                    "bleed",
                    StringComparison.Ordinal))
            .OrderBy(value => value.TowerDefinitionId, StringComparer.Ordinal)
            .ThenBy(value => value.TowerLevel)
            .ThenBy(value => value.First.CardId, StringComparer.Ordinal)
            .ThenBy(value => value.Second.CardId, StringComparer.Ordinal)
            .First();
        var runner = new CardExperimentSimulationRunner(loader);
        var seed = new SeedPair(970001, 970002);
        EvaluationRunMetrics baseline = runner.RunAsync(
                new CardExperimentVariant(
                    "mixed-subject-baseline",
                    pair.DifficultyId,
                    pair.TowerDefinitionId,
                    pair.TowerLevel,
                    CardExperimentVariantKind.Baseline,
                    Array.Empty<CardProgramStep>()),
                seed,
                default)
            .GetAwaiter().GetResult();
        EvaluationRunMetrics candidate = runner.RunAsync(
                new CardExperimentVariant(
                    "mixed-subject-candidate",
                    pair.DifficultyId,
                    pair.TowerDefinitionId,
                    pair.TowerLevel,
                    CardExperimentVariantKind.Program,
                    new[] { pair.First, pair.Second }),
                seed,
                default)
            .GetAwaiter().GetResult();
        Verify.True(
            baseline.IsValid,
            "Baseline fixture failed: " + baseline.FailureReason);
        Verify.True(
            candidate.IsValid,
            "Mixed-subject fixture failed: " + candidate.FailureReason);
        Verify.True(candidate.FixtureVerified);
        Verify.Equal(baseline.FixtureContextHash, candidate.FixtureContextHash);
        Verify.False(
            string.IsNullOrWhiteSpace(candidate.ScenarioHash),
            "The fixture scenario was not hashed.");
    }

    private void CoverageNoviceFixtureUsesAuthoritativeCommands()
    {
        var card = new CardProgramStep(
            "mark",
            SubjectType.Enemy,
            1);
        var variant = new CardExperimentVariant(
            "coverage-novice-authoritative-fixture",
            "easy",
            "ballista",
            4,
            CardExperimentVariantKind.Program,
            new[] { card });
        var runner = new CardExperimentSimulationRunner(
            loader,
            new CardExperimentSimulationOptions
            {
                CoverageNoviceMode = true
            });
        EvaluationRunMetrics metrics = runner.RunAsync(
                variant,
                new SeedPair(975001, 975002),
                default)
            .GetAwaiter().GetResult();
        Verify.True(
            metrics.IsValid,
            "Coverage novice fixture was invalid: " +
            metrics.FailureReason);
        Verify.True(
            metrics.FixtureVerified,
            "Coverage novice fixture did not verify its exact placement.");
        Verify.False(
            metrics.IsRuntimeFailure,
            "Coverage novice fixture used a rejected command or hit a " +
            "runtime guard: " + metrics.FailureReason);
        Verify.False(
            string.IsNullOrWhiteSpace(metrics.ScenarioHash),
            "Coverage novice fixture did not retain its scenario hash.");

        var commandScenario = new SimulationScenario
        {
            ScenarioId = "coverage-novice-command-proof",
            ForcedStartingTowerId = "ballista",
            ForcedPlacedTowerId = "ballista",
            ForcedTowerLevel = 4,
            ForcedTowerLevelIsMinimum = true,
            StartingGoldOverride = 10000,
            FixtureControlCardId = "slow",
            FixtureCardProgram = new List<SimulationCardFixtureSlot>
            {
                new()
                {
                    Order = 0,
                    CardId = card.CardId,
                    SlotIndex = card.SlotIndex,
                    SubjectType = card.SubjectType
                }
            },
            DisableCardRewardChoices = true,
            ReplaceStartingCards = true,
            AdditionalStartingCards = new List<string> { "slow", card.CardId },
            MaximumLogicalTicks = 1,
            MaximumDecisions = 1000,
            CaptureReplay = true,
            CaptureTelemetry = true
        };
        SimulationRunOutput output = new HeadlessRunDriver(loader).Execute(
            new SimulationRunRequest
            {
                DifficultyId = "easy",
                PolicyId = "card-coverage-novice",
                GameSeed = 975003,
                PolicySeed = 975004,
                Scenario = commandScenario,
                WriteResult = false,
                WriteReplay = false
            },
            new CardCoverageNovicePolicy());
        ReplayRecord replay = output.Replay ??
            throw new VerificationException(
                "Coverage novice command proof did not capture a replay.");
        Verify.True(
            replay.Commands.All(command => command.Accepted),
            "Coverage novice command proof recorded a rejected GameCommand.");

        CommandLogEntry subjectCommand = replay.Commands.Single(command =>
            command.Type == GameCommandType.SetTowerSlotSubjectType &&
            command.SecondaryId == card.SlotIndex &&
            command.TertiaryId == (int)card.SubjectType);
        Verify.True(
            subjectCommand.ActionId.StartsWith(
                "subject:",
                StringComparison.Ordinal),
            "The subject command did not originate from a LegalAction.");
        CommandLogEntry equipCommand = replay.Commands.Single(command =>
            command.Type == GameCommandType.EquipCard &&
            command.SecondaryId == subjectCommand.PrimaryId &&
            command.TertiaryId == card.SlotIndex);
        Verify.True(
            equipCommand.ActionId.StartsWith("equip:", StringComparison.Ordinal),
            "The equip command did not originate from a LegalAction.");
        Verify.Contains(
            replay.FinalCards,
            equipped =>
                equipped.CardInstanceId == equipCommand.PrimaryId &&
                string.Equals(
                    equipped.CardId,
                    card.CardId,
                    StringComparison.Ordinal) &&
                equipped.TowerInstanceId == subjectCommand.PrimaryId &&
                equipped.SlotIndex == card.SlotIndex &&
                equipped.SubjectType == card.SubjectType,
            "The accepted commands did not produce the exact fixture card, " +
            "slot and subject in the authoritative final snapshot.");
    }

    private void CardPackLoadoutsPreserveGlobalSingleCardLimit()
    {
        AssertCardPackLoadoutLimit(
            new CardCoverageNovicePolicy(),
            976003,
            976004);
    }

    private void AssertCardPackLoadoutLimit(
        IPlayerPolicy policy,
        ulong gameSeed,
        ulong policySeed)
    {
        var scenario = SimulationScenario.Standard();
        scenario.ScenarioId = "card-pack-limit-" + policy.PolicyId;
        scenario.MaximumLogicalTicks = 60000;
        scenario.MaximumDecisions = 200000;
        scenario.CaptureReplay = true;
        scenario.CaptureTelemetry = true;
        scenario.WorldCardPackProgressThresholdOverride = 10000;
        SimulationRunOutput output = new HeadlessRunDriver(loader).Execute(
            new SimulationRunRequest
            {
                DifficultyId = "easy",
                PolicyId = policy.PolicyId,
                GameSeed = gameSeed,
                PolicySeed = policySeed,
                Scenario = scenario,
                WriteResult = false,
                WriteReplay = false
            },
            policy);
        Verify.True(
            output.Result.Result is
                SimulationOutcome.Victory or SimulationOutcome.Defeat,
            policy.PolicyId + " did not reach a terminal simulation state: " +
            output.Result.Result + " " + output.Result.Error);
        Verify.Equal(
            0,
            output.Result.RejectedCommandCount,
            policy.PolicyId + " submitted a rejected command.");
        ReplayRecord replay = output.Replay ??
            throw new VerificationException(
                policy.PolicyId + " did not capture a replay.");

        var equippedTowerByCard = new Dictionary<int, int>();
        bool awaitingWorldChoice = false;
        bool inWorldLoadout = false;
        bool equippedDuringWorldLoadout = false;
        bool completedWorldLoadout = false;
        foreach (CommandLogEntry command in replay.Commands
                     .OrderBy(value => value.Sequence))
        {
            Verify.True(
                command.Accepted,
                policy.PolicyId + " replay contains rejected command " +
                command.ActionId + ".");
            switch (command.Type)
            {
                case GameCommandType.OpenCardPack:
                    awaitingWorldChoice = true;
                    break;
                case GameCommandType.SelectCardPack:
                    inWorldLoadout = awaitingWorldChoice;
                    awaitingWorldChoice = false;
                    equippedDuringWorldLoadout = false;
                    break;
                case GameCommandType.UnequipCard:
                    equippedTowerByCard.Remove(command.PrimaryId);
                    break;
                case GameCommandType.EquipCard:
                case GameCommandType.MoveCard:
                    int otherCardsOnTarget = equippedTowerByCard.Count(pair =>
                        pair.Key != command.PrimaryId &&
                        pair.Value == command.SecondaryId);
                    Verify.Equal(
                        0,
                        otherCardsOnTarget,
                        policy.PolicyId + " placed a card on a tower that " +
                        "already held another card during card-pack loadout.");
                    int otherEquippedCards = equippedTowerByCard.Count(pair =>
                        pair.Key != command.PrimaryId);
                    Verify.Equal(
                        0,
                        otherEquippedCards,
                        policy.PolicyId + " bypassed EquipOnlyOneCard " +
                        "while equipping a pending card.");
                    equippedTowerByCard[command.PrimaryId] =
                        command.SecondaryId;
                    if (inWorldLoadout)
                    {
                        equippedDuringWorldLoadout = true;
                    }
                    break;
                case GameCommandType.ResumeCardPackCombat:
                    if (inWorldLoadout)
                    {
                        Verify.True(
                            equippedDuringWorldLoadout,
                            policy.PolicyId + " resumed a world card pack " +
                            "without equipping through a public command.");
                        completedWorldLoadout = true;
                    }
                    inWorldLoadout = false;
                    equippedDuringWorldLoadout = false;
                    break;
            }

            Verify.True(
                equippedTowerByCard
                    .GroupBy(pair => pair.Value)
                    .All(group => group.Count() <= 1),
                policy.PolicyId + " exceeded one card per tower.");
            Verify.True(
                equippedTowerByCard.Count <= 1,
                policy.PolicyId + " exceeded its global one-card limit.");
        }

        Verify.True(
            completedWorldLoadout,
            policy.PolicyId + " did not exercise a complete world-card-pack " +
            "LegalAction/GameCommand loadout path.");
    }

    private static void SynergyLookupRequiresExactContext()
    {
        const double exactLift = 7.0;
        const double contaminatingLift = 100.0;
        var program = new[]
        {
            new CardProgramStep("mark", SubjectType.Enemy, 0),
            new CardProgramStep("burn", SubjectType.Projectile, 1)
        };

        RuleforgeTD.BalanceCli.Policies.CardSynergyEntry Entry(
            string difficulty = "easy",
            string tower = "ballista",
            int level = 4,
            int firstSlot = 0,
            int secondSlot = 1,
            SubjectType firstSubject = SubjectType.Enemy,
            SubjectType secondSubject = SubjectType.Projectile,
            double lift = contaminatingLift) => new()
        {
            Difficulty = difficulty,
            TowerDefinition = tower,
            TowerLevel = level,
            FirstCardId = program[0].CardId,
            FirstSubjectType = firstSubject,
            FirstSlotIndex = firstSlot,
            SecondCardId = program[1].CardId,
            SecondSubjectType = secondSubject,
            SecondSlotIndex = secondSlot,
            SampleSize = 2,
            SynergyLift = lift
        };

        var mismatched = new List<
            RuleforgeTD.BalanceCli.Policies.CardSynergyEntry>
        {
            Entry(difficulty: "hard"),
            Entry(tower: "mutation_obelisk"),
            Entry(level: 5),
            Entry(firstSlot: 1),
            Entry(secondSlot: 2),
            Entry(firstSubject: SubjectType.Projectile),
            Entry(secondSubject: SubjectType.Enemy)
        };
        var mismatchedOnly = new
            RuleforgeTD.BalanceCli.Policies.CardSynergyIndex
        {
            Entries = mismatched
        };
        Verify.True(
            double.IsNaN(mismatchedOnly.ScoreProgram(
                "easy",
                "ballista",
                4,
                program)),
            "A context-mismatched synergy entry was treated as an exact hit.");
        Verify.True(
            double.IsNaN(mismatchedOnly.Score(
                "easy",
                program[0].CardId,
                program[0].SubjectType,
                program[1].CardId,
                program[1].SubjectType,
                "ballista",
                4,
                program[0].SlotIndex,
                program[1].SlotIndex)),
            "The pair fallback used a context-mismatched synergy entry.");

        var withExact = new RuleforgeTD.BalanceCli.Policies.CardSynergyIndex
        {
            Entries = mismatched
                .Append(Entry(lift: exactLift))
                .ToList()
        };
        Verify.Equal(
            exactLift,
            withExact.ScoreProgram("easy", "ballista", 4, program),
            "Mismatched high-score entries contaminated the exact synergy " +
            "lookup.");
        Verify.Equal(
            exactLift,
            withExact.Score(
                "easy",
                program[0].CardId,
                program[0].SubjectType,
                program[1].CardId,
                program[1].SubjectType,
                "ballista",
                4,
                program[0].SlotIndex,
                program[1].SlotIndex),
            "Mismatched high-score entries contaminated the exact pair " +
            "fallback.");

    }

    private void TripleDiscoveryEnumeratesThreeSlotContexts()
    {
        CardExperimentEnumeration enumeration =
            new CompiledCardExperimentEnumerator(loader).Enumerate(
                "hard",
                new CardExperimentEnumerationOptions
                {
                    IncludePairExperiments = true,
                    MaximumPairExperiments = 500,
                    MinimumUnlockedSlotsForPairs = 3
                });
        Verify.True(
            enumeration.PairExperiments.Count > 0,
            "No pair context survived the three-slot requirement.");
        LoadedSimulationContent loaded = loader.Load(
            "hard",
            SimulationScenario.Standard());
        foreach (CardSynergyPairExperiment pair in
                 enumeration.PairExperiments)
        {
            Verify.True(
                loaded.Content.TryGetTowerId(
                    pair.TowerDefinitionId,
                    out TowerDefinitionId towerId),
                "Enumerated pair references an unknown tower.");
            CompiledTowerDefinition tower = loaded.Content.GetTower(towerId);
            Verify.True(
                tower.TryGetLevel(
                    pair.TowerLevel,
                    out CompiledTowerLevelBalance level),
                "Enumerated pair references an unknown tower level.");
            Verify.True(
                level.UnlockedSlots >= 3,
                "A triple pair context has fewer than three unlocked slots.");
        }
    }

    private void FixedCardScenariosDisableEveryRewardSource()
    {
        LoadedSimulationContent standard = loader.Load(
            "easy",
            SimulationScenario.Standard());
        Verify.True(
            standard.Content.Run.CardPackProgressThresholds.Length > 0,
            "The base fixture does not exercise world card-pack thresholds.");

        LoadedSimulationContent fixedCards = loader.Load(
            "easy",
            new SimulationScenario
            {
                DisableCardRewardChoices = true
            });
        Verify.Equal(0, fixedCards.Content.Run.RegularDraftWaveIndices.Length);
        Verify.Equal(0, fixedCards.Content.Run.BossCardPackWaveIndices.Length);
        Verify.Equal(1, fixedCards.Content.Run.CardPackProgressThresholds.Length);
        Verify.Equal(
            1_000_000_000,
            fixedCards.Content.Run.CardPackProgressThresholds[0]);
        Verify.Equal(1, fixedCards.Content.Run.NormalKillProgress);
        Verify.Equal(1, fixedCards.Content.Run.EliteKillProgress);
    }

    private void CardRuntimeFailuresRemainMatchedLosses()
    {
        var seeds = new[]
        {
            new SeedPair(980001, 980002),
            new SeedPair(980003, 980004)
        };
        var experiment = new CardStrengthExperiment(
            "easy",
            "test_tower",
            1,
            new CardProgramStep(
                "test_card",
                SubjectType.Projectile,
                0));
        CardExperimentRunner runtimeFailureRunner = (variant, seed, _) =>
        {
            bool failed = variant.Kind == CardExperimentVariantKind.Program;
            string? reason = !failed
                ? null
                : seed == seeds[0]
                    ? CardExperimentFailureCodes.SafetyLimitReached + ": 1"
                    : CardExperimentFailureCodes.SimulationTimeout +
                        ": MaximumLogicalTicks reached.";
            return ValueTask.FromResult(new EvaluationRunMetrics(
                seed,
                true,
                20,
                8,
                0,
                1,
                1,
                true,
                reason,
                "scenario",
                "fixture",
                true,
                failed));
        };
        RuleforgeTD.BalanceCli.Evaluation.CardStrengthIndex index =
            new CardStrengthEvaluator().EvaluateAsync(
                new[] { experiment },
                seeds,
                runtimeFailureRunner)
            .GetAwaiter().GetResult();
        RuleforgeTD.BalanceCli.Evaluation.CardStrengthEntry entry =
            index.Entries.Single();
        Verify.True(entry.IsEvaluable);
        Verify.Equal(2, entry.Lift.MatchedSeedCount);
        Verify.Equal(0, entry.Lift.InvalidRunCount);
        Verify.Equal(0, entry.Lift.CleanMetricSeedCount);
        Verify.Equal(2, entry.Lift.RuntimeFailureSeedCount);
        Verify.Equal(2, entry.Lift.RuntimeFailureRunCount);
        Verify.Equal(1.0, entry.Lift.BaselineWinRate);
        Verify.Equal(0.0, entry.Lift.CandidateWinRate);
        Verify.Equal(-1.0, entry.Lift.WinRateLift);
        Verify.Equal(2, entry.Lift.RuntimeFailureDiagnostics.Count);

        CardExperimentEnumeration enumeration =
            new CompiledCardExperimentEnumerator(loader).Enumerate(
                "easy",
                new CardExperimentEnumerationOptions
                {
                    IncludePairExperiments = false
                });
        CardStrengthExperiment real = enumeration.StrengthExperiments.First();
        var timeoutRunner = new CardExperimentSimulationRunner(
            loader,
            new CardExperimentSimulationOptions
            {
                MaximumLogicalTicks = 1,
                MaximumDecisions = 100,
                RequireFixtureExecution = false
            });
        EvaluationRunMetrics timeout = timeoutRunner.RunAsync(
                new CardExperimentVariant(
                    "runtime-timeout-fixture",
                    real.DifficultyId,
                    real.TowerDefinitionId,
                    real.TowerLevel,
                    CardExperimentVariantKind.Baseline,
                    Array.Empty<CardProgramStep>()),
                seeds[0],
                default)
            .GetAwaiter().GetResult();
        Verify.True(timeout.IsValid,
            "A runtime timeout was incorrectly removed from the denominator.");
        Verify.True(timeout.IsRuntimeFailure);
        Verify.False(timeout.Victory);
        Verify.True(
            timeout.FailureReason?.StartsWith(
                CardExperimentFailureCodes.SimulationTimeout,
                StringComparison.Ordinal) == true,
            "The timeout diagnostic was not preserved: " +
            timeout.FailureReason);
    }

    private static void BatchRuntimeFailuresAreLosses()
    {
        SimulationResult Run(ulong gameSeed, int safety, int rejected) => new()
        {
            DifficultyId = "easy",
            PolicyId = "test-policy",
            PolicyVersion = "1",
            ScenarioId = "runtime-failure-accounting",
            ContentHash = "content",
            DifficultyProfileHash = "profile",
            GameSeed = gameSeed,
            PolicySeed = gameSeed + 100,
            Result = SimulationOutcome.Victory,
            RemainingBaseHealth = 20,
            SafetyLimitReachedCount = safety,
            RejectedCommandCount = rejected,
            TotalDecisions = 10
        };

        SimulationResult clean = Run(1, 0, 0);
        SimulationResult safety = Run(2, 1, 0);
        SimulationResult rejected = Run(3, 0, 1);
        BatchStatisticalReport report = new BatchEvaluator().Aggregate(
            new[] { clean, safety, rejected });
        Verify.Equal(1, report.VictoryCount);
        Verify.Equal(2, report.DefeatCount);
        Verify.Equal(2, report.RuntimeFailureCount);
        Verify.Equal(1, report.SafetyLimitFailureCount);
        Verify.Equal(1, report.RejectedCommandRunCount);
        Verify.Equal(1d / 3d, report.WinRate);
        Verify.False(RunOutcomeClassifier.IsSuccessful(safety));
        Verify.False(RunOutcomeClassifier.IsSuccessful(rejected));
    }

    private void FrozenPolicyHashesMatch()
    {
        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllText(paths.PolicyLock));
        JsonElement files = document.RootElement.GetProperty("files");
        int count = 0;
        foreach (JsonProperty property in files.EnumerateObject())
        {
            string path = Path.Combine(
                paths.Root,
                property.Name.Replace('/', Path.DirectorySeparatorChar));
            Verify.Equal(
                property.Value.GetString(),
                JsonSupport.Sha256File(path),
                "Frozen hash mismatch for " + property.Name + ".");
            count++;
        }
        Verify.True(count >= 4, "The policy lock does not freeze enough inputs.");
    }

    private void AuthoritativeRunReachesTerminalPhase()
    {
        SimulationResult result = FirstRun().Result;
        Verify.True(
            result.Result is SimulationOutcome.Victory or
                SimulationOutcome.Defeat,
            "The run did not terminate: " + result.Result + " / " +
            result.Error);
        Verify.True(
            result.FinalRunPhase is RunPhase.Victory or RunPhase.Defeat,
            "The authoritative phase is not terminal: " +
            result.FinalRunPhase + ".");
        Verify.Equal(
            result.Result == SimulationOutcome.Victory
                ? RunPhase.Victory
                : RunPhase.Defeat,
            result.FinalRunPhase);
        Verify.True(result.TotalLogicalTicks > 0);
        Verify.True(result.TotalDecisions > 0);
        Verify.Equal(0, result.RejectedCommandCount,
            "The supposedly legal policy run submitted rejected commands.");
    }

    private void SameSeedsAreDeterministic()
    {
        SimulationResult first = FirstRun().Result;
        SimulationResult second = SecondRun().Result;
        Verify.Equal(first.Result, second.Result);
        Verify.Equal(first.FinalRunPhase, second.FinalRunPhase);
        Verify.Equal(first.TotalLogicalTicks, second.TotalLogicalTicks);
        Verify.Equal(first.TotalDecisions, second.TotalDecisions);
        Verify.Equal(first.RemainingBaseHealth, second.RemainingBaseHealth);
        Verify.Equal(first.GoldUnspent, second.GoldUnspent);
        Verify.Equal(first.FinalStateHash, second.FinalStateHash);
        Verify.Equal(first.FinalSnapshotHash, second.FinalSnapshotHash);
        Verify.Equal(
            JsonSupport.SerializeStable(first.Commands),
            JsonSupport.SerializeStable(second.Commands),
            "The command stream changed for identical seeds.");
        Verify.Equal(
            JsonSupport.SerializeStable(first.Telemetry),
            JsonSupport.SerializeStable(second.Telemetry),
            "Telemetry changed for identical seeds.");
    }

    private void ReplayMatches()
    {
        SimulationRunOutput output = FirstRun();
        ReplayRecord replay = output.Replay ??
            throw new VerificationException("The terminal run has no replay.");
        ReplayVerificationResult verification =
            new ReplayRunner(loader).Run(replay);
        Verify.True(
            verification.Matches,
            "Replay mismatches:\n" +
            string.Join("\n", verification.Mismatches));
        Verify.Equal(output.Result.FinalStateHash, verification.FinalStateHash);
        Verify.Equal(
            output.Result.FinalSnapshotHash,
            verification.FinalSnapshotHash);
    }

    private void TimeoutReplayOutcomeIsDerived()
    {
        var scenario = SimulationScenario.Standard();
        scenario.ScenarioId = "verification-timeout-replay";
        scenario.MaximumLogicalTicks = 1;
        scenario.MaximumDecisions = 1000;
        scenario.CaptureReplay = true;
        SimulationRunOutput output = new HeadlessRunDriver(loader).Execute(
            new SimulationRunRequest
            {
                DifficultyId = "current",
                PolicyId = "no-spend",
                GameSeed = 991001,
                PolicySeed = 991002,
                Scenario = scenario,
                WriteResult = false,
                WriteReplay = false
            },
            new NoSpendPolicy());
        Verify.Equal(SimulationOutcome.Timeout, output.Result.Result);
        ReplayRecord replay = output.Replay ??
            throw new VerificationException(
                "Timeout fixture did not capture a replay.");
        Verify.Equal(2, replay.SchemaVersion);
        Verify.Equal(output.Result.TotalDecisions, replay.TotalDecisions);

        ReplayVerificationResult verification =
            new ReplayRunner(loader).Run(replay);
        Verify.True(
            verification.Matches,
            "Timeout replay mismatches:\n" +
            string.Join("\n", verification.Mismatches));
        Verify.Equal(SimulationOutcome.Timeout, verification.Result);

        int recordedDecisions = replay.TotalDecisions;
        replay.TotalDecisions = recordedDecisions + 1;
        ReplayVerificationResult decisionTamper =
            new ReplayRunner(loader).Run(replay);
        Verify.False(
            decisionTamper.Matches,
            "Replay verifier trusted a tampered decision count.");
        Verify.Contains(
            decisionTamper.Mismatches,
            mismatch => mismatch.Contains(
                "total decisions",
                StringComparison.Ordinal),
            "Tampered decision count was not diagnosed.");
        replay.TotalDecisions = recordedDecisions;

        replay.Result = SimulationOutcome.Error;
        ReplayVerificationResult tampered = new ReplayRunner(loader).Run(replay);
        Verify.False(
            tampered.Matches,
            "Replay verifier trusted the tampered recorded outcome.");
        Verify.Equal(SimulationOutcome.Timeout, tampered.Result);
        Verify.Contains(
            tampered.Mismatches,
            mismatch => mismatch.Contains(
                "result mismatch",
                StringComparison.Ordinal),
            "Tampered timeout replay did not report an outcome mismatch.");

        var decisionScenario = SimulationScenario.Standard();
        decisionScenario.ScenarioId = "verification-decision-timeout-replay";
        decisionScenario.MaximumLogicalTicks = 100;
        decisionScenario.MaximumDecisions = 1;
        decisionScenario.CaptureReplay = true;
        SimulationRunOutput decisionOutput = new HeadlessRunDriver(loader).Execute(
            new SimulationRunRequest
            {
                DifficultyId = "current",
                PolicyId = "no-spend",
                GameSeed = 991005,
                PolicySeed = 991006,
                Scenario = decisionScenario,
                WriteResult = false,
                WriteReplay = false
            },
            new NoSpendPolicy());
        Verify.Equal(SimulationOutcome.Timeout, decisionOutput.Result.Result);
        ReplayRecord decisionReplay = decisionOutput.Replay ??
            throw new VerificationException(
                "Decision-timeout fixture did not capture a replay.");
        ReplayVerificationResult decisionVerification =
            new ReplayRunner(loader).Run(decisionReplay);
        Verify.True(
            decisionVerification.Matches,
            "Decision-timeout replay mismatches:\n" +
            string.Join("\n", decisionVerification.Mismatches));
        Verify.Equal(
            SimulationOutcome.Timeout,
            decisionVerification.Result);
    }

    private void UnreplayablePolicyOrHostErrorDoesNotMatch()
    {
        var scenario = SimulationScenario.Standard();
        scenario.ScenarioId = "verification-unreplayable-policy-error";
        scenario.MaximumLogicalTicks = 10;
        scenario.MaximumDecisions = 10;
        scenario.CaptureReplay = true;
        SimulationRunOutput output = new HeadlessRunDriver(loader).Execute(
            new SimulationRunRequest
            {
                DifficultyId = "current",
                PolicyId = "invalid-action-fixture",
                GameSeed = 991003,
                PolicySeed = 991004,
                Scenario = scenario,
                WriteResult = false,
                WriteReplay = false
            },
            new InvalidActionPolicy());
        Verify.Equal(SimulationOutcome.Error, output.Result.Result);
        ReplayRecord replay = output.Replay ??
            throw new VerificationException(
                "Policy-error fixture did not capture a replay.");

        ReplayVerificationResult verification =
            new ReplayRunner(loader).Run(replay);
        Verify.False(
            verification.Matches,
            "A policy exception with no replayable operation was certified.");
        Verify.Contains(
            verification.Mismatches,
            mismatch => mismatch.Contains(
                "not independently verifiable",
                StringComparison.Ordinal),
            "The unverifiable policy error was not diagnosed.");

        ReplayRecord hostErrorReplay = JsonSerializer.Deserialize<ReplayRecord>(
                JsonSupport.SerializeStable(replay),
                JsonSupport.Options) ??
            throw new VerificationException(
                "Could not clone the replay for the host-error fixture.");
        hostErrorReplay.PolicyId = "synthetic-host-error";
        hostErrorReplay.Error =
            "Synthetic host exception with no authoritative command.";
        ReplayVerificationResult hostVerification =
            new ReplayRunner(loader).Run(hostErrorReplay);
        Verify.False(
            hostVerification.Matches,
            "A host exception with no replayable operation was certified.");
        Verify.Contains(
            hostVerification.Mismatches,
            mismatch => mismatch.Contains(
                "not independently verifiable",
                StringComparison.Ordinal),
            "The unverifiable host error was not diagnosed.");
    }

    private SimulationRunOutput FirstRun() =>
        firstRun ??= RunDeterministicIntegration();

    private SimulationRunOutput SecondRun() =>
        secondRun ??= RunDeterministicIntegration();

    private SimulationRunOutput RunDeterministicIntegration(
        ISimulationProgressObserver? progressObserver = null)
    {
        var driver = new HeadlessRunDriver(loader);
        return driver.Execute(
            new SimulationRunRequest
            {
                DifficultyId = "current",
                PolicyId = "no-spend",
                GameSeed = IntegrationGameSeed,
                PolicySeed = IntegrationPolicySeed,
                Scenario = new SimulationScenario
                {
                    ScenarioId = "verification-terminal-run",
                    MaximumLogicalTicks = 60000,
                    MaximumDecisions = 200000,
                    CaptureReplay = true,
                    CaptureTelemetry = true
                }
            },
            PolicyRegistry.Create("no-spend"),
            progressObserver: progressObserver);
    }

    private int CountSourceCards()
    {
        int total = CountCardsInJson(paths.ContentJson);
        if (!Directory.Exists(paths.CardModules))
        {
            return total;
        }
        foreach (string module in Directory.EnumerateFiles(
                     paths.CardModules,
                     "*.json",
                     SearchOption.AllDirectories))
        {
            total = checked(total + CountCardsInJson(module));
        }
        return total;
    }

    private static PolicyContext CreatePolicyContext(
        IReadOnlyList<LegalAction> actions,
        PublicGameKnowledge knowledge,
        SimulationScenario scenario,
        ulong policySeed)
    {
        return new PolicyContext
        {
            DifficultyId = "current",
            Scenario = scenario,
            LegalActions = actions,
            PublicKnowledge = knowledge,
            Random = new PolicyRandom(policySeed),
            Memory = new PolicyMemory()
        };
    }

    private static int CountCardsInJson(string path)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.TryGetProperty(
            "cards",
            out JsonElement cards)
            ? cards.GetArrayLength()
            : 0;
    }

    private static void SubmitAccepted(
        GameSimulation simulation,
        LegalAction action)
    {
        Verify.True(action.Command.HasValue,
            "Action has no command: " + action.ActionId);
        GameCommand command = action.Command.GetValueOrDefault();
        CommandResult result = simulation.Submit(in command);
        Verify.True(
            result.Accepted,
            action.ActionId + " was rejected: " + result.Error + " " +
            result.Message);
    }

    private static void SubmitBoth(
        GameSimulation observed,
        GameSimulation control,
        GameCommand command,
        SimulationTelemetry telemetry,
        CompiledContent content,
        int sequence)
    {
        SimulationSnapshot beforeObserved = observed.GetSnapshot();
        SimulationSnapshot beforeControl = control.GetSnapshot();
        CommandResult observedResult = observed.Submit(in command);
        CommandResult controlResult = control.Submit(in command);
        SimulationSnapshot afterObserved = observed.GetSnapshot();
        SimulationSnapshot afterControl = control.GetSnapshot();

        Verify.Equal(controlResult.Accepted, observedResult.Accepted);
        Verify.Equal(controlResult.Error, observedResult.Error);
        Verify.Equal(controlResult.Message, observedResult.Message);
        Verify.True(observedResult.Accepted,
            "Telemetry fixture command was rejected: " +
            observedResult.Error + " " + observedResult.Message);
        telemetry.ObserveCommand(
            "telemetry-fixture:" + sequence,
            in command,
            in observedResult,
            beforeObserved,
            afterObserved,
            content,
            sequence);
        telemetry.ObserveCommandEvents(
            observed.ReadPresentationEvents(),
            afterObserved);
        Verify.Equal(
            control.ComputeStateHash(),
            observed.ComputeStateHash(),
            "Telemetry observation changed command state.");
        Verify.Equal(beforeControl.Phase, beforeObserved.Phase);
        Verify.Equal(afterControl.Phase, afterObserved.Phase);
    }
}

internal sealed class InvalidActionPolicy : IPlayerPolicy
{
    public string PolicyId => "invalid-action-fixture";
    public string PolicyVersion => "1.0.0";

    public PolicyDecision Decide(
        SimulationSnapshot snapshot,
        PolicyContext context) =>
        new("this-action-does-not-exist", "TEST_INVALID_ACTION");
}

internal sealed class RecordingProgressObserver : ISimulationProgressObserver
{
    public int UpdateCount { get; private set; }
    public SimulationProgressUpdate? Latest { get; private set; }

    public void Observe(SimulationProgressUpdate update)
    {
        UpdateCount++;
        Latest = update;
    }
}

internal static class Verify
{
    public static void True(bool condition, string message = "Expected true.")
    {
        if (!condition)
        {
            throw new VerificationException(message);
        }
    }

    public static void False(bool condition, string message = "Expected false.") =>
        True(!condition, message);

    public static void Equal<T>(
        T expected,
        T actual,
        string? message = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new VerificationException(
                message ?? "Expected '" + expected + "', actual '" + actual +
                "'.");
        }
    }

    public static void Contains<T>(
        IEnumerable<T> values,
        Func<T, bool> predicate,
        string message)
    {
        if (!values.Any(predicate))
        {
            throw new VerificationException(message);
        }
    }

    public static TException Throws<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
        }
        catch (Exception exception)
        {
            throw new VerificationException(
                "Expected " + typeof(TException).Name + ", received " +
                exception.GetType().Name + ".",
                exception);
        }

        throw new VerificationException(
            "Expected " + typeof(TException).Name + " to be thrown.");
    }
}

internal sealed class VerificationException : Exception
{
    public VerificationException(string message)
        : base(message)
    {
    }

    public VerificationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
