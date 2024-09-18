using System;
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
                    MessageBox.Show("Error: playlist does not exist");

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

        private void AddSongFromURL_KeyDown(object sender, KeyEventArgs e)
        {
            TextBox input = sender as TextBox;
            
            if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(input.Text) && Playlists.SelectedItem != null)
            {
                DownloadSong(input.Text);
                input.Text = string.Empty;
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
                    MessageBox.Show("Error: playlist already exists");
                
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
                string fullImagePath = Path.Combine(baseDirectory, paths.ElementAt(i) + ".png");
                
                if (!File.Exists(fullImagePath)) MessageBox.Show($"Image not found: {fullImagePath}");
                else 
                {
                    Image img = new Image();
                    img.Tag = Path.Combine(baseDirectory, paths.ElementAt(i) + ".opus");
                    if (!File.Exists(img.Tag.ToString()))
                        img.Tag = Path.Combine(baseDirectory, paths.ElementAt(i) + ".m4a");
                    img.Source = new BitmapImage(new Uri(fullImagePath));
                    img.Height = gridScale;
                    img.Width = gridScale * (img.Width / img.Height);
                    img.MouseDown += (o, e) => 
                    {
                        if (img.Tag == currentSongPath) Pause_Click(null, null);
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
        
        private void PositionSlider_MouseMove(object sender, MouseEventArgs e)
            => PositionLabel.Content = $"Position: [{((int)PositionSlider.Value).ToString("D4")}/{((int)PositionSlider.Maximum).ToString("D4")}]";

        private void PositionSlider_MouseUp(object sender, MouseButtonEventArgs e)
            => PlaySongAsync(currentSongPath, (float)PositionSlider.Value);

        private async Task PlaySongAsync(string path, float startTime = 0)
        {
            StopCurrentSong();
            currentSongPath = path;
            ActiveSongLabel.Content = $"Active Song: {Path.GetFileNameWithoutExtension(path)}";
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
                        Dispatcher.InvokeAsync(() => { waveOut.Volume = (float)VolumeSlider.Value; });
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

        private async void DownloadSong(string url)
        {
            // download song and add to current playlist
            var ytdl = new YoutubeDL { YoutubeDLPath = ytdlPath, FFmpegPath = ffmpegPath };
            var res = await ytdl.RunAudioDownload(url, overrideOptions: new OptionSet() { WriteThumbnail = true, ConvertThumbnails = "png" });
            AddSongToPlaylist(res.Data);
            ReloadPlaylists((Playlists.SelectedItem as ContentControl).Content.ToString());
        }

        private void Pause_Click(object sender, RoutedEventArgs e)
        {
            // unpause
            if (cancellationTokenSource.IsCancellationRequested)
                PositionSlider_MouseUp(null, null);
            
            // pause
            else StopCurrentSong();
        } 
    }
}
