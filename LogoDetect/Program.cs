using System.CommandLine;
using LogoDetect.Services;

namespace LogoDetect;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        var inputOption = new Option<FileInfo>(
            name: "--input",
            description: "Input video file path (only MP4 supported)")
        { IsRequired = true };

        var logoThresholdOption = new Option<double>(
            name: "--logo-threshold",
            description: "Logo detection threshold (default 1.0)",
            getDefaultValue: () => 1.0);

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

        if (Path.GetExtension(normalizedInputPath).ToLowerInvariant() != ".mp4")
            throw new ArgumentException("Input file must be an MP4 video", normalizedInputPath);

        // Normalize the output path if provided, otherwise create one based on the input path
        var outputPath = output != null
            ? Path.GetFullPath(output.FullName)
            : Path.ChangeExtension(normalizedInputPath, ".segments.csv");
        Console.WriteLine($"Processing video: {normalizedInputPath}");
        Console.WriteLine($"Output CSV file: {outputPath}");

        FFmpegBinariesHelper.RegisterFFmpegBinaries();


        Console.WriteLine("Loading video file...");
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        using var videoProcessor = new VideoProcessor(normalizedInputPath);
        stopwatch.Stop();
        Console.WriteLine($"Video loaded in {stopwatch.Elapsed.TotalSeconds:F1} seconds");


        Console.WriteLine("Generating logo reference...");
        stopwatch.Restart();
        videoProcessor.GenerateLogoReference(
            Path.ChangeExtension(normalizedInputPath, ".logo.png"),
            new Progress<double>(p =>
            {
                Console.Write($"\rProgress: {p:F1}%");
            })
        );
        stopwatch.Stop();
        Console.WriteLine($"\nLogo reference generated in {stopwatch.Elapsed.TotalSeconds:F1} seconds");


        Console.WriteLine("Detecting logo frames...");
        stopwatch.Restart();
        var logoDetections = videoProcessor.DetectLogoFrames(new Progress<double>(p =>
        {
            Console.Write($"\rProgress: {p:F1}%");
        }));
        stopwatch.Stop();
        Console.WriteLine($"\nLogo frames detected in {stopwatch.Elapsed.TotalSeconds:F1} seconds");


        Console.WriteLine("Saving graph of logo detections...");
        stopwatch.Restart();
        videoProcessor.SaveGraphOfLogoDetections(logoThreshold, videoProcessor.MediaFile.GetDurationTimeSpan(), logoDetections);
        stopwatch.Stop();
        Console.WriteLine($"Graph saved in {stopwatch.Elapsed.TotalSeconds:F1} seconds");


        // Generate segments based on logo detections
        Console.WriteLine("Generating segments...");
        stopwatch.Restart();
        var segments = videoProcessor.GenerateSegments(logoDetections, logoThreshold, minDuration);
        File.WriteAllLines(Path.ChangeExtension(outputPath, "-logo-only.csv"), segments.Select(s => s.ToString()));
        stopwatch.Stop();
        Console.WriteLine($"Segments generated in {stopwatch.Elapsed.TotalSeconds:F1} seconds");


        // // Extend segments to nearest scene changes
        // Console.WriteLine($"Extending {segments.Count()} segments to scene changes...");
        // stopwatch.Restart();
        // segments = videoProcessor.ExtendSegmentsToSceneChanges(
        //     segments,
        //     sceneThreshold,
        //     new Progress<double>(p =>
        //     {
        //         Console.Write($"\rProgress: {p:F1}%");
        //         if (p >= 100) Console.WriteLine();
        //     })
        // );
        // stopwatch.Stop();
        // Console.WriteLine($"Segments extended in {stopwatch.Elapsed.TotalSeconds:F1} seconds");

        // Write segments to CSV file
        stopwatch.Restart();
        File.WriteAllLines(outputPath, segments.Select(s => s.ToString()));
        stopwatch.Stop();
        Console.WriteLine($"CSV file written in {stopwatch.Elapsed.TotalSeconds:F1} seconds");

        Console.WriteLine("Processing complete!");
        Console.WriteLine($"CSV file written to: {outputPath}");
    }
}
