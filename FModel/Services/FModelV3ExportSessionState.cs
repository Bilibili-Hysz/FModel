#nullable enable

using System;
using System.Threading;
using CUE4Parse.FModelUEFormat.ExportPipeline.Pipeline;
using FModel.Framework;

namespace FModel.Services;

/// <summary>
/// Process-local material policy for the integrated UEFormat backend.
/// The official mesh exporters consult this state only while serializing UEFormat;
/// ActorX, GLTF and USD never query it.
/// </summary>
public sealed class FModelV3ExportSessionState : ViewModel
{
    public static FModelV3ExportSessionState Current { get; } = new();

    private int _initialized;
    private bool _canToggle = true;
    private string _activationSource = "default";
    private FModelUeFormatMaterialMode _materialMode = FModelUeFormatMaterialMode.Linked;

    private FModelV3ExportSessionState()
    {
    }

    public bool IsEnabled => true;

    public bool CanToggle
    {
        get => _canToggle;
        private set
        {
            if (!SetProperty(ref _canToggle, value)) return;
            RaisePropertyChanged(nameof(Description));
            RaisePropertyChanged(nameof(MaterialModeDescription));
        }
    }

    public string ActivationSource
    {
        get => _activationSource;
        private set
        {
            if (!SetProperty(ref _activationSource, value)) return;
            RaisePropertyChanged(nameof(Description));
        }
    }

    public FModelUeFormatMaterialMode MaterialMode
    {
        get => _materialMode;
        set
        {
            if (!CanToggle)
            {
                RaisePropertyChanged(nameof(MaterialMode));
                RaisePropertyChanged(nameof(MaterialModeText));
                return;
            }
            if (!Enum.IsDefined(value) || !SetProperty(ref _materialMode, value))
                return;

            RaisePropertyChanged(nameof(MaterialModeText));
            RaisePropertyChanged(nameof(MaterialModeDescription));
            FModelV3ExportFeedback.ReportMaterialModeChanged(value);
        }
    }

    public string MaterialModeText => MaterialMode switch
    {
        FModelUeFormatMaterialMode.Linked => "Linked",
        FModelUeFormatMaterialMode.Strict => "Strict",
        FModelUeFormatMaterialMode.Disabled => "Disabled",
        _ => MaterialMode.ToString()
    };

    public string MaterialModeDescription => CanToggle
        ? $"Material-link policy used by the next UEFormat mesh serialization. Non-UEFormat exports are unaffected. Current: {MaterialModeText}."
        : $"UEFormat material-link policy is fixed for this session: {MaterialModeText}.";

    public string StatusText => IsEnabled ? "Enabled" : "Disabled";

    public string Description =>
        "Mesh Format selects the backend automatically: only UEFormat requests the FModel format extension; other formats retain the official root exporter and format switch.";

    internal IDisposable EnterBatchLock()
    {
        var previous = CanToggle;
        CanToggle = false;
        return new BatchLock(this, previous);
    }

    internal void Initialize(bool enabled, bool isOneShot, string activationSource, FModelUeFormatMaterialMode materialMode)
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0)
            throw new InvalidOperationException("The V3 export session state has already been initialized.");
        if (isOneShot && !enabled)
            throw new ArgumentException("One-shot verification requires UEFormat V3 to be enabled.", nameof(enabled));
        if (!Enum.IsDefined(materialMode))
            throw new ArgumentOutOfRangeException(nameof(materialMode));

        _materialMode = materialMode;
        CanToggle = !isOneShot;
        RaisePropertyChanged(nameof(MaterialMode));
        RaisePropertyChanged(nameof(MaterialModeText));
        RaisePropertyChanged(nameof(MaterialModeDescription));
        ActivationSource = isOneShot ? activationSource : "mesh-format";
    }

    private sealed class BatchLock(FModelV3ExportSessionState owner, bool previousCanToggle) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            owner.CanToggle = previousCanToggle;
        }
    }

}
