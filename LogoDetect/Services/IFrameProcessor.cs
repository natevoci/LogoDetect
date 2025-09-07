using LogoDetect.Models;

namespace LogoDetect.Services;

public interface IFrameProcessor
{
    void SetDebugFileTracker(Action<string> tracker);
    void SetSharedPlotManager(SharedPlotManager plotManager);
    void Initialize(IProgressMsg? progress = null);
    void ProcessFrame(Frame current, Frame? previous);
    void Complete(IProgressMsg? progress = null);
}
