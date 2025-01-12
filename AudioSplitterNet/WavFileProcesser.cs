using AudioSplitterNet;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Threading;

public class WavFileProcesser
{
    private struct SplitInfo
    {
        public string SongName;
        public TimeSpan StartTime;
        public TimeSpan EndTime;
    }

    private List<SplitInfo> _splits = new List<SplitInfo>();
    private int _totalCount;

    public WavFileProcesser()
    {
    }

    public async Task ProcessFile(string txtFile, string wavFile, string outputFolder,
        TextBlock errorText, TextBlock progress, Dispatcher dispatcher, CancellationToken cancellation)
    {
        dispatcher.Invoke(() => errorText.Text = "");
        var memoryBefore = GC.GetTotalMemory(true);
        MainWindow.Log("");
        var totalTime = DateTime.Now;

        if (string.IsNullOrEmpty(txtFile) || !File.Exists(txtFile))
        {
            dispatcher.Invoke(() => progress.Text = $"File {txtFile} not found");
            return;
        }

        if (string.IsNullOrEmpty(wavFile) || !File.Exists(wavFile))
        {
            dispatcher.Invoke(() => progress.Text = $"File {wavFile} not found");
            return;
        }

        MainWindow.Log($"Process, txt file: {txtFile}, WavFile: {wavFile}");

        var file = new FileInfo(wavFile);

        dispatcher.Invoke(() => progress.Text = "Opening wav file");

        using (var inputStream = new FileStream(wavFile, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true))
        {
            using (var inputFile = new WaveFileReader(inputStream))
            {
                var destinationPath = string.IsNullOrEmpty(outputFolder) ? file.Directory.FullName + "/" : outputFolder + "/";

                MainWindow.Log($"Destination path: {destinationPath}");

                _splits.Clear();

                ProcessProToolsFile(inputFile, file, txtFile, progress, dispatcher);

                var currentFile = 1;

                foreach (var split in _splits)
                {
                    if (cancellation.IsCancellationRequested)
                    {
                        break;
                    }

                    MainWindow.Log($"Processing {split.SongName}, cut from start: {split.StartTime}, cut from end: {split.EndTime}");

                    var invalidChars = Path.GetInvalidFileNameChars();
                    var invalidCharsRemoved = split.SongName.Where(x => !invalidChars.Contains(x));

                    MainWindow.Log($"Song name without invalid characters: {string.Join("", invalidCharsRemoved)}");

                    var destinationFileName = String.Concat(invalidCharsRemoved.Where(c => !char.IsWhiteSpace(c)));

                    MainWindow.Log($"destinationFileName: {destinationFileName}");

                    dispatcher.Invoke(() => progress.Text = $"{currentFile} / {_totalCount}: Processing {split.SongName}");
                    await WavFileUtils.TrimWavFile(inputFile, destinationPath + destinationFileName + ".wav", split.StartTime, split.EndTime, cancellation);
                    currentFile++;
                }

                MainWindow.Log($"Processing completed, time in seconds: {DateTime.Now.Subtract(totalTime).TotalSeconds}s");

                System.Diagnostics.Process.Start(destinationPath);
            }
        }

        GC.Collect();
        GC.WaitForFullGCComplete();
        var memoryAfter = GC.GetTotalMemory(true);
        var erotus = memoryAfter - memoryBefore;
    }

    private void ProcessProToolsFile(WaveFileReader inputFile, FileInfo file, string txtFile,
        TextBlock progress, Dispatcher dispatcher)
    {
        MainWindow.Log("");
        var teksti = File.ReadAllLines(txtFile);

        var framerate = 0;

        var found = false;

        for (int i = 0; i < teksti.Length; i++)
        {
            if (found)
            {
                var splitted = teksti[i].Split('\t');

                if (splitted[4].StartsWith("[s]"))
                {
                    var startTime = TimecodeToTimespan(splitted[1], framerate);

                    MainWindow.Log($"Found start at index {i}, whole line: {teksti[i]}, splitted at 4: {splitted[4]}, " +
                        $"start time: {startTime}");

                    var endTime = new TimeSpan();
                    var endString = "";
                    if (i < teksti.Length - 1)
                    {
                        // Seek next end
                        for (int next = i + 1; next < teksti.Length; next++)
                        {
                            if (teksti[next].Split('\t')[4].StartsWith("[s]"))
                            {
                                MainWindow.Log($"Start string before next end, use it, index {next}");
                                endString = teksti[next].Split('\t')[1];
                                break;
                            }

                            if (teksti[next].Split('\t')[4].StartsWith("[e]"))
                            {
                                MainWindow.Log($"End stringfound, use it, index {next}");
                                endString = teksti[next].Split('\t')[1];
                                break;
                            }
                        }

                        if (endString != "")
                        {
                            endTime = TimecodeToTimespan(endString, framerate);
                            MainWindow.Log($"EndTime {endTime}, tolta time: {inputFile.TotalTime}");
                            endTime = inputFile.TotalTime - endTime;
                            MainWindow.Log($"final endTime {endTime}");
                        }
                        else
                        {
                            MainWindow.Log("End string nout found");
                        }
                    }

                    var songName = splitted[4].Replace("[s]", "");

                    MainWindow.Log($"Song name: {songName}");

                    _splits.Add(new SplitInfo { SongName = songName, StartTime = startTime, EndTime = endTime });
                }
            }
            else
            {
                if (teksti[i].StartsWith("TIMECODE FORMAT"))
                {
                    var split = teksti[i].Split('\t');
                    framerate = int.Parse(split[1].Split(' ')[0]);
                    MainWindow.Log($"Found timeformat code: {framerate}");
                }

                if (teksti[i] == "M A R K E R S  L I S T I N G")
                {
                    MainWindow.Log($"Found M A R K E R S  L I S T I N G");
                    found = true;
                    i++; // Jump over next line
                    _totalCount = 0;
                    foreach (var line in teksti)
                    {
                        var lineSplit = line.Split('\t');
                        if (lineSplit != null && lineSplit.Length > 4 && lineSplit[4].StartsWith("[s]"))
                        {
                            _totalCount++;
                        }
                    }
                }
            }
        }

        dispatcher.Invoke(() =>
        {
            if (!found)
            {
                progress.Text = "Error: could not locate 'M A R K E R S  L I S T I N G' from txt file";
                MainWindow.Log(progress.Text);
            }
            else
            {
                progress.Text = "Processing completed";
            }
        });
    }

    private TimeSpan TimecodeToTimespan(string code, int framerate)
    {
        MainWindow.Log("");
        var splitted = code.Split(':');
        var milliseconds = int.Parse(splitted[3]) * (1000f / (float)framerate);
        var output = new TimeSpan(0, int.Parse(splitted[0]), int.Parse(splitted[1]), int.Parse(splitted[2]), (int)milliseconds);
        return output;
    }
}