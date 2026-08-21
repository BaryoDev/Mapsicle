using Xunit;

// xUnit runs collections in parallel by default. That is unsafe here, because the API under test
// is static mutable global state: Mapper.UseLruCache, Mapper.MaxCacheSize, Mapper.MaxDepth and the
// caches behind them are process-wide. A test that sets MaxCacheSize to bound the LRU is changing
// configuration for every other collection running beside it, and eleven classes in this assembly
// carry no collection attribute at all.
//
// This was not theoretical. The LRU bound test produced a single failure in a solution-wide run
// that four consecutive runs of this project alone could not reproduce, which is the signature of
// cross-collection interference rather than a real defect.
//
// The [Collection("StaticMapperTests")] attributes stay, because they document which classes touch
// static state, but the guarantee comes from here. The whole suite takes about five seconds; a
// flaky test costs far more than that, because it teaches everyone to rerun until green.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
