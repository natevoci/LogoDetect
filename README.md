# LogoDetect

A .NET 8 command-line tool for detecting TV channel logos in video files and generating EDL (Edit Decision List) files for commercial break detection.

## Features

- Automatic logo detection using edge detection and frame comparison
- Hardware-accelerated video processing (NVIDIA CUDA or Intel QuickSync when available)
- GPU-accelerated image processing using CUDA (when available)
- Scene change detection for accurate cut points
- EDL file generation for post-processing

## Requirements

- .NET 8.0 Runtime
- FFmpeg libraries
- For GPU acceleration:
  - NVIDIA GPU with CUDA support, or
  - Intel CPU with QuickSync support

## Installation

1. Download the latest release
2. Extract to a folder of your choice
3. Run `winget install ffmpeg`
3. Run from command line using `LogoDetect.exe`

## Usage

```powershell
LogoDetect.exe --input <video-file> [options]
```

### Command Line Options

- `--input <path>` (Required): Path to the input video file (supports mp4, ts, and other FFmpeg-supported formats)
- `--logo-threshold <value>` (Optional): Logo detection threshold (0.0-1.0, default: 0.3)
- `--scene-change-threshold <value>` (Optional): Scene change detection threshold (0.0-1.0, default: 0.2)
- `--min-duration <seconds>` (Optional): Minimum duration for detected segments in seconds (default: 60)
- `--output <path>` (Optional): Output EDL file path (defaults to input filename with .edl extension)

### Examples

Basic usage with default settings:
```powershell
LogoDetect.exe --input "C:\Videos\tv-recording.mp4"
```

Custom thresholds and minimum duration:
```powershell
LogoDetect.exe --input "C:\Videos\tv-recording.mp4" --logo-threshold 0.25 --scene-change-threshold 0.15 --min-duration 45
```

Specify output file:
```powershell
LogoDetect.exe --input "C:\Videos\tv-recording.mp4" --output "C:\EDL\commercials.edl"
```

## How It Works

1. The tool samples frames from the video at regular intervals to create a logo reference image
2. Edge detection is performed on the reference image and subsequent frames
3. Frames are compared against the reference to detect logo presence/absence
4. Scene changes are detected to improve cut point accuracy
5. Segments without logos (potential commercial breaks) are identified
6. An EDL file is generated with the detected segments

## Performance Optimization

- Uses hardware acceleration for video decoding when available:
  1. First tries NVIDIA CUDA
  2. Falls back to Intel QuickSync if available
  3. Uses software decoding as last resort
- Uses CUDA for matrix operations in image processing when available
- Processes frames sequentially to minimize memory usage

## EDL File Format

The generated EDL file follows the standard CMX3600 format:
```
HH:MM:SS:FF HH:MM:SS:FF C Logo Segment
```

Where:
- First timestamp is the start of the segment
- Second timestamp is the end of the segment
- 'C' indicates a cut

## Limitations

- Logo detection works best with static, opaque logos
- Very small or transparent logos may be harder to detect
- Accuracy depends on logo consistency and video quality
- Some older video formats may not support hardware acceleration

## Development

### Building from Source

1. Clone the repository
2. Open the solution in Visual Studio 2022 or later
3. Restore NuGet packages
4. Build the solution

### Prerequisites for Development

- Visual Studio 2022 or later
- .NET 8.0 SDK
- C# development workload
- (Optional) CUDA SDK for GPU acceleration development

## Contributing

Pull requests are welcome. For major changes, please open an issue first to discuss what you would like to change.

## License

[MIT License](https://opensource.org/licenses/MIT)

## Acknowledgments

- FFmpeg.AutoGen for video processing
- SkiaSharp for image processing
- MathNet.Numerics for matrix operations
- System.CommandLine for CLI argument parsing
