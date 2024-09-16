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
        private CancellationTokenSource cancellationTokenSource;
        private float gridScale = 100;

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
            string[] buttonLabels = Directory.GetFiles(baseDirectory, "*.mbox")
                                             .Select(file => Path.GetFileNameWithoutExtension(file))
                                             .ToArray();
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
                else
                    ReloadThumbnails(File.ReadLines(path));
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
            if (e.Key == Key.Enter)
            {
                TextBox input = sender as TextBox;

                if (Playlists.SelectedItem != null && !string.IsNullOrEmpty(input.Text))
                {
                    DownloadSong(input.Text);
                    input.Text = string.Empty;
                }
            }
        }

        private void NewPlaylist_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                TextBox input = sender as TextBox;
                
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
                    img.Source = new BitmapImage(new Uri(fullImagePath));
                    img.Height = gridScale;
                    img.Width = gridScale * (img.Width / img.Height);
                    img.Stretch = System.Windows.Media.Stretch.Uniform;
                    img.MouseDown += (o, e) => { PlaySongAsync(img.Tag.ToString()); };
                    ImageWrapPanel.Children.Add(img);
                }
            }
        }

        private async Task PlaySongAsync(string path)
        {
            StopCurrentSong();
            ActiveSongLabel.Content = $"Active Song: {Path.GetFileNameWithoutExtension(path)}";
            const int SampleRate = 48000;
            const int Channels = 2;

            // init cancellation token
            cancellationTokenSource = new CancellationTokenSource();
            CancellationToken token = cancellationTokenSource.Token;

            // start quiet ffmpeg process to decode audio file
            Process ffmpegProcess = new Process { StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = $"-i \"{path}\" -f s16le -ar {SampleRate} -ac {Channels} pipe:1",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            } };
            ffmpegProcess.Start();

            // pipe PCM data from ffmpeg to naudio
            using (WaveOutEvent waveOut = new WaveOutEvent())
            {
                BufferedWaveProvider waveProvider = new BufferedWaveProvider(new WaveFormat(SampleRate, Channels)) { BufferDuration = TimeSpan.FromSeconds(5) };
                waveOut.Init(waveProvider);
                waveOut.Play();

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
            ActiveSongLabel.Content = "Active Song:";
            ffmpegProcess.Dispose();
        }

        private void StopCurrentSong()
        {
            cancellationTokenSource?.Cancel();
            ActiveSongLabel.Content = "Active Song:";
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

        private void Stop_Click(object sender, RoutedEventArgs e)
            => StopCurrentSong();
    }
}
