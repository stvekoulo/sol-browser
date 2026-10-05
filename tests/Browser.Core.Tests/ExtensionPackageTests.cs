using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Browser.Core;

public class ExtensionPackageTests
{
    [Fact]
    public void Signed_store_package_preserves_identity_and_rejects_tampering_and_unsafe_paths()
    {
        using var key = RSA.Create(2048);
        var publicKey = key.ExportSubjectPublicKeyInfo();
        var digest = SHA256.HashData(publicKey)[..16];
        var id = string.Concat(digest.SelectMany(b => new[] { (char)('a' + (b >> 4)), (char)('a' + (b & 15)) }));
        var directory = Path.Combine(Path.GetTempPath(), "sol-extension-" + Guid.NewGuid());
        byte[] Field(int tag, byte[] data)
        {
            using var result = new MemoryStream();
            void Varint(uint value) { while (value >= 128) { result.WriteByte((byte)(value | 128)); value >>= 7; } result.WriteByte((byte)value); }
            Varint((uint)((tag << 3) | 2)); Varint((uint)data.Length); result.Write(data);
            return result.ToArray();
        }
        byte[] Package(string name)
        {
            using var archive = new MemoryStream();
            using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, true))
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write("{\"manifest_version\":3,\"name\":\"Test\",\"version\":\"1.0\"}");
            }
            var signed = Field(1, digest);
            var payload = "CRX3 SignedData\0"u8.ToArray().Concat(BitConverter.GetBytes(signed.Length)).Concat(signed).Concat(archive.ToArray()).ToArray();
            var signature = key.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var proof = Field(1, publicKey).Concat(Field(2, signature)).ToArray();
            var header = Field(2, proof).Concat(Field(10000, signed)).ToArray();
            return "Cr24"u8.ToArray().Concat(BitConverter.GetBytes(3)).Concat(BitConverter.GetBytes(header.Length)).Concat(header).Concat(archive.ToArray()).ToArray();
        }
        try
        {
            var package = Package("manifest.json");
            ExtensionPackage.Extract(package, id, directory);
            using var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json")));
            Assert.Equal(Convert.ToBase64String(publicKey), manifest.RootElement.GetProperty("key").GetString());
            Assert.Equal(id, ExtensionPackage.StoreId($"https://chromewebstore.google.com/detail/test/{id}?hl=fr"));
            Assert.Throws<ArgumentException>(() => ExtensionPackage.StoreId($"https://chromewebstore.google.com.evil.test/detail/{id}"));
            package[^1] ^= 1;
            Assert.Throws<InvalidDataException>(() => ExtensionPackage.Extract(package, id, directory + "-bad"));
            Assert.Throws<InvalidDataException>(() => ExtensionPackage.Extract(Package("../outside.json"), id, directory + "-bad"));
            Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(directory)!, "outside.json")));
            ExtensionCatalog.CopyTo(directory, directory + "-copy");
            Assert.Equal(File.ReadAllText(Path.Combine(directory, "manifest.json")), File.ReadAllText(Path.Combine(directory + "-copy", "manifest.json")));
            Assert.Throws<ArgumentException>(() => ExtensionCatalog.CopyTo(directory, Path.Combine(directory, "nested")));
        }
        finally
        {
            foreach (var folder in new[] { directory, directory + "-copy", directory + "-bad" })
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }
}
