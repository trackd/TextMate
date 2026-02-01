using System.Reflection;
using PSTextMate.Utilities;
using Spectre.Console;
using Spectre.Console.Rendering;

#pragma warning disable CS0103 // The name 'SixelImage' does not exist in the current context

namespace PSTextMate.Rendering;

/// <summary>
/// Handles rendering of images in markdown using Sixel format when possible.
/// </summary>
public static class ImageRenderer {
    private static string? _lastSixelError;
    private static string? _lastImageError;
    private static readonly TimeSpan ImageTimeout = TimeSpan.FromSeconds(5); // Increased to 5 seconds

    /// <summary>
    /// The base directory for resolving relative image paths in markdown.
    /// Set this before rendering markdown content to enable relative path resolution.
    /// </summary>
    public static string? CurrentMarkdownDirectory { get; set; }

    /// <summary>
    /// Renders an image using Sixel format if possible, otherwise falls back to a link.
    /// </summary>
    /// <param name="altText">Alternative text for the image</param>
    /// <param name="imageUrl">URL or path to the image</param>
    /// <param name="maxWidth">Maximum width for the image (optional)</param>
    /// <param name="maxHeight">Maximum height for the image (optional)</param>
    /// <returns>A renderable representing the image or fallback</returns>
    public static IRenderable RenderImage(string altText, string imageUrl, int? maxWidth = null, int? maxHeight = null) {
        try {
            // Clear previous errors
            _lastImageError = null;
            _lastSixelError = null;

            // Check if the image format is likely supported
            if (!ImageFile.IsLikelySupportedImageFormat(imageUrl)) {
                _lastImageError = $"Unsupported image format: {imageUrl}";
                return CreateImageFallback(altText, imageUrl);
            }

            // Use a timeout for image processing
            string? localImagePath = null;
            Task<string?> imageTask = Task.Run(async () => {
                string? result = await ImageFile.NormalizeImageSourceAsync(imageUrl, CurrentMarkdownDirectory);
                // Track what paths we're trying to resolve for error reporting
                if (result is null && CurrentMarkdownDirectory is not null) {
                    _lastImageError = $"Failed to resolve '{imageUrl}' with base directory '{CurrentMarkdownDirectory}'";
                }
                return result;
            });

            if (imageTask.Wait(ImageTimeout)) {
                localImagePath = imageTask.Result;
            }
            else {
                // Timeout occurred
                _lastImageError = $"Image download timeout after {ImageTimeout.TotalSeconds} seconds: {imageUrl}";
                return CreateImageFallback(altText, imageUrl);
            }

            if (localImagePath is null) {
                _lastImageError = $"Failed to normalize image source: {imageUrl}";
                return CreateImageFallback(altText, imageUrl);
            }

            // Verify the downloaded file exists and has content
            if (!File.Exists(localImagePath)) {
                _lastImageError = $"Downloaded image file does not exist: {localImagePath}";
                return CreateImageFallback(altText, imageUrl);
            }

            var fileInfo = new FileInfo(localImagePath);
            if (fileInfo.Length == 0) {
                _lastImageError = $"Downloaded image file is empty: {localImagePath} (0 bytes)";
                return CreateImageFallback(altText, imageUrl);
            }

            // Set reasonable defaults for markdown display
            int defaultMaxWidth = maxWidth ?? 80;  // Default to ~80 characters wide for terminal display
            int defaultMaxHeight = maxHeight ?? 30; // Default to ~30 lines high

            if (TryCreateSixelImage(localImagePath, defaultMaxWidth, defaultMaxHeight, out IRenderable? sixelImage) && sixelImage is not null) {
                // Return the sixel image directly. The caller may append an explicit Text.NewLine
                // so it renders as a separate row (avoids embedding the blank row inside the same widget).
                return sixelImage;
            }
            else {
                // Fallback to enhanced link representation with file info
                _lastImageError = $"SixelImage creation failed. File: {localImagePath} ({fileInfo.Length} bytes). Sixel error: {_lastSixelError}";
                return CreateEnhancedImageFallback(altText, imageUrl, localImagePath);
            }
        }
        catch (Exception ex) {
            // If anything goes wrong, fall back to the basic link representation
            _lastImageError = $"Exception in RenderImage: {ex.Message}";
            return CreateImageFallback(altText, imageUrl);
        }
    }

