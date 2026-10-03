"""Per-repository settings for the squad tools (.NET profile). Seeded once by adopt-template and kept on
later refreshes; the scripts that import it are template-managed."""

# Solution or project file the analyzer gate builds.
SOLUTION = "DockerUpdateGuard.slnx"

# Coverage gate (.squad/tools/coverage-check.py)
COVERAGE_FORMAT = "cobertura"
COVERAGE_REPORT_GLOB = "TestResults/**/coverage.cobertura.xml"
COVERAGE_PATHSPECS = ["src/*.cs", "src/*.razor"]
COVERAGE_EXCLUDES = ["src/Tests/*", "src/DockerUpdateGuard.Data/Migrations/*"]  # tests and generated EF Core migrations
