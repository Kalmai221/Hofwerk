using System.Text.Json;
using System.Text.Json.Serialization;
using Hofwerk.Core.Presets;

namespace Hofwerk.Core;

/// <summary>The few JSON shapes the app reads and writes. Generated at build time so the app can be trimmed without reflection.</summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip)]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(PresetManifest))]
internal sealed partial class HofwerkJson : JsonSerializerContext;