    /// <summary>
    /// Renders an image inline (without panel) using Sixel format if possible.
    /// </summary>
    /// <param name="altText">Alternative text for the image</param>
    /// <param name="imageUrl">URL or path to the image</param>
    /// <param name="maxWidth">Maximum width for the image (optional)</param>
    /// <param name="maxHeight">Maximum height for the image (optional)</param>
    /// <returns>A renderable representing the image or fallback</returns>
    public static IRenderable RenderImageInline(string altText, string imageUrl, int? maxWidth = null, int? maxHeight = null) {
        try {
            // Check if the image format is likely supported
            if (!ImageFile.IsLikelySupportedImageFormat(imageUrl)) {
                return CreateImageFallbackInline(altText, imageUrl);
            }

            // Use a timeout for image processing
            string? localImagePath = null;
            Task<string?>? imageTask = Task.Run(async () => await ImageFile.NormalizeImageSourceAsync(imageUrl, CurrentMarkdownDirectory));

            if (imageTask.Wait(ImageTimeout)) {
                localImagePath = imageTask.Result;
            }
            else {
                // Timeout occurred
                return CreateImageFallbackInline(altText, imageUrl);
            }

            if (localImagePath is null) {
                return CreateImageFallbackInline(altText, imageUrl);
            }

            // Smaller defaults for inline images
            int width = maxWidth ?? 60;  // Default max width for inline images
            int height = maxHeight ?? 20; // Default max height for inline images

            if (TryCreateSixelImage(localImagePath, width, height, out IRenderable? sixelImage) && sixelImage is not null) {
                return sixelImage;
            }
            else {
                // Fallback to inline link representation
                return CreateImageFallbackInline(altText, imageUrl);
            }
        }
        catch {
            // If anything goes wrong, fall back to the link representation
            return CreateImageFallbackInline(altText, imageUrl);
        }
    }

    /// <summary>
    /// Attempts to create a SixelImage using reflection for forward compatibility.
    /// </summary>
    /// <param name="imagePath">Path to the image file</param>
    /// <param name="maxWidth">Maximum width</param>
    /// <param name="maxHeight">Maximum height</param>
    /// <param name="result">The created SixelImage, if successful</param>
    /// <returns>True if SixelImage was successfully created</returns>
    private static bool TryCreateSixelImage(string imagePath, int? maxWidth, int? maxHeight, out IRenderable? result) {
        result = null;

        try {
            // Try multiple approaches to find SixelImage

            // First, try the direct approach - SixelImage is in Spectre.Console namespace
            // but might be in different assemblies (Spectre.Console vs Spectre.Console.ImageSharp)
            Type? sixelImageType = Type.GetType("Spectre.Console.SixelImage, Spectre.Console.ImageSharp")
                            ?? Type.GetType("Spectre.Console.SixelImage, Spectre.Console") ?? Type.GetType("PwshSpectreConsole.PixelImage, PwshSpectreConsole");

            // If that fails, search through loaded assemblies
            if (sixelImageType is null) {
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) {
                    string? assemblyName = assembly.GetName().Name;
                    if (assemblyName?.Contains("Spectre.Console") == true) {
                        // SixelImage is in Spectre.Console namespace regardless of assembly
                        sixelImageType = assembly.GetType("Spectre.Console.SixelImage");
                        if (sixelImageType is not null) {
                            break;
                        }
                    }
                }
            }

            if (sixelImageType is null) {
                // Debug: Let's see what Spectre.Console types are available
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) {
                    if (assembly.GetName().Name?.Contains("Spectre.Console") == true) {
                        string?[]? spectreTypes = [.. assembly.GetTypes()
                            .Where(t => t.Name.Contains("Sixel", StringComparison.OrdinalIgnoreCase))
                            .Select(t => t.FullName)
                            .Where(name => name is not null)];

                        if (spectreTypes.Length > 0) {
                            // Found some Sixel-related types, try the first one
                            sixelImageType = assembly.GetType(spectreTypes[0]!);
                            break;
                        }
                    }
                }
            }

            if (sixelImageType is null) {
                return false;
            }

            // Create SixelImage instance
            ConstructorInfo? constructor = sixelImageType.GetConstructor([typeof(string), typeof(bool)]);
            if (constructor is null) {
                _lastSixelError = $"Constructor not found for SixelImage with (string, bool) parameters";
                return false;
            }

            object? sixelInstance = null;
            try {
                sixelInstance = constructor.Invoke([imagePath, false]); // false = animation disabled
            }
            catch (Exception ex) {
                _lastSixelError = $"Failed to invoke SixelImage constructor: {ex.InnerException?.Message ?? ex.Message}";
                return false;
            }

            if (sixelInstance is null) {
                _lastSixelError = $"SixelImage constructor returned null";
                return false;
            }

