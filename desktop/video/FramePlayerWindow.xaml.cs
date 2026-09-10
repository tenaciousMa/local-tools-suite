using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LibVLCSharp.Shared;

namespace VideoGridDesktop;

public partial class FramePlayerWindow : Window
{
    private readonly VideoEntry _video;
    private readonly List<double> _times;
    private readonly DispatcherTimer _ticker;
    private LibVLC? _libVlc;
    private MediaPlayer? _mediaPlayer;
    private Media? _media;
    private bool _seeking;
    private bool _ready;
    private bool _closed;
    private bool _isFullscreen;
    private long _lengthMs;
    private long _lastTimeMs;

    public FramePlayerWindow(VideoEntry video)
    {
        InitializeComponent();
        _video = video;
        _times = new List<double>(video.ManualTimes);
        Title = $"视频播放器 - {video.OriginalName}";
        _ticker = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200),
        };
        _ticker.Tick += Ticker_Tick;

        Loaded += async (_, _) => await InitializePlayer();
        Closed += (_, _) => DisposePlayer();
        RefreshFrameList();
    }

    public List<double> ResultTimes { get; private set; } = new();

    public bool IsPlaybackActive => _mediaPlayer?.IsPlaying == true || _lastTimeMs > 0;

    public long CurrentTimeMs => _mediaPlayer?.Time ?? _lastTimeMs;

    public long MediaLengthMs => _lengthMs;

    public int AddCurrentFrameForSmoke()
    {
        AddCurrentFrame();
        return _times.Count;
    }

    private async Task InitializePlayer()
    {
        try
        {
            PlayerStatusText.Text = "正在初始化播放器...";
            _libVlc = await VlcRuntime.GetLibVlcAsync();
            if (_closed)
            {
                return;
            }

            _mediaPlayer = new MediaPlayer(_libVlc);
            PlayerView.MediaPlayer = _mediaPlayer;
            _media = new Media(_libVlc, new Uri(_video.FilePath, UriKind.Absolute));

            _mediaPlayer.TimeChanged += (_, args) =>
            {
                _lastTimeMs = args.Time;
                Dispatcher.InvokeAsync(() => UpdateTimeline(args.Time));
            };
            _mediaPlayer.LengthChanged += (_, args) =>
            {
                _lengthMs = args.Length;
                Dispatcher.InvokeAsync(() =>
                {
                    SeekSlider.Maximum = Math.Max(0.001, _lengthMs / 1000d);
                    UpdateTimeline(_lastTimeMs);
                });
            };
            _mediaPlayer.Playing += (_, _) =>
            {
                _ready = true;
                Dispatcher.InvokeAsync(() =>
                {
                    PlayerStatusText.Text = "播放中，拖动进度条后点击“添加当前帧”";
                });
            };
            _mediaPlayer.Paused += (_, _) =>
            {
                _ready = true;
                Dispatcher.InvokeAsync(() => PlayerStatusText.Text = "已暂停");
            };
            _mediaPlayer.EndReached += (_, _) =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    PlayerStatusText.Text = "播放结束";
                    _mediaPlayer.Time = 0;
                });
            };
            _mediaPlayer.EncounteredError += (_, _) =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    PlayerStatusText.Text = "播放失败，可继续使用自动等分截图";
                    MessageBox.Show(
                        this,
                        "LibVLC 无法播放这个文件。自动等分截图仍可继续使用。",
                        "无法预览视频",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                });
            };

            _ready = true;
            _mediaPlayer.Volume = (int)VolumeSlider.Value;
            if (!_mediaPlayer.Play(_media))
            {
                PlayerStatusText.Text = "已加载视频，点击播放按钮开始";
            }

            _ticker.Start();
        }
        catch (Exception exception)
        {
            PlayerStatusText.Text = "播放器初始化失败";
            MessageBox.Show(
                this,
                "播放器初始化失败：" + exception.Message,
                "无法启动播放器",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void UpdateTimeline(long timeMs)
    {
        if (_seeking)
        {
            return;
        }

        double seconds = Math.Max(0, timeMs / 1000d);
        double totalSeconds = Math.Max(0, _lengthMs / 1000d);
        SeekSlider.Value = Math.Min(SeekSlider.Maximum, seconds);
        TimeText.Text = $"{TokenComposer.FormatTimestamp(seconds, "HH:mm:ss")} / " +
            $"{TokenComposer.FormatTimestamp(totalSeconds, "HH:mm:ss")}";
    }

    private void Ticker_Tick(object? sender, EventArgs e)
    {
        if (_mediaPlayer is null || !_ready)
        {
            return;
        }

        if (_lengthMs <= 0 && _mediaPlayer.Length > 0)
        {
            _lengthMs = _mediaPlayer.Length;
            SeekSlider.Maximum = Math.Max(0.001, _lengthMs / 1000d);
        }

        if (!_seeking)
        {
            _lastTimeMs = _mediaPlayer.Time;
            UpdateTimeline(_lastTimeMs);
        }
    }

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        PlayMedia();
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        PauseMedia();
    }

    private void PlayMedia()
    {
        if (_mediaPlayer is null || _media is null)
        {
            PlayerStatusText.Text = "播放器尚未准备好";
            return;
        }

        if (_lengthMs > 0 && _mediaPlayer.Time >= _lengthMs - 200)
        {
            _mediaPlayer.Time = 0;
        }

        if (!_mediaPlayer.IsPlaying && !_mediaPlayer.Play(_media))
        {
            PlayerStatusText.Text = "播放失败";
        }
    }

    private void PauseMedia()
    {
        if (_mediaPlayer?.IsPlaying == true)
        {
            _mediaPlayer.Pause();
            PlayerStatusText.Text = "已暂停";
        }
    }

    private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        TogglePlayPause();
    }

    private void TogglePlayPause()
    {
        if (_mediaPlayer is null)
        {
            PlayerStatusText.Text = "播放器尚未准备好";
            return;
        }

        if (_mediaPlayer.IsPlaying)
        {
            PauseMedia();
        }
        else
        {
            PlayMedia();
        }
    }

    private void JumpBackButton_Click(object sender, RoutedEventArgs e)
    {
        SeekBy(-10_000);
    }

    private void JumpForwardButton_Click(object sender, RoutedEventArgs e)
    {
        SeekBy(10_000);
    }

    private void MuteButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleMute();
    }

    private void ToggleMute()
    {
        if (_mediaPlayer is null)
        {
            return;
        }

        _mediaPlayer.Mute = !_mediaPlayer.Mute;
        MuteButton.Content = _mediaPlayer.Mute ? "取消静音" : "静音";
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_mediaPlayer is null)
        {
            return;
        }

        _mediaPlayer.Volume = (int)Math.Round(VolumeSlider.Value);
        if (VolumeSlider.Value > 0 && _mediaPlayer.Mute)
        {
            _mediaPlayer.Mute = false;
            MuteButton.Content = "静音";
        }
    }

    private void FullscreenButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleFullscreen();
    }

    private void ToggleFullscreen()
    {
        if (!_isFullscreen)
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
            RootGrid.Margin = new Thickness(0);
            SidePanel.Visibility = Visibility.Collapsed;
            PlaybackControlsBorder.Visibility = Visibility.Collapsed;
            VideoSurfaceBorder.CornerRadius = new CornerRadius(0);
            FullscreenButton.Content = "退出全屏";
            _isFullscreen = true;
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowState = WindowState.Normal;
            RootGrid.Margin = new Thickness(14);
            SidePanel.Visibility = Visibility.Visible;
            PlaybackControlsBorder.Visibility = Visibility.Visible;
            VideoSurfaceBorder.CornerRadius = new CornerRadius(8);
            FullscreenButton.Content = "全屏";
            _isFullscreen = false;
        }
    }

    private void SeekBy(long deltaMs)
    {
        if (_mediaPlayer is null || !_mediaPlayer.IsSeekable)
        {
            return;
        }

        long upperBound = _lengthMs > 0 ? _lengthMs - 1 : long.MaxValue;
        _mediaPlayer.Time = Math.Clamp(_mediaPlayer.Time + deltaMs, 0, upperBound);
        _lastTimeMs = _mediaPlayer.Time;
        UpdateTimeline(_lastTimeMs);
    }

    private void AddFrameButton_Click(object sender, RoutedEventArgs e)
    {
        AddCurrentFrame();
    }

    private bool AddCurrentFrame()
    {
        double total = _lengthMs > 0
            ? _lengthMs / 1000d
            : _video.DurationSeconds;
        double seconds = _mediaPlayer?.Time / 1000d ?? _lastTimeMs / 1000d;
        if (total <= 0)
        {
            PlayerStatusText.Text = "视频尚未准备好，请稍候再添加";
            return false;
        }

        seconds = Math.Clamp(seconds, 0, Math.Max(0, total - 0.001));
        _times.Add(Math.Round(seconds, 3));
        RefreshFrameList();
        PlayerStatusText.Text = $"已添加 {TokenComposer.FormatTimestamp(seconds, "HH:mm:ss.mmm")}";
        return true;
    }

    private void DeleteFrameButton_Click(object sender, RoutedEventArgs e)
    {
        if (FrameListBox.SelectedItem is ManualTimeItem selected)
        {
            _times.RemoveAll(time => Math.Abs(time - selected.Seconds) < 0.001);
            RefreshFrameList();
        }
    }

    private void ClearFramesButton_Click(object sender, RoutedEventArgs e)
    {
        _times.Clear();
        RefreshFrameList();
    }

    private void DoneButton_Click(object sender, RoutedEventArgs e)
    {
        ResultTimes = _times
            .Distinct()
            .OrderBy(time => time)
            .ToList();
        if (ResultTimes.Count == 0)
        {
            MessageBox.Show(
                this,
                "还没有添加任何截图时间点。",
                "手动选帧",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void RefreshFrameList()
    {
        FrameListBox.Items.Clear();
        List<double> ordered = _times
            .Distinct()
            .OrderBy(time => time)
            .ToList();
        foreach (double time in ordered)
        {
            FrameListBox.Items.Add(new ManualTimeItem(time));
        }
    }

    private void SeekSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _seeking = true;
    }

    private void SeekSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        _seeking = false;
        ApplySliderSeek();
    }

    private void SeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_seeking)
        {
            ApplySliderSeek();
        }
    }

    private void ApplySliderSeek()
    {
        if (_mediaPlayer is null || !_mediaPlayer.IsSeekable)
        {
            return;
        }

        long value = (long)Math.Round(SeekSlider.Value * 1000d);
        _mediaPlayer.Time = Math.Max(0, value);
        _lastTimeMs = _mediaPlayer.Time;
        UpdateTimeline(_lastTimeMs);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space || e.Key == Key.K)
        {
            TogglePlayPause();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Left)
        {
            SeekBy(-5_000);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Right)
        {
            SeekBy(5_000);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.J)
        {
            SeekBy(-10_000);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.L)
        {
            SeekBy(10_000);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Up)
        {
            VolumeSlider.Value = Math.Min(100, VolumeSlider.Value + 5);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down)
        {
            VolumeSlider.Value = Math.Max(0, VolumeSlider.Value - 5);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.M)
        {
            ToggleMute();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F)
        {
            ToggleFullscreen();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && _isFullscreen)
        {
            ToggleFullscreen();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Home)
        {
            SeekToMilliseconds(0);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.End && _lengthMs > 0)
        {
            SeekToMilliseconds(Math.Max(0, _lengthMs - 1));
            e.Handled = true;
            return;
        }

        int percent;
        if (e.Key >= Key.D0 && e.Key <= Key.D9)
        {
            percent = e.Key - Key.D0;
        }
        else if (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9)
        {
            percent = e.Key - Key.NumPad0;
        }
        else
        {
            return;
        }

        if (_lengthMs > 0)
        {
            SeekToMilliseconds((long)(_lengthMs * percent / 10d));
            e.Handled = true;
        }
    }

    private void SeekToMilliseconds(long milliseconds)
    {
        if (_mediaPlayer is null || !_mediaPlayer.IsSeekable)
        {
            return;
        }

        long upperBound = _lengthMs > 0 ? _lengthMs - 1 : long.MaxValue;
        _mediaPlayer.Time = Math.Clamp(milliseconds, 0, upperBound);
        _lastTimeMs = _mediaPlayer.Time;
        UpdateTimeline(_lastTimeMs);
    }

    private void DisposePlayer()
    {
        _closed = true;
        _ticker.Stop();
        try
        {
            PlayerView.MediaPlayer = null;
            _mediaPlayer?.Stop();
            _mediaPlayer?.Dispose();
            _media?.Dispose();
            _libVlc = null;
        }
        catch
        {
            // Native player cleanup is best effort.
        }
    }

    private sealed class ManualTimeItem
    {
        public ManualTimeItem(double seconds)
        {
            Seconds = seconds;
        }

        public double Seconds { get; }

        public override string ToString() =>
            TokenComposer.FormatTimestamp(Seconds, "HH:mm:ss.mmm");
    }
}
