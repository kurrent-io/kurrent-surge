---
name: verify
description: Build the solution and run all unit tests. Use after code changes to verify nothing is broken.
---

Run a full build followed by all unit tests. Both steps must pass.

## Steps

1. **Build** the entire solution:

```sh
dotnet build -c Release
```

If the build fails, stop and report the errors. Do not proceed to tests.

2. **Run all unit tests** (excludes integration tests) as a background task:

```sh
dotnet test -c Release --no-build --ignore-exit-code 8 --results-directory ./artifacts/test-results \
-- --treenode-filter "/*/*/*/*[Category!=Integration]" \
--adaptive-metrics --report-trx --coverage --output Detailed --log-level Trace
```

Use `run_in_background: true` with a 5-minute timeout. Never block the main conversation.

3. **Report results** concisely: total, passed, failed, skipped. If any tests failed, list the failed test names and their error messages.
