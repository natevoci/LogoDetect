using System.CommandLine;
using LogoDetect.Models;
using LogoDetect.Services;
using SkiaSharp;

namespace LogoDetect;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        var inputOption = new Option<FileInfo>(
            name: "--input",
            description: "Input video file path")
        { IsRequired = true };

        var logoThresholdOption = new Option<double>(
            name: "--logo-threshold",
            description: "Logo detection threshold (0.0-1.0)",
            getDefaultValue: () => 0.3);

        var sceneChangeThresholdOption = new Option<double>(
            name: "--scene-change-threshold",
            description: "Scene change detection threshold (0.0-1.0)",
            getDefaultValue: () => 0.2);

        var minDurationOption = new Option<int>(
            name: "--min-duration",
            description: "Minimum cut duration in seconds",
            getDefaultValue: () => 60);

        var outputOption = new Option<FileInfo?>(
            name: "--output",
            description: "Output EDL file path (defaults to input filename with .edl extension)",
            getDefaultValue: () => null);

        var rootCommand = new RootCommand("LogoDetect - Video logo detection and EDL generation tool")
        {
            inputOption,
            logoThresholdOption,
            sceneChangeThresholdOption,
            minDurationOption,
            outputOption
        };

        rootCommand.SetHandler(async (input, logoThreshold, sceneThreshold, minDuration, output) =>
        {
            try
            {
                await ProcessVideo(input, logoThreshold, sceneThreshold, TimeSpan.FromSeconds(minDuration), output);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        }, inputOption, logoThresholdOption, sceneChangeThresholdOption, minDurationOption, outputOption);

        return await rootCommand.InvokeAsync(args);
    }

    private static async Task ProcessVideo(
        FileInfo input,
        double logoThreshold,
        double sceneThreshold,
        TimeSpan minDuration,
        FileInfo? output)
    {
        if (!input.Exists)
            throw new FileNotFoundException("Input video file not found", input.FullName);

        var outputPath = output?.FullName ?? Path.ChangeExtension(input.FullName, ".edl");
        Console.WriteLine($"Processing video: {input.FullName}");
        Console.WriteLine($"Output EDL file: {outputPath}");

        FFmpegBinariesHelper.RegisterFFmpegBinaries();

        using var videoProcessor = new VideoProcessor();
        var imageProcessor = new ImageProcessor();
        var edlGenerator = new EdlGenerator();

        // Step 1: Generate logo reference image
        Console.WriteLine("Extracting frames for logo reference...");
        var referenceFrames = await videoProcessor.ExtractFramesAsync(input.FullName, 250);
        Console.WriteLine("Detecting edges in reference frames...");
        var edgeDetectedFrames = referenceFrames.Select(f => imageProcessor.DetectEdges(f)).ToArray();
        var logoReference = imageProcessor.GenerateLogoReference(edgeDetectedFrames);
        
        // Save logo reference image
        var logoReferencePath = Path.ChangeExtension(input.FullName, ".logo.png");
        using (var stream = File.OpenWrite(logoReferencePath))
        {
            logoReference.Encode(SKEncodedImageFormat.Png, 100).SaveTo(stream);
        }
        Console.WriteLine($"Logo reference image saved to: {logoReferencePath}");

        // Step 2: Process all keyframes
        Console.WriteLine("Processing video keyframes...");
        var keyframes = new List<(TimeSpan Time, SKBitmap Frame)>();
        var sceneChanges = new List<TimeSpan>();
        SKBitmap? previousFrame = null;
        
        // TODO: Implement proper keyframe extraction
        // For now, we'll sample every second
        for (var time = TimeSpan.Zero; time < TimeSpan.FromHours(24); time += TimeSpan.FromSeconds(1))
        {
            var frame = await videoProcessor.ExtractFrameAtTimestampAsync((long)(time.TotalSeconds * 1_000_000));
            if (frame == null) break;

            if (previousFrame != null && imageProcessor.IsSceneChange(previousFrame, frame, sceneThreshold))
            {
                sceneChanges.Add(time);
            }

            keyframes.Add((time, frame));
            previousFrame = frame;
        }

        Console.WriteLine($"Found {keyframes.Count} keyframes and {sceneChanges.Count} scene changes");

        // Step 3-5: Detect logo presence and generate EDL
        Console.WriteLine("Detecting logo presence...");
        var logoDetections = keyframes
            .Select(k => (k.Time, HasLogo: imageProcessor.CompareImages(logoReference, imageProcessor.DetectEdges(k.Frame)) <= logoThreshold))
            .ToList();

        var edlEntries = edlGenerator.GenerateEdlEntries(logoDetections, minDuration, sceneChanges);
        edlGenerator.WriteEdlFile(outputPath, edlEntries);

        Console.WriteLine("Processing complete!");
        Console.WriteLine($"EDL file written to: {outputPath}");
    }
}
