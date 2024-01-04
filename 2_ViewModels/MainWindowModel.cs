using B2S_API_Comm.Domain;
using B2S_API_Comm.Models;
using B2S_OCR_Tool._3_Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;

namespace B2S_OCR_Tool._2_ViewModels
{
    public partial class MainWindowModel : ObservableObject
    {
        #region Variables

        private readonly string _license = "IRONSUITE.KJ.BUY2SELL.DK.17872-2014EC04C8-DIVOPU7-FQ547C6MZYWU-OAQVUEMHJTPM-LC5OF2GP34XS-3DAMGPJO4STL-B4JXYAYSBNGG-26U2B5FIVUKK-OLXLCL-TLF7OBBUW5WLEA-DEPLOYMENT.TRIAL-YIELDD.TRIAL.EXPIRES.07.JAN.2024";

        public CameraOCRHandler CameraOCRHandler { get; } = new();
        private readonly int _numberOfPictures = 50;
        private readonly int _numberOfThreads = 10;

        public List<string> _words = new();
        #endregion

        #region Properties
        [ObservableProperty] public string _OcrText = "";
        [ObservableProperty] public string _ProductDataText = "";
        [ObservableProperty] public string _BarcodeText = "";
        [ObservableProperty] public string _DBInfoText = "";
        [ObservableProperty] public bool _NotRunning = true;

        [ObservableProperty] public bool _DeNoise = true;

		[ObservableProperty] public List<TextColorOptions> _TextColorOptionsDropdownOptions = new()
        {
            TextColorOptions.Ignore_TextColor, TextColorOptions.Black_TextColor, TextColorOptions.White_TextColor
        };
		[ObservableProperty] public TextColorOptions _TextColorOptionsSelected;

        [ObservableProperty] public List<string> _BrandOptions = new();
		[ObservableProperty] public string _BrandOptionSelected = string.Empty;
		#endregion

		#region Constructor
		public MainWindowModel()
        {
            if (!IronOcr.License.IsValidLicense(_license))
            {
                System.Diagnostics.Debug.WriteLine("License is not valid");
                App.Current.Shutdown();
            }
            IronOcr.License.LicenseKey = _license;

            new Task(() => SetupBrandDropbox()).Start();
        }
		#endregion

		#region Commands
		/// <summary>
		/// Initiates the process of reading text in the imageview
		/// </summary>
		[RelayCommand]
        private void GetText()
        {
            new Task(async () =>
            {
				// For disabling the buttons
				NotRunning = false;

                #if DEBUG
                // Initiate the timer ------------
                int sec = 0;
                System.Timers.Timer t = new(1000)
                {
                    AutoReset = true,
                };
                t.Elapsed += new ElapsedEventHandler((s, e) => { sec++; });
                t.Start();
                // -----------------
                #endif

                Reset();
                await GetProductsViaText();

                #if DEBUG
                // Ends the time take ---------------
                System.Diagnostics.Debug.WriteLine($"Time STOP: {sec} sec");
                t.Stop();
                t.Dispose();
                // -----------------
                #endif

                NotRunning = true;
            }).Start();
        }

		/// <summary>
		/// Initiates the process of reading barcodes in the imageview
		/// </summary>
		[RelayCommand]
        public void GetBarcode()
        {
            new Task(async () =>
            {
                // For disabling the buttons
                NotRunning = false;

                #if DEBUG
                // Initiate the timer ------------
                int sec = 0;
                System.Timers.Timer t = new(1000)
                {
                    AutoReset = true,
                };
                t.Elapsed += new ElapsedEventHandler((s, e) => { sec++; });
                t.Start();
                // -----------------
                #endif

                Reset();

                GetBarcodeCommand.CanExecute(false); // Button is disabled
                await GetProductsViaBarcode();
                GetBarcodeCommand.CanExecute(true); // Button is enabled

                #if DEBUG
                // Ends the time take ---------------
                System.Diagnostics.Debug.WriteLine($"Time STOP: {sec} sec");
                t.Stop();
                t.Dispose();
                // -----------------
                #endif

                NotRunning = true;
            }).Start();
        }
		#endregion

