# Measurements: issue 506 (Trivy v0.76.2)

Run by the orchestrator in step 3, with the Product Manager's leave to execute the binary. `$T` is `usr/local/bin/trivy` extracted from layer `sha256:d7953b8b…915c` of the linux/amd64 manifest `sha256:6fa071ef…836a6` (index `sha256:8f6bb947…768fb`) with `tar -xzf b3 -C bin usr/local/bin/trivy`; `$S` is the scratchpad. AC2 was measured and logged by Security (step 3, round 1).

## AC3 (binary)
```
$ sha256sum $T
6a972c69606c7b3ea4c6bdcc6fc9332ea9ad38381624310c1e85cc77345909f3
$ $T --version
Version: 0.76.2
$ file -b $T
ELF 64-bit LSB executable, x86-64, version 1 (SYSV), statically linked, Go BuildID=GZ8uQKNhqhunqnnjDxHj/EQV_Q-
$ $T server --help | grep -- --listen
  $ trivy server --listen 0.0.0.0:10000
      --listen string         listen address in server mode (default "localhost:4954")
```

## AC4 (option cases)

Each case: `env HOME=$S/home TRIVY_CACHE_DIR=$S/home/c TRIVY_SERVER=http://127.0.0.1:9 strace -f -e trace=open,openat,openat2 -o caseN.txt timeout 90 $T image --format json --quiet --scanners vuln --pkg-types os,library --timeout 30s [--] <target>`.

### Case 1: `'--config=x.y/z:latest'`
```
first error line: 2026-11-18T17:01:22Z	FATAL	Fatal error	config file "x.y/z:latest" loading error: open x.y/z:latest: no such file or directory
open* lines naming the target path:
2209  openat(AT_FDCWD, "x.y/z:latest", O_RDONLY|O_CLOEXEC) = -1 ENOENT (No such file or directory)
(count: 1)
```

### Case 2: `-- --config=x.y/z:latest`
```
first error line: 2026-11-18T17:01:22Z	FATAL	Fatal error	run error: image scan error: scan error: unable to initialize a scan service: unable to initialize container image: unable to find the specified image "--config=x.y/z:latest" in ["docker" "containerd" "podman" "remote"]: 4 errors occurred:
open* lines naming the target path:
(count: 0)
```

### Case 3: `-- --input=x.y/z:latest`
```
first error line: 2026-11-18T17:01:23Z	FATAL	Fatal error	run error: image scan error: scan error: unable to initialize a scan service: unable to initialize container image: unable to find the specified image "--input=x.y/z:latest" in ["docker" "containerd" "podman" "remote"]: 4 errors occurred:
open* lines naming the target path:
(count: 0)
```

### Case 4: `-- -a/b`
```
first error line: 2026-11-18T17:01:24Z	FATAL	Fatal error	run error: image scan error: scan error: unable to initialize a scan service: unable to initialize container image: unable to find the specified image "-a/b" in ["docker" "containerd" "podman" "remote"]: 4 errors occurred:
open* lines naming the target path:
(count: 0)
```

### Case 5: `-- library/alpine:3.20`
```
first error line: 2026-11-18T17:01:27Z	FATAL	Fatal error	run error: image scan error: scan error: scan failed: failed analysis: unable to get missing layers: unable to fetch missing layers: backoff: permanent error (last error: twirp error internal: failed to do request: Post "http://127.0.0.1:9/twirp/trivy.cache.v1.Cache/MissingBlobs": dial tcp 
open* lines naming the target path:
(count: 0)
```

Exit code of every case: 1 (read from the runs' `echo exit=$?`: case 1–5 = 1). Case 5 last error: `…Post "http://127.0.0.1:9/twirp/trivy.cache.v1.Cache/MissingBlobs": dial tcp 127.0.0.1:9: connect: connection refused`.

## AC5 (destinations)

Target as case 5. With settings: `env HOME=$S/homea TRIVY_CACHE_DIR=$S/homea/c TRIVY_SERVER=http://127.0.0.1:9 TRIVY_SKIP_VERSION_CHECK=true TRIVY_DISABLE_TELEMETRY=true strace -f -e trace=connect,sendto,sendmsg,write -s 1024 -o ac5_on.txt timeout 90 $T image --format json --quiet --scanners vuln --pkg-types os,library --timeout 30s -- library/alpine:3.20`. Control: the same with `env -u TRIVY_SKIP_VERSION_CHECK -u TRIVY_DISABLE_TELEMETRY` and a fresh `HOME` (`homeb`). Both exit 1.

### on
```
$ grep -aoE "CONNECT [^ \\]+" ac5_on.txt | sort | uniq -c
      1 CONNECT auth.docker.io:443
      1 CONNECT index.docker.io:443
      1 CONNECT production.cloudfront.docker.com:443
$ other connect() lines (not the proxy)
      1 connect(10, {sa_family=AF_INET, sin_port=htons(9), sin_addr=inet_addr("127.0.0.1")}, 16) = -1 EINPROGRESS (Operation now
      6 connect(5, {sa_family=AF_UNIX, sun_path="/var/run/docker.sock"}, 23) = -1 ENOENT (No such file or directory)
```

### off
```
$ grep -aoE "CONNECT [^ \\]+" ac5_off.txt | sort | uniq -c
      1 CONNECT auth.docker.io:443
      1 CONNECT check.trivy.dev:443
      1 CONNECT index.docker.io:443
      1 CONNECT production.cloudfront.docker.com:443
$ other connect() lines (not the proxy)
      1 connect(10, {sa_family=AF_INET, sin_port=htons(9), sin_addr=inet_addr("127.0.0.1")}, 16) = -1 EINPROGRESS (Operation now
      5 connect(5, {sa_family=AF_UNIX, sun_path="/var/run/docker.sock"}, 23) = -1 ENOENT (No such file or directory)
      1 connect(8, {sa_family=AF_UNIX, sun_path="/var/run/docker.sock"}, 23) = -1 ENOENT (No such file or directory)
```
