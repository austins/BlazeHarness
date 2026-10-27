using BlazeHarness.Web.Tests.E2E;
using Xunit.Sdk;
using Xunit.v3;

[assembly: Parallelization(Mode = ParallelMode.All)]
[assembly: AssemblyFixture(typeof(BrowserFixture))]
