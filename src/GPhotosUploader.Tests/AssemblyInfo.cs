using Xunit;

// Loc.Culture est un état statique global : on sérialise les tests pour éviter que
// deux tests changeant la culture ne se marchent dessus.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
