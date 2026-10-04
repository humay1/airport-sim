// 07-conventions.md L4: tests sharing a process must not distort each other's
// timing budgets, so xUnit test parallelisation is disabled for this assembly.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
