# Logo Detection Video Analysis Web Viewer

A web-based interactive visualization tool for analyzing the combined CSV output from the LogoDetect application.

## 🚀 Features

- **Interactive Plotting**: Zoom, pan, and hover over data points
- **Dual Y-Axes**: Scene changes and luminance on left axis, logo detection on right axis
- **Selective Display**: Toggle visibility of different data series
- **Statistics Dashboard**: Overview of key metrics
- **Time Navigation**: Hover to see exact timestamps
- **Export**: Save plots as PNG images

## 📋 Quick Start

1. **Open the viewer**: Simply open `index.html` in any modern web browser
2. **Load CSV data**: Click "📁 Load CSV File" and select a `.combined.csv` file from your LogoDetect output
3. **Explore the data**: Use the interactive controls to analyze your video data

## 🎛️ Controls

### Data Series Toggles
- **Logo Detection**: Shows the logo detection confidence over time (right Y-axis)
- **Scene Changes**: Displays scene change detection points (left Y-axis)
- **Black Frames**: Shows detected black frames with their luminance
- **White Frames**: Shows detected white frames with their luminance
- **Mean Luminance**: Shows the overall luminance trend for normal frames

### Interactive Plot Features
- **Zoom**: 
  - Select area by clicking and dragging
  - Use mouse wheel to zoom in/out
  - Double-click to reset zoom
- **Pan**: Click and drag to move around the timeline
- **Hover**: Get detailed information about data points
- **Export**: Use the camera icon to save the current view as PNG

## 📊 Understanding the Visualization

### Left Y-Axis (0-1 range)
- **Scene Changes**: Blue dots showing scene transition intensity
- **Black/White Frames**: Red/blue diamonds showing frame types
- **Mean Luminance**: Gold line showing brightness levels (normalized)

### Right Y-Axis (Logo Detection Values)
- **Logo Detection**: Green line showing logo confidence scores
- Values above the threshold typically indicate logo presence

### Time Axis
- Shows video timeline in seconds
- Hover over points to see exact timestamps in HH:MM:SS.fff format

## 🗂️ Expected CSV Format

The viewer expects CSV files with these columns:
```csv
Time,LogoDiff,MeanLuminance,IsBlackFrame,IsWhiteFrame,SceneChange
00:00:01.000,0.250000,128.50,false,false,0.120000
00:00:02.000,0.300000,130.20,false,false,0.080000
```

- **Time**: HH:MM:SS.fff format
- **LogoDiff**: Float value for logo detection confidence
- **MeanLuminance**: Float value 0-255 for frame brightness
- **IsBlackFrame/IsWhiteFrame**: Boolean values
- **SceneChange**: Float value for scene change intensity

## 🎯 Use Cases

- **Logo Detection Analysis**: Identify periods where logos are present/absent
- **Scene Change Detection**: Find video transitions and cuts
- **Quality Assessment**: Detect black frames, white frames, and brightness issues
- **Timeline Navigation**: Correlate different metrics across the video timeline
- **Threshold Tuning**: Analyze detection sensitivity and adjust parameters

## 🔧 Technical Requirements

- Modern web browser (Chrome, Firefox, Edge, Safari)
- No installation required - pure HTML/JavaScript
- Works offline once loaded

## 💡 Tips for Best Results

1. **Large Files**: For very large CSV files (>100MB), consider filtering the data or using a subset
2. **Performance**: Disable unused data series to improve rendering performance
3. **Zoom Navigation**: Use the zoom feature to focus on specific time periods of interest
4. **Export**: Save plots before making major changes to preserve different views

## 🔄 Integration with LogoDetect

This viewer is designed to work seamlessly with the combined CSV output from your LogoDetect application. After processing a video, simply:

1. Locate the `.combined.csv` file in your output directory
2. Load it into this web viewer
3. Analyze the results interactively

## 📈 Example Analysis Workflow

1. **Load Data**: Import your video analysis CSV
2. **Overview**: Check the statistics panel for a quick summary
3. **Logo Analysis**: Focus on logo detection periods and thresholds
4. **Scene Analysis**: Identify major scene transitions
5. **Quality Check**: Look for black/white frame anomalies
6. **Export Results**: Save visualizations for reporting or documentation
