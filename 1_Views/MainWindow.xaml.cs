using B2S_OCR_Tool._2_ViewModels;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace B2S_OCR_Tool
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        readonly MainWindowModel _model = new();

        public MainWindow()
        {
            InitializeComponent();

            DataContext = _model;

            Closing += ThisWindow_Closing;

            // Live feed
            new Task(StartCamera).Start();
        }

        /// <summary>
        /// Updates the UI Image
        /// </summary>
        public void StartCamera()
        {
            while (!_model.CameraOCRHandler.IsDisposed())
            {
                Dispatcher.BeginInvoke(() => {
                    try
                    {
                        imgCamPreview.Source = _model.CameraOCRHandler.GetBitmapImage();
                    }
                    catch
                    {
                        imgCamPreview.Source = null;
                        System.Diagnostics.Debug.WriteLine("Failed to get BitMapImage");
                    }
                });
                Thread.Sleep(200);
            }
        }

        private void ThisWindow_Closing(object? sender, CancelEventArgs e)
        {
            if (_model.CameraOCRHandler.IsOpened())
                _model.CameraOCRHandler.Dispose();
            App.Current.Shutdown();
        }
    }
}

