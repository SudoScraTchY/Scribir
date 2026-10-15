namespace Scriber.Pipeline;

/// <summary>
/// Status update reporting progress through the transcription pipeline.
/// </summary>
/// <param name="Stage">Current pipeline execution stage.</param>
/// <param name="ProgressFraction">Fraction of overall work completed (0.0 to 1.0).</param>
/// <param name="Message">Human-readable description of current activity.</param>
public record PipelineProgress(PipelineStage Stage, double ProgressFraction, string Message);
