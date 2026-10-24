# 0007: Client certificates are validated as PKCS#12 at startup

- **Status:** Accepted
- **Date:** 2026-10-24
- **Area:** Docker instances
- **Source:** Issue #269
- **Supersedes:** —

## Context

`DockerUpdateGuard:DockerInstances:<n>:CertificatePath` supplies the client certificate for mutual TLS to a Docker
Engine. Until now a file without a `.pfx`/`.p12` extension was loaded as a bare certificate without private key, which
cannot authenticate a TLS client, and a path that did not exist was skipped without a word. Either way the operator
believed mutual TLS was configured while the connection went out without a usable client certificate. A broken or
password-protected bundle surfaced only inside the first Engine call, and because the pooled HTTP client is created
through a `Lazy<HttpClient>` that caches its factory's exception, the instance then kept failing until restart, even
after the file was fixed. The Product Manager decided that only password-less PKCS#12 is accepted and that a wrong,
missing or unreadable file fails validation. The options validator was free of I/O so far; the guarantee that
integration clients return outcomes instead of throwing (`.squad/project.md`, *Guarantees*) must hold.

## Options considered

1. **One shared loader (`ClientCertificateLoader`) used by the startup validation and by `DockerInstanceClient`, with
   the file read injected as a delegate** — the rules cannot drift between startup and runtime, and the tests feed
   bytes and read errors without touching the file system. Chosen.
2. **A file-system abstraction registered in DI for the validator** — the same testability with more wiring; the
   loader needs exactly one operation (read all bytes).
3. **Validate only where the transport presents the certificate (`https`, TLS `tcp`)** — matches "no effect" on the
   other transports, but hides an error such as a forgotten `UseTls: true`, and departs from the decision as worded
   ("set for an enabled instance").
4. **Validate disabled instances too** — would block startup for a parked instance whose certificate is not mounted,
   although every Engine call for it answers *not configured*.
5. **Keep loading the certificate on plain `http` and non-TLS `tcp` at runtime** — a file that disappears would then
   fail operations on a transport that never presents it.
6. **Leave the pooled client's failure cached** — a certificate missing for a moment (a renewal) would disable the
   instance until restart.
7. **Accept a PEM certificate plus a separate key file** — the Docker `cert.pem`/`key.pem` layout would work unchanged,
   but it adds an option and a second parser; out of scope by the Product Manager's decision.

## Decision

`CertificatePath` must name a password-less PKCS#12 bundle (`.pfx`/`.p12`) with a certificate that has a private key;
startup validation checks this for every enabled instance that sets it, whatever its transport, and the runtime loads
it with the same loader only for `https` connections and fails the operation when it cannot. A pooled HTTP client
whose creation failed is dropped from the pool so the next operation tries again. The rules are in
[Docker instances](../areas/docker-instances.md), *TLS options* and *Limits and errors*.

## Consequences

- Breaking for operators who configured a PEM or DER file: startup now fails with a message naming the key and the
  reason; the README gives the `openssl pkcs12 -export` conversion.
- The options validator reads a file. A reloaded configuration reads it again, so a certificate deleted while the
  application runs makes the reloaded configuration invalid, with the consequences of the reload gap in
  [Hosting](../areas/hosting.md) (#281).
- A password option is not offered; revisit by adding one (and a secret-handling rule for it) if operators need
  encrypted bundles.
