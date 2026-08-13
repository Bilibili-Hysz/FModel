#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using CUE4Parse.UE4.Versions;
using CUE4Parse_Conversion.Options;
using FModel.Settings;
using FModel.ViewModels;
using FModel.ViewModels.ApiEndpoints.Models;
using Serilog;

namespace FModel.Services;

internal sealed record FModelNewPipelineOneShotProfile(
    string FixtureRoot,
    string ObjectPath,
    EGame GameProfile,
    string Mappings,
    string OutputRoot);

/// <summary>
/// New-pipeline-only command-line startup state. It configures the existing FModel
/// provider/settings path; it does not own exporter registration or disk writing.
/// </summary>
internal static class FModelV3NewPipelineStartupOptions
{
    internal const string Switch = "--enable-v3-ueformat";
    internal const string EnabledEnvironmentVariable = "FMODEL_V3_UEFORMAT_ENABLED";
    internal const string AesEnvironmentVariable = "FMODEL_V3_AES_KEY";
    private const string FixtureRootOption = "--v3-one-shot-fixture-root";
    private const string ObjectPathOption = "--v3-one-shot-object-path";
    private const string GameProfileOption = "--v3-one-shot-game-profile";
    private const string MappingsOption = "--v3-one-shot-mappings";
    private const string OutputRootOption = "--v3-one-shot-output-root";
    private static readonly string[] OneShotOptions =
        [FixtureRootOption, ObjectPathOption, GameProfileOption, MappingsOption, OutputRootOption];
    private static readonly string[] RequiredOneShotOptions =
        [FixtureRootOption, ObjectPathOption, GameProfileOption, OutputRootOption];

    internal static bool IsEnabled => FModelV3ExportSessionState.Current.IsEnabled;
    internal static string ActivationSource => FModelV3ExportSessionState.Current.ActivationSource;
    internal static bool IsOneShot => OneShotProfile is not null;
    internal static FModelNewPipelineOneShotProfile? OneShotProfile { get; private set; }

    internal static void Initialize(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (!TryParse(args, out var enabled, out var profile, out var error))
            throw new ArgumentException(error, nameof(args));

        var environmentEnabled = ReadBoolean(Environment.GetEnvironmentVariable(EnabledEnvironmentVariable));
        var activationSource = (enabled, environmentEnabled) switch
        {
            (true, true) => "command-line+environment",
            (true, false) => "command-line",
            (false, true) => "environment",
            _ => "default"
        };

        OneShotProfile = profile;
        FModelV3ExportSessionState.Current.Initialize(
            enabled || environmentEnabled,
            profile is not null,
            activationSource,
            FModelV3ExporterFactory.ReadMaterialMode());
    }

    internal static void LogState()
    {
        Log.Information("v3.newpipeline.activation enabled {Enabled} mode {Mode} source {Source}",
            IsEnabled, IsOneShot ? "one-shot" : "normal", ActivationSource);
    }

    private static bool TryParse(
        string[] args,
        out bool enabled,
        out FModelNewPipelineOneShotProfile? profile,
        out string error)
    {
        enabled = false;
        profile = null;
        error = string.Empty;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (string.Equals(argument, Switch, StringComparison.Ordinal))
            {
                if (enabled) { error = "Duplicate V3 activation switch."; return false; }
                enabled = true;
                continue;
            }

            if (Array.IndexOf(OneShotOptions, argument) < 0)
            {
                if (argument.StartsWith("--v3-one-shot-", StringComparison.Ordinal))
                {
                    error = "Unknown V3 one-shot option.";
                    return false;
                }
                continue;
            }
            if (values.ContainsKey(argument)) { error = "Duplicate V3 one-shot option."; return false; }
            if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]) || args[index].StartsWith("--", StringComparison.Ordinal))
            {
                error = "Missing V3 one-shot option value.";
                return false;
            }
            values.Add(argument, args[index]);
        }

        if (values.Count == 0) return true;
        if (!enabled) { error = "V3 one-shot options require exact activation."; return false; }
        foreach (var requiredOption in RequiredOneShotOptions)
        {
            if (!values.ContainsKey(requiredOption)) { error = "Incomplete V3 one-shot option set."; return false; }
        }
        if (!values[GameProfileOption].StartsWith("GAME_", StringComparison.Ordinal) ||
            !Enum.TryParse(values[GameProfileOption], false, out EGame gameProfile) || !Enum.IsDefined(gameProfile))
        {
            error = "Invalid V3 one-shot game profile.";
            return false;
        }

        profile = new FModelNewPipelineOneShotProfile(
            values[FixtureRootOption], values[ObjectPathOption], gameProfile,
            values.GetValueOrDefault(MappingsOption, string.Empty), values[OutputRootOption]);
        return true;
    }

    private static bool ReadBoolean(string? value)
    {
        return value?.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on" or "enabled";
    }

    internal static void ConfigureOneShotProfile()
    {
        if (OneShotProfile is not { } profile) return;

        var aes = Environment.GetEnvironmentVariable(AesEnvironmentVariable, EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable(AesEnvironmentVariable, null, EnvironmentVariableTarget.Process);

        var settings = UserSettings.Default;
        settings.OutputDirectory = profile.OutputRoot;
        settings.RawDataDirectory = Path.Combine(profile.OutputRoot, "Exports");
        settings.PropertiesDirectory = settings.RawDataDirectory;
        settings.TextureDirectory = settings.RawDataDirectory;
        settings.AudioDirectory = settings.RawDataDirectory;
        settings.CodeDirectory = settings.RawDataDirectory;
        settings.ModelDirectory = settings.RawDataDirectory;
        settings.GameDirectory = profile.FixtureRoot;
        settings.AesReload = EAesReload.Never;
        settings.DiscordRpc = EDiscordRpc.Never;
        settings.MeshExportFormat = EMeshFormat.UEFormat;
        settings.SaveEmbeddedMaterials = true;

        var directory = DirectorySettings.Default("V3NewPipelineOneShot", profile.FixtureRoot, true, profile.GameProfile, aes ?? string.Empty);
        directory.AesKeys = new AesResponse
        {
            MainKey = aes ?? string.Empty,
            DynamicKeys = new List<DynamicKey>()
        };
        directory.Endpoints = EndpointSettings.Default("V3NewPipelineOneShot");
        if (!string.IsNullOrWhiteSpace(profile.Mappings))
        {
            directory.Endpoints[(int) EEndpointType.Mapping] = new EndpointSettings
            {
                Overwrite = true,
                FilePath = profile.Mappings
            };
        }
        settings.CurrentDir = directory;
        settings.PerDirectory.Remove(profile.FixtureRoot);
    }
}
