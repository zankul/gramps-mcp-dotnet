using Xunit.Abstractions;
using Xunit.Sdk;

namespace GrampsMcp.LiveTests.Writes;

/// <summary>
/// Forces <c>ExampleTreeUnchanged_AfterWriteSuite</c> to run last among
/// <c>WriteRoundTripTests</c>' own test methods. xunit's default test case order within a class is
/// not a documented guarantee, and that test's whole point is observing the *other* tests' cleanup
/// (per-test <see cref="TestObjectScope"/> disposal) before asserting the tree's object-count
/// fingerprint — if it ran first by an ordering coincidence, it would prove nothing about whether
/// this suite leaked. Every other test keeps whatever relative order xunit already gives it.
/// </summary>
internal sealed class WriteRoundTripOrderer : ITestCaseOrderer
{
    private const string FingerprintCheckMethodName = "ExampleTreeUnchanged_AfterWriteSuite";

    public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases)
        where TTestCase : ITestCase =>
        testCases.OrderBy(testCase =>
            testCase.TestMethod.Method.Name == FingerprintCheckMethodName ? 1 : 0);
}
