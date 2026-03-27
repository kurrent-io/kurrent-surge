---
name: integration
description: Run all integration tests against a live KurrentDB container. Use to validate end-to-end behavior.
---

Run all integration tests. These spin up Docker containers via FluentDocker and require Docker to be running.

## Steps

1. **Run integration tests** as a background task:

```sh
dotnet test -c Release --ignore-exit-code 8 --results-directory ./artifacts/test-results \
-- --treenode-filter "/*/*/*/*[Category=Integration]" \
--adaptive-metrics --report-trx --coverage --output Detailed --log-level Trace
```

Use `run_in_background: true` with a 5-minute timeout. Never block the main conversation.

2. **Report results** concisely: total, passed, failed, skipped. If any tests failed, list the failed test names and their error messages.
