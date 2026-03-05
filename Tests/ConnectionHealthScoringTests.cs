using Zer0Talk.Utilities;
using Xunit;

namespace Zer0Talk.Tests;

public class ConnectionHealthScoringTests
{
    [Fact]
    public void Evaluate_HealthySnapshot_ReturnsPerfectScoreAndHealthyDoctorMessage()
    {
        var snapshot = new NetworkDiagnostics.Snapshot
        {
            DirectSuccess = 3,
            DirectFail = 0,
            NatSuccess = 2,
            NatFail = 0,
            RelaySuccess = 1,
            RelayFail = 0,
            UidMismatch = 0,
        };

        var result = ConnectionHealthScoring.Evaluate(snapshot);

        Assert.Equal(100, result.Score);
        Assert.Equal("Network looks healthy. No action needed.", result.DoctorSummary);
    }

    [Fact]
    public void Evaluate_FailingSnapshot_AppliesAllPenaltiesAndAggregatesDoctorGuidance()
    {
        var snapshot = new NetworkDiagnostics.Snapshot
        {
            DirectSuccess = 0,
            DirectFail = 4,
            NatSuccess = 0,
            NatFail = 3,
            RelaySuccess = 0,
            RelayFail = 2,
            UidMismatch = 9,
        };

        var result = ConnectionHealthScoring.Evaluate(snapshot);

        Assert.Equal(15, result.Score);
        Assert.Contains("Identity mismatch seen", result.DoctorSummary);
        Assert.Contains("NAT path failing", result.DoctorSummary);
        Assert.Contains("Direct path unavailable", result.DoctorSummary);
        Assert.Contains("Relay failing", result.DoctorSummary);
    }
}