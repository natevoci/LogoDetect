# LogoDetect

A .NET 8 command-line tool for detecting TV channel logos in video files and generating CSV cut lists for commercial break detection, compatible with LosslessCut.

## Features

- Automatic logo detection using edge detection and frame comparison
- Hardware-accelerated video processing (NVIDIA CUDA or Intel QuickSync when available)
- GPU-accelerated image processing using CUDA (when available)
- Scene change detection for accurate cut points (Not yet working)
- CSV file generation for post-processing in LosslessCut

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

- `--input <path>` (Required): Path to the input video file (only MP4 works well)
- `--logo-threshold <value>` (Optional): Logo detection threshold (default: 1.0)
- `--reload` (Optional): Force reprocessing of video frames, ignoring cached results (default: false)
- `--scene-change-threshold <value>` (Optional): Scene change detection threshold (0.0-1.0, default: 0.2)
- `--black-frame-threshold <value>` (Optional): Black frame detection threshold (0.0-1.0, lower values require darker pixels, default: 0.1)
- `--min-duration <seconds>` (Optional): Minimum cut duration in seconds (default: 60)
- `--output <path>` (Optional): Output CSV file path (defaults to input filename with `.segments.csv` extension)

### Examples

Basic usage with default settings:
```powershell
LogoDetect.exe --input "C:\Videos\tv-recording.mp4"
```

Custom thresholds and minimum duration:
```powershell
LogoDetect.exe --input "C:\Videos\tv-recording.mp4" --logo-threshold 1.25 --scene-change-threshold 0.15 --min-duration 45
```

Specify output file:
```powershell
LogoDetect.exe --input "C:\Videos\tv-recording.mp4" --output "C:\CutLists\commercials.segments.csv"
```

## How It Works

1. The tool samples frames from the video at regular intervals to create a logo reference image which is the average of edge detections
2. Edge detection is performed on frames every second in the file, with a rolling average over 30 seconds
3. Rolling averages are compared against the reference to detect logo presence/absence
4. Scene changes are detected to improve cut point accuracy
5. Segments with logos are identified
6. A CSV file is generated with the detected segments, ready for import into LosslessCut

## Performance Optimization

- Uses hardware acceleration for video decoding when available:
  1. First tries NVIDIA CUDA
  2. Falls back to Intel QuickSync if available
  3. Uses software decoding as last resort
- Uses CUDA for matrix operations in image processing when available
- Processes frames sequentially to minimize memory usage

## CSV File Format for LosslessCut

The generated CSV file is compatible with LosslessCut's cut list import:
```
start,end
00:00:00.000,00:10:23.500
00:12:00.000,00:22:15.000
...etc
```

Where:
- Each row represents a segment to keep (logo present)
- Timestamps are in `HH:MM:SS.sss` format

## Limitations

- Logo detection works best with static, opaque logos
- Very small or transparent logos may be harder to detect
- Accuracy depends on logo consistency and video quality

## Development

### Building from Source

1. Clone the repository
2. Open the solution in Visual Studio 2022 or later, or vscode
3. Restore NuGet packages
4. Build the solution

### Prerequisites for Development

- Visual Studio 2022 or later, or vscode
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
