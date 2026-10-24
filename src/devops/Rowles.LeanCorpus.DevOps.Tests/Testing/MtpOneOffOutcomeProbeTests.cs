namespace Rowles.LeanCorpus.DevOps.Tests.Testing;

public sealed class MtpOneOffOutcomeProbeTests
{
    [Fact]
    public void OneOffProbeCase()
    {
        if (Environment.GetEnvironmentVariable("LEANCORPUS_TEST_MTP_FAILURE_PROBE") == "issue97-deliberate-failure-probe")
        {
            Assert.Fail("Deliberate MTP evidence probe failure.");
        }
    }
}
