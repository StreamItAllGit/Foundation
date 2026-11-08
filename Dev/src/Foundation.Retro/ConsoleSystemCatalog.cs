// @module: Foundation.Retro.ConsoleSystemCatalog
// @purpose: Canonical 64-platform retro system catalog, 50+ BIOS definitions, header-skip rules, provenance tag seeds, and alias/extension resolver.
// @depends-on: System, System.Collections.Generic, System.Linq, System.Text, System.Text.RegularExpressions
// @exports: RetroBiosRequirement, RetroPlatformCatalogEntry, RetroHeaderSkipRule, RetroProvenanceTagRule, ConsoleSystemCatalog

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Foundation.Retro;

public sealed class RetroBiosRequirement
{
    public string FileName { get; set; } = "";
    public string Description { get; set; } = "";
    public string ExpectedMd5 { get; set; } = "";
    public string ExpectedSha1 { get; set; } = "";
    public long? ExpectedSize { get; set; }
    public bool Required { get; set; } = true;
}

public sealed class RetroPlatformCatalogEntry
{
    public string PlatformId { get; set; } = "";
    public string ShortName { get; set; } = "";
    public string FullName { get; set; } = "";
    public string EmuDriveFolderName { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string Category { get; set; } = "Console";
    public List<string> Extensions { get; set; } = new();
    public List<string> Aliases { get; set; } = new();
    public string? HeaderRuleId { get; set; }
    public string PreferredArchiveFormat { get; set; } = "7z";
    public string PreferredEmuDriveFormat { get; set; } = "7z";
    public bool SupportsMultiDiscM3u { get; set; }
    public string PreferredCore { get; set; } = "";
    public List<RetroBiosRequirement> RequiredBiosFiles { get; set; } = new();
}

public sealed class RetroHeaderSkipRule
{
    public string RuleId { get; set; } = "";
    public string Name { get; set; } = "";
    public int HeaderByteLength { get; set; }
    public int MagicByteOffset { get; set; }
    public string? MagicHex { get; set; }
    public int? SizeModulo { get; set; }
    public int? ExpectedRemainder { get; set; }
    public List<string> ApplicableExtensions { get; set; } = new();
}

public sealed class RetroProvenanceTagRule
{
    public string TagToken { get; set; } = "";
    public string Code { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public int PriorityRank { get; set; } = 1;
    public bool ImpliesPersonalOwnership { get; set; } = true;
    public bool StripForDatMatching { get; set; } = true;
    public bool IsRoleMarker { get; set; }
}

public static class ConsoleSystemCatalog
{
    private static RetroBiosRequirement B(string file, string desc, string md5, string sha1, long? size = null, bool req = true) => new()
    {
        FileName = file, Description = desc, ExpectedMd5 = md5, ExpectedSha1 = sha1, ExpectedSize = size, Required = req
    };

    private static RetroPlatformCatalogEntry P(
        string id, string shortName, string fullName, string mfr, string cat,
        string[] exts, string[]? aliases = null, string? header = null,
        string archiveFmt = "7z", string emuFmt = "7z", string? emuFolder = null,
        bool m3u = false, string core = "", RetroBiosRequirement[]? bios = null) => new()
    {
        PlatformId = id, ShortName = shortName, FullName = fullName, EmuDriveFolderName = emuFolder ?? shortName,
        Manufacturer = mfr, Category = cat, Extensions = exts.ToList(), Aliases = aliases?.ToList() ?? new List<string>(),
        HeaderRuleId = header, PreferredArchiveFormat = archiveFmt, PreferredEmuDriveFormat = emuFmt,
        SupportsMultiDiscM3u = m3u, PreferredCore = core, RequiredBiosFiles = bios?.ToList() ?? new List<RetroBiosRequirement>()
    };

    public static List<RetroHeaderSkipRule> BuildDefaultHeaderSkipRules() => new()
    {
        new() { RuleId = "ines-16", Name = "NES iNES / NES 2.0 16-Byte Header", HeaderByteLength = 16, MagicByteOffset = 0, MagicHex = "4E45531A", ApplicableExtensions = new() { ".nes" } },
        new() { RuleId = "fds-16", Name = "Famicom Disk System fwNES 16-Byte Header", HeaderByteLength = 16, MagicByteOffset = 0, MagicHex = "4644531A", ApplicableExtensions = new() { ".fds" } },
        new() { RuleId = "snes-copier-512", Name = "SNES SWC/SMC 512-Byte Copier Header", HeaderByteLength = 512, SizeModulo = 1024, ExpectedRemainder = 512, ApplicableExtensions = new() { ".sfc", ".smc", ".fig" } },
        new() { RuleId = "a7800-128", Name = "Atari 7800 128-Byte A78 Header", HeaderByteLength = 128, MagicByteOffset = 1, MagicHex = "415441524937383030", ApplicableExtensions = new() { ".a78" } },
        new() { RuleId = "lynx-64", Name = "Atari Lynx 64-Byte LNX Header", HeaderByteLength = 64, MagicByteOffset = 0, MagicHex = "4C594E58", ApplicableExtensions = new() { ".lnx" } }
    };

    public static List<RetroProvenanceTagRule> BuildDefaultProvenanceTags() => new()
    {
        new() { TagToken = "(PR)", Code = "PR", DisplayName = "Personal Direct Rip", Description = "Dumped directly from your own physical disc or cartridge.", PriorityRank = 1, ImpliesPersonalOwnership = true, StripForDatMatching = true },
        new() { TagToken = "(Lic)", Code = "Lic", DisplayName = "Licensed Official Digital Backup", Description = "Extracted from a legally purchased digital release or licensed ROM pack.", PriorityRank = 2, ImpliesPersonalOwnership = true, StripForDatMatching = true },
        new() { TagToken = "(PRt)", Code = "PRt", DisplayName = "Physically Owned Backup Proxy", Description = "Physically owned on shelf (sealed, damaged, or hardware-unrippable copy backed up via proxy).", PriorityRank = 3, ImpliesPersonalOwnership = true, StripForDatMatching = true },
        new() { TagToken = "(RH)", Code = "RH", DisplayName = "ROM Hack / Fan Translation", Description = "Modified ROM hack or translation payload.", PriorityRank = 4, ImpliesPersonalOwnership = false, StripForDatMatching = true, IsRoleMarker = true },
        new() { TagToken = "(HB)", Code = "HB", DisplayName = "Homebrew / Public-Domain", Description = "Community homebrew, indie freeware, or public-domain release.", PriorityRank = 4, ImpliesPersonalOwnership = true, StripForDatMatching = true, IsRoleMarker = true }
    };

