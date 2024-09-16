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
using Thread = System.Threading.Thread;
using YoutubeDLSharp.Options;

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

        private void AddFromURL_KeyDown(object sender, KeyEventArgs e)
        {
            TextBox input = sender as TextBox;
            if (Playlists.SelectedItem != null && !string.IsNullOrEmpty(input.Text))
            {
                DownloadSong(input.Text);
                input.Text = string.Empty;
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
            int numColumns = 3;
            int numRows = (int)Math.Ceiling((float)paths.Count() / numColumns);

            // Define grid rows and columns
            ImageGrid.RowDefinitions.Clear();
            ImageGrid.ColumnDefinitions.Clear();
            ImageGrid.Children.Clear();
            for (int i = 0; i < numRows; ++i) ImageGrid.RowDefinitions.Add(new RowDefinition());
            for (int i = 0; i < numColumns; ++i) ImageGrid.ColumnDefinitions.Add(new ColumnDefinition());

            // Add images to the grid
            for (int i = 0; i < paths.Count(); ++i)
            {
                string fullImagePath = Path.Combine(baseDirectory, paths.ElementAt(i) + ".png");
                
                if (!File.Exists(fullImagePath)) MessageBox.Show($"Image not found: {fullImagePath}");
                else 
                {
                    // Create a new Image control
                    Image img = new Image();
                    img.Tag = Path.Combine(baseDirectory, paths.ElementAt(i) + ".opus");
                    img.Source = new BitmapImage(new Uri(fullImagePath));
                    img.Height = 100; // Adjust size as necessary
                    img.Width = 100;
                    img.Margin = new Thickness(5);

                    // Add the image to the correct grid position
                    Grid.SetRow(img, i / numColumns);
                    Grid.SetColumn(img, i % numColumns);
                    
                    img.MouseDown += (o, e) => { PlaySong(img.Tag.ToString()); };

                    ImageGrid.Children.Add(img);
                }
            }
        }

        private void PlaySong(string path)
        {
            const int SampleRate = 48000;
            const int Channels = 2;

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
                        while (waveProvider.BufferedDuration.TotalSeconds > 4) Thread.Sleep(100);
                    }
                }
            }

            // clean up
            ffmpegProcess.WaitForExit();
            ffmpegProcess.Dispose();
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
    }
}
