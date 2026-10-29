using SateAIs.Core.Analysis;
using SateAIs.Core.Api;
using SateAIs.Core.Credentials;
using Xunit;

namespace SateAIs.Core.Tests;

public class SubmitGateTests
{
    public static TheoryData<Exception> FailuresTheInputsCause => new()
    {
        new ValidationException("VALIDATION_ERROR", "polygon area (61115572.5 km²) exceeds 30000 km² limit"),
        new InvalidAnalysisRequestException("ship: date is required"),
    };

    public static TheoryData<Exception> FailuresThatMayPass => new()
    {
        new InsufficientCreditsException("INSUFFICIENT_CREDITS", "balance too low"),
        new AuthenticationException("UNAUTHORIZED", "bad key"),
        new PermissionDeniedException("FORBIDDEN", "not enabled"),
        new AuthNotConfiguredException(),
        new RateLimitException("RATE_LIMIT_EXCEEDED", "too many jobs"),
        new ConflictException("CONFLICT", "not ready"),
        new ServerException(503, null, "unavailable"),
        new NotFoundException(404, null, "not found"),
        new HttpRequestException("no route to host"),
        new TaskCanceledException("timed out"),
    };

    [Theory]
    [MemberData(nameof(FailuresTheInputsCause))]
    public void A_rejection_of_the_inputs_holds_the_button(Exception failure) =>
        Assert.True(SubmitGate.Blocks(failure));

    [Theory]
    [MemberData(nameof(FailuresThatMayPass))]
    public void Account_server_and_network_failures_leave_it_to_the_server(Exception failure) =>
        Assert.False(SubmitGate.Blocks(failure));

    [Theory]
    [InlineData("SCENE_NOT_FOUND")]
    [InlineData("INSUFFICIENT_SCENES")]
    public void No_usable_scene_holds_the_button(string code) =>
        Assert.True(SubmitGate.Blocks(new Preview { Warnings = [new ApiWarning(code, "none")] }));

    [Fact]
    public void Other_warnings_and_partial_coverage_do_not()
    {
        var preview = new Preview
        {
            Coverage = new PreviewCoverage(Ratio: 0.4),
            Warnings = [new ApiWarning("CREDITS_NOT_ESTIMABLE", "unknown"), new ApiWarning(null, "free text")],
        };
        Assert.False(SubmitGate.Blocks(preview));
    }
}
