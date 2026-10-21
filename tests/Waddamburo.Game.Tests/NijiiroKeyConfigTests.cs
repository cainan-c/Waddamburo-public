using System.Security.Cryptography;
using Waddamburo.Game.Flow;

namespace Waddamburo.Game.Tests;

public sealed class NijiiroKeyConfigTests
{
    [Fact]
    public void UpgradesAndMenuSavesPreserveUserKeysWithoutDiagnosticDisclosure()
    {
        string tables = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        string charts = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var original = $"config_version = 21\nnijiiro_datatable_key = {tables}\nnijiiro_fumen_key = {charts}\n";
        var upgraded = ArcadeSettings.Upgrade(original);
        var settings = ArcadeSettings.Parse(upgraded);
        Assert.Equal(Convert.FromHexString(tables), settings.NijiiroDatatableKey!.ToArray());
        Assert.Equal(Convert.FromHexString(charts), settings.NijiiroFumenKey!.ToArray());
        Assert.DoesNotContain(tables, settings.ToString());
        Assert.DoesNotContain(charts, settings.ToString());
        Assert.Single(upgraded.Split('\n').Where(s => s.StartsWith("nijiiro_datatable_key =")));
        var dir = Directory.CreateTempSubdirectory("nijiiro-key-settings-").FullName;
        try
        {
            var path = Path.Combine(dir, "config.cfg"); File.WriteAllText(path, upgraded);
            ArcadeSettings.SaveMenuSettings(path, settings with { MusicVolume = 37 });
            var loaded = ArcadeSettings.LoadOrCreate(path);
            Assert.Equal(settings.NijiiroDatatableKey, loaded.NijiiroDatatableKey);
            Assert.Equal(settings.NijiiroFumenKey, loaded.NijiiroFumenKey);
            Assert.Equal(37, loaded.MusicVolume);
        }
        finally { Directory.Delete(dir, true); }
        Assert.Null(ArcadeSettings.Parse(ArcadeSettings.DefaultFileText).NijiiroDatatableKey);
        Assert.Null(ArcadeSettings.Parse(ArcadeSettings.DefaultFileText).NijiiroFumenKey);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ")]
    public void MalformedKeysAreRejectedWithoutEchoingTheirValue(string value)
    {
        var error = Assert.Throws<InvalidDataException>(() => ArcadeSettings.Parse("nijiiro_datatable_key = " + value));
        Assert.DoesNotContain(value, error.ToString());
    }
}
