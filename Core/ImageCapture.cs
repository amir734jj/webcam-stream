using System;
using System.Collections.Concurrent;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using LibVLCSharp.Shared;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using WebcamStream.Models;

namespace WebcamStream
{
    public class ImageCapture
    {
        /// <summary>
        /// RGBA is used, so 4 byte per pixel, or 32 bits.
        /// </summary>
        private const uint BytePerPixel = 4;

        /// <summary>
        /// the number of bytes per "line"
        /// For performance reasons inside the core of VLC, it must be aligned to multiples of 32.
        /// </summary>
        private readonly uint _pitch;

        /// <summary>
        /// The number of lines in the buffer.
        /// For performance reasons inside the core of VLC, it must be aligned to multiples of 32.
        /// </summary>
        private readonly uint _lines;

        private readonly uint _height;
        private readonly uint _width;
        private readonly TimeSpan _between;
        private readonly ILogger<ImageCapture> _logger;

        public EventHandler<Payload> ImageHandler;

        public ImageCapture(uint width, uint height, TimeSpan between, ILogger<ImageCapture> logger)
        {
            _height = height;
            _width = width;
            _between = between;
            _logger = logger;
            _filesToProcess = new ConcurrentQueue<(MemoryMappedFile file, MemoryMappedViewAccessor accessor)>();

            _pitch = Align(width * BytePerPixel);
            _lines = Align(height);

            uint Align(uint size)
            {
                if (size % 32 == 0)
                {
                    return size;
                }

                return (size / 32 + 1) * 32; // Align on the next multiple of 32
            }
        }

        private MemoryMappedFile _currentMappedFile;
        private MemoryMappedViewAccessor _currentMappedViewAccessor;
        private readonly ConcurrentQueue<(MemoryMappedFile file, MemoryMappedViewAccessor accessor)> _filesToProcess;
        private long _frameCounter;

        public async Task Run()
        {
            // Load native libVlc library
            Core.Initialize();

            using var libVlc = new LibVLC();
            using var mediaPlayer = new MediaPlayer(libVlc);
            // Listen to events
            var processingCancellationTokenSource = new CancellationTokenSource();
            mediaPlayer.Stopped += (s, e) => processingCancellationTokenSource.CancelAfter(1);

            // Create new media
            using var media = new Media(libVlc, "v4l2:///dev/video0", FromType.FromLocation);
            media.AddOption($":chroma=mp2v --v4l2-width {_width} --v4l2-height {_height}");
            media.AddOption("::sout=#transcode{vcodec=h264,acodec=mpga,ab=128,channels=2,samplerate=44100,scodec=none}");
            media.AddOption(":no-sout-all");
            media.AddOption(":sout-keep");
            media.AddOption(":no-audio");
            // Set the size and format of the video here.
            mediaPlayer.SetVideoFormat("RV32", _width, _height, _pitch);
            mediaPlayer.SetVideoCallbacks(Lock, null, Display);

            // Start recording
            mediaPlayer.Play(media);

            // Waits for the processing to stop
            try
            {
                await ProcessThumbnailsAsync(processingCancellationTokenSource.Token);
            }
            catch (OperationCanceledException exception)
            {
                _logger.LogError(exception, "Operation cancelled");
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Unexpected exception");
            }
        }

        private async Task ProcessThumbnailsAsync(CancellationToken token)
        {
            var frameNumber = 0UL;
            while (!token.IsCancellationRequested)
            {
                if (_filesToProcess.TryDequeue(out var file))
                {
                    using (var image =
                        new Image<SixLabors.ImageSharp.PixelFormats.Bgra32>((int)(_pitch / BytePerPixel), (int)_lines))
                    await using (var sourceStream = file.file.CreateViewStream())
                    {
                        sourceStream.Read(MemoryMarshal.AsBytes(image.GetPixelMemoryGroup().Single().Span));
                        ImageHandler?.Invoke(null, new Payload(frameNumber, image));
                        _logger.LogInformation(@"Successfully emitted frame number: {}", frameNumber);
                    }

                    file.accessor.Dispose();
                    file.file.Dispose();
                    frameNumber++;
                }
                else
                {
                    await Task.Delay(_between, token);
                }
            }
        }

        private IntPtr Lock(IntPtr opaque, IntPtr planes)
        {
            _currentMappedFile = MemoryMappedFile.CreateNew(null, _pitch * _lines);
            _currentMappedViewAccessor = _currentMappedFile.CreateViewAccessor();
            Marshal.WriteIntPtr(planes, _currentMappedViewAccessor.SafeMemoryMappedViewHandle.DangerousGetHandle());
            return IntPtr.Zero;
        }

        private void Display(IntPtr opaque, IntPtr picture)
        {
            if (_frameCounter % 100 == 0)
            {
                _filesToProcess.Enqueue((_currentMappedFile, _currentMappedViewAccessor));
                _currentMappedFile = null;
                _currentMappedViewAccessor = null;
            }
            else
            {
                _currentMappedViewAccessor.Dispose();
                _currentMappedFile.Dispose();
                _currentMappedFile = null;
                _currentMappedViewAccessor = null;
            }

            _frameCounter++;
        }
    }
}