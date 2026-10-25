using System.IO.Compression;
using System.Security.Cryptography;
using Browser.Core;

public sealed class LargeExtensionPackageTests
{
    [Fact]
    public void Signed_package_above_32_MiB_and_extraction_above_128_MiB_are_accepted()
    {
        var root = Path.Combine(Path.GetTempPath(), "sol-large-extension-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var (file, id) = CreatePackage(root);
            Assert.True(new FileInfo(file).Length > 32L * 1024 * 1024);
            var destination = Path.Combine(root, "unpacked");
            using var input = File.OpenRead(file);
            ExtensionPackage.Extract(input, id, destination);
            Assert.True(input.CanRead, "The caller owns its stream.");
            Assert.Equal(33L * 1024 * 1024, new FileInfo(Path.Combine(destination, "random.bin")).Length);
            Assert.Equal(129L * 1024 * 1024, new FileInfo(Path.Combine(destination, "large-model.bin")).Length);
            Assert.NotNull(ExtensionCatalog.Read(destination, id));
        }
        finally { Directory.Delete(root, true); }
    }

    private static (string Path, string Id) CreatePackage(string root)
    {
        var archive = Path.Combine(root, "archive.zip");
        using (var stream = File.Create(archive))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open()))
                writer.Write("""{"manifest_version":3,"name":"Large extension regression","version":"1.0"}""");
            // Independent random blocks prevent compression from hiding the old download ceiling.
            using (var random = zip.CreateEntry("random.bin", CompressionLevel.NoCompression).Open())
            {
                var block = new byte[1024 * 1024];
                for (var i = 0; i < 33; i++) { RandomNumberGenerator.Fill(block); random.Write(block); }
            }
            using var model = zip.CreateEntry("large-model.bin", CompressionLevel.Fastest).Open();
            var zeros = new byte[1024 * 1024];
            for (var i = 0; i < 129; i++) model.Write(zeros);
        }
        using var key = RSA.Create(2048);
        var publicKey = key.ExportSubjectPublicKeyInfo();
        var digest = SHA256.HashData(publicKey)[..16];
        var id = string.Concat(digest.SelectMany(b => new[] { (char)('a' + (b >> 4)), (char)('a' + (b & 15)) }));
        var signed = Field(1, digest);
        using var sha = SHA256.Create();
        using (var hashing = new CryptoStream(Stream.Null, sha, CryptoStreamMode.Write, leaveOpen: true))
        {
            hashing.Write("CRX3 SignedData\0"u8);
            hashing.Write(BitConverter.GetBytes(signed.Length));
            hashing.Write(signed);
            using var input = File.OpenRead(archive);
            input.CopyTo(hashing);
            hashing.FlushFinalBlock();
        }
        var proof = Field(1, publicKey).Concat(Field(2, key.SignHash(sha.Hash!, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))).ToArray();
        var header = Field(2, proof).Concat(Field(10000, signed)).ToArray();
        var path = Path.Combine(root, "large.crx");
        using var package = File.Create(path);
        package.Write("Cr24"u8);
        package.Write(BitConverter.GetBytes(3));
        package.Write(BitConverter.GetBytes(header.Length));
        package.Write(header);
        using var body = File.OpenRead(archive);
        body.CopyTo(package);
        return (path, id);
    }

    private static byte[] Field(int tag, byte[] data)
    {
        using var result = new MemoryStream();
        void Varint(uint value) { while (value >= 128) { result.WriteByte((byte)(value | 128)); value >>= 7; } result.WriteByte((byte)value); }
        Varint((uint)((tag << 3) | 2)); Varint((uint)data.Length); result.Write(data);
        return result.ToArray();
    }
}