		#region Get Brands
        /// <summary>
        /// Gets brands in the DB
        /// </summary>
        public async void SetupBrandDropbox()
        {
            string defaultString = "Select Brand";

            BrandOptions.Add(defaultString);
            BrandOptionSelected = defaultString;

            foreach (Brand b in await GlobalVariables.B2SHttpClientHandler.GetBrandsAsync())
            {
                if (b.BrdName != null)
                {
					BrandOptions.Add(b.BrdName);
				}
			}
		}
		#endregion

		#region Get Products
		/// <summary>
		/// Gets the text from the OCR by MultiThreading, then filteres text and makes a request to the API
		/// </summary>
		/// <returns>The result of the reading in Json format</returns>
		public async Task<string> GetProductsViaText()
        {
            // Create the various tasks, start them thereafter
            Task[] tasks = new Task[_numberOfThreads];
            for (int i = 0; i < _numberOfThreads; i++)
            {
                tasks[i] = new Task(ReadingThread);
                tasks[i].Start();
            }
            Task.WaitAll(tasks);

            // Filter words and add pair (word + amount)
            Dictionary<string, int> wordsCount = new();
            foreach (KeyValuePair<string, int> pair in CameraOCRHandler.FilterText(_words, _numberOfPictures))
            {
                #if DEBUG
                ProductDataText += $"{pair.Key} --- ({pair.Value})\n"; // Print
                System.Diagnostics.Debug.WriteLine($"{pair.Key} --- ({pair.Value})\n");
                #elif RELEASE
                ProductDataText += $"{pair.Key}\n";
                #endif

                wordsCount.Add(pair.Key, pair.Value);
            }

			// Add Brand name and then Filter out short words
			List<string> words = new();
            if (!BrandOptionSelected.Equals("Select Brand"))
            {
                words.Add(BrandOptionSelected);
            }
			foreach (string word in wordsCount.Keys.ToList())
            {
                if (word.Length >= 4)
                {
                    words.Add(word);
                }
            }

            // returns a JSON string of the result
            DBInfoText = await CameraOCRHandler.GetDBProductsJson(words);
            if (string.IsNullOrEmpty(DBInfoText))
            {
                DBInfoText = "No Results";
            }
            return DBInfoText;
        }

        /// <summary>
        /// Gets the barcode then makes a request to the API
        /// </summary>
        /// <returns>The result of the reading in Json format</returns>
        public async Task<string> GetProductsViaBarcode()
        {
            string barcode = CameraOCRHandler.ReadBarCode(); // Read the barcode
            DBInfoText = await CameraOCRHandler.GetDBProductJson(barcode); // Get DB result and Print
            return DBInfoText;
        }
        #endregion

        #region Read Thread
        /// <summary>
        /// Makes an amount of readings depending on _numberOfPictures and _numberOfThreads
        /// </summary>
        private void ReadingThread()
        {
            bool deNoise = DeNoise;
            TextColorOptions textColor = TextColorOptionsSelected;

			for (int i = 0; i < _numberOfPictures / _numberOfThreads; i++)
            {
                List<string> words = CameraOCRHandler.ReadText(textColor, deNoise); // Read text from the OCR

                // Lock to prevent threads from running into an object usage exception
                lock (_words)
                {
					// Lock to prevent threads from running into an object usage exception
					lock (OcrText)
                    {
                        OcrText = string.Empty;
                        foreach (string word in words)
                        {
                            OcrText += word + "\n";
                        }
                    }

                    // Add local collection of words to the global collection of words
                    _words.AddRange(words);
                }
            }
        }
        #endregion

        #region Reset
        /// <summary>
        /// Resets the strings in each textbox
        /// </summary>
        private void Reset()
        {
            _words.Clear();

            OcrText = string.Empty;
            ProductDataText = string.Empty;
            BarcodeText = string.Empty;
            DBInfoText = string.Empty;
        }
        #endregion
    }
}