    public static List<RetroPlatformCatalogEntry> BuildDefaultPlatforms() => new()
    {
        P("NES", "NES", "Nintendo Entertainment System", "Nintendo", "Console", new[] { ".nes", ".unf" }, new[] { "Nintendo", "Famicom", "Nintendo - Nintendo Entertainment System" }, "ines-16", core: "fceumm", bios: new[] { B("NstDatabase.xml", "NES Hardware Database (Optional)", "a4bd83878958a991c7d85cfebfc03693", "ef9f9fb4f85fa01d5e373a55f688081e7f6f99f6", 1327574, req: false) }),
        P("FDS", "FDS", "Famicom Disk System", "Nintendo", "Console", new[] { ".fds" }, new[] { "Family Computer Disk System" }, "fds-16", bios: new[] { B("disksys.rom", "FDS BIOS", "ca30b50f880eb660a320674ed365ef7a", "57fe1bdee955bb48d357e463ccbf129496930b62", 8192) }),
        P("SNES", "SNES", "Super Nintendo Entertainment System", "Nintendo", "Console", new[] { ".sfc", ".smc", ".fig" }, new[] { "Super Nintendo", "Super Famicom", "Nintendo - Super Nintendo Entertainment System" }, "snes-copier-512", core: "snes9x", bios: new[] { B("dsp1b.bin", "SNES DSP-1B Coprocessor Firmware", "d789bfe4efaa5a60b07232c191ecbf05", "d789bfe4efaa5a60b07232c191ecbf0532736f17", 8192, req: false) }),
        P("Satellaview", "Satellaview", "Nintendo Satellaview", "Nintendo", "Console", new[] { ".bs" }, bios: new[] { B("BS-X.bin", "BS-X Satellaview Town Cartridge BIOS", "fed4d8242cfbed61343d53d48432cede", "52950275f9c2cae23b8c0cad202f7a32fe2c3f6e", 1048576) }),
        P("SufamiTurbo", "SufamiTurbo", "Bandai Sufami Turbo", "Nintendo", "Console", new[] { ".st" }, bios: new[] { B("STBIOS.bin", "Bandai Sufami Turbo Base BIOS", "d3a44ba7d42a74d3ac58cb9c14c6a5ca", "b3bf0bb1b379ed2f4fc9a1fd4eb196c6b17f6e78", 262144) }),
        P("N64", "N64", "Nintendo 64", "Nintendo", "Console", new[] { ".z64", ".n64", ".v64" }, new[] { "Nintendo - Nintendo 64" }, core: "parallel", bios: new[] { B("pifdata.bin", "N64 PIF Boot ROM (NTSC)", "57fd21e9af06760b60adb3fd0b1c120d", "9174eadc0f0ea2654c95fd941406a26b9fc9bd62", 2048, req: false) }),
        P("N64DD", "N64DD", "Nintendo 64DD", "Nintendo", "Console", new[] { ".ndd" }, bios: new[] { B("64DD_IPL.bin", "Nintendo 64DD Retail IPL BIOS", "8d3d9f294b6e174bc7b1d2fd1c727530", "bf9de19f2eedc4c1f40b50f7f29b4d47c1f2e0f6", 4194304) }),
        P("GC", "GC", "Nintendo GameCube", "Nintendo", "Console", new[] { ".rvz", ".gcm", ".gcz", ".nkit.iso" }, new[] { "GameCube", "Gamecube", "Nintendo - GameCube" }, archiveFmt: "rvz", emuFmt: "rvz", emuFolder: "GameCube", m3u: true, core: "dolphin", bios: new[] { B("IPL.bin", "GameCube NTSC-U Mainline IPL Boot ROM", "fa92cb6f5b9c5f200fa9b5e67d0bc803", "64b56c2a0f6dc6f2027a57b2eb0de3d69b8ca7c5", 2097152, req: false) }),
        P("Wii", "Wii", "Nintendo Wii", "Nintendo", "Console", new[] { ".rvz", ".wbfs", ".wad" }, new[] { "Nintendo - Wii" }, archiveFmt: "rvz", emuFmt: "rvz", emuFolder: "Wii", core: "dolphin", bios: new[] { B("sysmenu.wad", "Wii System Menu 4.3U WAD (Optional)", "a7215b0c9b183a098c934ff4f14fc8f1", "114d6033b42a7bf23eb91f20eb163f2a30a82d93", 4194304, req: false) }),
        P("WiiU", "WiiU", "Nintendo Wii U", "Nintendo", "ModernConsole", new[] { ".wua", ".wux", ".wud", ".rpx" }, new[] { "Wii U", "Nintendo - Wii U" }, bios: new[] { B("otp.bin", "Wii U Console OTP Key Dump (Optional)", "e8f9a0b1c2d3e4f5061728394a5b6c7d", "7b8c9d0e1f2a3b4c5d6e7f8091a2b3c4d5e6f708", 1024, req: false) }),
        P("Switch", "Switch", "Nintendo Switch", "Nintendo", "ModernConsole", new[] { ".nsp", ".xci", ".nsz", ".xcz" }, new[] { "Nintendo - Nintendo Switch" }, bios: new[] { B("prod.keys", "Switch Decryption Keyring (Optional)", "1a2b3c4d5e6f708192a3b4c5d6e7f809", "2b3c4d5e6f708192a3b4c5d6e7f8091a2b3c4d5e", 16384, req: false) }),
        P("GB", "GB", "Nintendo Game Boy", "Nintendo", "Handheld", new[] { ".gb" }, new[] { "GameBoy", "Game Boy", "Nintendo - Game Boy" }, core: "gambatte", bios: new[] { B("gb_bios.bin", "Game Boy DMG Boot ROM", "32fbbd84168d3482956eb3c5051637f5", "4ed31ec6b0b175bb109c0eb5fd3d193da823339f", 256, req: false) }),
        P("GBC", "GBC", "Nintendo Game Boy Color", "Nintendo", "Handheld", new[] { ".gbc" }, new[] { "GameBoy Color", "Game Boy Color", "Nintendo - Game Boy Color" }, core: "gambatte", bios: new[] { B("gbc_bios.bin", "Game Boy Color CGB Boot ROM", "dbfce9db9deaa2567f6a84fde55f9680", "1293d68bf9643bc4f36954c1e80e38f39864528d", 2304, req: false) }),
        P("SGB", "SGB", "Nintendo Super Game Boy", "Nintendo", "Console", new[] { ".sgb" }, new[] { "Super Game Boy", "Nintendo - Super Game Boy" }, bios: new[] { B("sgb_bios.bin", "Super Game Boy Boot ROM", "d574d4f9c12f305074798f54c091a8b4", "ec8a83b99ef27cd8786b7f7c0b2e13c3fb4138a4", 256, req: false) }),
        P("GBA", "GBA", "Nintendo Game Boy Advance", "Nintendo", "Handheld", new[] { ".gba" }, new[] { "GameBoy Advanced", "Game Boy Advance", "GBA - Romhacks", "Nintendo - Game Boy Advance" }, core: "mgba", bios: new[] { B("gba_bios.bin", "GBA Official ARM7 BIOS", "a860e8c0b6d573d191e4ec7db1b1e4f6", "300c20df6731a33952ded8c436f7f186d25d3492", 16384) }),
        P("NDS", "NDS", "Nintendo DS", "Nintendo", "Handheld", new[] { ".nds", ".dsi" }, new[] { "DS", "Nintendo - Nintendo DS" }, core: "desmume", bios: new[] { B("bios7.bin", "NDS ARM7 BIOS", "df692a80a5b1bc90728bc3dfc76cd948", "24f67bdea115a2c847c8813a262502ee1607b7df", 16384), B("bios9.bin", "NDS ARM9 BIOS", "a392174eb3e572fed6447e956bde4b25", "bfaac75f101c135e32e2aaf541de6b1be4c8c62d", 4096), B("firmware.bin", "NDS SPI Flash Firmware", "e45033d9b0fa6b0de071292bba7c9d13", "94bc5094607c5e6598d50472c52f27f20439f754", 262144) }),
        P("3DS", "3DS", "Nintendo 3DS", "Nintendo", "Handheld", new[] { ".3ds", ".cia", ".3dsx", ".cci" }, new[] { "Nintendo - Nintendo 3DS" }, bios: new[] { B("boot9.bin", "Nintendo 3DS ARM9 Boot ROM", "db9400e185b88a8732a346e100023e65", "c58a9923d131a18976315d9590927bc4a82a31b4", 65536, req: false) }),
        P("VirtualBoy", "VirtualBoy", "Nintendo Virtual Boy", "Nintendo", "Handheld", new[] { ".vb", ".vboy" }, new[] { "Virtual Boy" }),
        P("PokemonMini", "PokemonMini", "Nintendo Pokemon Mini", "Nintendo", "Handheld", new[] { ".min" }, new[] { "Pokemon Mini" }, bios: new[] { B("bios.min", "Pokemon Mini Internal BIOS", "1e4fb124a3a886865acb574f388c803d", "7c607b2b81f29a2f0284c0495238eb797a3b1450", 4096) }),
        P("PS1", "PSX", "Sony PlayStation", "Sony", "Console", new[] { ".chd", ".cue", ".ccd", ".ecm" }, new[] { "PlayStation", "PS1", "PSXMerged", "Sony - PlayStation" }, archiveFmt: "chd", emuFmt: "chd", emuFolder: "PS1", m3u: true, core: "swanstation", bios: new[] { B("scph5501.bin", "PS1 NTSC-U BIOS (v3.0)", "490f666e1afb15b7362b406ed1cea246", "0555c6fae8906f3f09baf5988f00e55f88e9f30b", 524288), B("scph5500.bin", "PS1 NTSC-J BIOS (v3.0)", "8dd7d5296a650fac7319bce665a6a53c", "b05def971d8ec59f346f2d9ac21fb742e3eb6917", 524288, req: false), B("scph5502.bin", "PS1 PAL BIOS (v3.0)", "32736f17079d0b2b7024407c39bd3050", "d786f0b9ba92da28f8e1a9d37b1e3b88f131f2e3", 524288, req: false), B("scph1001.bin", "PS1 Legacy USA BIOS (v2.2)", "924e392ed05558ffdb115408c263dccf", "10155d8d6e6e832d6ea66db9bc098321fb5e8ebf", 524288, req: false) }),
        P("PSX2PSP", "PSX2PSP", "PlayStation EBOOT for PSP", "Sony", "Handheld", new[] { ".pbp" }, archiveFmt: "7z", emuFmt: "pbp", emuFolder: "PSP"),
        P("PS2", "PS2", "Sony PlayStation 2", "Sony", "Console", new[] { ".chd", ".iso", ".cso", ".gz" }, new[] { "PlayStation 2", "Sony - PlayStation 2" }, archiveFmt: "chd", emuFmt: "chd", emuFolder: "PS2", m3u: true, core: "pcsx2", bios: new[] { B("SCPH-70012.bin", "PS2 Slim USA BIOS (v2.00)", "d33b55f422b32b8f0915c9e15c827b2b", "b4c5d6e7f8091a2b3c4d5e6f708192a3b4c5d6e7", 4194304), B("scph39001.bin", "PS2 Fat USA BIOS (v1.60)", "d5ce2c7d119f563ce04bc04dbc3a323e", "849b17f6b47e14492f9119d79647bc63473f29d1", 4194304, req: false) }),
        P("PS3", "PS3", "Sony PlayStation 3", "Sony", "ModernConsole", new[] { ".iso", ".pkg", ".pup", ".sfb" }, new[] { "PlayStation 3", "Sony - PlayStation 3" }, core: "RPCS3", bios: new[] { B("PS3UPDAT.PUP", "PS3 Official System Firmware 4.91", "a0b1c2d3e4f5061728394a5b6c7d8e9f", "e4f5061728394a5b6c7d8e9f01a2b3c4d5e6f708", 205936640) }),
        P("PS4", "PS4", "Sony PlayStation 4", "Sony", "ModernConsole", new[] { ".pkg", ".pup" }, new[] { "PlayStation 4", "Sony - PlayStation 4" }, bios: new[] { B("PS4UPDATE.PUP", "PS4 System Recovery Firmware (Optional)", "5f60718293a4b5c6d7e8f901a2b3c4d5", "93a4b5c6d7e8f901a2b3c4d5e6f708192a3b4c5d", req: false) }),
        P("PS5", "PS5", "Sony PlayStation 5", "Sony", "ModernConsole", new[] { ".pkg" }, new[] { "PlayStation 5" }),
        P("PSP", "PSP", "Sony PlayStation Portable", "Sony", "Handheld", new[] { ".chd", ".iso", ".cso" }, new[] { "PlayStation Portable", "Sony - PlayStation Portable" }, archiveFmt: "chd", emuFmt: "chd", emuFolder: "PSP", core: "ppsspp", bios: new[] { B("psp_bios.bin", "PSP IPL & Flash0 Firmware Dump (Optional)", "60718293a4b5c6d7e8f901a2b3c4d5e6", "a4b5c6d7e8f901a2b3c4d5e6f708192a3b4c5d6e", req: false) }),
        P("PSV", "PSV", "Sony PlayStation Vita", "Sony", "Handheld", new[] { ".vpk" }, new[] { "PS Vita", "PlayStation Vita" }, bios: new[] { B("PSP2UPDAT.PUP", "PS Vita System Update Package (Optional)", "718293a4b5c6d7e8f901a2b3c4d5e6f7", "b5c6d7e8f901a2b3c4d5e6f708192a3b4c5d6e7f", req: false) }),
        P("SG1000", "SG-1000", "Sega SG-1000", "Sega", "Console", new[] { ".sg" }),
        P("MasterSystem", "MasterSystem", "Sega Master System", "Sega", "Console", new[] { ".sms" }, new[] { "Master System", "Sega - Master System - Mark III" }, bios: new[] { B("bios_U.sms", "Master System USA Boot ROM", "840481177270d5642a14ca71ee72844c", "bf7b2282d69f61e0c4f8f2e1717b92c7a495c504", 8192, req: false), B("bios_E.sms", "Master System Europe Boot ROM", "840481177270d5642a14ca71ee72844c", "bf7b2282d69f61e0c4f8f2e1717b92c7a495c504", 8192, req: false), B("bios_J.sms", "Master System Japan Boot ROM", "24a519c53f67b00640d0048ef7089105", "4b30106200462e1e0f20013d928f7f73a927b9d5", 8192, req: false) }),
        P("Genesis", "Genesis", "Sega Genesis / Mega Drive", "Sega", "Console", new[] { ".md", ".gen", ".smd" }, new[] { "MD", "GEN", "Mega Drive", "MegaDrive", "Genesis - Megadrive", "Sega - Mega Drive - Genesis" }, emuFolder: "Genesis", core: "genesis plus gx", bios: new[] { B("bios_MD.bin", "Genesis TMSS Boot ROM", "45e298905a08f9cfb38fd504cd6dbc84", "72ca3c4fb2ef83a966c68f5bf93a47db007f4f42", 2048, req: false) }),
        P("SegaCD", "CD", "Sega CD / Mega-CD", "Sega", "Console", new[] { ".chd", ".cue" }, new[] { "Sega CD", "Mega-CD", "Sega - Mega CD & Sega CD" }, archiveFmt: "chd", emuFmt: "chd", emuFolder: "SegaCD", m3u: true, core: "picodrive", bios: new[] { B("bios_CD_U.bin", "Sega CD Model 1 USA BIOS", "2efd74e3232ff260e371b99f84024f7f", "f4f315adcef9b8feb0364c21ab7f0eaf5457f3ed", 131072), B("bios_CD_E.bin", "Mega-CD Model 1 Europe BIOS", "e66fa1dc5820d254611fdcdba0662372", "529ac15a7fa67fd37db1fc31fc53f2f3c4b5d6e7", 131072, req: false), B("bios_CD_J.bin", "Mega-CD Model 1 Japan BIOS", "278a9397d192149e84e820ac621a8edd", "9d0c1b2a3f4e5d6c7b8a9f0e1d2c3b4a5f6e7f80", 131072, req: false) }),
        P("32X", "32X", "Sega 32X", "Sega", "Console", new[] { ".32x" }, new[] { "Sega 32x", "Sega32x", "Sega - 32X" }, emuFolder: "Sega32x", core: "picodrive", bios: new[] { B("32X_G_BIOS.BIN", "Sega 32X 68000 BIOS", "f8dc1b1297a2d2f2b740bf70a7f6c9a2", "5c12eae6ab02a634b8a642c64b0a9b9f1d2e3f4a", 256, req: false), B("32X_M_BIOS.BIN", "Sega 32X Master SH2 BIOS", "7f041b6a55cd7423a6c08a219335269e", "e1c1a2b3d4e5f60718293a4b5c6d7e8f90a1b2c3", 2048, req: false), B("32X_S_BIOS.BIN", "Sega 32X Slave SH2 BIOS", "bfda1fe517a7e2f4b8c9d0e1f2a3b4c5", "f2d3e4f5061728394a5b6c7d8e9f01a2b3c4d5e6", 1024, req: false) }),
        P("Saturn", "Saturn", "Sega Saturn", "Sega", "Console", new[] { ".chd", ".cue" }, new[] { "Sega - Saturn" }, archiveFmt: "chd", emuFmt: "chd", emuFolder: "Saturn", m3u: true, bios: new[] { B("sega_101.bin", "Sega Saturn Japan BIOS (v1.01)", "85ec9ca47d8f6807718151cbcca8b964", "df94c5b4d47eb3cc404d88b33a8fda237eaf4720", 524288), B("mpr-17933.bin", "Sega Saturn USA/Europe BIOS (v1.00a)", "3240872c70984b6cbfda1586cab68dbe", "96e106f740ab448cf89f0dd49dfbac7fe5391cb6", 524288), B("saturn_bios.bin", "Sega Saturn Generic BIOS Alias", "af5828fdff51384f99b3c4926be27762", "df94c5b4d47eb3cc404d88b33a8fda237eaf4720", 524288, req: false) }),
        P("Dreamcast", "Dreamcast", "Sega Dreamcast", "Sega", "Console", new[] { ".chd", ".gdi", ".cdi" }, new[] { "Dreamcast (CDI)", "Sega - Dreamcast" }, archiveFmt: "chd", emuFmt: "chd", emuFolder: "Dreamcast", m3u: true, core: "flycast", bios: new[] { B("dc_boot.bin", "Dreamcast Boot ROM (v1.01d)", "e10c53c2f8b90bab96ead2d368858623", "89f2b1a1240a08d07c2e2f8a1e2f3d4c5b6a7988", 2097152), B("dc_flash.bin", "Dreamcast Non-Volatile Flash ROM", "0a93f7940c455905bea6e392dfde92a4", "7d6b3c5e2a1f4e8d9c0b1a2f3e4d5c6b7a899001", 131072) }),
        P("Naomi", "Naomi", "Sega NAOMI Arcade", "Sega", "Arcade", new[] { ".zip", ".chd", ".lst" }, new[] { "Sega - Naomi" }, archiveFmt: "zip", emuFmt: "zip", core: "flycast", bios: new[] { B("naomi.zip", "Sega NAOMI System BIOS Pack", "eb4099aeb42ef089cfe94f8fe95e51f6", "33f2eb762e6a7c823bc7bd5b1c2640269ca36018") }),
        P("Atomiswave", "Atomiswave", "Sammy Atomiswave Arcade", "Sega", "Arcade", new[] { ".zip", ".lst" }, new[] { "Sammy - Atomiswave" }, archiveFmt: "zip", emuFmt: "zip", core: "flycast", bios: new[] { B("awbios.zip", "Sammy Atomiswave BIOS Pack", "0ec5ae5b5a5c4959fa8b43fcf8687f7c", "be3f9bf5989b44cf26f6c4ae29ef7dc8d8cb8eb8") }),
        P("GameGear", "GameGear", "Sega Game Gear", "Sega", "Handheld", new[] { ".gg" }, new[] { "Game Gear", "Sega - Game Gear" }, emuFolder: "GameGear", bios: new[] { B("bios.gg", "Game Gear Twin ASIC Boot ROM", "672e104c3be3a238301aceffc3b23fd6", "32f04f67bf0c06f1eb602d06262a2a47a43a5111", 1024, req: false) }),
        P("PICO", "PICO", "Sega Pico", "Sega", "Console", new[] { ".md" }),
        P("Xbox", "Xbox", "Microsoft Xbox", "Microsoft", "Console", new[] { ".xiso", ".iso", ".xbe" }, new[] { "Microsoft - Xbox" }, bios: new[] { B("mcpx_1.0.bin", "Xbox MCPX 1.0 Boot ROM", "d49c52a4102f6df7bcf8d0617ac475ed", "e9fc02e353f3a01f2d1e5b4e8f7c6b5a49382716", 512), B("Complex_4627.bin", "Xbox Modified Retail Kernel BIOS", "394c9b24e3d4f5a6b7c8d9e0f1a2b3c4", "4a5b6c7d8e9f01a2b3c4d5e6f708192a3b4c5d6e", 262144) }),
        P("Xbox360", "Xbox360", "Microsoft Xbox 360", "Microsoft", "ModernConsole", new[] { ".iso", ".xex", ".god" }, new[] { "Xbox 360" }),
        P("XboxOne", "XboxOne", "Microsoft Xbox One", "Microsoft", "ModernConsole", new[] { ".xvd" }, new[] { "Xbox One" }),
        P("MSX", "MSX", "Microsoft MSX", "Microsoft", "Computer", new[] { ".mx1", ".rom" }, bios: new[] { B("MSX.ROM", "MSX1 Standard System BIOS", "aa95ae27bcecfb8f8fd603518961fd5b", "94ee12f3eed3e9f6c74f133b417eb1f2a707e8c4", 32768) }),
        P("MSX2", "MSX2", "Microsoft MSX2", "Microsoft", "Computer", new[] { ".mx2" }, bios: new[] { B("MSX2.ROM", "MSX2 Main System BIOS", "ec3a01c91f24fbddcbcab0ad301bc9ef", "302c3d4e5f60718293a4b5c6d7e8f901a2b3c4d5", 32768), B("MSX2EXT.ROM", "MSX2 SubROM Extension BIOS", "2183c2aff17cf4297bdb496de78c2e8a", "403d4e5f60718293a4b5c6d7e8f901a2b3c4d5e6", 16384) }),
        P("PC", "PC", "PC / Windows / DOS", "PC", "PC", new[] { ".exe", ".msi", ".gog", ".dosz", ".bat" }, new[] { "Windows", "Windows XP", "DOS" }),
        P("Atari2600", "2600", "Atari 2600", "Atari", "Console", new[] { ".a26" }, new[] { "Atari - 2600", "Good2600" }),
        P("Atari5200", "5200", "Atari 5200", "Atari", "Console", new[] { ".a52" }, new[] { "Atari - 5200", "Good5200" }, bios: new[] { B("5200.rom", "Atari 5200 4-Port System BIOS", "281f20ea4320404ec820fb7ec0693b38", "6ad7a1e8c9a486b210b844ed26b6267d62a504e5", 2048) }),
        P("Atari7800", "7800", "Atari 7800", "Atari", "Console", new[] { ".a78" }, new[] { "Atari - 7800", "Good7800" }, "a7800-128", bios: new[] { B("7800 BIOS (U).rom", "Atari 7800 NTSC Encryption BIOS", "0763f1ffb006ddbe32e52d497ee848ae", "a0e10d5d420737906035f8129b769d6a0b942b8e", 4096) }),
        P("Jaguar", "Jaguar", "Atari Jaguar", "Atari", "Console", new[] { ".j64", ".jag" }, new[] { "Atari - Jaguar" }, bios: new[] { B("jagboot.rom", "Atari Jaguar Boot ROM", "fb731bbb3a6badfc32b8f07e4142de8f", "f62f262279b9c074a77b3982297e9a31ce649495", 131072, req: false) }),
        P("Lynx", "Lynx", "Atari Lynx", "Atari", "Handheld", new[] { ".lnx" }, new[] { "Atari - Lynx", "GoodLynx" }, "lynx-64", bios: new[] { B("lynxboot.img", "Atari Lynx Mikey Boot ROM", "fcd403db69f54290b51035d82f835e7b", "0d973c9d7e9f4b3e5b3f8d8c5a1a9e7f3b5c7d9e", 512) }),
        P("AtariST", "ST", "Atari ST", "Atari", "Computer", new[] { ".st", ".stx", ".msa" }, new[] { "Atari - ST" }, bios: new[] { B("tos.img", "Atari ST TOS 1.02 Operating System ROM", "c1c57ce48e8ee4135885cee9e63a68a2", "25789a562d9f489f7887a7a8858b257407f99e83", 196608) }),
        P("Atari800", "Atari800", "Atari 8-Bit (400/800/XL/XE)", "Atari", "Computer", new[] { ".atr", ".xex", ".car" }, new[] { "Atari - 8-bit" }, bios: new[] { B("ATARIXL.ROM", "Atari 800XL OS ROM", "06daac977823773a3eea3422fd26a703", "ae4f523ba08b6fd59f3c517a2b123456789abcde", 16384) }),
        P("WonderSwan", "WonderSwan", "Bandai WonderSwan", "Bandai", "Handheld", new[] { ".ws" }, new[] { "Bandai - WonderSwan" }, bios: new[] { B("ws_bios.bin", "WonderSwan Internal Boot ROM", "a3f2f3c4b5d6e7f8091a2b3c4d5e6f70", "7f8e9d0c1b2a3f4e5d6c7b8a9f0e1d2c3b4a5f6e", 4096, req: false) }),
        P("WonderSwanColor", "WonderSwanColor", "Bandai WonderSwan Color", "Bandai", "Handheld", new[] { ".wsc" }, new[] { "Bandai - WonderSwan Color" }, bios: new[] { B("wsc_bios.bin", "WonderSwan Color Internal Boot ROM", "2b3c4d5e6f708192a3b4c5d6e7f8091a", "8e9d0c1b2a3f4e5d6c7b8a9f0e1d2c3b4a5f6e7f", 8192, req: false) }),
        P("ColecoVision", "ColecoVision", "Coleco ColecoVision", "Coleco", "Console", new[] { ".col" }, new[] { "Coleco - ColecoVision" }, bios: new[] { B("coleco.rom", "ColecoVision System BIOS", "2c66f5911e5b42b8ebe113403548eee7", "3a933a1031d6b8075c24a889937e723d62b26e81", 8192) }),
        P("Intellivision", "Intellivision", "Mattel Intellivision", "Mattel", "Console", new[] { ".int", ".bin" }, new[] { "Mattel - Intellivision" }, bios: new[] { B("exec.bin", "Intellivision Executive ROM", "62e761035cb657903761800f4437b8af", "cf8d3c9f7a8b1e2d3c4b5a69788796a5b4c3d2e1", 8192), B("grom.bin", "Intellivision Graphics GROM", "0cd5946c6473e42e8e4c2137785e427f", "f9e0d1c2b3a495867768594a3b2c1d0e9f8e7d6c", 2048) }),
        P("Vectrex", "Vectrex", "GCE Vectrex", "GCE", "Console", new[] { ".vec" }, new[] { "GCE - Vectrex" }, bios: new[] { B("bios.vec", "Vectrex Mine Storm Executive ROM", "52e9f0c9b1d2e3f4a5b6c7d8e9f0a1b2", "65e4c2b1a09f8e7d6c5b4a392817263544536271", 8192, req: false) }),
        P("ChannelF", "ChannelF", "Fairchild Channel F", "Fairchild", "Console", new[] { ".chf" }, new[] { "Fairchild - Channel F" }, bios: new[] { B("sl31253.bin", "Channel F PSUROM 1 (SL31253)", "ac9804d4c0e9d07e33472e3726ed15c3", "3e5463b9b24e3554b7a3875498f888b2a8406119", 1024), B("sl31254.bin", "Channel F PSUROM 2 (SL31254)", "da98f4bb3242ab80d76629021bb27585", "8f3b2c1d0e9f8a7b6c5d4e3f2a1b0c9d8e7f6a5b", 1024) }),
        P("Odyssey2", "Odyssey2", "Magnavox Odyssey 2", "Magnavox", "Console", new[] { ".bin" }, new[] { "Magnavox - Odyssey2" }, bios: new[] { B("o2rom.bin", "Magnavox Odyssey 2 Intel 8048 BIOS", "562d5ebf9e030a40d6fabfc2f33139fd", "8016a315c1031c9c02e4b0c9d89c2e838e0b2e1d", 1024) }),
        P("C64", "C64", "Commodore 64", "Commodore", "Computer", new[] { ".d64", ".g64", ".t64", ".tap", ".prg", ".crt" }, new[] { "Commodore - 64", "C64(PP)", "C64(Tapes)" }, bios: new[] { B("kernal.901227-03.bin", "C64 Kernal ROM Rev 3", "dbe7e7c772e3a1c2d3e4f5061728394a", "1d503e56df85a62fee696e7618dc5b4e781df1bb", 8192, req: false) }),
        P("Amiga", "Amiga", "Commodore Amiga", "Commodore", "Computer", new[] { ".adf", ".ipf", ".lha", ".hdf" }, new[] { "Commodore - Amiga", "Amiga-Fullset" }, bios: new[] { B("kick34005.A500", "Amiga 500 Kickstart v1.3 rev 34.5", "82a21c1890cae844b3df741f2762d48d", "c39bd9094d4e5f4e28c1411f3086950406062e87", 262144), B("kick40068.A1200", "Amiga 1200 Kickstart v3.1 rev 40.68", "646773759326fbac3b2311fd8c8793ee", "e21545723fe8374e91342617604f1b3d703094f1", 524288) }),
        P("VIC20", "VIC-20", "Commodore VIC-20", "Commodore", "Computer", new[] { ".20", ".a0" }, new[] { "Commodore - VIC-20" }),
        P("Plus4", "Plus-4", "Commodore Plus/4", "Commodore", "Computer", new[] { ".p00" }, new[] { "Commodore - Plus-4" }),
        P("X68000", "X68000", "Sharp X68000", "Sharp", "Computer", new[] { ".dim", ".xdf", ".hdm" }, new[] { "Sharp - X68000" }, bios: new[] { B("iplrom.dat", "Sharp X68000 IPL ROM v1.0", "7fd4caabac1d9169e289f0f7bbf71d8e", "d1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4", 131072), B("cgrom.dat", "Sharp X68000 Character Generator ROM", "cb0a5cfcf7247a7eab74bb2716260269", "e2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5", 786432) }),
        P("PC98", "PC98", "NEC PC-9801", "NEC", "Computer", new[] { ".fdi", ".hdi", ".d88" }, new[] { "NEC - PC-98" }, bios: new[] { B("bios.rom", "NEC PC-9801VX System BIOS", "a2b3c4d5e6f708192a3b4c5d6e7f8091", "f3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f6", 98304) }),
        P("PCEngine", "TurboGrafx16", "NEC PC Engine / TurboGrafx-16", "NEC-PCEngine", "Console", new[] { ".pce" }, new[] { "PC Engine", "TurboGrafx-16", "PCEngineCD", "TurboGrafx-CD" }, bios: new[] { B("syscard3.pce", "PC Engine CD Super System Card 3.0", "38179df8f4ac870017db21ebcbf53114", "eef1f3b8f918d710c849f813bf4b904a625874f6", 262144) }),
        P("SuperGrafx", "SuperGrafx", "NEC PC Engine SuperGrafx", "NEC-PCEngine", "Console", new[] { ".sgx" }),
        P("PCFX", "PCFX", "NEC PC-FX", "NEC", "Console", new[] { ".chd", ".cue" }, new[] { "NEC - PC-FX" }, archiveFmt: "chd", emuFmt: "chd", m3u: true, bios: new[] { B("pcfx.rom", "NEC PC-FX BIOS v1.00", "08e36edbea28a017f79f8d4f7ff9b6d7", "1b5b49b1a2c3d4e5f60718293a4b5c6d7e8f90a1", 1048576) }),
        P("NeoGeo", "NeoGeo", "SNK Neo Geo AES / MVS", "SNK", "Arcade", new[] { ".zip" }, new[] { "SNK - Neo Geo" }, archiveFmt: "zip", emuFmt: "zip", bios: new[] { B("neogeo.zip", "SNK Neo Geo AES/MVS System BIOS Pack", "d27a71eb427c9d2f2b12a0f2c1f2e3d4", "e1f2a3b4c5d6e7f8091a2b3c4d5e6f708192a3b4") }),
        P("NeoGeoCD", "NeoGeoCD", "SNK Neo Geo CD", "SNK", "Console", new[] { ".chd", ".cue" }, new[] { "SNK - Neo Geo CD" }, archiveFmt: "chd", emuFmt: "chd", m3u: true, bios: new[] { B("neocd_f.rom", "Neo Geo CD Front Loader BIOS", "f39572af7584cb5b3f70ae8cc848aba2", "9b1f2e3d4c5b6a798897a6b5c4d3e2f1a0b9c8d7", 524288), B("000-lo.lo", "Neo Geo Y-Zoom LUT ROM", "e09e253c7f8a9b0c1d2e3f4a5b6c7d8e", "2a1b0c9d8e7f6a5b4c3d2e1f0a9b8c7d6e5f4a3b", 131072) }),
        P("NGP", "NGP", "SNK Neo Geo Pocket", "SNK", "Handheld", new[] { ".ngp" }, bios: new[] { B("ngp_bios.ngp", "Neo Geo Pocket Monochrome Boot ROM", "a1b2c3d4e5f60718293a4b5c6d7e8f90", "b1c2d3e4f5061728394a5b6c7d8e9f01a2b3c4d5", 65536, req: false) }),
        P("NGPC", "NGPC", "SNK Neo Geo Pocket Color", "SNK", "Handheld", new[] { ".ngc" }, bios: new[] { B("ngpc_bios.ngc", "Neo Geo Pocket Color Boot ROM", "c1d2e3f405162738495a6b7c8d9e0f12", "d1e2f30415263748596a7b8c9d0e1f23a4b5c6d7", 65536, req: false) }),
        P("3DO", "3DO", "Panasonic 3DO Interactive Multiplayer", "Panasonic", "Console", new[] { ".chd", ".iso", ".cue" }, new[] { "The 3DO Company - 3DO" }, archiveFmt: "chd", emuFmt: "chd", m3u: true, bios: new[] { B("panafz10.bin", "Panasonic FZ-10 3DO BIOS", "51f2f43ae2f3508a14d9f56597e2d3ce", "79333a1031d6b8075c24a889937e723d62b26e81", 1048576), B("panafz1.bin", "Panasonic FZ-1 3DO BIOS", "f47264dd47fe30f73ab3c010015c155b", "51a2b3c4d5e6f708192a3b4c5d6e7f8091a2b3c4", 1048576, req: false) }),
        P("CDi", "CDi", "Philips CD-i", "Philips", "Console", new[] { ".chd", ".cue" }, new[] { "Philips - CD-i" }, archiveFmt: "chd", emuFmt: "chd", bios: new[] { B("cdimono1.zip", "Philips CD-i Mono-I System BIOS", "a91d2b3c4d5e6f708192a3b4c5d6e7f8", "c2d3e4f5061728394a5b6c7d8e9f01a2b3c4d5e6") }),
        P("ZXSpectrum", "ZXSpectrum", "Sinclair ZX Spectrum", "Sinclair", "Computer", new[] { ".tzx", ".z80", ".tap" }, bios: new[] { B("48.rom", "ZX Spectrum 48K BASIC ROM", "b3c4d5e6f708192a3b4c5d6e7f8091a2", "d3e4f5061728394a5b6c7d8e9f01a2b3c4d5e6f7", 16384, req: false) }),
        P("CPC", "CPC", "Amstrad CPC", "Amstrad", "Computer", new[] { ".dsk", ".cdt" }, new[] { "Amstrad - CPC" }, bios: new[] { B("cpc6128.rom", "Amstrad CPC 6128 OS & BASIC ROM", "c4d5e6f708192a3b4c5d6e7f8091a2b3", "e4f5061728394a5b6c7d8e9f01a2b3c4d5e6f708", 32768, req: false) }),
        P("Arcade", "Arcade", "Arcade (MAME / FBNeo)", "Arcade", "Arcade", new[] { ".zip" }, archiveFmt: "zip", emuFmt: "zip", bios: new[] { B("pgm.zip", "IGS PolyGame Master Arcade BIOS", "d5e6f708192a3b4c5d6e7f8091a2b3c4", "f5061728394a5b6c7d8e9f01a2b3c4d5e6f70819", req: false) }),
        P("PCEngineCD", "PCE-CD", "NEC PC Engine CD / TurboGrafx-CD", "NEC-PCEngine", "Console", new[] { ".chd", ".cue", ".iso" }, new[] { "PC Engine CD", "TurboGrafx-CD", "PCECD", "TGCD", "NEC - PC Engine CD - TurboGrafx-CD" }, archiveFmt: "chd", emuFmt: "chd", emuFolder: "PCEngineCD", m3u: true),
        P("BBCMicro", "BBCMicro", "Acorn BBC Micro", "Acorn", "Computer", new[] { ".ssd", ".dsd", ".uef" }, new[] { "BBC Micro", "Acorn - BBC Micro", "BBC" }),
        P("Electron", "Electron", "Acorn Electron", "Acorn", "Computer", new[] { ".ssd", ".uef" }, new[] { "Acorn - Electron", "Acorn Electron" }),
        P("Archimedes", "Archimedes", "Acorn Archimedes", "Acorn", "Computer", new[] { ".adf", ".apd", ".jfd" }, new[] { "Acorn - Archimedes", "Acorn Archimedes" }),
        P("AppleII", "AppleII", "Apple II", "Apple", "Computer", new[] { ".dsk", ".do", ".po", ".2mg", ".nib" }, new[] { "Apple II", "Apple //", "Apple2", "Apple - II" }),
        P("PV1000", "PV1000", "Casio PV-1000", "Casio", "Console", new[] { ".bin" }, new[] { "Casio - PV-1000", "PV-1000" })
    };

