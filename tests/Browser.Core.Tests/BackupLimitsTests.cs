using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using Browser.Core;

public class BackupLimitsTests
{
    [Fact]
    public void An_entry_declaring_excessive_expansion_is_rejected()
    {
        Assert.Null(Backup.Open(Archive(1, 256 * 1024 * 1024 + 1), "test key"));
    }

    [Fact]
    public void Excessive_combined_expansion_is_rejected()
    {
        Assert.Null(Backup.Open(Archive(5, 110 * 1024 * 1024), "test key"));
    }

    [Fact]
    public void Excessive_entry_count_is_rejected_before_zip_metadata_allocation()
    {
        Assert.Null(Backup.Open(Archive(10001, 0), "test key"));
    }

    [Fact]
    public void Seal_refuses_combined_content_that_cannot_be_restored()
    {
        // Le même tampon est réutilisé : le test ne consomme pas 513 Mo pour vérifier ce plafond.
        var bytes = new byte[1024 * 1024];
        var files = Enumerable.Range(0, 513).ToDictionary(n => $"extensions/{n}.bin", _ => bytes);
        Assert.Throws<InvalidDataException>(() => Backup.Seal(files, "test key"));
    }

    // Archive authentifiée dont seul le catalogue ZIP est forgé : aucune allocation massive nécessaire.
    private static byte[] Archive(int count, uint declaredLength)
    {
        using var plain = new MemoryStream();
        using (var zip = new ZipArchive(plain, ZipArchiveMode.Create, true))
            for (var n = 0; n < count; n++)
            {
                using var entry = zip.CreateEntry($"extensions/{n}.js").Open();
            }
        var zipBytes = plain.ToArray();
        for (var n = 0; n < zipBytes.Length - 28; n++)
            if (BinaryPrimitives.ReadUInt32LittleEndian(zipBytes.AsSpan(n, 4)) == 0x02014b50)
                BinaryPrimitives.WriteUInt32LittleEndian(zipBytes.AsSpan(n + 24, 4), declaredLength);
        var result = new byte[54 + zipBytes.Length];
        "SOLBACKUP1"u8.CopyTo(result);
        RandomNumberGenerator.Fill(result.AsSpan(10, 28));
        var key = Rfc2898DeriveBytes.Pbkdf2("test key"u8, result.AsSpan(10, 16), 600_000, HashAlgorithmName.SHA256, 32);
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(result.AsSpan(26, 12), zipBytes, result.AsSpan(54), result.AsSpan(38, 16), result.AsSpan(0, 10));
        return result;
    }
}
