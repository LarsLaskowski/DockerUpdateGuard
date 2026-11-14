# Tasks: Count Docker Scout's Docker Hub login against DockerHub:MaxParallelRequests

Plan: [plan.md](plan.md)

| # | Group | Task | Files | Criteria | Depends on | Owner | Done |
| - | ----- | ---- | ----- | -------- | ---------- | ----- | ---- |
| 1 | G1 | Skeleton: constructor parameter `requestLimiter` and field (plan *Signatures*); adapt the four existing call sites | src/DockerUpdateGuard/Vulnerabilities/DockerScoutVulnerabilityProvider.cs; DockerScoutVulnerabilityProviderTests.cs, DockerScoutTokenExpiryTests.cs, VulnerabilityProviderResolverTests.cs | AC1 | — | Dev | [ ] |
| 2 | G1 | Tests first (plan *Tests per criterion*), `CreateProvider` takes an optional limiter | src/Tests/DockerUpdateGuard.Tests/DockerScoutVulnerabilityProviderTests.cs; src/Tests/DockerUpdateGuard.Tests/ServiceCollectionExtensionsTests.cs | AC1, AC2, AC3, AC4 | 1 | Tester | [ ] |
| 3 | G1 | Wait for a slot before the login call and release it in `finally` (plan *Approach*) | src/DockerUpdateGuard/Vulnerabilities/DockerScoutVulnerabilityProvider.cs | AC1, AC2, AC3, AC4 | 2 | Dev | [ ] |
| 4 | G1 | Documentation updates (plan *Documentation updates*) | docs/areas/registries.md; docs/areas/vulnerabilities.md; docs/ARCHITECTURE.md; README.md | AC1, AC3 | 3 | Dev | [ ] |
