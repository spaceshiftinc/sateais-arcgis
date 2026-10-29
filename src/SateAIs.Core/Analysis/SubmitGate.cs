using SateAIs.Core.Api;

namespace SateAIs.Core.Analysis;

/// <summary>
/// Whether the estimate already shows that submitting the current inputs cannot succeed.
/// Only outcomes that depend on the inputs alone count: editing the inputs runs a new estimate,
/// so the button comes back by itself. Account and server states can change without an edit,
/// so they never hold the button; the server stays the judge on submit.
/// </summary>
public static class SubmitGate
{
    /// <summary>The inputs were rejected: area or period over the limit, or a malformed area.</summary>
    public static bool Blocks(Exception estimateFailure) =>
        estimateFailure is ValidationException or InvalidAnalysisRequestException;

    /// <summary>No usable scene: a submitted job would fail with the same code.</summary>
    public static bool Blocks(Preview preview) => Wording.Wording.ScenesUnavailable(preview.Warnings);
}
