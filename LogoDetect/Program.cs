using System.CommandLine;
using LogoDetect.Services;
using Velopack;

namespace LogoDetect;

public class Program
{
    private const string GithubOwner = "natevoci";
    private const string GithubRepo = "LogoDetect";

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
            Console.WriteLine($"Update check failed: {ex.Message}");
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

        rootCommand.SetHandler((input, logoThreshold, reload, sceneThreshold, blankThreshold, minDuration, output) =>
        {
            try
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

                Task.Run(() => videoProcessor.ProcessVideo(new VideoProcessorSettings()
                {
                    outputPath = outputPath,
                    logoThreshold = logoThreshold,
                    sceneThreshold = sceneThreshold,
                    blankThreshold = blankThreshold,
                    minDuration = TimeSpan.FromSeconds(minDuration),
                    forceReload = reload,
                }
                )).Wait();

                Console.WriteLine("Video processing complete.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        }, inputOption, logoThresholdOption, reloadOption, sceneChangeThresholdOption, blackFrameThresholdOption, minDurationOption, outputOption);

        return rootCommand.Invoke(args);
    }
}