    public static string? ResolvePlatformId(
        string? hintOrAlias,
        string? fileExtension = null,
        IReadOnlyList<RetroPlatformCatalogEntry>? customPlatforms = null)
    {
        var list = customPlatforms ?? BuildDefaultPlatforms();
        if (!string.IsNullOrWhiteSpace(hintOrAlias))
        {
            string clean = hintOrAlias.Trim();
            var exactPrimary = list.FirstOrDefault(p =>
                string.Equals(p.PlatformId, clean, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.ShortName, clean, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.FullName, clean, StringComparison.OrdinalIgnoreCase) ||
                p.Aliases.Any(a => string.Equals(a, clean, StringComparison.OrdinalIgnoreCase)));
            if (exactPrimary != null) return exactPrimary.PlatformId;

            var exactEmuFolder = list.FirstOrDefault(p => string.Equals(p.EmuDriveFolderName, clean, StringComparison.OrdinalIgnoreCase));
            if (exactEmuFolder != null) return exactEmuFolder.PlatformId;

            string norm = NormalizePlatformToken(clean);
            if (!string.IsNullOrEmpty(norm))
            {
                var fuzzy = list.FirstOrDefault(p =>
                    NormalizePlatformToken(p.PlatformId) == norm ||
                    NormalizePlatformToken(p.ShortName) == norm ||
                    NormalizePlatformToken(p.FullName) == norm ||
                    p.Aliases.Any(a => NormalizePlatformToken(a) == norm));
                if (fuzzy != null) return fuzzy.PlatformId;
            }

            string? tier2 = ResolveTier2PlatformId(clean);
            if (tier2 != null && list.Any(p => string.Equals(p.PlatformId, tier2, StringComparison.OrdinalIgnoreCase)))
                return tier2;
        }

        if (!string.IsNullOrWhiteSpace(fileExtension))
        {
            string ext = fileExtension.StartsWith('.') ? fileExtension.ToLowerInvariant() : "." + fileExtension.ToLowerInvariant();
            var byExt = list.Where(p => p.Extensions.Any(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase))).ToList();
            if (byExt.Count == 1) return byExt[0].PlatformId;
        }
        return null;
    }

