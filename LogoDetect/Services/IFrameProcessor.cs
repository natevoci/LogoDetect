using LogoDetect.Models;

namespace LogoDetect.Services;

public interface IFrameProcessor
{
    void Initialize(IProgressMsg? progress = null);
    void ProcessFrame(Frame current, Frame? previous);
    void Complete(IProgressMsg? progress = null);
}
