# webcam-stream
Stream webcam as image using LibVLCSharp

```csharp
var logging = LoggerFactory.Create(x => x.AddConsole());
var logger = logging.CreateLogger<ImageCapture>();

// width, height, spin wait between each frame, logger instance
var imageCapture = new ImageCapture(920, 720, TimeSpan.FromSeconds(1), logger);
imageCapture.ImageHandler += (_, payload) =>
{
    var stream = new MemoryStream();
    payload.Image.SaveAsJpeg(stream);
    File.WriteAllBytes($"../../../{payload.SequenceNumber:0000}.jpg", stream.ToArray());
};

await imageCapture.Run();
```

Notes:
- Follow this guide to install [libvlc](https://github.com/videolan/libvlcsharp#installation)

Bonus:
- Blazor app to stream images to create a live stream

![screenshot](assets/screencapture.png)

TODO:
- ability to select the webcam form a list
- detect webcam resolution instead of guessing it

Source:
https://code.videolan.org/mfkl/libvlcsharp-samples/-/blob/master/PreviewThumbnailExtractor/Program.cs
