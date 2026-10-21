using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using Waddamburo.Catalog;

namespace Waddamburo.Providers.Stock.Tests;

public sealed class NijiiroConfiguredKeyTests
{
    [Fact]
    public async Task DistinctConfiguredKeysLoadTablesAndChartWithoutLoader()
    {
        var install = Directory.CreateTempSubdirectory("nijiiro-configured-").FullName;
        try
        {
            var data = Path.Combine(install, "Data", "x64");
            Directory.CreateDirectory(Path.Combine(data, "datatable"));
            Directory.CreateDirectory(Path.Combine(data, "fumen", "test"));
            Directory.CreateDirectory(Path.Combine(data, "sound"));
            var tables = RandomNumberGenerator.GetBytes(32);
            var charts = RandomNumberGenerator.GetBytes(32);
            byte[] Pack(byte[] key, byte[] plain)
            {
                using var stream = new MemoryStream();
                using (var zip = new GZipStream(stream, CompressionLevel.Optimal, true)) zip.Write(plain);
                using var aes = Aes.Create(); aes.Key = key;
                var iv = RandomNumberGenerator.GetBytes(16);
                return [.. iv, .. aes.EncryptCbc(stream.ToArray(), iv, PaddingMode.PKCS7)];
            }
            File.WriteAllBytes(Path.Combine(data, "datatable", "musicinfo.bin"), Pack(tables,
                """{"items":[{"id":"test","genreNo":0,"starEasy":1}]}"""u8.ToArray()));
            File.WriteAllBytes(Path.Combine(data, "datatable", "wordlist.bin"), Pack(tables,
                """{"items":[{"key":"song_test","englishUsText":"Synthetic"}]}"""u8.ToArray()));
            var fumen = new byte[608];
            BinaryPrimitives.WriteInt32LittleEndian(fumen.AsSpan(512), 1);
            BinaryPrimitives.WriteSingleLittleEndian(fumen.AsSpan(520), 120);
            BinaryPrimitives.WriteSingleLittleEndian(fumen.AsSpan(524), -2000);
            BinaryPrimitives.WriteUInt16LittleEndian(fumen.AsSpan(560), 1);
            foreach (int at in new[] { 564, 596, 604 }) BinaryPrimitives.WriteSingleLittleEndian(fumen.AsSpan(at), 1);
            BinaryPrimitives.WriteInt32LittleEndian(fumen.AsSpan(568), 1);
            File.WriteAllBytes(Path.Combine(data, "fumen", "test", "test_e.bin"), Pack(charts, fumen));
            File.WriteAllBytes(Path.Combine(data, "sound", "song_test.nus3bank"), []);

            var tableKey = NijiiroDecryptionKey.Parse(Convert.ToHexString(tables));
            var chartKey = NijiiroDecryptionKey.Parse(Convert.ToHexString(charts));
            var provider = new NijiiroCatalogProvider(install, datatableKey: tableKey, fumenKey: chartKey);
            Assert.Null(provider.Problem);
            var result = await provider.ScanAsync(null, default);
            var descriptor = Assert.Single(Assert.Single(result.Songs).Charts);
            var chart = await provider.LoadChartAsync(descriptor.Key, descriptor.ChartAsset);
            Assert.Equal(1, chart.NoteCount);
            Assert.False(File.Exists(Path.Combine(install, "Executable", "Release", "bnusio.dll")));
            var wrong = new NijiiroCatalogProvider(install, datatableKey: tableKey, fumenKey: tableKey);
            var error = await Assert.ThrowsAsync<InvalidDataException>(() => wrong.LoadChartAsync(descriptor.Key, descriptor.ChartAsset).AsTask());
            Assert.Contains("nijiiro_fumen_key", error.Message);
            Assert.DoesNotContain(Convert.ToHexString(tables), error.ToString());
        }
        finally { Directory.Delete(install, true); }
    }
}
