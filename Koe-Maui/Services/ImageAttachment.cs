namespace Koe.Services;

public sealed class ImageAttachment
{
    public const int MaxSourceBytes = 20 * 1024 * 1024;
    public const int MaxImageBytes = 8 * 1024 * 1024;
    public string Name { get; }
    public byte[] Bytes { get; }
    public string MediaType { get; }
    public string DataUrl => $"data:{MediaType};base64,{Convert.ToBase64String(Bytes)}";

    public ImageAttachment(string name, byte[] bytes)
    {
        if (bytes.Length == 0) throw new InvalidOperationException("Die Bilddatei ist leer.");
        if (bytes.Length > MaxImageBytes) throw new InvalidOperationException("Das Bild ist zu groß. Bitte ein kleineres Foto auswählen.");
        MediaType = DetectMediaType(bytes);
        Name = name;
        Bytes = bytes;
    }

    public static async Task<byte[]> ReadSourceAsync(Stream source, CancellationToken ct = default)
    {
        using var destination = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            if (destination.Length + read > MaxSourceBytes) throw new InvalidOperationException("Die Datei ist größer als 20 MB. Bitte ein kleineres Foto auswählen.");
            destination.Write(buffer, 0, read);
        }
        if (destination.Length == 0) throw new InvalidOperationException("Die Bilddatei ist leer.");
        return destination.ToArray();
    }

    static string DetectMediaType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return "image/png";
        if (bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255) return "image/jpeg";
        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8)) return "image/webp";
        throw new InvalidOperationException("Dieses Bildformat wird nicht unterstützt. Bitte ein JPG-, PNG- oder WebP-Bild auswählen.");
    }
}
