using System;
using System.Collections.Generic;
using GenericSubscription;
using GenericSubscription.Interfaces;
using Microsoft.Extensions.Logging;
using WebcamStream;

namespace Api
{
    public class ImageStream
    {
        public ISubscriptionManagement<string, ImageCapture> SubscriptionMgmt { get; }
        
        public IReadOnlyList<string> Devices { get; }

        public ImageStream(ILogger<ImageStream> logger)
        {
            var imageCapture = new ImageCapture(logger);

            Devices = imageCapture.GetVideoDevices();
            
            SubscriptionMgmt = SubscriptionMgmtBuilder.AsSubscriptionMgmt((string device) =>
            {
                var imageCapture = new ImageCapture(logger);

                imageCapture.Run(device, 640, 480, TimeSpan.FromSeconds(1));

                return imageCapture;
            });
        }
    }
}