            // Apply size constraints if available
            if (maxWidth.HasValue) {
                PropertyInfo? maxWidthProperty = sixelImageType.GetProperty("MaxWidth");
                if (maxWidthProperty?.CanWrite == true) {
                    maxWidthProperty.SetValue(sixelInstance, maxWidth.Value);
                }
                else {
                    // Try method-based approach as fallback
                    MethodInfo? maxWidthMethod = sixelImageType.GetMethod("MaxWidth");
                    if (maxWidthMethod is not null) {
                        sixelInstance = maxWidthMethod.Invoke(sixelInstance, [maxWidth.Value]);
                    }
                }
            }

            if (maxHeight.HasValue) {
                PropertyInfo? maxHeightProperty = sixelImageType.GetProperty("MaxHeight");
                if (maxHeightProperty?.CanWrite == true) {
                    maxHeightProperty.SetValue(sixelInstance, maxHeight.Value);
                }
                else {
                    // Try method-based approach as fallback
                    MethodInfo? maxHeightMethod = sixelImageType.GetMethod("MaxHeight");
                    if (maxHeightMethod is not null) {
                        sixelInstance = maxHeightMethod.Invoke(sixelInstance, [maxHeight.Value]);
                    }
                }
            }

            if (sixelInstance is IRenderable renderable) {
                result = renderable;
                return true;
            }
        }
        catch (Exception ex) {
            // Capture the error for debugging
            _lastSixelError = ex.Message;
        }

        return false;
    }

    /// <summary>
    /// Creates a fallback representation of an image as a clickable link with an icon.
    /// </summary>
    /// <param name="altText">Alternative text for the image</param>
    /// <param name="imageUrl">URL or path to the image</param>
    /// <returns>A markup string representing the image as a link</returns>
    private static Text CreateImageFallback(string altText, string imageUrl) {
        string linkText = $"🖼️ Image: {altText}";
        var style = new Style(Color.Blue, null, Decoration.Underline, imageUrl);
        return new Text(linkText, style);
    }

    /// <summary>
    /// Creates an enhanced fallback representation with file information.
    /// </summary>
    /// <param name="altText">Alternative text for the image</param>
    /// <param name="imageUrl">Original URL or path to the image</param>
    /// <param name="localPath">Local path to the image file</param>
    /// <returns>A panel with enhanced image information</returns>
    private static IRenderable CreateEnhancedImageFallback(string altText, string imageUrl, string localPath) {
        try {
            var fileInfo = new FileInfo(localPath);
            string? sizeText = fileInfo.Exists ? $" ({fileInfo.Length / 1024:N0} KB)" : "";

            // Build a text-based content with clickable link style
            string display = $"🖼️ {altText}{sizeText}";
            var linkStyle = new Style(Color.Blue, null, Decoration.Underline, imageUrl);
            var text = new Text(display, linkStyle);
            return new Panel(text)
                .Header("Image (Sixel not available)")
                .Border(BoxBorder.Rounded)
                .BorderColor(Color.Grey);
        }
        catch {
            return CreateImageFallback(altText, imageUrl);
        }
    }

    /// <summary>
    /// Creates an inline fallback representation of an image as a clickable link with an icon.
    /// </summary>
    /// <param name="altText">Alternative text for the image</param>
    /// <param name="imageUrl">URL or path to the image</param>
    /// <returns>A markup string representing the image as a link</returns>
    private static Text CreateImageFallbackInline(string altText, string imageUrl) {
        string display = $"🖼️ {altText}";
        var style = new Style(Color.Blue, null, Decoration.Underline, imageUrl);
        return new Text(display, style);
    }

    /// <summary>
    /// Gets debug information about the last image processing error.
    /// </summary>
    /// <returns>The last error message, if any</returns>
    public static string? GetLastImageError() => _lastImageError;

    /// <summary>
    /// Gets debug information about the last Sixel error.
    /// </summary>
    /// <returns>The last error message, if any</returns>
    public static string? GetLastSixelError() => _lastSixelError;

    /// <summary>
    /// Checks if SixelImage type is available in the current environment.
    /// </summary>
    /// <returns>True if SixelImage can be found</returns>
    public static bool IsSixelImageAvailable() {
        try {

            // Try direct approaches first
            Type? sixelImageType = Type.GetType("Spectre.Console.SixelImage, Spectre.Console.ImageSharp")
                            ?? Type.GetType("Spectre.Console.SixelImage, Spectre.Console");

            if (sixelImageType is not null)
                return true;

            // Search through loaded assemblies
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) {
                string? assemblyName = assembly.GetName().Name;
                if (assemblyName?.Contains("Spectre.Console") == true) {
                    sixelImageType = assembly.GetType("Spectre.Console.SixelImage");
                    if (sixelImageType is not null)
                        return true;
                }
            }

            return false;
        }
        catch {
            return false;
        }
    }

}
