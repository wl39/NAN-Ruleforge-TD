# Ruleforge Balance CLI verification

This is a zero-NuGet, executable verification harness. Its 29 checks cover strict
balance schemas, patch and seed guards, active card compilation, authoritative
legal actions, card fixtures and ordering, current-profile identity, policy-seed
determinism, terminal simulation, deterministic repeats, stable snapshots,
runtime-failure accounting, replay (including timeout/error cases), telemetry
isolation, live-dashboard observation/rendering, and invalid policy/LLM action
rejection.

Run it from the repository root:

```sh
dotnet run --project Tests/Ruleforge.BalanceCli.Tests/Ruleforge.BalanceCli.Tests.csproj -- "$PWD"
```

The process exits with `0` only when every check passes. The two terminal runs
use the same game and policy seeds and intentionally execute the real
`GameSimulation`; no mock combat or Unity runtime is involved.
