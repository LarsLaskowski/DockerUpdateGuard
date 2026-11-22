# Plan: Update Reihitsu to 1.5.0

Source: Product Manager request (no bug issue); follow-up issue #523, referenced, not closed
Tier: trivial — version bumps of a build-time analyzer and formatter plus three analyzer severities as configuration
defaults, no production or test code, but a rule set change for every C# file that needs the full build, tests and
image build check, which the `patch` gates do not run.
Security areas touched: dependency (`Reihitsu.Analyzer`, `reihitsu.cli`); build configuration
(`Directory.Packages.props`, `.config/dotnet-tools.json`, `.editorconfig`)

## Problem / root cause

Reihitsu 1.5.0 adds RH2008, RH2009 and RH2111, which the baseline `.squad/dotnet-rules.editorconfig` does not set, so
the analyzer default `error` fails the build on existing code (#523). The Product Manager decided to lower them through
`DOTNET_RULES_PENDING` until #523, without refactoring now.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | Analyzer and formatter are at 1.4.0 and kept in step | `Directory.Packages.props:27-28`, `.config/dotnet-tools.json:6` | confirmed |
| 2 | The baseline sets none of RH2008, RH2009, RH2111 | `.squad/dotnet-rules.editorconfig`: RH2007, then RH2101 | confirmed |
| 3 | Pending accepts rules the baseline does not set, and one that reports nothing | `dotnet-rules.py` `unset_rules()` (247-255); scratch run: exit 0, section "Pending rules the baseline does not set" with the three at `suggestion`, `--check` exit 0; nothing checks that a rule reports | confirmed |
| 4 | `.squad/dotnet-rules-pending.globalconfig` stays unchanged | scratch run: "pending global config is current" | confirmed |
| 5 | `reihitsu-format` 1.5.0 changes no file | scratch copy of HEAD, tool 1.5.0+5af53bd, *Format*: "Formatted 0 of 468 file(s)" | confirmed |
| 6 | With 1.5.0 the build fails; RH2009 reports nothing; migrations unaffected | orchestrator's measurement (intake, `log.md`) | confirmed |
| 7 | #523 counts 34 findings (31 RH2008) | its list sums to 33 (30 RH2008, 3 RH2111) | refuted |
| 8 | The `.editorconfig` line reaches every finding, Razor-generated code included | to verify: *Build*, Dev, step 6; a finding still at `error` goes back to the Lead (`decide`) | unverified |
| 9 | No lock file; CI restores the formatter from the manifest; the analyzer does not reach the image | `git ls-files`; `ci.yml:67-70`, `release.yml:36-39`; `Directory.Build.props:26-28` (`PrivateAssets` `all`) | confirmed |

## Acceptance criteria

- AC1: `Reihitsu.Analyzer` 1.5.0 in `Directory.Packages.props`, `reihitsu.cli` 1.5.0 in `.config/dotnet-tools.json`.
- AC2: `DOTNET_RULES_PENDING` is `{"RH2008": "#523", "RH2009": "#523", "RH2111": "#523"}`, the `.editorconfig` block
  is `dotnet-rules.py` output, and `python3 .squad/tools/dotnet-rules.py --check` exits 0.
- AC3: *Build* has no error, *Test* passes, the *Analyzer gate* reports no diagnostic in a changed file.
- AC4: *Format check* with 1.5.0 passes and the diff holds no C# or Razor file.
- AC5: The *Image build check* passes.

## Tests per criterion

None: every criterion is verified under *Verification without tests*.

## Verification without tests

Steps 4, 5 and the *Coverage gate* are not applicable: no production or test code changes.

- AC1: check: Dev, step 6 — *Restore*, `dotnet tool restore`, `dotnet tool list --local` shows 1.5.0.
- AC2: check: Dev, step 6 — `dotnet-rules.py`, then `--check`; step 7 again through the *Analyzer gate*.
- AC3: check: Dev, step 6 — *Build*, *Test*; step 7 — *Analyzer gate*; CI on the pull request.
- AC4: check: step 7 — *Format check*; the Reviewer confirms the file list of the diff.
- AC5: check: Dev, step 6 — *Image build check* (`docker build`, Docker is installed; otherwise the reproduction in
  `.squad/stack.md`).

## Approach

1. `Directory.Packages.props`: `Reihitsu.Analyzer` `1.4.0` → `1.5.0`.
2. `.config/dotnet-tools.json`: `reihitsu.cli` `1.4.0` → `1.5.0`.
3. `.squad/tools/squad_settings.py`: the three entries of AC2.
4. `python3 .squad/tools/dotnet-rules.py` writes the block; no hand edit.
5. *Format*: no diff expected (fact 5). If it changes a C# file, the Dev reverts it and returns to the Lead: a
   reformatted file becomes a changed file whose RH2008/RH2111 suggestions the *Analyzer gate* reports.

RH2009 is listed though it reports nothing, so a later finding stays visible instead of failing an unrelated build.

## Options considered

Decided by the Product Manager: refactor now (deferred to #523); severities outside the generated blocks (forbidden,
`dotnet_config.py` rejects it); add the rules to the baseline (template-managed, follow-up below); stay on 1.4.0
(update requested).

## Affected projects and types

| Project | Type / file | Change |
| ------- | ----------- | ------ |
| (build) | `Directory.Packages.props`, `.config/dotnet-tools.json` | versions |
| (settings) | `.squad/tools/squad_settings.py`, `.editorconfig` | pending entries, regenerated block |

## Signatures (for the Dev's skeleton)

None: the change has no API.

## Test files

None; no test call sites affected; the configuration files are verified as above.

## Areas

none (no change in behavior)

## Documentation updates

none: no document lists the Reihitsu version or pending rules. The orchestrator updates #523's body to name RH2009
and correct the count (fact 7).

## Architecture check

No guarantee touched: the analyzer runs at build time only (fact 9).

## Security considerations

- [x] no limits.
- [x] none: no runtime code changes.
- Same package IDs from nuget.org, no new source; only method/type length and method complexity rules are lowered, no security rule.

## Decision records

none: a dependency update touching no guarantee (`docs/decisions/README.md`); #523 stands beside each pending line.

## Out of scope / follow-ups

- #523: fix the findings, remove the entries.
- Step 12 `squad-upstream` lesson: add RH2008, RH2009, RH2111 to the template baseline.

## Security notes

Security plan review: specs/issue-523/plan.md (Reihitsu 1.4.0 -> 1.5.0), mode plan, round 1.

Tier check: `trivial` holds. The change creates, changes and removes no security control. It does not touch CI permissions, secrets, triggers or actions, signing or provenance. No runtime code changes, and no security area from .squad/project.md is touched. *Security areas touched* lists the two that apply: the dependency, and the build configuration (Directory.Packages.props, .config/dotnet-tools.json, .editorconfig). No area is missing.

What I checked (read-only, using the NuGet cache in ~/.nuget/packages, which already holds both versions):
- **Where it comes from.** `.nupkg.metadata` gives `"source": "https://api.nuget.org/v3/index.json"` for reihitsu.analyzer 1.4.0 and 1.5.0. There is no nuget.config in the repository root, so no new feed comes in. Both versions of both packages have nuspec `<authors>Thoenissen</authors>` and repository `https://github.com/thoenissen/Reihitsu`. 1.5.0 is built from commit 5af53bd1b2c8..., which matches fact 5's tool version `1.5.0+5af53bd`.
- **Dependencies.** Neither the analyzer nuspec nor the Reihitsu.Cli.nuspec declares a `<dependency>` in 1.4.0 or 1.5.0, so no new transitive package comes in. The tool directory still holds 35 files.
- **Code that runs at build time.** `diff -r` of `buildTransitive/` shows one change in Reihitsu.Analyzer.targets, which only adds `<CompilerVisibleProperty Include="IsTestProject" />` and `IsTestingPlatformApplication`. That adds no Exec, task or download. `tools/install.ps1` and `uninstall.ps1` have the same sha256 in both versions.
- **What reaches the image.** Directory.Build.props:26-28 sets `PrivateAssets` to `all`, which confirms fact 9: the analyzer does not ship in the image.
- **CI.** Both the formatter (`dotnet tool run reihitsu-format --check .`) and the analyzer run in ci.yml's build job. That job has `SONAR_TOKEN` in its env and `checks: write`. They also run in release.yml, which has `contents: write`. All of this was already true before the change, and the update keeps the same package ID, publisher and feed. So the update does not widen this exposure, and I raise no finding on it.
- **What the three rules check.** From the README.MD of the 1.5.0 package: RH2008 is "Methods should not exceed the line limit.", RH2009 is "Methods should not exceed the complexity limit." and RH2111 is "Types should not exceed the line limit.". None of them is a security rule, and the plan lowers no Sonar or security rule.

Findings:

[NON-BLOCKING] plan.md, *Security considerations*, third bullet: "only length rules are lowered" is not accurate. RH2009 is a complexity limit, not a length limit (README.MD of reihitsu.analyzer 1.5.0, line 37). The conclusion still holds, because none of the three is a security rule. Smallest fix, verbatim. Replace
"- Same package IDs from nuget.org, no new source; only length rules are lowered, no security rule."
with
"- Same package IDs from nuget.org, no new source; only method/type length and method complexity rules are lowered,
  no security rule."
Recommendation: fix now, in the Lead's plan edit, if one happens anyway. Otherwise leave it, since it changes no decision.

No measurement-backed security claim needs my own run here. I did not run the build, the formatter or dotnet-rules.py. Facts 3 to 6 and 8 rest on the orchestrator's and the Lead's runs, and on the Dev's checks in step 6, as the plan says.

Relevant files:
- /home/user/DockerUpdateGuard/specs/issue-523/plan.md
- /home/user/DockerUpdateGuard/Directory.Packages.props
- /home/user/DockerUpdateGuard/.config/dotnet-tools.json
- /home/user/DockerUpdateGuard/Directory.Build.props
- /home/user/DockerUpdateGuard/.github/workflows/ci.yml
- /home/user/DockerUpdateGuard/.github/workflows/release.yml
