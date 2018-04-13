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
using Microsoft.WindowsAPICodePack.Dialogs;
using System.Windows.Threading;
using System.Threading;

namespace AudioSplitterNet
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        private CancellationTokenSource _cancel = new CancellationTokenSource();
        private static bool _firstLog = true;

        private WavFileProcesser _processer = new WavFileProcesser();

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
                           Log("File had changed since last check, start processing");
                           StartProcessing();
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
            StartProcessing();                 
        }

        private async Task StartProcessing()
        {
            if (_isProcessing)
            {
                return;
            }

            CancelButton.Visibility = Visibility.Visible;
            MainPanel.IsEnabled = false;

            _isProcessing = true;

            _cancel = new CancellationTokenSource();
            try
            {
                var txt = TxtFile.Text;
                var wav = WavFile.Text;
                var output = OutputFolder.Text;
                await Task.Run(async () => await Process(txt, wav, output));
            }
            catch (Exception err)
            {
                Log($"Error from Process: {err}");
                Progress.Text = $"ERROR: {err.Message}";                
            }
            finally
            {
                Done();
            }
        }

        private void Done()
        {
            MainPanel.IsEnabled = true;
            CancelButton.Visibility = Visibility.Collapsed;
            _isProcessing = false;
            Progress.Text = "";
        }

        private void CancelButtonClick(object sender, RoutedEventArgs e)
        {
            Log("");
            _cancel.Cancel();
            Done();
        }

        private async Task Process(string txt, string wav, string output)
        {
            _isProcessing = true;

            try
            {
                await _processer.ProcessFile(txt, wav, output, ErrorText, Progress, Dispatcher, _cancel.Token);
            } 
            catch (Exception e)
            {
                Dispatcher.Invoke(() => ErrorText.Text = $"Error in processing: {e.Message}");
                Log($"Error in processing: {e.Message}, {e.StackTrace}");
            }
            finally
            {
                _isProcessing = false;
            }            
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
            var folderBrowser = new CommonOpenFileDialog();

            folderBrowser.InitialDirectory = OutputFolder.Text != "" ? OutputFolder.Text: "";

            folderBrowser.IsFolderPicker = true;

            if (folderBrowser.InitialDirectory == "")
            {
                folderBrowser.InitialDirectory = TxtFile.Text != "" ? new FileInfo(TxtFile.Text).DirectoryName : 
                    new FileInfo(WavFile.Text).DirectoryName;
            }

            var result = folderBrowser.ShowDialog();
            if (result == CommonFileDialogResult.Ok)
            {
                OutputFolder.Text = folderBrowser.FileName;

                Properties.Settings.Default[OutputFolder.Name] = OutputFolder.Text;
                Properties.Settings.Default.Save();
            }

            folderBrowser.Dispose();
        }

        public static void Log(string text,
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
