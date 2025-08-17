using LogoDetect.Models;

namespace LogoDetect.Services;

public interface IFrameProcessor
{
    void SetDebugFileTracker(Action<string> tracker);
    void Initialize(IProgressMsg? progress = null);
    void ProcessFrame(Frame current, Frame? previous);
    void Complete(IProgressMsg? progress = null);
}
