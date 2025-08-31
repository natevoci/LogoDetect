using LogoDetect.Models;
using MathNet.Numerics.LinearAlgebra;
using System.Drawing;

namespace LogoDetect.Services;

/// <summary>
/// Interface for image processing operations used in video frame analysis
/// </summary>
public interface IImageProcessor
{
    /// <summary>
    /// Detects edges in the provided luminance data using Sobel edge detection
    /// </summary>
    /// <param name="input">Input luminance data</param>
    /// <returns>Edge-detected luminance data</returns>
    YData DetectEdges(YData input);

    /// <summary>
    /// Determines if there is a scene change between two frames
    /// </summary>
    /// <param name="prevData">Previous frame luminance data</param>
    /// <param name="currData">Current frame luminance data</param>
    /// <param name="threshold">Scene change threshold</param>
    /// <returns>True if scene change detected</returns>
    bool IsSceneChange(YData prevData, YData currData, double threshold);

    /// <summary>
    /// Calculates the amount of scene change between two frames
    /// </summary>
    /// <param name="prevData">Previous frame luminance data</param>
    /// <param name="currData">Current frame luminance data</param>
    /// <returns>Scene change amount (0.0 to 1.0)</returns>
    double CalculateSceneChangeAmount(YData prevData, YData currData);

    /// <summary>
    /// Compares edge data between reference and current frame for logo detection
    /// </summary>
    /// <param name="reference">Reference edge data</param>
    /// <param name="current">Current frame edge data</param>
    /// <returns>Comparison score</returns>
    float CompareEdgeData(Matrix<float> reference, Matrix<float> current);

    /// <summary>
    /// Compares edge data within a specific bounding rectangle for logo detection
    /// </summary>
    /// <param name="reference">Reference edge data</param>
    /// <param name="current">Current frame edge data</param>
    /// <param name="boundingRect">Region to compare within</param>
    /// <returns>Comparison score</returns>
    float CompareEdgeData(Matrix<float> reference, Matrix<float> current, Rectangle boundingRect);

    /// <summary>
    /// Detects if a frame is predominantly black
    /// </summary>
    /// <param name="data">Frame luminance data</param>
    /// <param name="threshold">Black detection threshold</param>
    /// <returns>True if frame is black</returns>
    bool IsBlackFrame(YData data, double threshold);

    /// <summary>
    /// Detects if a frame is predominantly white
    /// </summary>
    /// <param name="data">Frame luminance data</param>
    /// <param name="threshold">White detection threshold</param>
    /// <returns>True if frame is white</returns>
    bool IsWhiteFrame(YData data, double threshold);

    /// <summary>
    /// Hardware-accelerated detection of black or white frames using SIMD operations
    /// </summary>
    /// <param name="data">Frame luminance data</param>
    /// <param name="threshold">Detection threshold</param>
    /// <returns>Tuple indicating if frame is black and/or white</returns>
    (bool IsBlack, bool IsWhite) IsBlackOrWhiteFrame(YData data, double threshold);
}
