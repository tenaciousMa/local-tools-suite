using System;
using System.Threading.Tasks;
using LibVLCSharp.Shared;

namespace VideoGridDesktop;

public static class VlcRuntime
{
    private static readonly Lazy<Task<LibVLC>> InstanceFactory =
        new(() => Task.Run(() =>
        {
            Core.Initialize();
            var libVlc = new LibVLC(
                "--no-video-title-show",
                "--quiet",
                "--file-caching=800");
            using (var warmup = new MediaPlayer(libVlc))
            {
                // Warm native video/audio output modules before the first player opens.
            }

            return libVlc;
        }));

    public static Task<LibVLC> GetLibVlcAsync()
    {
        return InstanceFactory.Value;
    }
}
