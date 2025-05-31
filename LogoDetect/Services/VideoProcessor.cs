using FFmpeg.AutoGen;
using SkiaSharp;
using System.Runtime.InteropServices;
using static FFmpeg.AutoGen.ffmpeg;

namespace LogoDetect.Services;

public unsafe class VideoProcessor : IDisposable
{
    private AVFormatContext* _formatContext;
    private AVCodecContext* _codecContext;
    private int _videoStream = -1;
    private bool _disposed;
    
    public Task<SKBitmap[]> ExtractFramesAsync(string inputPath, int frameCount)
    {
        return Task.Run(() =>
        {
            InitFFmpeg(inputPath);
            var frames = new List<SKBitmap>();
            var duration = GetDuration();
            var interval = duration / (frameCount - 1);

            for (int i = 0; i < frameCount; i++)
            {
                var timestamp = i * interval;
                var frame = ExtractFrameAtTimestampInternal(timestamp);
                if (frame != null)
                {
                    frames.Add(frame);
                }
            }

            return frames.ToArray();
        });
    }

    public Task<SKBitmap?> ExtractFrameAtTimestampAsync(long timestamp)
    {
        return Task.Run(() => ExtractFrameAtTimestampInternal(timestamp));
    }
    
    private unsafe SKBitmap? ExtractFrameAtTimestampInternal(long timestamp)
    {
        var frame = av_frame_alloc();
        var packet = av_packet_alloc();

        try
        {
            av_seek_frame(_formatContext, _videoStream, timestamp, AVSEEK_FLAG_BACKWARD);

            while (av_read_frame(_formatContext, packet) >= 0)
            {
                if (packet->stream_index == _videoStream)
                {
                    int response = avcodec_send_packet(_codecContext, packet);
                    if (response < 0) continue;

                    response = avcodec_receive_frame(_codecContext, frame);
                    if (response < 0) continue;

                    return ConvertFrameToSKBitmap(frame);
                }
                av_packet_unref(packet);
            }
        }
        finally
        {
            if (frame != null)
            {
                AVFrame* framePtr = frame;
                av_frame_free(&framePtr);
            }
            if (packet != null)
            {
                AVPacket* packetPtr = packet;
                av_packet_free(&packetPtr);
            }
        }

        return null;
    }
    
    private unsafe SKBitmap ConvertFrameToSKBitmap(AVFrame* frame)
    {
        var bitmap = new SKBitmap(frame->width, frame->height, SKColorType.Bgra8888, SKAlphaType.Premul);

        var swsContext = sws_getContext(
            frame->width, frame->height, (AVPixelFormat)frame->format,
            frame->width, frame->height, AVPixelFormat.AV_PIX_FMT_BGRA,
            0, null, null, null);

        if (swsContext == null) return bitmap;

        try
        {
            byte_ptrArray4 data = new byte_ptrArray4();
            int_array4 linesize = new int_array4();

            IntPtr pixelsAddr = bitmap.GetPixels();
            data.UpdateFrom(new[] { (byte*)pixelsAddr });
            linesize.UpdateFrom(new[] { frame->width * 4 });

            sws_scale(swsContext,
                frame->data,
                frame->linesize,
                0,
                frame->height,
                data,
                linesize);
        }
        finally
        {
            sws_freeContext(swsContext);
        }

        return bitmap;
    }
    
    private unsafe void InitFFmpeg(string inputPath)
    {
        _formatContext = avformat_alloc_context();
        AVFormatContext* formatContext = null;
        AVDictionary* options = null;

        // Try NVIDIA GPU acceleration first
        av_dict_set(&options, "hwaccel", "cuda", 0);
        int result = avformat_open_input(&formatContext, inputPath, null, &options);

        if (result < 0)
        {
            // Try Intel QuickSync
            av_dict_set(&options, "hwaccel", "qsv", 0);
            result = avformat_open_input(&formatContext, inputPath, null, &options);

            if (result < 0)
            {
                // Fall back to software decoding
                result = avformat_open_input(&formatContext, inputPath, null, null);
            }
        }

        _formatContext = formatContext;
        result.ThrowIfError();

        avformat_find_stream_info(_formatContext, null).ThrowIfError();

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
        _codecContext = avcodec_alloc_context3(codec);
        avcodec_parameters_to_context(_codecContext, codecParams).ThrowIfError();

        // Try to find and use hardware accelerated decoder
        var hwDecoderName = TryGetHardwareDecoderName(codecParams->codec_id);
        if (hwDecoderName != null)
        {
            var hwCodec = avcodec_find_decoder_by_name(hwDecoderName);
            if (hwCodec != null)
            {
                var hwContext = _codecContext;
                avcodec_free_context(&hwContext);
                _codecContext = avcodec_alloc_context3(hwCodec);
                avcodec_parameters_to_context(_codecContext, codecParams).ThrowIfError();
            }
        }

        avcodec_open2(_codecContext, codec, null).ThrowIfError();
    }
    
    private unsafe long GetDuration()
    {
        return _formatContext->duration;
    }
    
    public unsafe void Dispose()
    {
        if (!_disposed)
        {
            if (_codecContext != null)
            {
                AVCodecContext* codec = _codecContext;
                avcodec_free_context(&codec);
            }
            if (_formatContext != null)
            {
                AVFormatContext* format = _formatContext;
                avformat_close_input(&format);
            }
            _disposed = true;
        }
    }

    private unsafe string? TryGetHardwareDecoderName(AVCodecID codecId)
    {
        var baseCodec = avcodec_find_decoder(codecId);
        if (baseCodec == null) return null;

        var baseName = Marshal.PtrToStringAnsi((IntPtr)baseCodec->name);
        if (baseName == null) return null;

        // Check for NVIDIA GPU support
        var cudaName = $"{baseName}_cuda";
        if (avcodec_find_decoder_by_name(cudaName) != null)
            return cudaName;

        // Check for Intel QuickSync support
        var qsvName = $"{baseName}_qsv";
        if (avcodec_find_decoder_by_name(qsvName) != null)
            return qsvName;

        return null;
    }
}

public static class FFmpegExtensions
{
    public static unsafe void ThrowIfError(this int error)
    {
        if (error < 0)
        {
            var bufferSize = 1024;
            var buffer = stackalloc byte[bufferSize];
            av_strerror(error, buffer, (ulong)bufferSize);
            throw new Exception(Marshal.PtrToStringAnsi((IntPtr)buffer) ?? "Unknown FFmpeg error");
        }
    }
}
