//using Microsoft.Win32;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Forms;
using NLog;

namespace AudioSplitterNet
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        private bool _cancel;
        private bool _firstLog = true;
        private string _destinationPath;
        private List<SplitInfo> _splits = new List<SplitInfo>();
        private int _totalCount = 0;
        private DateTime _lastFileTime = default(DateTime);

        private bool _isProcessing = false;

        private System.Threading.Timer _timer;
        private bool processNextTimeIfNotChanged = false;

        public MainWindow()
        {
            Log("");

            AppDomain.CurrentDomain.FirstChanceException += CurrentDomain_FirstChanceException;

            InitializeComponent();
            WavFile.Text = (string)Properties.Settings.Default[WavFile.Name];
            TxtFile.Text = (string)Properties.Settings.Default[TxtFile.Name];
            OutputFolder.Text = (string)Properties.Settings.Default[OutputFolder.Name];

            _timer = new System.Threading.Timer(CheckFile, null, 5000, 5000);

            Unloaded += MainWindow_Unloaded;            
        }

        private void CurrentDomain_FirstChanceException(object sender, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs e)
        {
            Log(e.Exception.ToString(), true);
        }
        
        private void CheckFile(Object stateInfo)
        {
           Dispatcher.BeginInvoke(new Action(() =>
           {
               if (MonitorChanges.IsChecked.HasValue && 
               MonitorChanges.IsChecked.Value && 
               !string.IsNullOrEmpty(WavFile.Text) && !_isProcessing)
               {
                   Log($"Check file {WavFile.Text} status");

                   var fileInfo = new FileInfo(WavFile.Text);

                   if (fileInfo.Exists)
                   {
                       if (_lastFileTime == default(DateTime))
                       {
                           _lastFileTime = fileInfo.LastWriteTime;
                       }
                       else if (_lastFileTime != fileInfo.LastWriteTime)
                       {
                           Log("File had changed, wire up for processing if does not change after 5 sec");
                            // Timestamps differ, but wait until next round has passed
                            processNextTimeIfNotChanged = true;
                           _lastFileTime = fileInfo.LastWriteTime;
                       }
                       else if (processNextTimeIfNotChanged && _lastFileTime == fileInfo.LastWriteTime)
                       {
                           processNextTimeIfNotChanged = false;
                           Log("File had not since last check, start processing");
                           Process();
                       }
                   }
               }
           }));
        }

        private void MainWindow_Unloaded(object sender, RoutedEventArgs e)
        {
            Unloaded -= MainWindow_Unloaded;
            AppDomain.CurrentDomain.FirstChanceException -= CurrentDomain_FirstChanceException;
            _timer.Dispose();
            _timer = null;
        }

        private void SelectWavButton_Click(object sender, RoutedEventArgs e)
        {
            Log("");
            ShowOpenFile(WavFile, "Wav file (*.wav)|*.wav");
        }

        private void SelectTxtButton_Click(object sender, RoutedEventArgs e)
        {
            Log("");
            ShowOpenFile(TxtFile, "Exported pro tools info(*.txt)|*.txt");
        }

        private void SplitButton_Click(object sender, RoutedEventArgs e)
        {
            Log("");

            try
            {
                Process();
            }
            catch (Exception err)
            {
                Log($"Error from Process: {err}");
                Progress.Text = $"ERROR: {err.Message}";
                _isProcessing = false;
            }            
        }

        private void CancelButtonClick(object sender, RoutedEventArgs e)
        {
            Log("");
            _cancel = true;
            CancelButton.Visibility = Visibility.Collapsed;
        }

        private async Task Process()
        {
            _isProcessing = true;
            var memoryBefore = GC.GetTotalMemory(true);
            Log("");
            var totalTime = DateTime.Now;

            if (string.IsNullOrEmpty(TxtFile.Text) || !File.Exists(TxtFile.Text) )
            {
                Progress.Text = $"File {TxtFile.Text} not found";
                return;
            }

            if (string.IsNullOrEmpty(WavFile.Text) || !File.Exists(WavFile.Text))
            {
                Progress.Text = $"File {WavFile.Text} not found";
                return;
            }

            Log($"Process, txt file: {TxtFile.Text}, WavFile: {WavFile.Text}");

            _cancel = false;
            CancelButton.Visibility = Visibility.Visible;
            MainPanel.IsEnabled = false;

            var file = new FileInfo(WavFile.Text);

            Progress.Text = "Opening wav file";

            var inputStream = new FileStream(WavFile.Text, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
            var inputFile = new WaveFileReader(inputStream);

            _destinationPath = string.IsNullOrEmpty(OutputFolder.Text) ? file.Directory.FullName + "/" : OutputFolder.Text + "/";

            Log($"Destination path: {_destinationPath}");

            _splits.Clear();

            ProcessProToolsFile(inputFile, file);

            var currentFile = 1;

            foreach ( var split in _splits)
            {
                Log($"Processing {split.SongName}, start: {split.StartTime}, end: {split.EndTime}");

                var invalidChars = System.IO.Path.GetInvalidFileNameChars();
                var invalidCharsRemoved = split.SongName.Where(x => !invalidChars.Contains(x));

                Log($"Song name without invalid characters: {invalidCharsRemoved}");

                var destinationFileName = String.Concat(invalidCharsRemoved.Where(c => !char.IsWhiteSpace(c)));

                Log($"destinationFileName: {destinationFileName}");

                Progress.Text = $"{currentFile} / {_totalCount}: Processing {split.SongName}";
                await WavFileUtils.TrimWavFile(inputFile, _destinationPath + destinationFileName + ".wav", split.StartTime, split.EndTime);
                currentFile++;
            }

            Log($"Processing completed, time in seconds: {DateTime.Now.Subtract(totalTime).TotalSeconds}s");

            System.Diagnostics.Process.Start(_destinationPath);

            inputStream.Close();
            inputFile.Close();
            MainPanel.IsEnabled = true;
            CancelButton.Visibility = Visibility.Collapsed;
            inputFile.Dispose();
            inputStream.Dispose();

            GC.Collect();
            GC.WaitForFullGCComplete();
            var memoryAfter = GC.GetTotalMemory(true);
            var erotus = memoryAfter - memoryBefore;

            _isProcessing = false;
        }

        private struct SplitInfo
        {
            public string SongName;
            public TimeSpan StartTime;
            public TimeSpan EndTime;
        }

        private void ProcessProToolsFile(WaveFileReader inputFile, FileInfo file)
        {
            Log("");
            var teksti = File.ReadAllLines(TxtFile.Text);            

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

                        Log($"Found start at index {i}, whole line: {teksti[i]}, splitted at 4: {splitted[4]}, " +
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
                                    Log($"Start string before next end, use it, index {next}");
                                    endString = teksti[next].Split('\t')[1];
                                    break;
                                }

                                if (teksti[next].Split('\t')[4].StartsWith("[e]"))
                                {
                                    Log($"End stringfound, use it, index {next}");
                                    endString = teksti[next].Split('\t')[1];
                                    break;
                                }
                            }

                            if (endString != "")
                            {
                                endTime = TimecodeToTimespan(endString, framerate);
                                Log($"EndTime {endTime}, tolta time: {inputFile.TotalTime}");
                                endTime = inputFile.TotalTime - endTime;
                                Log($"final endTime {endTime}");
                            }
                            else
                            {
                                Log("End string nout found");
                            }
                        }

                        var songName = splitted[4].Replace("[s]", "");

                        Log($"Song name: {songName}");

                        _splits.Add(new SplitInfo { SongName = songName, StartTime = startTime, EndTime = endTime });                        
                    }

                    if (_cancel)
                    {
                        break;
                    }
                }
                else
                {
                    if (teksti[i].StartsWith("TIMECODE FORMAT"))
                    {
                        var split = teksti[i].Split('\t');
                        framerate = int.Parse(split[1].Split(' ')[0]);
                        Log($"Found timeformat code: {framerate}");
                    }

                    if (teksti[i] == "M A R K E R S  L I S T I N G")
                    {
                        Log($"Found M A R K E R S  L I S T I N G");
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

            if (!found)
            {
                Progress.Text = "Error: could not locate 'M A R K E R S  L I S T I N G' from txt file";
                Log(Progress.Text);
            }
            else
            {
                Progress.Text = "Processing completed";                
            }
        }

        private TimeSpan TimecodeToTimespan(string code, int framerate)
        {
            Log("");
            var splitted = code.Split(':');
            var milliseconds = int.Parse(splitted[3]) * (1000f / (float)framerate);
            var output = new TimeSpan(0, int.Parse(splitted[0]), int.Parse(splitted[1]), int.Parse(splitted[2]), (int)milliseconds);
            return output;
        }

        private void ShowOpenFile(TextBlock target, string extension)
        {
            Log("");
            var openFileDialog = new OpenFileDialog();

            if (!string.IsNullOrEmpty(target.Text))
            {
                var fileInfo = new FileInfo(target.Text);
                openFileDialog.InitialDirectory = fileInfo.Exists ? fileInfo.DirectoryName : "";
            }
            
            openFileDialog.Filter = extension;

            var dialogResponse = openFileDialog.ShowDialog();

            if (dialogResponse == System.Windows.Forms.DialogResult.OK)
            {
                Log($"Target: {target.Name}, file: {openFileDialog.FileName}");

                // TODO: If support for other formats as well, change this too
                if (target.Name == TxtFile.Name && !openFileDialog.FileName.EndsWith(".txt"))
                {
                    Progress.Text = "Invalid file, was excpecting .txt";
                    return;
                }

                if (target.Name == WavFile.Name && !openFileDialog.FileName.EndsWith(".wav"))
                {
                    Progress.Text = "Invalid file, was excpecting .wav";
                    return;
                }

                if (target.Name == WavFile.Name && string.IsNullOrEmpty(OutputFolder.Text))
                {
                    OutputFolder.Text = (new FileInfo(openFileDialog.FileName)).DirectoryName;
                    Properties.Settings.Default[OutputFolder.Name] = OutputFolder.Text;
                }

                target.Text = openFileDialog.FileName;
                Properties.Settings.Default[target.Name] = target.Text;
                Properties.Settings.Default.Save();
            }

            openFileDialog.Dispose();
        }        

        private void VisitBtn_Click(object sender, RoutedEventArgs e)
        {
            Log("");
            System.Diagnostics.Process.Start("http://www.ultimatium.com");
        }

        private void InfoBtn_Click(object sender, RoutedEventArgs e)
        {
            Log("");
            InfoPopup.IsOpen = true;
        }

        private void SouceForge_Click(object sender, RoutedEventArgs e)
        {
            Log("");
            System.Diagnostics.Process.Start("https://sourceforge.net/projects/marker-audio-splitter/");
        }

        private void ClosePopup_Click(object sender, RoutedEventArgs e)
        {
            Log("");
            InfoPopup.IsOpen = false;
        }

        private void Url_Click(object sender, RoutedEventArgs e)
        {
            Log("");
            System.Diagnostics.Process.Start(((Hyperlink)sender).NavigateUri.ToString());
        }

        private void OutputFolderClick(object sender, RoutedEventArgs e)
        {
            Log("");
            var folderBrowser = new FolderBrowserDialog();

            var initialPath = "";

            if (!string.IsNullOrEmpty(OutputFolder.Text))
            {
                var fileInfo = new FileInfo(OutputFolder.Text);
                initialPath = fileInfo.Exists ? fileInfo.DirectoryName : "";
            }

            var result = folderBrowser.ShowDialog();
            if (result == System.Windows.Forms.DialogResult.OK)
            {
                OutputFolder.Text = folderBrowser.SelectedPath;

                Properties.Settings.Default[OutputFolder.Name] = OutputFolder.Text;
                Properties.Settings.Default.Save();
            }

            folderBrowser.Dispose();
        }

        private void Log(string text,
            bool fatal = false,
            [System.Runtime.CompilerServices.CallerMemberName] string memberName = "",
            [System.Runtime.CompilerServices.CallerLineNumber] int sourceLineNumber = 0)
        {

            if (_firstLog)
            {
                logger.Info("***************************************");
                logger.Info("*********** New session ***************");
                logger.Info("***************************************");
                _firstLog = false;

            }

            var startString = $"{DateTime.Now}, {memberName}, line {sourceLineNumber}:";

            for (int i = startString.Length; i < 50; i++)
            {
                startString += "-";
            }

            startString += "> ";

            if(fatal)
            {
                logger.Info($"{startString} {text}");
            }
            else
            {
                logger.Error($"{startString} {text}");
            }
        }
    }
}
