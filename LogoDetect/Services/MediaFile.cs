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

    public string FilePath { get; }
    private AVFormatContext* _formatContext;
    private AVCodecContext* _codecContext;
    private AVFrame* _frame;
    private AVFrame* _quarterFrame;
    private AVPacket* _packet;
    private SwsContext* _swsContext;
    private int _videoStream = -1;
    private long _currentTimestamp;
    private bool _disposed;
    private HardwareAccelMode _accelMode = HardwareAccelMode.None;

    public MediaFile(string inputPath)
    {
        FilePath = inputPath;
        _frame = av_frame_alloc();
        _quarterFrame = av_frame_alloc();
        _packet = av_packet_alloc();
        InitFFmpeg(inputPath);
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

        // Initialize scaling context for quarter-size frames
        InitializeScalingContext();
    }

    private void InitializeScalingContext()
    {
        var quarterWidth = _codecContext->width / 4;
        var quarterHeight = _codecContext->height / 4;

        // Create scaling context
        _swsContext = sws_getContext(
            _codecContext->width, _codecContext->height, _codecContext->pix_fmt,
            quarterWidth, quarterHeight, AVPixelFormat.AV_PIX_FMT_GRAY8,
            SWS_BILINEAR, null, null, null);

        if (_swsContext == null)
        {
            throw new Exception("Failed to create scaling context");
        }

        // Setup quarter frame
        _quarterFrame->format = (int)AVPixelFormat.AV_PIX_FMT_GRAY8;
        _quarterFrame->width = quarterWidth;
        _quarterFrame->height = quarterHeight;

        var bufferSize = av_image_get_buffer_size(AVPixelFormat.AV_PIX_FMT_GRAY8, quarterWidth, quarterHeight, 1);
        var buffer = (byte*)av_malloc((ulong)bufferSize);
        
        // Manually set the first plane data and linesize for grayscale
        _quarterFrame->data[0] = buffer;
        _quarterFrame->linesize[0] = quarterWidth;
    }

    public Frame? ReadNextFrame(bool onlyKeyFrames = false)
    {
        try
        {
            while (av_read_frame(_formatContext, _packet) >= 0)
            {
                if (_packet->stream_index == _videoStream && (!onlyKeyFrames || (_packet->flags & AV_PKT_FLAG_KEY) != 0))
                {
                    int response = avcodec_send_packet(_codecContext, _packet);
                    if (response < 0) continue;

                    response = avcodec_receive_frame(_codecContext, _frame);
                    if (response < 0) continue;

                    // Extract Y data (luminance) from frame
                    var yDataBytes = new byte[_frame->linesize[0] * _frame->height];
                    Marshal.Copy((IntPtr)_frame->data[0], yDataBytes, 0, yDataBytes.Length);

                    // Scale frame to quarter size
                    sws_scale(_swsContext, _frame->data, _frame->linesize, 0, _frame->height,
                        _quarterFrame->data, _quarterFrame->linesize);

                    // Extract quarter-size Y data
                    var quarterYDataBytes = new byte[_quarterFrame->linesize[0] * _quarterFrame->height];
                    Marshal.Copy((IntPtr)_quarterFrame->data[0], quarterYDataBytes, 0, quarterYDataBytes.Length);

                    _currentTimestamp = _frame->best_effort_timestamp;

                    // Convert timestamp from tbn to AV_TIME_BASE
                    var tbn = _formatContext->streams[_videoStream]->time_base;
                    _currentTimestamp = (long)(_currentTimestamp * tbn.num / (double)tbn.den * AV_TIME_BASE);

                    av_packet_unref(_packet);
                    var yData = new YData(yDataBytes, _frame->width, _frame->height, _frame->linesize[0]);
                    var quarterYData = new YData(quarterYDataBytes, _quarterFrame->width, _quarterFrame->height, _quarterFrame->linesize[0]);
                    return new Frame(yData, quarterYData, _currentTimestamp);
                }
                av_packet_unref(_packet);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading frame: {ex.Message}");
            return null;
        }

        return null;
    }

    public Task<Frame?> ReadNextFrameAsync(bool onlyKeyFrames = false, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ReadNextFrame(onlyKeyFrames), cancellationToken);
    }

    public Frame? ReadNextKeyFrame()
    {
        return ReadNextFrame(onlyKeyFrames: true);
    }

    public Frame? GetFrameAtTimestamp(long timestamp)
    {
        if (timestamp < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timestamp), "Timestamp is out of range for the media file.");
        }

        if (timestamp >= GetDuration())
        {
            return null;
        }

        try
            {
                // Convert timestamp based on AV_TIME_BASE to stream time base
                var tbn = _formatContext->streams[_videoStream]->time_base;
                var streamTimestamp = (long)(timestamp / AV_TIME_BASE * tbn.den / (double)tbn.num);

                int ret = av_seek_frame(_formatContext, _videoStream, streamTimestamp, AVSEEK_FLAG_BACKWARD);
                if (ret < 0)
                {
                    var bufferSize = 1024;
                    var buffer = stackalloc byte[bufferSize];
                    av_strerror(ret, buffer, (ulong)bufferSize);
                    throw new Exception(Marshal.PtrToStringAnsi((IntPtr)buffer) ?? "Unknown FFmpeg error");
                }

                // Flush codec buffers to ensure we don't get stale frames
                avcodec_flush_buffers(_codecContext);

                _currentTimestamp = timestamp;
            }
            catch
            {
                return null;
            }

        return ReadNextFrame();
    }

    public Frame? GetFrameAtTimeSpan(TimeSpan timeSpan)
    {
        return GetFrameAtTimestamp(timeSpan.ToTimestamp());
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
            if (_swsContext != null)
            {
                sws_freeContext(_swsContext);
            }

            if (_quarterFrame != null)
            {
                // Free the allocated buffer for quarter frame
                if (_quarterFrame->data[0] != null)
                {
                    av_free(_quarterFrame->data[0]);
                }
                var quarterFrame = _quarterFrame;
                av_frame_free(&quarterFrame);
            }

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
