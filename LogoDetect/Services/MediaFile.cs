using FFmpeg.AutoGen;
using System.Runtime.InteropServices;
using static FFmpeg.AutoGen.ffmpeg;

namespace LogoDetect.Services;

public unsafe class MediaFile : IDisposable
{
    private enum HardwareAccelMode
    {
        None,
        Cuda,
        QuickSync
    }

    private AVFormatContext* _formatContext;
    private AVCodecContext* _codecContext;
    private AVFrame* _frame;
    private AVPacket* _packet;
    private int _videoStream = -1;
    private long _currentTimestamp;
    private bool _disposed;
    private HardwareAccelMode _accelMode = HardwareAccelMode.None;

    public MediaFile(string inputPath)
    {
        InitFFmpeg(inputPath);
        _frame = av_frame_alloc();
        _packet = av_packet_alloc();
    }

    private unsafe void InitFFmpeg(string inputPath)
    {
        _formatContext = avformat_alloc_context();
        AVFormatContext* formatContext = null;
        AVDictionary* options = null;
        
        // Try NVIDIA GPU acceleration first
        av_dict_set(&options, "hwaccel", "cuda", 0);
        int result = avformat_open_input(&formatContext, inputPath, null, &options);
        if (result >= 0)
        {
            _accelMode = HardwareAccelMode.Cuda;
        }
        else
        {
            // Try Intel QuickSync
            av_dict_set(&options, "hwaccel", "qsv", 0);
            result = avformat_open_input(&formatContext, inputPath, null, &options);
            if (result >= 0)
            {
                _accelMode = HardwareAccelMode.QuickSync;
            }
            else
            {
                // Fall back to software decoding
                result = avformat_open_input(&formatContext, inputPath, null, null);
                _accelMode = HardwareAccelMode.None;
            }
        }

        _formatContext = formatContext;
        if (result < 0)
        {
            var bufferSize = 1024;
            var buffer = stackalloc byte[bufferSize];
            av_strerror(result, buffer, (ulong)bufferSize);
            throw new Exception(Marshal.PtrToStringAnsi((IntPtr)buffer) ?? "Unknown FFmpeg error");
        }

        var findStreamsResult = avformat_find_stream_info(_formatContext, null);
        if (findStreamsResult < 0)
        {
            var bufferSize = 1024;
            var buffer = stackalloc byte[bufferSize];
            av_strerror(findStreamsResult, buffer, (ulong)bufferSize);
            throw new Exception(Marshal.PtrToStringAnsi((IntPtr)buffer) ?? "Unknown FFmpeg error");
        }

        // Find video stream
        for (var i = 0; i < _formatContext->nb_streams; i++)
        {
            if (_formatContext->streams[i]->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO)
            {
                _videoStream = i;
                break;
            }
        }
        if (_videoStream == -1)
            throw new InvalidOperationException("No video stream found");

        var codecParams = _formatContext->streams[_videoStream]->codecpar;
        var codec = avcodec_find_decoder(codecParams->codec_id);

        // Try to find hardware accelerated decoder
        var hwDecoderName = TryGetHardwareDecoderName(codecParams->codec_id);
        if (hwDecoderName != null)
        {
            var hwCodec = avcodec_find_decoder_by_name(hwDecoderName);
            if (hwCodec != null)
            {
                codec = hwCodec;
            }
        }

        _codecContext = avcodec_alloc_context3(codec);
        var paramResult = avcodec_parameters_to_context(_codecContext, codecParams);
        if (paramResult < 0)
        {
            var bufferSize = 1024;
            var buffer = stackalloc byte[bufferSize];
            av_strerror(paramResult, buffer, (ulong)bufferSize);
            throw new Exception(Marshal.PtrToStringAnsi((IntPtr)buffer) ?? "Unknown FFmpeg error");
        }

        var openResult = avcodec_open2(_codecContext, codec, null);
        if (openResult < 0)
        {
            var bufferSize = 1024;
            var buffer = stackalloc byte[bufferSize];
            av_strerror(openResult, buffer, (ulong)bufferSize);
            throw new Exception(Marshal.PtrToStringAnsi((IntPtr)buffer) ?? "Unknown FFmpeg error");
        }
    }

