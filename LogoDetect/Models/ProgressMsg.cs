using System.Text;

namespace LogoDetect.Models;

public class ProgressMsg
{
    public double Progress;
    public string? Message;
}

public interface IProgressMsg : IProgress<ProgressMsg>
{
    void Report(double Progress, string? Message = null);
    void NewLine();
};

public class Progress : System.Progress<ProgressMsg>, IProgressMsg
{
    private int _lastPosition = 0;

    public Progress() : base()
    { }

    public void Report(double Progress, string? Message = null)
    {
        ((IProgress<ProgressMsg>)this).Report(new ProgressMsg
        {
            Progress = Progress,
            Message = Message
        });
    }

    public void NewLine()
    {
        _lastPosition = 0;
        Console.WriteLine();
    }

    override protected void OnReport(ProgressMsg value)
    {
        var msg = new StringBuilder();
        if (!string.IsNullOrEmpty(value.Message))
            msg.Append($"{value.Message}");
        else
            msg.Append($"Progress");
        msg.Append($": {value.Progress:F1}%");

        var padding = msg.Length > _lastPosition ? 0 : _lastPosition;
        _lastPosition = msg.Length;

        Console.Write($"\r{msg}".PadRight(padding, ' '));
        if (value.Progress >= 100) Console.WriteLine();
    }

}
