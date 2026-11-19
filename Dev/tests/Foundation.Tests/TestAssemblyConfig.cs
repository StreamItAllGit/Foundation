// @module: Foundation.Tests.AssemblyConfig
// @purpose: Disables xUnit cross-class test parallelization so tests mutating process-wide environment variables or Win32 desktop state do not race.
// @depends-on: Xunit
// @exports: CollectionBehaviorAttribute

using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
