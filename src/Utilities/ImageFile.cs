// class to normalize image file path/url/base64, basically any image source that is allowed in markdown.
// if it is something Spectre.Console.SixelImage(string filename, bool animations) cannot handle we need to fix that, like downloading to a temporary file or converting the base64 to a file..

using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PSTextMate.Utilities;

/// <summary>
/// Normalizes various image sources (file paths, URLs, base64) into file paths that can be used by Spectre.Console.SixelImage.
/// </summary>
internal static partial class ImageFile {
    private static readonly HttpClient HttpClient = new();

    [GeneratedRegex(@"^data:image\/(?<type>[a-zA-Z]+);base64,(?<data>[A-Za-z0-9+/=]+)$", RegexOptions.Compiled)]
    private static partial Regex Base64Regex();

    /// <summary>
    /// Normalizes an image source to a local file path that can be used by SixelImage.
    /// </summary>
    /// <param name="imageSource">The image source (file path, URL, or base64 data URI)</param>
    /// <param name="baseDirectory">Optional base directory for resolving relative paths (defaults to current directory)</param>
    /// <returns>A local file path, or null if the image cannot be processed</returns>
    public static async Task<string?> NormalizeImageSourceAsync(string imageSource, string? baseDirectory = null) {
        if (string.IsNullOrWhiteSpace(imageSource)) {
            return null;
        }

        // Check if it's a base64 data URI
        Match base64Match = Base64Regex().Match(imageSource);
        if (base64Match.Success) {
            return await ConvertBase64ToFileAsync(base64Match.Groups["type"].Value, base64Match.Groups["data"].Value);
        }

        // Check if it's a URL
        if (Uri.TryCreate(imageSource, UriKind.Absolute, out Uri? uri) &&
            (uri.Scheme == "http" || uri.Scheme == "https")) {
            return await DownloadImageToTempFileAsync(uri);
        }

        // Check if it's a local file path
        if (File.Exists(imageSource)) {
            return imageSource;
        }

        // Try to resolve relative paths
        // Use provided baseDirectory or fall back to current directory
        string resolveBasePath = baseDirectory ?? Environment.CurrentDirectory;
        string fullPath = Path.GetFullPath(Path.Combine(resolveBasePath, imageSource));

        // Debug: For troubleshooting, we can add logging here if needed
        // System.Diagnostics.Debug.WriteLine($"Resolving '{imageSource}' with base '{resolveBasePath}' -> '{fullPath}' (exists: {File.Exists(fullPath)})");

        return File.Exists(fullPath) ? fullPath : null;
    }

    /// <summary>
    /// Converts a base64 encoded image to a temporary file.
    /// </summary>
    /// <param name="imageType">The image type (e.g., "png", "jpg")</param>
    /// <param name="base64Data">The base64 encoded image data</param>
    /// <returns>Path to the temporary file, or null if conversion fails</returns>
    private static async Task<string?> ConvertBase64ToFileAsync(string imageType, string base64Data) {
        try {
            byte[] imageBytes = Convert.FromBase64String(base64Data);
            string tempFileName = Path.Combine(Path.GetTempPath(), $"pstextmate_img_{Guid.NewGuid():N}.{imageType}");

            await File.WriteAllBytesAsync(tempFileName, imageBytes);

            // Schedule cleanup after a reasonable time (1 hour)
            _ = Task.Delay(TimeSpan.FromHours(1)).ContinueWith(_ => {
                try {
                    if (File.Exists(tempFileName)) {
                        File.Delete(tempFileName);
                    }
                }
                catch {
                    // Ignore cleanup errors
                }
            });

            return tempFileName;
        }
        catch {
            return null;
        }
    }

    /// <summary>
    /// Downloads an image from a URL to a temporary file.
    /// </summary>
    /// <param name="imageUri">The image URL</param>
    /// <returns>Path to the temporary file, or null if download fails</returns>
    private static async Task<string?> DownloadImageToTempFileAsync(Uri imageUri) {
        try {
            using HttpResponseMessage response = await HttpClient.GetAsync(imageUri);
            if (!response.IsSuccessStatusCode) {
                return null;
            }

            string? contentType = response.Content.Headers.ContentType?.MediaType;
            string extension = GetExtensionFromContentType(contentType) ??
                            Path.GetExtension(imageUri.LocalPath) ??
                            ".img";

            string tempFileName = Path.Combine(Path.GetTempPath(), $"pstextmate_img_{Guid.NewGuid():N}{extension}");

            using FileStream fileStream = File.Create(tempFileName);
            await response.Content.CopyToAsync(fileStream);

            // Schedule cleanup after a reasonable time (1 hour)
            _ = Task.Delay(TimeSpan.FromHours(1)).ContinueWith(_ => {
                try {
                    if (File.Exists(tempFileName)) {
                        File.Delete(tempFileName);
                    }
                }
                catch {
                    // Ignore cleanup errors
                }
            });

            return tempFileName;
        }
        catch {
            return null;
        }
    }

    /// <summary>
    /// Gets the file extension based on the content type.
    /// </summary>
    /// <param name="contentType">The MIME content type</param>
    /// <returns>The appropriate file extension</returns>
    private static string? GetExtensionFromContentType(string? contentType) {
        return contentType?.ToLowerInvariant() switch {
            "image/jpeg" => ".jpg",
            "image/jpg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/bmp" => ".bmp",
            "image/webp" => ".webp",
            "image/svg+xml" => ".svg",
            "image/tiff" => ".tif",
            _ => null
        };
    }

    /// <summary>
    /// Checks if the image source is likely to be supported by SixelImage.
    /// </summary>
    /// <param name="imageSource">The image source to check</param>
    /// <returns>True if the image source is likely supported</returns>
    public static bool IsLikelySupportedImageFormat(string imageSource) {
        if (string.IsNullOrWhiteSpace(imageSource)) {
            return false;
        }

        // Check for supported extensions
        string extension = Path.GetExtension(imageSource).ToLowerInvariant();
        string[] supportedExtensions = [".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp"];

        if (supportedExtensions.Contains(extension)) {
            return true;
        }

        // Check for base64 data URI with supported format
        Match base64Match = Base64Regex().Match(imageSource);
        if (base64Match.Success) {
            string imageType = base64Match.Groups["type"].Value.ToLowerInvariant();
            string[] supportedTypes = ["jpg", "jpeg", "png", "gif", "bmp", "webp"];
            return supportedTypes.Contains(imageType);
        }

        // For URLs, check the extension in the URL path
        if (Uri.TryCreate(imageSource, UriKind.Absolute, out Uri? uri) &&
            (uri.Scheme == "http" || uri.Scheme == "https")) {
            string urlExtension = Path.GetExtension(uri.LocalPath).ToLowerInvariant();
            return supportedExtensions.Contains(urlExtension);
        }

        return false;
    }
}