    public Frame? ReadNextFrame()
    {
        try
        {
            while (av_read_frame(_formatContext, _packet) >= 0)
            {
                if (_packet->stream_index == _videoStream)
                {
                    int response = avcodec_send_packet(_codecContext, _packet);
                    if (response < 0) continue;

                    response = avcodec_receive_frame(_codecContext, _frame);
                    if (response < 0) continue;

                    // Extract Y data (luminance) from frame
                    var yDataBytes = new byte[_frame->linesize[0] * _frame->height];
                    Marshal.Copy((IntPtr)_frame->data[0], yDataBytes, 0, yDataBytes.Length);

                    _currentTimestamp = _frame->best_effort_timestamp;
                    av_packet_unref(_packet);
                    var yData = new YData(yDataBytes, _frame->width, _frame->height);
                    return new Frame(yData, _currentTimestamp);
                }
                av_packet_unref(_packet);
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    public Frame? ReadNextKeyFrame()
    {
        try
        {
            while (av_read_frame(_formatContext, _packet) >= 0)
            {
                if (_packet->stream_index == _videoStream && (_packet->flags & AV_PKT_FLAG_KEY) != 0)
                {
                    int response = avcodec_send_packet(_codecContext, _packet);
                    if (response < 0) continue;

                    response = avcodec_receive_frame(_codecContext, _frame);
                    if (response < 0) continue;

                    // Extract Y data (luminance) from frame
                    var yDataBytes = new byte[_frame->linesize[0] * _frame->height];
                    Marshal.Copy((IntPtr)_frame->data[0], yDataBytes, 0, yDataBytes.Length);

                    _currentTimestamp = _frame->best_effort_timestamp;
                    av_packet_unref(_packet);
                    var yData = new YData(yDataBytes, _frame->width, _frame->height);
                    return new Frame(yData, _currentTimestamp);
                }
                av_packet_unref(_packet);
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    public Frame? GetYDataAtTimestamp(long timestamp)
    {
        try
        {
            int ret = av_seek_frame(_formatContext, _videoStream, timestamp, AVSEEK_FLAG_BACKWARD);
            if (ret < 0)
            {
                var bufferSize = 1024;
                var buffer = stackalloc byte[bufferSize];
                av_strerror(ret, buffer, (ulong)bufferSize);
                throw new Exception(Marshal.PtrToStringAnsi((IntPtr)buffer) ?? "Unknown FFmpeg error");
            }

            _currentTimestamp = timestamp;
        }
        catch
        {
            return null;
        }

        return ReadNextFrame();
    }

    public Frame? GetYDataAtTimeSpan(TimeSpan timeSpan)
    {
        return GetYDataAtTimeSpan(timeSpan);
    }

    public long GetDuration() => _formatContext->duration;

    public TimeSpan GetDurationTimeSpan()
    {
        var durationInSeconds = _formatContext->duration / (double)AV_TIME_BASE;
        return TimeSpan.FromSeconds(durationInSeconds);
    }
    
    private string? TryGetHardwareDecoderName(AVCodecID codecId)
    {
        var baseCodec = avcodec_find_decoder(codecId);
        if (baseCodec == null) return null;

        var baseName = Marshal.PtrToStringAnsi((IntPtr)baseCodec->name);
        if (baseName == null) return null;

        // Only check for the decoder matching our current acceleration mode
        switch (_accelMode)
        {
            case HardwareAccelMode.Cuda:
                // Create dictionary for CUDA codec names
                var codecLookup = new Dictionary<AVCodecID, string>
                {
                    { AVCodecID.AV_CODEC_ID_H264, "h264_cuvid" },
                    { AVCodecID.AV_CODEC_ID_HEVC, "hevc_cuvid" },
                    { AVCodecID.AV_CODEC_ID_VP9, "vp9_cuvid" },
                    { AVCodecID.AV_CODEC_ID_AV1, "av1_cuvid" }
                };
                if (codecLookup.TryGetValue(codecId, out var decoderName))
                {
                    return avcodec_find_decoder_by_name(decoderName) != null ? decoderName : null;
                }
                break;

            case HardwareAccelMode.QuickSync:
                var qsvName = $"{baseName}_qsv";
                return avcodec_find_decoder_by_name(qsvName) != null ? qsvName : null;
        }

        return null;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            if (_frame != null)
            {
                var frame = _frame;
                av_frame_free(&frame);
            }

            if (_packet != null)
            {
                var packet = _packet;
                av_packet_free(&packet);
            }

            if (_codecContext != null)
            {
                var codec = _codecContext;
                avcodec_free_context(&codec);
            }

            if (_formatContext != null)
            {
                var format = _formatContext;
                avformat_close_input(&format);
            }

            _disposed = true;
        }
    }
}

public static class TimeSpanExtensions
{
    public static long ToTimestamp(this TimeSpan timeSpan)
    {
        return (long)(timeSpan.TotalSeconds * AV_TIME_BASE);
    }

    public static TimeSpan FromTimestamp(long timestamp)
    {
        return TimeSpan.FromSeconds(timestamp / (double)AV_TIME_BASE);
    }
}