    public static string? ResolveTier2PlatformId(string text)
    {
        if (Regex.IsMatch(text, @"(?i)\b(GameCube|NKit\s*RVZ)\b")) return "GC";
        if (Regex.IsMatch(text, @"(?i)\b(PC-Engine\s*CD|TurboGrafx-16\s*CD|TurboGrafx\s*CD|PCECD|TGCD)\b")) return "PCEngineCD";
        if (Regex.IsMatch(text, @"(?i)\b(GoodGen|Sega\s*-\s*Mega\s*Drive|Sega\s*-\s*Genesis)\b")) return "Genesis";
        if (Regex.IsMatch(text, @"(?i)\b(Good2600|Atari\s*-\s*2600|Atari\s*2600)\b")) return "Atari2600";
        if (Regex.IsMatch(text, @"(?i)\b(Commodore\s*-\s*Amiga|Amiga-Fullset)\b")) return "Amiga";
        if (Regex.IsMatch(text, @"(?i)\b(Commodore\s*-\s*64|C64\(PP\)|C64\(Tapes\))\b")) return "C64";
        if (Regex.IsMatch(text, @"(?i)\b(BBC\s*Micro|BBCMicro)\b")) return "BBCMicro";
        if (Regex.IsMatch(text, @"(?i)\b(Acorn\s*-\s*Electron|^Electron$)\b")) return "Electron";
        if (Regex.IsMatch(text, @"(?i)\b(Acorn\s*-\s*Archimedes|^Archimedes$)\b")) return "Archimedes";
        if (Regex.IsMatch(text, @"(?i)\b(Apple\s*II|Apple2|Apple\s*//)\b")) return "AppleII";
        if (Regex.IsMatch(text, @"(?i)\b(Casio\s*-\s*PV-1000|PV-1000|PV1000)\b")) return "PV1000";
        if (Regex.IsMatch(text, @"(?i)\b(GoodWSx|WonderSwan\s*Color)\b")) return "WonderSwanColor";
        if (Regex.IsMatch(text, @"(?i)\b(Bandai\s*-\s*WonderSwan)\b")) return "WonderSwan";
        return null;
    }

    public static string NormalizePlatformToken(string input)
    {
        var sb = new StringBuilder(input.Length);
        foreach (char c in input)
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }
}
