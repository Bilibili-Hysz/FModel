namespace CUE4Parse.FModelUEFormat.ExportPipeline.Diagnostics;

public enum FModelUeFormatDiagnosticSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2
}

public enum FModelUeFormatPipelineStage
{
    Activation = 0,
    Queue = 1,
    Eligibility = 2,
    Conversion = 3,
    MaterialProjection = 4,
    StandardSerialization = 5,
    ExtensionSerialization = 6,
    Write = 7,
    IndependentParse = 8,
    Repeatability = 9
}

/// <summary>Redacted, deterministic diagnostic emitted by the V3 adapter.</summary>
public sealed record FModelUeFormatPipelineDiagnostic
{
    public FModelUeFormatPipelineDiagnostic(
        string code,
        FModelUeFormatDiagnosticSeverity severity,
        FModelUeFormatPipelineStage stage,
        string objectPath,
        string message,
        string? materialSlot = null,
        string? textureParameter = null,
        string? recovery = null,
        string evidenceGeneration = "fmodel-export-pipeline-v2/v3-adapter-v1")
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Diagnostic code is required.", nameof(code));
        if (string.IsNullOrWhiteSpace(objectPath)) throw new ArgumentException("Object path is required.", nameof(objectPath));
        if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("Diagnostic message is required.", nameof(message));
        if (string.IsNullOrWhiteSpace(evidenceGeneration)) throw new ArgumentException("Evidence generation is required.", nameof(evidenceGeneration));
        if (message.Contains('\n') || message.Contains('\r')) throw new ArgumentException("Message must be single-line.", nameof(message));

        Code = code;
        Severity = severity;
        Stage = stage;
        ObjectPath = objectPath;
        Message = message;
        MaterialSlot = materialSlot;
        TextureParameter = textureParameter;
        Recovery = recovery;
        EvidenceGeneration = evidenceGeneration;
    }

    public string Code { get; }
    public FModelUeFormatDiagnosticSeverity Severity { get; }
    public FModelUeFormatPipelineStage Stage { get; }
    public string ObjectPath { get; }
    public string Message { get; }
    public string? MaterialSlot { get; }
    public string? TextureParameter { get; }
    public string? Recovery { get; }
    public string EvidenceGeneration { get; }
}
