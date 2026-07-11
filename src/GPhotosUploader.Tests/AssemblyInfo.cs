using Xunit;

// Loc.Culture is a global static state: we serialize the tests to prevent
// two tests changing the culture from stepping on each other.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
