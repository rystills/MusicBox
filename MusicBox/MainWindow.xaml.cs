﻿using System;
using NAudio.Wave;
using System.IO;
using System.Windows;
using YoutubeDLSharp;
using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Linq;
using System.Collections.Generic;
using YoutubeDLSharp.Options;
using System.Threading.Tasks;
using System.Threading;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace MusicBox
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private string baseDirectory;
        private string ytdlPath = "C:\\Users\\Ryan\\Desktop\\ffmpeg\\bin\\yt-dlp.exe";
        private string ffmpegPath = "C:\\Users\\Ryan\\Desktop\\ffmpeg\\bin\\ffmpeg.exe";
        private string ffprobePath = "C:\\Users\\Ryan\\Desktop\\ffmpeg\\bin\\ffprobe.exe";
        private CancellationTokenSource cancellationTokenSource;
        private Random random = new Random();
        private float gridScale = 100;
        private string currentSongPath;
        private DateTime playbackStartTime;

        public MainWindow()
        {
            // Get the path of the bin folder
            baseDirectory = AppDomain.CurrentDomain.BaseDirectory;

            InitializeComponent();
            ReloadPlaylists();
            HookMediaKeys(this);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        private void HookMediaKeys(Window window)
        {
            IntPtr handle = new WindowInteropHelper(window).EnsureHandle();
            HwndSource source = HwndSource.FromHwnd(handle);
            source.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // WM_APPCOMMAND
            if (msg == 0x0319)
            {
                int cmd = (int)((long)lParam >> 16 & 0xFFFF);

                // toggle pause key
                if (cmd == 14)
                {
                    Pause_Click(null, null);
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        private void ReloadPlaylists(string desiredSelection = null)
        {
            // get playlist names
            IEnumerable<string> buttonLabels = Directory.GetFiles(baseDirectory, "*.mbox")
                                             .Select(file => Path.GetFileNameWithoutExtension(file));
            // add playlists to ListBox
            Playlists.Items.Clear();
            foreach (string label in buttonLabels)
                Playlists.Items.Add(new ListBoxItem() { Content = label });

            // select desired playlist by name
            if (desiredSelection != null)
                Playlists.SelectedItem = Playlists.Items.OfType<ListBoxItem>().FirstOrDefault(item 
                                        => item.Content.ToString() == desiredSelection);
        }

        private void Playlists_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Playlists.SelectedItem != null)
            {
                string path = Path.Combine(baseDirectory, (Playlists.SelectedItem as ContentControl).Content + ".mbox");

                // verify playlist exists
                if (!File.Exists(path))
                    MessageBox.Show($"Error: playlist '{path}' does not exist");

                // display playlist contents
                else ReloadThumbnails(File.ReadLines(path));
            }
        }

        private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                // resize thumbnail grid
                gridScale = Math.Max(1, gridScale + e.Delta * .1f);
                foreach (Image img in ImageWrapPanel.Children)
                {
                    img.Height = gridScale;
                    img.Width = gridScale * (img.Width / img.Height);
                }
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // spacebar pauses the song unless we're typing into a TextBox
            if (e.Key == Key.Space && !(Keyboard.FocusedElement is TextBox))
            {
                Pause_Click(null, null);
                e.Handled = true;
            }
        }

        private void NewPlaylist_KeyDown(object sender, KeyEventArgs e)
        {
            TextBox input = sender as TextBox;
            
            if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(input.Text))
            {
                // verify playlist doesn't already exist
                string path = Path.Combine(baseDirectory, input.Text + ".mbox");
                if (File.Exists(path))
                    MessageBox.Show($"Error: playlist '{path}' already exists");
                
                // create new playlist
                else
                {
                    File.Create(path).Close();
                    ReloadPlaylists(input.Text);
                }

                // clear input
                input.Text = string.Empty;
            }
        }

        private void ReloadThumbnails(IEnumerable<string> paths)
        {
            ImageWrapPanel.Children.Clear();

            // add clickable thumbnail images
            for (int i = 0; i < paths.Count(); ++i)
            {
                string path = paths.ElementAt(i);
                
                // cut out optional song data if present
                if (path[path.Length - 13] != '[')
                    path = path.Substring(0, path.LastIndexOf('[') + 12) + ']';
                string fullImagePath = Path.Combine(baseDirectory, path + ".png");
                
                if (!File.Exists(fullImagePath)) MessageBox.Show($"Image not found: {fullImagePath}");
                else 
                {
                    Image img = new Image();
                    img.Tag = Path.Combine(baseDirectory, paths.ElementAt(i) + ".opus");
                    if (!File.Exists(Path.Combine(baseDirectory, path + ".opus")))
                        img.Tag = Path.Combine(baseDirectory, paths.ElementAt(i) + ".m4a");
                    img.Source = new BitmapImage(new Uri(fullImagePath));
                    img.Height = gridScale;
                    img.Width = gridScale * (img.Width / img.Height);
                    img.MouseDown += (o, e) => 
                    {
                        if (img.Tag.ToString() == currentSongPath) Pause_Click(null, null);
                        else PlaySongAsync(img.Tag.ToString());
                    };
                    ImageWrapPanel.Children.Add(img);
                }
            }
        }

        private double GetSongDuration(string path)
        {
            // run ffprobe
            Process ffprobeProcess = new Process { StartInfo = new ProcessStartInfo
            {
                FileName = ffprobePath,
                Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{path}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            } };
            ffprobeProcess.Start();

            // parse result
            string output = ffprobeProcess.StandardOutput.ReadToEnd();
            ffprobeProcess.WaitForExit();

            if (double.TryParse(output.Trim(), out double duration))
                return duration;
            return 0;
        }

        private void VolumeSlider_MouseMove(object sender, MouseEventArgs e)
            => VolumeLabel.Content = $"Volume: [{((int)(VolumeSlider.Value*100)).ToString("D3")}/100]";

        private void SongGainSlider_MouseMove(object sender, MouseEventArgs e)
        {
            string prevLabel = SongGainLabel.Content.ToString();
            SongGainLabel.Content = $"Song Gain: [{(SongGainSlider.Value <= .495f ? "  " : "+")}{((int)(SongGainSlider.Value * 200 - 100)).ToString("D3")}/100]";

            // write new song gain to playlist file
            if (prevLabel != SongGainLabel.Content.ToString()
                && !string.IsNullOrEmpty(currentSongPath)
                && (SongGainSlider.Value != .5 || currentSongPath[currentSongPath.LastIndexOf('[') + 12] != ']'))
            {
                string path = Path.Combine(baseDirectory, (Playlists.SelectedItem as ContentControl).Content + ".mbox");
                string newLine = Path.GetFileNameWithoutExtension(currentSongPath);

                // cut gain value from search string
                string searchString = newLine;
                if (searchString[searchString.Length - 13] != '[')
                    searchString = searchString.Substring(0, searchString.LastIndexOf('[') + 12);
                
                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                    if (lines[i].Contains(searchString))
                    {
                        // remove old gain value if present
                        if (newLine[newLine.Length - 13] != '[')
                            newLine = newLine.Substring(0, newLine.LastIndexOf('[') + 12) + ']';

                        // apply new gain value
                        newLine = newLine.Substring(0, newLine.Length - 1) + " " + (SongGainSlider.Value == 1 ? "1" : SongGainSlider.Value == 0 ? "0" : SongGainSlider.Value.ToString("F4").Substring(1)) + ']';

                        // write back to file
                        lines[i] = newLine;
                        File.WriteAllLines(path, lines);
                        
                        // update playlist image tag & currentSongPath
                        for (int k = 0; k < ImageWrapPanel.Children.Count; ++k)
                            if ((ImageWrapPanel.Children[k] is Image curImg) && curImg.Tag.ToString() == currentSongPath) {
                                curImg.Tag = Path.Combine(baseDirectory, newLine) + Path.GetExtension(currentSongPath);
                                currentSongPath = curImg.Tag.ToString();
                                break;
                            }
                        break;
                    }
            }
        }

        private void SongGainSlider_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                // right/up increments by LargeChange
                case Key.Right:
                case Key.Up:
                    SongGainSlider.Value = Math.Min(SongGainSlider.Maximum, SongGainSlider.Value + SongGainSlider.LargeChange);
                    goto Handler;

                // left/down decrements by LargeChange
                case Key.Left:
                case Key.Down:
                    SongGainSlider.Value = Math.Max(SongGainSlider.Minimum, SongGainSlider.Value - SongGainSlider.LargeChange);

                Handler:
                    SongGainSlider_MouseMove(null, null);
                    e.Handled = true;
                    break;
            }
        }

        private void VolumeSlider_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                // right/up increments by LargeChange
                case Key.Right:
                case Key.Up:
                    VolumeSlider.Value = Math.Min(VolumeSlider.Maximum, VolumeSlider.Value + VolumeSlider.LargeChange);
                    goto Handler;

                // left/down decrements by LargeChange
                case Key.Left:
                case Key.Down:
                    VolumeSlider.Value = Math.Max(VolumeSlider.Minimum, VolumeSlider.Value - VolumeSlider.LargeChange);

            Handler:
                    VolumeSlider_MouseMove(null, null);
                    e.Handled = true;
                    break;
            }
        }

        private void PositionSlider_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                // right/up increments by LargeChange
                case Key.Right:
                case Key.Up:
                    PlaySongAsync(currentSongPath, (float)PositionSlider.Value + PositionSlider.LargeChange);
                    goto Handler;

                // left/down decrements by LargeChange
                case Key.Left:
                case Key.Down:
                    PlaySongAsync(currentSongPath, (float)PositionSlider.Value - PositionSlider.LargeChange);

                Handler:
                    e.Handled = true;
                    break;
            }
        }

        private void PositionSlider_MouseMove(object sender, MouseEventArgs e)
            => PositionLabel.Content = $"Position: [{((int)PositionSlider.Value).ToString("D4")}/{((int)PositionSlider.Maximum).ToString("D4")}]";

        private void PositionSlider_MouseUp(object sender, MouseButtonEventArgs e)
            => PlaySongAsync(currentSongPath, PositionSlider.Value);

        private void PasteCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (Playlists.SelectedItem != null && Clipboard.ContainsText())
                DownloadSong(Clipboard.GetText());
        }

        private void SetSongGain(double val)
        {
            SongGainSlider.Value = val;
            SongGainSlider_MouseMove(null, null);
        }

        private async Task PlaySongAsync(string path, double startTime = 0)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            StopCurrentSong();
            currentSongPath = path;
            string songName = Path.GetFileNameWithoutExtension(path);

            // apply optional song data if present
            if (songName[songName.Length - 13] == '[')
                SetSongGain(.5);
            else
            {
                SetSongGain(double.Parse(songName.Substring(0, songName.Length - 1).Split(' ').Last()));
                path = Path.Combine(Path.GetDirectoryName(path), songName.Substring(0, songName.LastIndexOf('[') + 12) + ']' + Path.GetExtension(path));
            }

            // subtract 13 characters to remove optional song data without potentially exceeding [video_id]
            songName = songName.Substring(0, songName.LastIndexOf(' ', songName.Length - 13));

            ActiveSongLabel.Content = $"Active Song: {songName}";
            const int SampleRate = 48000;
            const int Channels = 2;

            PositionSlider.Maximum = GetSongDuration(path);
            PositionSlider.Value = startTime;

            // init cancellation token
            cancellationTokenSource = new CancellationTokenSource();
            CancellationToken token = cancellationTokenSource.Token;

            // start quiet ffmpeg process to decode audio file
            Process ffmpegProcess = new Process { StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = $"-ss {startTime} -i \"{path}\" -f s16le -ar {SampleRate} -ac {Channels} pipe:1",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            } };
            ffmpegProcess.Start();

            // sync position slider to position
            Task updateSliderTask = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    Dispatcher.InvokeAsync(() => { UpdatePositionSlider(); });
                    await Task.Delay(50);
                }
            });

            // pipe PCM data from ffmpeg to naudio
            using (WaveOutEvent waveOut = new WaveOutEvent())
            {
                BufferedWaveProvider waveProvider = new BufferedWaveProvider(new WaveFormat(SampleRate, Channels)) { BufferDuration = TimeSpan.FromSeconds(5) };
                waveOut.Init(waveProvider);
                waveOut.Play();
                playbackStartTime = DateTime.Now.AddSeconds(-startTime);

                // sync volume to slider
                Task updateVolumeTask = Task.Run(async () =>
                {
                    while (!token.IsCancellationRequested)
                    {
                        Dispatcher.InvokeAsync(() => { waveOut.Volume = (float)(VolumeSlider.Value * SongGainSlider.Value); });
                        await Task.Delay(50);
                    }
                });

                using (Stream pcmStream = ffmpegProcess.StandardOutput.BaseStream)
                {
                    byte[] buffer = new byte[4096];
                    for (int bytesRead; (bytesRead = pcmStream.Read(buffer, 0, buffer.Length)) > 0;)
                    {
                        waveProvider.AddSamples(buffer, 0, bytesRead);

                        // wait for the buffer to empty a bit before refilling
                        while (waveProvider.BufferedDuration.TotalSeconds > 4) await Task.Delay(100);

                        // stop playback early
                        if (token.IsCancellationRequested) break;
                    }
                }
                
                // play out the rest of the buffer
                while (waveProvider.BufferedDuration.TotalSeconds > 0)
                    await Task.Delay(100, token);
            }

            await Task.Run(() => ffmpegProcess.WaitForExit(), token);

            // clean up
            ffmpegProcess.Dispose();

            // end behavior
            switch ((EndBehavior.SelectedItem as ContentControl).Content.ToString())
            {
                // play once
                case "1":
                default:
                    break;

                // play looped
                case "∞":
                    PlaySongAsync(currentSongPath);
                    break;

                // play random
                case "?":
                    PlayRandomSong();
                    break;
            }
        }

        private void UpdatePositionSlider()
        {
            if (!PositionSlider.IsMouseCaptureWithin)
            {
                // recalculate position
                TimeSpan elapsedTime = DateTime.Now - playbackStartTime;
                double progress = elapsedTime.TotalSeconds / PositionSlider.Maximum;
                progress = Math.Max(0, Math.Min(1, progress));

                // apply
                PositionSlider.Value = progress * PositionSlider.Maximum;
            }

            PositionSlider_MouseMove(null, null);
        }

        private void PlayRandomSong()
        {
            int songInd = random.Next(ImageWrapPanel.Children.Count);
            PlaySongAsync(((Image)ImageWrapPanel.Children[songInd]).Tag.ToString());
        }

        private void StopCurrentSong()
        {
            cancellationTokenSource?.Cancel();
        }

        private void AddSongToPlaylist(string songPath)
            => File.AppendAllText(Path.Combine(baseDirectory, (Playlists.SelectedItem as ContentControl).Content + ".mbox"),
                                  Path.GetFileNameWithoutExtension(songPath) + Environment.NewLine);

        private async void DownloadSong(string url, bool playWhenReady = true)
        {
            // download song and add to current playlist
            var ytdl = new YoutubeDL { YoutubeDLPath = ytdlPath, FFmpegPath = ffmpegPath };
            var res = await ytdl.RunAudioDownload(url, overrideOptions: new OptionSet() { WriteThumbnail = true, ConvertThumbnails = "png" });
            if (string.IsNullOrWhiteSpace(res.Data))
                MessageBox.Show($"Error: failed to download '{url}'");
            else
            {
                AddSongToPlaylist(res.Data);
                ReloadPlaylists((Playlists.SelectedItem as ContentControl).Content.ToString());
                if (playWhenReady) PlaySongAsync(res.Data);
            }
        }

        private void Pause_Click(object sender, RoutedEventArgs e)
        {
            // unpause
            if (cancellationTokenSource != null && cancellationTokenSource.IsCancellationRequested)
                PositionSlider_MouseUp(null, null);
            
            // pause
            else StopCurrentSong();
        } 
    }
}
