namespace Koe.Services;

public static class AttachmentLoader
{
    public static async Task<ImageAttachment> LoadAsync(FileResult file, CancellationToken ct)
    {
        await using var stream = await file.OpenReadAsync();
        var bytes = await ImageAttachment.ReadSourceAsync(stream, ct);
#if IOS
        // UIKit decodes HEIC, fixes orientation, limits image size and strips camera metadata.
        bytes = await MainThread.InvokeOnMainThreadAsync(() => NormalizeForUpload(bytes));
#endif
        ct.ThrowIfCancellationRequested();
        return new ImageAttachment(file.FileName, bytes);
    }

#if IOS
    static byte[] NormalizeForUpload(byte[] bytes)
    {
        using var data = Foundation.NSData.FromArray(bytes);
        using var image = UIKit.UIImage.LoadFromData(data) ?? throw new InvalidOperationException("Das Bild konnte nicht geöffnet werden.");
        var width = (double)image.Size.Width;
        var height = (double)image.Size.Height;
        if (width <= 0 || height <= 0) throw new InvalidOperationException("Das Bild hat keine gültige Größe.");
        var factor = Math.Min(1, 2560 / Math.Max(width, height));
        var size = new CoreGraphics.CGSize(Math.Max(1, Math.Round(width * factor)), Math.Max(1, Math.Round(height * factor)));
        using var format = new UIKit.UIGraphicsImageRendererFormat { Scale = 1, Opaque = true };
        using var renderer = new UIKit.UIGraphicsImageRenderer(size, format);
        using var normalized = renderer.CreateImage(context =>
        {
            var bounds = new CoreGraphics.CGRect(CoreGraphics.CGPoint.Empty, size);
            context.CGContext.SetFillColor(UIKit.UIColor.White.CGColor);
            context.CGContext.FillRect(bounds);
            image.Draw(bounds);
        });
        using var jpeg = normalized.AsJPEG(0.85f) ?? throw new InvalidOperationException("Das Foto konnte nicht vorbereitet werden.");
        return jpeg.ToArray();
    }
#endif
}
