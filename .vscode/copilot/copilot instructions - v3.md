# TV Logo Detection Project Instructions

This document provides instructions for implementing a TV channel logo detection system that processes video files and generates CSV cut list files denoting blocks of scenes where the logo is present.

## Project Overview

Create a .NET 8 console application that:
1. Processes video files to detect channel logo presence
2. Uses hardware acceleration (NVIDIA CUDA and Intel QuickSync) for performance, when available
3. Generates CSV files marking groups of scenes with logos
4. Includes scene change detection for accurate cut points

## Core Requirements

### 1. Project Setup
- Create a .NET 8 Console Application
- Required NuGet packages:
  - FFmpeg.AutoGen (7.1.1 or later)
  - SkiaSharp (2.88.7 or later)
  - System.CommandLine (2.0.0-beta4.22272.1 or later)
  - MathNet.Numerics (5.0.0 or later)
  - MathNet.Numerics.Providers.CUDA (5.0.0 or later)

### 2. Core Architecture

Create the following class structure:

```
/Services
  - FFmpegBinariesHelper.cs  # Detect location of FFMpeg libraries and set ffmpeg.RootPath
  - MediaFile.cs             # Video loading, seeking, extraction of Y data of frames, and keeping track of the current timestamp
  - YData.cs                 # Container for a frame. Includes YData, Width, and Height properties and ToBitmap and FromBitmap methods
  - VideoProcessor.cs        # Video frame processing and segment grouping, and segment adjusting to scene changes
  - ImageProcessor.cs        # Edge detection and image comparison
/Models
  - VideoSegment.cs         # Represents a video segment with logo status
Program.cs                 # Command line interface
```

### 3. Implementation Steps

#### Step 1: Logo Reference Generation
- Sample 250 frames evenly spaced throughout the video
- For each frame, load the Y-Image data and perform edge detection and add the values to an accumulator 2d array of floats
- Once all the frames have been added to the accumulator, divide the values in the accumulator by the number of frames
- Convert the accumulator data to a greyscale bitmap and save it as a png file to the same folder as the input file

#### Step 2: Frame Processing
- Load the Y-Image data of each keyframe one at a time to complete the following and then dispose of the frame to conserve memory.
- Detect edges in each Y-Image data
- Compare the result to the logo reference image from Step 1. If the difference between the reference image and the edge detected keyframe is greater than a user supplied threshold then the logo should be considered present.
- Track logo presence/absence

#### Step 3: Scene grouping
- Find groups of keyframes from step 2 where the logo is considered present and create a cut for the cut list for each group. Groups should be of a minimum length defined by the user supplied number of seconds defaulting to 60 seconds.

#### Step 4: Detect scene changes for accurate cuts
- A scene change is when the Y-Image data changes by more than the scene-change-threshold value
- Scene changes should only be detected after the initial cut list has been generated
- For each cut in the cut list, find the last scene change or blank frame before the start point and adjust the cut's start point to match. This should be frame accurate.
- For each cut in the cut list, find the next scene change or blank frame after the end point and adjust the cut's end point to match. This should be frame accurate.

#### Step 5: CSV Generation
- Generate CSV file using the file format defined below in section 6. CSV File Format.


### 4. Command Line Interface

```
LogoDetect.exe [options]
Options:
  --input <path>                    Input video file path (required)
  --logo-threshold <value>          Logo detection threshold (0.0-1.0, default: 0.3)
  --scene-change-threshold <value>  Scene change threshold (0.0-1.0, default: 0.2)
  --min-duration <seconds>          Minimum segment duration (default: 60)
  --output <path>                   Output EDL path (optional)
```

### 5. Performance Optimization

#### Hardware Acceleration Priority
1. NVIDIA CUDA (if available)
2. Intel QuickSync (if available)
3. Software fallback

#### Memory Management
- Process frames sequentially
- Dispose of frames after processing
- Use hardware acceleration for matrix operations
- Implement proper resource disposal

### 6. CSV File Format

Each line contains a segment
```
HH:MM:SS:FF,HH:MM:SS:FF,
```
Where:
- First timestamp: segment start
- Second timestamp: segment end

## Implementation Guidelines

### FFmpeg Integration
- Use FFmpeg.AutoGen for video processing
- Only the luninance data of a frame should be used for any processing. Use scaler to load only Y data.
- Handle hardware acceleration setup
- Manage FFmpeg resources correctly

### Image Processing
- Use SkiaSharp for image manipulation
- Implement efficient edge detection
- Use matrix operations for comparisons
- Utilize GPU acceleration when available

### Error Handling
- Validate input files
- Handle missing/corrupt frames
- Provide fallback for hardware acceleration
- Clean up resources properly

## Testing Plan

Process each of the videos in the TestSamples folder. Output csv files should match the *-expected.csv files within 2 seconds for each timestamp.

## Best Practices

1. Resource Management
   - Use `using` statements
   - Implement IDisposable properly
   - Clean up unmanaged resources
   - Implement algorithms in a way that avoids having to keep many frames in memory

2. Error Handling
   - Provide clear error messages
   - Gracefully handle hardware failures
   - Log important operations

3. Performance
   - Use hardware acceleration when available
   - Minimize memory allocation
   - Except for when detecting scene changes use the AVSEEK_FLAG_BACKWARD flag to seek to the nearest keyframe.

4. Code Organization
   - Follow SOLID principles
   - Use dependency injection
   - Keep methods focused and small
