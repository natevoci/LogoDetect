using System;
using System.IO;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace LogoDetect.Services;

public class FFmpegBinariesHelper
{
    internal static void RegisterFFmpegBinaries()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var probe = Path.Combine("FFmpeg", "bin", Environment.Is64BitProcess ? "x64" : "x86");

            // Search from AppContext.BaseDirectory
            var baseDir = AppContext.BaseDirectory;
            while (baseDir != null)
            {
                var ffmpegBinaryPath = Path.Combine(baseDir, probe);
                if (Directory.Exists(ffmpegBinaryPath))
                {
                    Console.WriteLine($"FFmpeg binaries found in: {ffmpegBinaryPath}");
                    ffmpeg.RootPath = ffmpegBinaryPath;
                    return;
                }
                baseDir = Directory.GetParent(baseDir)?.FullName;
            }

            // Search from Environment.CurrentDirectory
            var current = Environment.CurrentDirectory;
            while (current != null)
            {
                var ffmpegBinaryPath = Path.Combine(current, probe);
                if (Directory.Exists(ffmpegBinaryPath))
                {
                    Console.WriteLine($"FFmpeg binaries found in: {ffmpegBinaryPath}");
                    ffmpeg.RootPath = ffmpegBinaryPath;
                    return;
                }
                current = Directory.GetParent(current)?.FullName;
            }

            throw new FileNotFoundException("FFmpeg binaries not found. Please ensure they are in FFmpeg/bin/x64 or FFmpeg/bin/x86 directory.");
        }
        else
            throw new NotSupportedException("Only Windows is supported at this time.");
    }
}
