using System.CommandLine;
using LogoDetect.Services;

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
            description: "Output CSV file path (defaults to input filename with .csv extension)",
            getDefaultValue: () => null);

        var rootCommand = new RootCommand("LogoDetect - Video logo detection and CSV cut list generation tool")
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
                await Task.Run(() => ProcessVideo(input, logoThreshold, sceneThreshold, TimeSpan.FromSeconds(minDuration), output));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        }, inputOption, logoThresholdOption, sceneChangeThresholdOption, minDurationOption, outputOption);

        return await rootCommand.InvokeAsync(args);
    }
    
    private static void ProcessVideo(
        FileInfo input,
        double logoThreshold,
        double sceneThreshold,
        TimeSpan minDuration,
        FileInfo? output)
    {
        // Normalize the input path to handle mixed slashes
        var normalizedInputPath = Path.GetFullPath(input.FullName);
        if (!File.Exists(normalizedInputPath))
            throw new FileNotFoundException("Input video file not found", normalizedInputPath);

        // Normalize the output path if provided, otherwise create one based on the input path
        var outputPath = output != null
            ? Path.GetFullPath(output.FullName)
            : Path.ChangeExtension(normalizedInputPath, ".csv"); Console.WriteLine($"Processing video: {normalizedInputPath}");
        Console.WriteLine($"Output CSV file: {outputPath}");

        FFmpegBinariesHelper.RegisterFFmpegBinaries();

        Console.WriteLine("Loading video file...");
        using var videoProcessor = new VideoProcessor(normalizedInputPath);

        Console.WriteLine("Generating logo reference...");
        videoProcessor.GenerateLogoReference(
            Path.ChangeExtension(normalizedInputPath, ".logo.png"),
            new Progress<double>(p =>
            {
                Console.Write($"\rProgress: {p:F1}%");
                if (p >= 100) Console.WriteLine();
            })
        );

        Console.WriteLine("Detecting logo frames...");
        var logoDetections = videoProcessor.DetectLogoFrames(logoThreshold, new Progress<double>(p =>
        {
            Console.Write($"\rProgress: {p:F1}%");
            if (p >= 100) Console.WriteLine();
        }));

        // Generate segments based on logo detections
        Console.WriteLine("Generating segments...");
        var segments = videoProcessor.GenerateSegments(logoDetections, minDuration);

        // Extend segments to nearest scene changes
        Console.WriteLine($"Extending {segments.Count()} segments to scene changes...");
        segments = videoProcessor.ExtendSegmentsToSceneChanges(
            segments,
            sceneThreshold,
            new Progress<double>(p =>
            {
                Console.Write($"\rProgress: {p:F1}%");
                if (p >= 100) Console.WriteLine();
            })
        );

        // Write segments to CSV file
        File.WriteAllLines(outputPath, segments.Select(s => s.ToString()));

        Console.WriteLine("Processing complete!");
        Console.WriteLine($"CSV file written to: {outputPath}");
    }
}
