// @module: Foundation.Installer.JsonContext
// @purpose: Source-generated NativeAOT System.Text.Json serializer context eliminating runtime reflection across Foundation.Installer.
// @depends-on: System.Collections.Generic, System.Text.Json.Serialization
// @exports: FoundationInstallerJsonContext

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Foundation.Installer;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(InstallManifest))]
[JsonSerializable(typeof(FoundationInstallManifest))]
[JsonSerializable(typeof(InstallerMarkupManifest))]
[JsonSerializable(typeof(InstallerConfigOverride))]
[JsonSerializable(typeof(InstallerMarkupTheme))]
[JsonSerializable(typeof(InstallerMarkupButtons))]
[JsonSerializable(typeof(InstallerMarkupTooltip))]
[JsonSerializable(typeof(InstallerMarkupSecondaryPicker))]
[JsonSerializable(typeof(InstallerPayloadAsset))]
[JsonSerializable(typeof(Dictionary<string, InstallerMarkupTooltip>))]
[JsonSerializable(typeof(List<InstallerMarkupSecondaryPicker>))]
[JsonSerializable(typeof(List<InstallerPayloadAsset>))]
[JsonSerializable(typeof(List<string>))]
internal partial class FoundationInstallerJsonContext : JsonSerializerContext
{
}
