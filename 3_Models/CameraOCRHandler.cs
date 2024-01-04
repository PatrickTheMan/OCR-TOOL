using B2S_API_Comm.Domain;
using B2S_API_Comm.Models;
using IronOcr;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace B2S_OCR_Tool._3_Models
{
    /// <summary>
    /// Handles the camera and the OCR
    /// </summary>
    public class CameraOCRHandler
    {
        #region Variables
        private readonly VideoCapture ocrCamera;
        private readonly Mat matImage = new();
        private readonly IronTesseract Ocr = new();

        private readonly int cameraNum = 0; // Camera to use 
        #endregion

        #region Constructor
        public CameraOCRHandler()
        {
            ocrCamera = new(cameraNum, VideoCaptureAPIs.DSHOW)
            {
                FrameWidth = 1280,
                FrameHeight = 720
            };

            Ocr = new();
            Ocr.Configuration.BlackListCharacters = @"/\$¨'|´´.,:;#¤<>=©‘¢®[]{}`&`´*~`^*_¢©«»°±·×‘’“”•…′″€™←↑→↓↔⇄⇒∅∼≅≈≠≤≥≪≫⌁⌘○◔◑◕●☐☑☒☕☮☯☺♡⚓✓✰?_@%*+=()—§¥£";
            Ocr.Configuration.PageSegmentationMode = TesseractPageSegmentationMode.Auto;
        }
        #endregion

        #region Reading/Filtering
        /// <summary>
        /// Makes a single reading of the <see cref="Mat"/>
        /// </summary>
        /// <param name="deNoise">Should the OCR use DeNoise</param>
        /// <returns>Returns the OCR output</returns>
        public List<string> ReadText(TextColorOptions textColor, bool deNoise = false)
        {
            List<string> results = new();
			var converted = GetMemoryStream(BitmapConverter.ToBitmap(matImage));
            using (var Input = new OcrInput())
            {
                Input.AddImage(converted);
				Input.Sharpen(); // Sharpens the image
				// Converts all light and dark colorations to HotPink, to make black, gray and white stand out
				Input.ReplaceColor(Color.Yellow, Color.HotPink, 175);
				Input.ReplaceColor(Color.Blue, Color.HotPink, 175);
                // Filters out the text with given color to black, makes everything else white
                switch (textColor)
                {
                    case (TextColorOptions.Ignore_TextColor): break;
					case (TextColorOptions.Black_TextColor): Input.SelectTextColor(Color.Black, 125); break;
					case (TextColorOptions.White_TextColor): Input.SelectTextColor(Color.White, 125); break;
				}
                Input.Scale(125);

				if (deNoise)
                {
                    Input.DeNoise(); // Good for low light or high reflection environments
                }

                OcrResult Result;
                try
                {
                    Result = Ocr.Read(Input);
                }
                catch
                {
                    return new(); // Return empty
                }

                foreach (var item in Result.Words)
                {
                    string addToList = item.ToString();
                    results.Add(addToList);
                }

                /*// Used to get image out
                foreach (var item in Input.SaveAsImages())
                {
					System.Diagnostics.Debug.WriteLine(item);
				}
                */
			}

            return results;
        }

        /// <summary>
        /// Filters the words via the given amount of pictures, 
        /// Looks at how relevant the words from the OCR (given in a list of <see cref="KeyValuePair"/>) are compared to the full amount of pictures
        /// </summary>
        /// <param name="results">The words gathered from the OCR</param>
        /// <param name="pictureAmount">The number of pictures provided</param>
        /// <returns>A dictionary of the filtered words with their count</returns>
        public Dictionary<string, int> FilterText(List<string> results, int pictureAmount)
        {
			// Create a dictionary for all the words and their respective counts
			Dictionary<string, int> wordCounts = results.GroupBy(w => w).ToDictionary(g => g.Key, g => g.Count());

            // Create a dictionary for the filtered list of words
            Dictionary<string, int> wordCountsFiltered = new();
            foreach (var wordCount in wordCounts)
            {
                if (wordCount.Value > calculateRelevanceThreshold(pictureAmount, 40)) 
                {
                    wordCountsFiltered.Add(wordCount.Key, wordCount.Value);
                }
            }
            return wordCountsFiltered;
        }

        /// <summary>
        /// Makes an API call with a given set of words to retrieve a product
        /// </summary>
        /// <returns>A JSON object of products that fit the given set of words</returns>
        public async Task<string> GetDBProductsJson(IEnumerable<string> words)
        {
            if (words.Count() < 1)
            {
                return "No certain words found";
            }
            #if DEBUG
            string ocrApiString = "";
            foreach (string word in words)
            {
                ocrApiString += word + "¤";
            }
            ocrApiString = ocrApiString.Substring(0, ocrApiString.Length - 1);
            System.Diagnostics.Debug.WriteLine($"OCR_API_STRING: {ocrApiString}");
            #endif

			return await GlobalVariables.B2SHttpClientHandler.GetProductsJsonString(words, true);
        }

        /// <summary>
        /// Reads the barcode on the <see cref="Mat"/>
        /// </summary>
        /// <returns>A barcode string</returns>
        public string ReadBarCode()
        {
            string barcodeString = "";

            Ocr.Configuration.ReadBarCodes = true; // Turn on
            var converted = GetMemoryStream(BitmapConverter.ToBitmap(matImage));
            using (OcrInput input = new())
            {
                input.AddImage(converted);
                input.Scale(200);
                OcrResult Result = Ocr.Read(input);
                foreach (var barcode in Result.Barcodes)
                {
                    barcodeString = barcode.ToString();
                }
            }
            Ocr.Configuration.ReadBarCodes = false; // Turn off
            return barcodeString;
        }

        /// <summary>
        /// Makes a API Call with the Words given in a list of <see cref="string"/>
        /// </summary>
        /// <param name="barcode">A barcode string</param>
        /// <returns>A product Json</returns>
        public async Task<string> GetDBProductJson(string barcode)
        {
            return await GlobalVariables.B2SAPICommunicationOCR.GetProductsJsonString(
                new ProductRequest() { EAN = barcode, UPC = barcode, ProductNumber = "BARCODE_CHECK" }
                );
        }
        #endregion

        #region Checks
        /// <summary>
        /// Checks if the camera is disposed
        /// </summary>
        /// <returns>State</returns>
        public bool IsDisposed()
        {
            return ocrCamera.IsDisposed;
        }
        /// <summary>
        /// Checks if the camera is open
        /// </summary>
        /// <returns>State of camera openness</returns>
        public bool IsOpened()
        {
            return ocrCamera.IsOpened();
        }
        #endregion

        #region Dispose
        /// <summary>
        /// Releases the resources used by the OCRCamera
        /// </summary>
        public void Dispose()
        {
            ocrCamera.Dispose();
        }
        #endregion

        #region GetBitImage
        /// <summary>
        /// Get the current <see cref="BitmapImage"/>
        /// </summary>
        /// <returns>The <see cref="BitmapImage"/></returns>
        public BitmapImage? GetBitmapImage()
        {
            if (!ocrCamera.IsDisposed)
            {
                ocrCamera.Read(matImage);
                return !matImage.Empty() ? Convert(BitmapConverter.ToBitmap(matImage)) : null;
            }
            return null;
        }
        #endregion

        #region Image / MemoryStream / Limiter
        /// <summary>
        /// Converts a <see cref="Bitmap"/> to a <see cref="BitmapImage"/>
        /// </summary>
        /// <param name="src">The <see cref="Bitmap"/></param>
        /// <returns>The <see cref="BitmapImage"/></returns>
        private BitmapImage Convert(Bitmap src)
        {
            BitmapImage image = new();
            image.BeginInit();
            image.StreamSource = GetMemoryStream(src);
            image.EndInit();
            return image;
        }

        /// <summary>
        /// Gets the <see cref="MemoryStream"/> of a <see cref="Bitmap"/>
        /// </summary>
        /// <param name="src">The <see cref="Bitmap"/></param>
        /// <returns>The <see cref="MemoryStream"/></returns>
        private MemoryStream GetMemoryStream(Bitmap src)
        {
            MemoryStream ms = new();
            src.Save(ms, ImageFormat.Bmp);
            ms.Seek(cameraNum, SeekOrigin.Begin);
            return ms;
        }

        /// <summary>
        /// Calculates the required amount of occurrences for a word to be relevant
        /// </summary>
        /// <param name="pictureAmount">The amount of pictures to be analyzed</param>
        /// <param name="percentage">The percentage of pictures a word should be present in</param>
        /// <returns>The relevance threshold, see summary above</returns>
        private int calculateRelevanceThreshold(int pictureAmount, int percentage)
        {
            return (int)(pictureAmount * percentage / 100);
        }
		#endregion
	}
}
