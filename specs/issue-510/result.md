RESULT line first, for log.md: RESULT: DONE (tier `docs`, no plan.md. Write this report to specs/issue-510/result.md with `squad-notes.py specs/issue-510 result`.)

# Issue #510: Lead result (mode plan)

## Tier
**`docs`.** The diff changes only `docs/CONTRIBUTING.md`, inside its `releases` project block. It adds a manual step to the contributor procedure *Updating the bundled Trivy*. No build file, workflow, Dockerfile or code changes, and no control is enforced, so this is not the `security` tier (that tier is for a control that is built in, such as signing or provenance checks inside the build). Security areas touched: none. Security or the Reviewer may still raise the tier.

## Claims checked
- **Confirmed:** step 1 of the procedure (lines 165–166) checks only that the digest resolves and that the pull succeeds. Nothing checks who published the image.
- **Confirmed:** the Dockerfile pins `aquasec/trivy@sha256:8f6bb947…768fb` (v0.76.2). Docker Hub shows the tag `0.76.2` has this digest, with media type `application/vnd.oci.image.index.v1+json`.
- **Confirmed by reading the registry (no cosign run):**
  - Trivy v0.76.2's `goreleaser.yml` uses `docker_signs` with `artifacts: manifests` and runs `cosign sign` keyless. `reusable-release.yaml` has `id-token: write` and installs cosign-installer v4.1.2.
  - The Docker Hub referrers API for the pinned index lists two `application/vnd.dev.sigstore.bundle.v0.3+json` referrers (predicate `https://sigstore.dev/cosign/sign/v1`). Their DSSE subject is digest `8f6bb947…768fb`.
  - Both certificates have SAN `URI:https://github.com/aquasecurity/trivy/.github/workflows/reusable-release.yaml@refs/tags/v0.76.2` and issuer (OID 1.3.6.1.4.1.57264.1.1) `https://token.actions.githubusercontent.com`.
  - There is no legacy `sha256-<hex>.sig` tag (404). The signature is stored only as an OCI referrer.
- **Confirmed:** Aqua's own page (`docs/getting-started/signature-verification.md` in aquasecurity/trivy, fetched raw) uses `--certificate-identity-regexp 'https://github\.com/aquasecurity/trivy/\.github/workflows/.+'`. That pattern accepts any workflow of the repository.
- **Confirmed:** the image has no build-provenance attestation. The workflow's `attest-build-provenance` covers only `dist/trivy_<v>_checksums.txt` (release assets), and the index's referrers are only the two signature bundles.
- **Unverified:** I could not check the signature cryptographically, because running cosign was denied here. Whether the `cosign verify` command below reports success with cosign v3 has a `manual:` check (AC1).
- `trivy.dev` returns 403 through the proxy, so the edit links the GitHub copy of Aqua's page, which I could open.

## Options considered
1. **`cosign verify` of the pinned index with the exact identity (release workflow at the new version's tag) and the exact issuer.** Chosen.
2. **The identity pattern from Aqua's page.** Rejected: it also accepts any other workflow in the repository, such as canary builds.
3. **Verify a provenance attestation.** Rejected: the image has none (see above).
4. **Check the signature automatically in CI or the image build.** Rejected: the issue asks for a step in the update procedure. Doing it automatically would change the build's trust and would be its own `security` change. It is not proposed as a follow-up unless the Product Manager wants one.

No decision record: this is a step in a manual procedure with no lasting effect on how the product is built, and the `docs` tier excludes `docs/decisions/`.

## Exact edit: `docs/CONTRIBUTING.md`
Replace lines 165–166 (step 1) with step 1 unchanged plus a new step 2, then renumber the old steps 2, 3, 4, 5 to 3, 4, 5, 6. Change only the leading number of each; their text stays as it is. New text for lines 165 onward, up to the old step 2:

````
1. Take the digest of the new version's multi-arch image index (`docker buildx imagetools inspect
   aquasec/trivy:<version>`, line `Digest:`) and check that `docker pull aquasec/trivy@<digest>` succeeds.
2. Check that Aqua Security published that index: a digest proves which bytes were fetched, not who published them.
   With cosign v3 or later, which reads the signature that Trivy's release attaches to the index as an OCI referrer,
   run

   ```shell
   cosign verify aquasec/trivy@<digest> \
     --certificate-oidc-issuer https://token.actions.githubusercontent.com \
     --certificate-identity https://github.com/aquasecurity/trivy/.github/workflows/reusable-release.yaml@refs/tags/v<version>
   ```

   and check that it reports the signature as verified. The identity names Trivy's release workflow at the new
   version's tag, not the pattern on
   [Trivy's signature verification page](https://github.com/aquasecurity/trivy/blob/main/docs/getting-started/signature-verification.md),
   which accepts any workflow of the repository. Put the command and its output in the pull request; if the
   verification fails, stop the update.
3. Extract `/usr/local/bin/trivy` from that image (`docker create`, `docker cp`) and check that `trivy --version`
````
After that, `3.` becomes `4.`, `4.` becomes `5.` and `5.` becomes `6.`. Nothing else in the file changes. The section's first paragraph already says the results go into the pull request.

Documentation sweep: the only other mention of the procedure is record 0051, which links the anchor `#updating-the-bundled-trivy` (the anchor does not change) and refers to no step number. `.github/pull_request_template.md` has no Trivy item. `docs/areas/hosting.md` describes the pin, not the update procedure. Nothing else to update.

## Acceptance criteria and their checks
- **AC1:** in *Updating the bundled Trivy*, a step right after the digest step runs `cosign verify aquasec/trivy@<digest>` with `--certificate-oidc-issuer https://token.actions.githubusercontent.com` and the exact `--certificate-identity https://github.com/aquasecurity/trivy/.github/workflows/reusable-release.yaml@refs/tags/v<version>` (no regexp), and says that a failed verification stops the update.
  - Reviewer: reads the diff against this result.
  - `manual:` Product Manager, before the PR opens (the orchestrator asks once and logs the answer): run the command with cosign v3 on `aquasec/trivy@sha256:8f6bb947dbc528902e11f2f5be52f7b519f6a39d5d148f41166f2040499768fb` and `v0.76.2`, and report success or the output. If it fails with cosign v3, the version clause is narrowed to what the run showed (a blocking finding for the orchestrator).
- **AC2:** the step says to put the command and its output in the update's pull request.
  - Reviewer: reads the diff.
- **AC3:** the steps are numbered 1–6 in a row, step 1 and the old steps' text are unchanged, the fenced block sits inside list item 2 (indented three spaces), and the link points at the GitHub page.
  - Reviewer: reads the diff.
  - CI: `scope-check.py --tier docs` and *Format check*.

Third-party page: the linked GitHub page opened from this session (raw fetch). Nothing is left for the Reviewer to report as `unopened`.

No proposed issues.

RESULT: DONE
