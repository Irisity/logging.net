using Xunit;

// All tests mutate static state on LogFactory / LogContext, so they must not run in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
