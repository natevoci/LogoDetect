Create a .NET 8 console application that processes video files, such as mp4 or ts, to detect scenes that contain an embossed logo and generate a cutlist EDL file.

The EDL cut lists would result in output files the only include the scenes with the logo.

## Implementation

The process to find scenes with a logo is as follows:

Step 1: Generate a logo reference image. This is done by taking 250 images that are evenly spaced from the start to the end of the file. Each of these images should have edge detection performed, and then an average of 250 edge detected images should be taken to generate the reference image. Once the image is created, save it as a png file to the same folder as the input file.

Step 2: For each keyframe in the video file, perform edge detection and compare the result to the logo reference image from Step 1. If the difference between the reference image and the edge detected keyframe is greater than a user supplied threshold then the logo should be considered present.

Step 3: Find groups of keyframes from step 2 where the logo is considered present and create a cut for the cut list for each group. Groups should be of a minimum length defined by a user supplied number of seconds defaulting to 60 seconds.

Step 4: For each cut in the cut list, find the last scene change before the start point and adjust the cut's start point to match.

Step 5: For each cut in the cut list, find the next scene change after the end point and adjust the cut's end point to match.

## Command Line Parameters

```plaintext
LogoDetect.exe [options]
Options:
  --input <path>                      Input video file path
  --logo-threshold <value>            Logo detection threshold (0.0-1.0)
  --scene-change-threshold <value>    Scene change detection threshold (0.0-1.0)
  --min-duration <secs>               Minimum cut duration in seconds
  --output <path>                     Output EDL file path (defaults to input filename with .edl extension)
```

## Testing Requirements

No testing is required

## Considerations

If the hardware supports it, the video decoding should be done using either NVIDIA GPU or Intel QuickSync acceleration for video decoding, and the image processing should be done using GPU accelerated matrix operations.

Out of memory exceptions are a concern, so processing should be done in a way that minimises the amount of video frames held in memory longer than necessary.
