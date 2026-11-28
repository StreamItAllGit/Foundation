// @module: Foundation.Core.JsonContext
// @purpose: Source-generated NativeAOT System.Text.Json serializer context for installer-reachable Foundation.Core contracts.
// @depends-on: System.Collections.Generic, System.Text.Json, System.Text.Json.Serialization
// @exports: FoundationCoreJsonContext

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Foundation.Core;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(JsonHivePlatformRegistry.HiveDocumentDto))]
[JsonSerializable(typeof(JsonHivePlatformRegistry.HiveEntryDto))]
[JsonSerializable(typeof(ReleaseUpdateManifest))]
[JsonSerializable(typeof(PidSafeLockEnvelope))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
internal partial class FoundationCoreJsonContext : JsonSerializerContext
{
}
