namespace TacitMemory.Core;

/// <summary>An auxiliary model, by the role name the status report uses.</summary>
public enum ModelRole
{
    Embedding,
    Reranker,
    Decision,
}

/// <summary>How a probe ended.</summary>
public enum ProbeOutcome
{
    /// <summary>The model answered in the format the service reads.</summary>
    Succeeded,

    /// <summary>The model is not configured; no request was sent.</summary>
    Unconfigured,

    /// <summary>The request failed; <see cref="ProbeResult.Failure"/> says how.</summary>
    Failed,
}

/// <summary>Why a probe failed.</summary>
public enum ProbeFailure
{
    /// <summary>The model answered with a non-success HTTP status.</summary>
    HttpStatus,

    /// <summary>The model answered with success, but not in the format the service reads.</summary>
    WrongFormat,

    /// <summary>The request could not be sent or its answer could not be received.</summary>
    Network,

    /// <summary>The model did not answer within the probe's time limit.</summary>
    Timeout,
}

/// <summary>
/// The result of probing one model. <see cref="StatusCode"/> and <see cref="ResponseBody"/> are set when a response came
/// back; the body is its first 500 characters, with every secret of the config replaced. <see cref="Elapsed"/> is zero
/// when no request was sent.
/// </summary>
public sealed record ProbeResult(
    ModelRole Role,
    ProbeOutcome Outcome,
    ProbeFailure? Failure,
    int? StatusCode,
    string? ResponseBody,
    TimeSpan Elapsed);
