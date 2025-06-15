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

        var reloadOption = new Option<bool>(
            name: "--reload",
            description: "Force reprocessing of video frames, ignoring cached results",
            getDefaultValue: () => false);

        var sceneChangeThresholdOption = new Option<double>(
            name: "--scene-change-threshold",
            description: "Scene change detection threshold (0.0-1.0)",
            getDefaultValue: () => 0.2);

        var blackFrameThresholdOption = new Option<double>(
            name: "--black-frame-threshold",
            description: "Black frame detection threshold (0.0-1.0, lower values require darker pixels)",
            getDefaultValue: () => 0.1);

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
            reloadOption,
            sceneChangeThresholdOption,
            blackFrameThresholdOption,
            minDurationOption,
            outputOption
        };

        rootCommand.SetHandler(async (input, logoThreshold, reload, sceneThreshold, blankThreshold, minDuration, output) =>
        {
            try
            {
                await Task.Run(() => ProcessVideo(input, logoThreshold, reload, sceneThreshold, blankThreshold, TimeSpan.FromSeconds(minDuration), output));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        }, inputOption, logoThresholdOption, reloadOption, sceneChangeThresholdOption, blackFrameThresholdOption, minDurationOption, outputOption);

        return await rootCommand.InvokeAsync(args);
    }
    
    private static void ProcessVideo(
        FileInfo input,
        double logoThreshold,
        bool reload,
        double sceneThreshold,
        double blankThreshold,
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
        using var videoProcessor = new VideoProcessor(normalizedInputPath, reload);
        stopwatch.Stop();
        Console.WriteLine($"Video loaded in {stopwatch.Elapsed.TotalSeconds:F1} seconds");
        
        // Edge detection method
        Console.WriteLine("Detecting logo frames with edge detection...");
        stopwatch.Restart();
        var logoDetections = videoProcessor.DetectLogoFramesWithEdgeDetection(logoThreshold, new Progress<double>(p =>
        {
            Console.Write($"\rProgress: {p:F1}%".PadRight(50, ' '));
        }));
        stopwatch.Stop();
        Console.WriteLine($"\nLogo frames detected in {stopwatch.Elapsed.TotalSeconds:F1} seconds");

        // Generate segments based on logo detections
        Console.WriteLine("Generating segments...");
        stopwatch.Restart();
        var segments = new List<Models.VideoSegment>();
        segments.AddRange(videoProcessor.GenerateSegments(logoDetections, logoThreshold, minDuration));
        File.WriteAllLines(Path.ChangeExtension(outputPath, "-edge.csv"), segments.Select(s => s.ToString()));
        stopwatch.Stop();
        Console.WriteLine($"Segments generated in {stopwatch.Elapsed.TotalSeconds:F1} seconds");


        Console.WriteLine("Processing scene changes and blank scenes...");
        stopwatch.Restart();
        var sceneChangesPath = Path.ChangeExtension(normalizedInputPath, ".scenechanges.csv");
        videoProcessor.ProcessSceneChanges(
            sceneChangesPath,
            sceneThreshold,
            blankThreshold,
            new Progress<double>(p =>
            {
                Console.Write($"\rProgress: {p:F1}%".PadRight(50, ' '));
                if (p >= 100) Console.WriteLine();
            })
        );
        stopwatch.Stop();
        Console.WriteLine($"\nScene changes processed in {stopwatch.Elapsed.TotalSeconds:F1} seconds");


        // // Extend segments to nearest scene changes
        // Console.WriteLine($"Extending {segments.Count()} segments to scene changes...");
        // stopwatch.Restart();
        // segments = videoProcessor.ExtendSegmentsToSceneChanges(
        //     segments,
        //     sceneThreshold,
        //     new Progress<double>(p =>
        //     {
        //         Console.Write($"\rProgress: {p:F1}%".PadRight(50, ' '));
        //         if (p >= 100) Console.WriteLine();
        //     })
        // );
        // stopwatch.Stop();
        // Console.WriteLine($"\nSegments extended in {stopwatch.Elapsed.TotalSeconds:F1} seconds");


        // Write segments to CSV file
        stopwatch.Restart();
        File.WriteAllLines(outputPath, segments.Select(s => s.ToString()));
        stopwatch.Stop();
        Console.WriteLine($"CSV file written in {stopwatch.Elapsed.TotalSeconds:F1} seconds");

        Console.WriteLine("Processing complete!");
        Console.WriteLine($"CSV file written to: {outputPath}");
    }
}
