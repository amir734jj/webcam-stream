using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using LibVLCSharp.Shared;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.PixelFormats;
using WebcamStream.Models;

namespace WebcamStream
{
    public class ImageCapture : IDisposable
    {
        /// <summary>
        /// RGBA is used, so 4 byte per pixel, or 32 bits.
        /// </summary>
        private const uint BytePerPixel = 4;

        /// <summary>
        /// the number of bytes per "line"
        /// For performance reasons inside the core of VLC, it must be aligned to multiples of 32.
        /// </summary>
        private uint _pitch;

        /// <summary>
        /// The number of lines in the buffer.
        /// For performance reasons inside the core of VLC, it must be aligned to multiples of 32.
        /// </summary>
        private uint _lines;

        /// <summary>
        /// Capture height
        /// </summary>
        private readonly uint _height;
        
        /// <summary>
        /// Capture width
        /// </summary>
        private readonly uint _width;
        
        /// <summary>
        /// Timespan between each capture
        /// </summary>
        private TimeSpan _between;

        /// <summary>
        /// Logger instance
        /// </summary>
        private readonly ILogger _logger;

        /// <summary>
        /// Callback when image is available
        /// </summary>
        public EventHandler<Payload> ImageHandler;
        
        /// <summary>
        /// Memory mapped access
        /// </summary>
        private MemoryMappedFile _currentMappedFile;
        
        /// <summary>
        /// Memory mapped viewer
        /// </summary>
        private MemoryMappedViewAccessor _currentMappedViewAccessor;
        
        /// <summary>
        /// Concurrent queue of files to process
        /// </summary>
        private readonly ConcurrentQueue<(MemoryMappedFile file, MemoryMappedViewAccessor accessor)> _filesToProcess;
        
        /// <summary>
        /// Current frame number
        /// </summary>
        private ulong _frameCounter;

        private readonly LibVLC _libVlc;
        
        private MediaPlayer _mediaPlayer;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="logger"></param>
        public ImageCapture(ILogger logger)
        {
            _logger = logger;
            _filesToProcess = new ConcurrentQueue<(MemoryMappedFile file, MemoryMappedViewAccessor accessor)>();
            _libVlc = new LibVLC();
        }

        /// <summary>
        /// Begins extracting the thumbnails
        /// </summary>
        /// <param name="selectedDevice"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <param name="between"></param>
        /// <returns></returns>
        public async Task Run(string selectedDevice, uint width, uint height, TimeSpan between)
        {
            _between = between;
            _pitch = Align(width * BytePerPixel);
            _lines = Align(height);

            static uint Align(uint size)
            {
                if (size % 32 == 0)
                {
                    return size;
                }

                return (size / 32 + 1) * 32; // Align on the next multiple of 32
            }
            
            // Load native libVlc library
            Core.Initialize();

            using (_mediaPlayer = new MediaPlayer(_libVlc))
            {
                // Listen to events
                var processingCancellationTokenSource = new CancellationTokenSource();
                _mediaPlayer.Stopped += delegate { processingCancellationTokenSource.CancelAfter(1); };

                // Create new media
                using var media = new Media(_libVlc, selectedDevice, FromType.FromLocation);
                media.AddOption($":chroma=mp2v --v4l2-width {_width} --v4l2-height {_height}");
                media.AddOption("::sout='#transcode{{vcodec=h264,acodec=mpga,ab=128,channels=2,samplerate=44100,scodec=none}}'");
                media.AddOption(":no-sout-all");
                media.AddOption(":sout-keep");
                media.AddOption(":no-audio");
                // Set the size and format of the video here.
                _mediaPlayer.SetVideoFormat("RV32", _width, _height, _pitch);
                _mediaPlayer.SetVideoCallbacks(Lock, null, Display);

                // Start recording
                _mediaPlayer.Play(media);

                // Waits for the processing to stop
                try
                {
                    await ProcessThumbnails(processingCancellationTokenSource.Token);
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
        }

        /// <summary>
        /// Process the thumbnail
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        private async Task ProcessThumbnails(CancellationToken token)
        {
            var frameNumber = 0UL;
            while (!token.IsCancellationRequested)
            {
                if (_filesToProcess.TryDequeue(out var file))
                {
                    using var image = new Image<Bgra32>((int)(_pitch / BytePerPixel), (int)_lines);
                    // ReSharper disable once UseAwaitUsing
                    using var sourceStream = file.file.CreateViewStream();
                    {
                        sourceStream.Read(MemoryMarshal.AsBytes(image.GetPixelMemoryGroup().Single().Span));

                        ImageHandler?.Invoke(null, new Payload(frameNumber, image));
                        _logger.LogInformation(@"Successfully emitted frame number: {frameNumber}", frameNumber);
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

        /// <summary>
        /// Lock memory map
        /// </summary>
        /// <param name="opaque"></param>
        /// <param name="planes"></param>
        /// <returns></returns>
        private IntPtr Lock(IntPtr opaque, IntPtr planes)
        {
            _currentMappedFile = MemoryMappedFile.CreateNew(null, _pitch * _lines);
            _currentMappedViewAccessor = _currentMappedFile.CreateViewAccessor();
            Marshal.WriteIntPtr(planes, _currentMappedViewAccessor.SafeMemoryMappedViewHandle.DangerousGetHandle());
            return IntPtr.Zero;
        }

        /// <summary>
        /// Extract image
        /// </summary>
        /// <param name="opaque"></param>
        /// <param name="picture"></param>
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

        /// <summary>
        /// Returns the media resource locator of the device
        /// </summary>
        /// <returns></returns>
        public List<string> GetVideoDevices()
        {
            var mds = _libVlc.MediaDiscoverers(MediaDiscovererCategory.Devices);
            if (mds.Any(x => x.LongName == "Video capture"))
            {
                var devices = mds.First(x => x.LongName == "Video capture");
                if (devices.Name != null)
                {
                    var md = new MediaDiscoverer(_libVlc, devices.Name);
                    md.Start();
                    if (md.MediaList != null)
                    {
                        return md.MediaList.Select(x => x.Mrl).ToList();
                    }
                    md.Dispose();
                }
            }

            return new List<string>();
        }

        public void Dispose()
        {
            _currentMappedFile?.Dispose();
            _currentMappedViewAccessor?.Dispose();
            _mediaPlayer.Dispose();
            _libVlc.Dispose();
        }
    }
}