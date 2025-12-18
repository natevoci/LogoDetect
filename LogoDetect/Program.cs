using System.CommandLine;
using System.IO;
using LogoDetect.Services;
using Velopack;

namespace LogoDetect;

public class Program
{
    private const string GithubOwner = "natevoci";
    private const string GithubRepo = "LogoDetect";

    [STAThread]
    public static int Main(string[] args)
    {
        // Check for updates using Velopack
        VelopackApp.Build().Run();
        try
        {
            var updateSource = $"https://github.com/{GithubOwner}/{GithubRepo}/releases/latest/download/";
            var mgr = new UpdateManager(updateSource);

            // check for new version
            var newVersion = mgr.CheckForUpdates();
            if (newVersion != null)
            {
                Console.WriteLine($"Updating to version {newVersion.TargetFullRelease.Version}");

                // download new version
                mgr.DownloadUpdates(newVersion);

                // install new version and restart app
                mgr.ApplyUpdatesAndRestart(newVersion);
                Console.WriteLine("Update complete! Please restart the application.");
                return 0;
            }
        }
        catch (Exception ex)
        {
#if DEBUG
            Console.WriteLine($"Update check failed: {ex}");
#else
            Console.WriteLine($"Update check failed: {ex.Message}");
#endif
        }

        var inputOption = new Option<FileInfo>(
            name: "--input",
            description: "Input video file path (only MP4 works well)")
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
            getDefaultValue: () => 0.05);

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

        var keepDebugFilesOption = new Option<bool>(
            name: "--keepDebugFiles",
            description: "Keep debug files (PNG, CSV, etc.) generated during processing",
            getDefaultValue: () => false);

        var maxFramesOption = new Option<int?>(
            name: "--max-frames",
            description: "Maximum number of frames to process (for debugging/testing)",
            getDefaultValue: () => null);

        var exportPerformanceJsonOption = new Option<bool>(
            name: "--export-performance-json",
            description: "Export detailed performance data to JSON file",
            getDefaultValue: () => false);

        var losslessCutOption = new Option<bool>(
            name: "--losslesscut",
            description: "Output in LosslessCut project format (.llc) instead of CSV",
            getDefaultValue: () => false);

        var rootCommand = new RootCommand("LogoDetect - Video logo detection and CSV cut list generation tool")
        {
            inputOption,
            logoThresholdOption,
            reloadOption,
            sceneChangeThresholdOption,
            blackFrameThresholdOption,
            minDurationOption,
            outputOption,
            keepDebugFilesOption,
            maxFramesOption,
            exportPerformanceJsonOption,
            losslessCutOption
        };

        rootCommand.SetHandler(async (context) =>
        {
            try
            {
                var input = context.ParseResult.GetValueForOption(inputOption)!;
                var logoThreshold = context.ParseResult.GetValueForOption(logoThresholdOption);
                var reload = context.ParseResult.GetValueForOption(reloadOption);
                var sceneThreshold = context.ParseResult.GetValueForOption(sceneChangeThresholdOption);
                var blankThreshold = context.ParseResult.GetValueForOption(blackFrameThresholdOption);
                var minDuration = context.ParseResult.GetValueForOption(minDurationOption);
                var output = context.ParseResult.GetValueForOption(outputOption);
                var keepDebugFiles = context.ParseResult.GetValueForOption(keepDebugFilesOption);
                var maxFrames = context.ParseResult.GetValueForOption(maxFramesOption);
                var exportPerformanceJson = context.ParseResult.GetValueForOption(exportPerformanceJsonOption);
                var losslessCut = context.ParseResult.GetValueForOption(losslessCutOption);

                // Normalize the input path to handle mixed slashes
                var normalizedInputPath = Path.GetFullPath(input.FullName);
                if (!File.Exists(normalizedInputPath))
                    throw new FileNotFoundException("Input video file not found", normalizedInputPath);

                if (Path.GetExtension(normalizedInputPath).ToLowerInvariant() != ".mp4")
                    throw new ArgumentException("Input file must be an MP4 video", normalizedInputPath);

                // Normalize the output path if provided, otherwise use the input path
                var outputPath = (output != null ? Path.GetDirectoryName(output.FullName) : Path.GetDirectoryName(normalizedInputPath)) ?? throw new InvalidOperationException("Could not determine output directory");
                var outputFilename = output != null ? Path.GetFileName(output.FullName) : null;
                
                Console.WriteLine($"Processing video: {normalizedInputPath}");
                Console.WriteLine($"Output path: {outputPath}");
                
                if (maxFrames.HasValue)
                {
                    Console.WriteLine($"Processing limited to {maxFrames.Value} frames for debugging/testing");
                }

                FFmpegBinariesHelper.RegisterFFmpegBinaries();

                Console.WriteLine("Loading video file...");
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                using var videoProcessor = new VideoProcessor(new VideoProcessorSettings()
                {
                    inputPath = normalizedInputPath,
                    outputPath = outputPath,
                    outputFilename = outputFilename,
                    logoThreshold = logoThreshold,
                    sceneThreshold = sceneThreshold,
                    blankThreshold = blankThreshold,
                    minDuration = TimeSpan.FromSeconds(minDuration),
                    forceReload = reload,
                    keepDebugFiles = keepDebugFiles,
                    maxFramesToProcess = maxFrames,
                    exportPerformanceJson = exportPerformanceJson,
                    losslessCut = losslessCut,
                });
                stopwatch.Stop();
                Console.WriteLine($"Video loaded in {stopwatch.Elapsed.TotalSeconds:F1} seconds");

                await videoProcessor.ProcessVideo();

                Console.WriteLine("Video processing complete.");
            }
            catch (Exception ex)
            {
#if DEBUG
                Console.Error.WriteLine($"Error: {ex}");
#else
                Console.Error.WriteLine($"Error: {ex.Message}");
#endif
                Environment.Exit(1);
            }
        });

        return rootCommand.Invoke(args);
    }
}
